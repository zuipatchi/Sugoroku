# マッチメイキング設計ドキュメント

## 概要

Unity Gaming Services (UGS) の **`com.unity.services.multiplayer`** を使ったオンラインマッチング機能。
ホストがルームを作成（定員 2〜4 人をステッパーで選ぶ）し、他プレイヤーはルーム一覧から手動参加する方式。Relay による NAT 越え対応。

---

## 使用パッケージ

| パッケージ | バージョン | 用途 |
|---|---|---|
| `com.unity.services.multiplayer` | 2.2.3 | Session / Authentication / Relay 統合 SDK |
| `com.unity.netcode.gameobjects` | 2.12.0 | NGO ネットワーク通信 |
| `com.unity.multiplayer.playmode` | 2.0.2 | エディター MPM テスト |

---

## 事前セットアップ（必須）

1. `dashboard.unity3d.com` でプロジェクトを作成し Lobby サービスを有効化
2. **Edit → Project Settings → Services** でプロジェクト ID を紐付け
3. ⚠️ WebGL 非対応（QoS フェーズ未サポート）。Windows / Mac ビルドを使用すること

---

## シーン構成

```
Title → Home →（オンラインプレイ）→ Matching → Main
```

- `Matching` は Home の「オンラインプレイ」ボタンから遷移する（「一人用モード」は `Matching` を経由せず `Main` へ直行する）
- `Matching` シーンでルーム選択・接続を完了させてから `Main` へ遷移
- `Common` シーンは常駐（既存の構成を維持）

---

## フロー

```
1. Matching シーン起動
   → 匿名認証（UnityServices.Initialize + SignInAnonymously）
   → ルーム一覧を表示

2a. ルームを作成（ホスト）
   → 人数ステッパーで定員 2〜4 を選ぶ
   → CreateSessionAsync(Name="Room", MaxPlayers=選んだ人数)
   → 定員が埋まるまで相手待ち（120秒タイムアウト・待機中は「◯/◯人」をライブ表示）
   → 全員揃った（AvailableSlots==0）→ Main シーンへ遷移
   → タイムアウト → 作成したセッションを退出（一覧から削除）→ リトライ確認ダイアログ

2b. ルームに手動参加
   → 一覧のルーム（「Room 1/4」等）をタップ → JoinSessionByIdAsync(sessionId)
   → Main シーンへ遷移（ホスト側で定員が埋まるとゲーム開始）

3. Main シーン開始
```

---

## アーキテクチャ

### 主要クラス

| クラス | 責務 |
|---|---|
| `MatchingModel` | マッチング状態を `ReactiveProperty` で管理 |
| `MatchingPresenter` | UI とマッチング状態のバインド（`IStartable` 実装）。入力を `MatchingFlow` へ転送する |
| `MatchingFlow` | フロー制御（認証・2秒ごとの自動ルーム更新ループ・ルーム作成〔定員2〜4〕/参加・相手待ち〔参加人数を Model に通知〕・ゲーム開始） |
| `MatchingService` | UGS Session API 呼び出し |
| `MatchingStateExtensions` | `IsLoading()` / `IsWaiting()` 拡張メソッドで状態グループ判定を一元化 |
| `MatchingLifetimeScope` | Matching シーン固有 DI 登録 |
| `GameSessionModel` | `ISession` を Common シーン跨ぎで保持（Singleton） |

### DI 登録

```
CommonLifeTimeScope（Common シーン常駐）
  └── GameSessionModel（Singleton）

MatchingLifetimeScope（Matching シーン）
  ├── MatchingModel
  ├── MatchingService
  ├── MatchingFlow
  └── MatchingPresenter（IStartable）
```

### IStartable の理由

Matching シーンを直接再生した場合、`CommonSceneLoader` が Common シーンをアディティブロードする間に
Unity の `Start()` が先に呼ばれる。VContainer の `IStartable.Start()` はスコープビルド後に呼ばれるため
注入タイミングの問題を回避できる。

---

## MatchingState

| 状態 | 意味 |
|---|---|
| `Idle` | 初期状態 |
| `Authenticating` | UGS 初期化・認証中 |
| `BrowsingRooms` | ルーム一覧表示中（ボタン有効・2秒ごと自動更新） |
| `CreatingRoom` | ルーム作成中 |
| `WaitingInCreatedRoom` | ルーム作成後の相手待ち（定員が埋まるまで・120秒タイムアウト・「◯/◯人」表示） |
| `JoiningRoom` | ルーム参加中 |
| `Starting` | Main シーンへ遷移中 |
| `TimedOut` | タイムアウト（リトライ確認中） |
| `Error` | エラー発生 |

---

## GetRoomsAsync の実装メモ

`GetRoomsAsync` は `QuerySessionsAsync` の結果を `LobbyInfo` 一覧に変換して返す。

- **取得できなかったときは `null` を返す**（「本当に 0 件」と区別するため）。呼び出し側（`MatchingFlow.RefreshRoomsAsync`）は `null` のとき表示を更新せず据え置く。`null` になるのは次の 2 ケース:
  - クエリが既に実行中（`_isQuerying` ガード）— 自動更新と手動更新の競合時など
  - `SessionException`（UGS SDK がセッション離脱直後の過渡期に投げる NullRef の回避。次のリフレッシュで再試行）
- 変換ロジックは純メソッド `MatchingService.MapSessionsToRooms(IList<ISessionInfo>)` に分離してある。満室（`AvailableSlots == 0`）を除外し、`PlayerCount = MaxPlayers - AvailableSlots` を算出する。EditMode テスト（`MatchingServiceTests`）の対象。

---

## WaitForPlayerAsync の実装メモ

`WaitForPlayerAsync` は `CancellationTokenSource(timeout)` と外部 `ct` をリンクし、**`AvailableSlots == 0`（＝定員が全員埋まる）になるまで** `AvailableSlots` を 500ms 間隔でポーリングして待機する。併せて `session.PlayerCount` を `onPlayerCount` で通知し、呼び出し側が「◯/◯人」の待機表示を更新する。

```csharp
onPlayerCount?.Invoke(session.PlayerCount);
if (session.AvailableSlots == 0) { return true; }   // 既に満室

using CancellationTokenSource timeoutCts = new(timeout);
using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, ct);

while (true)
{
    linked.Token.ThrowIfCancellationRequested();
    onPlayerCount?.Invoke(session.PlayerCount);
    if (session.AvailableSlots == 0) { return true; }   // 満室＝全員参加で成立
    await UniTask.Delay(TimeSpan.FromMilliseconds(500), cancellationToken: linked.Token);
}
```

**完了条件は「満室（`AvailableSlots == 0`）」だけにする**。`PlayerJoined` は 1 人参加するたびに発火するため、これを完了トリガーにすると 3〜4 人部屋でも 1 人目の参加で開始してしまう（過去バグ）。`AvailableSlots` のポーリングだけなら人数に依らず「全員そろってから」開始できる（最大 500ms の検知遅延は許容）。

**注意: タイムアウト起因のキャンセルはスレッドプールスレッドで継続され得る**

`new CancellationTokenSource(TimeSpan)` のタイマーは .NET のスレッドプールで発火するため、タイムアウトでキャンセルされたときの catch ブロックがスレッドプールスレッドに到達することがある（スタックトレースに `System.Threading._ThreadPoolWaitCallback:PerformWaitCallback()` が現れることで確認できる）。

そのため catch ブロック内で Unity API を触る前に `await UniTask.SwitchToMainThread()` を入れる。既にメインスレッドなら no-op なので、正常完了パスへの影響はない。

---

## エディター MPM テスト

`Window → Multiplayer → Multiplayer Play Mode` で Virtual Player を追加して Enter Play Mode。

メインエディターで人数（2〜4）を選んで「ルームを作成」して待機し、バーチャルプレイヤー側がルーム一覧から
そのルームをタップして参加する。定員が埋まると全員が Main シーンへ遷移する。

---

## ファイル配置

```
Assets/Scripts/
  Common/
    GameSession/
      GameSessionModel.cs       # ISession 保持・全シーン共有
  Matching/
    Injector/
      MatchingLifetimeScope.cs
    View/
      Matching.uxml
    LobbyInfo.cs                # ルーム情報の値型
    MatchingFlow.cs             # フロー制御（認証・自動更新・マッチ・待機）
    MatchingModel.cs
    MatchingPresenter.cs
    MatchingService.cs
    MatchingState.cs
Assets/Scenes/
  Matching.unity
```

---

## 未決事項

- [x] Main シーン側の NGO 同期実装（NetworkSessionStartup / NgoMessenger）※接続の土台（セッション接続・メッセージ送受信）のみ。ゲーム内容（手番・出目）の同期は未実装（[product.md](product.md)「未実装」参照）
- [x] オフライン時のフォールバック
