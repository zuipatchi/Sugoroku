# マッチメイキング設計ドキュメント

## 概要

Unity Gaming Services (UGS) の **`com.unity.services.multiplayer`** を使ったオンラインマッチング機能。
クイックマッチ（自動マッチング）またはルーム一覧からの手動参加に対応。Relay による NAT 越え対応。

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
Title → Home → Matching → Main
```

- `Matching` シーンでルーム選択・接続を完了させてから `Main` へ遷移
- `Common` シーンは常駐（既存の構成を維持）

---

## フロー

```
1. Matching シーン起動
   → 匿名認証（UnityServices.Initialize + SignInAnonymously）
   → ルーム一覧を表示

2a. クイックマッチ（推奨）
   → QuerySessions で Name="QuickMatch" かつ AvailableSlots>0 を検索
   → 見つかった → JoinSessionByIdAsync → Main シーンへ遷移
   → 見つからない → CreateSessionAsync(Name="QuickMatch", MaxPlayers=2)
     → PlayerJoined イベント待機（30秒タイムアウト）
     → タイムアウト → 作成したセッションを退出（一覧から削除）→ リトライ確認ダイアログ

2b. ルームを手動作成
   → CreateSessionAsync(MaxPlayers=2)
   → PlayerJoined イベント待機（120秒タイムアウト）
   → タイムアウト → 作成したセッションを退出（一覧から削除）→ リトライ確認ダイアログ

2c. ルームに手動参加
   → JoinSessionByIdAsync(sessionId)
   → Main シーンへ遷移

3. Main シーン開始
```

---

## アーキテクチャ

### 主要クラス

| クラス | 責務 |
|---|---|
| `MatchingModel` | マッチング状態を `ReactiveProperty` で管理 |
| `MatchingPresenter` | UI とマッチング状態のバインド（`IStartable` 実装）。2秒ごとの自動ルーム更新ループを持つ |
| `MatchingService` | UGS Session API 呼び出し |
| `MatchingStateExtensions` | `IsLoading()` / `IsWaiting()` 拡張メソッドで状態グループ判定を一元化 |
| `MatchingLifetimeScope` | Matching シーン固有 DI 登録 |
| `GameSessionModel` | `ISession` を Common シーン跨ぎで保持（Singleton） |

### DI 登録

```
CommonLifetimeScope（Common シーン常駐）
  └── GameSessionModel（Singleton）

MatchingLifetimeScope（Matching シーン）
  ├── MatchingModel
  ├── MatchingService
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
| `WaitingForPlayer` | クイックマッチ作成後の相手待ち（30秒タイムアウト） |
| `WaitingInCreatedRoom` | 手動ルーム作成後の相手待ち（120秒タイムアウト） |
| `JoiningRoom` | ルーム参加中 |
| `Starting` | Main シーンへ遷移中 |
| `TimedOut` | タイムアウト（リトライ確認中） |
| `Error` | エラー発生 |

---

## GetRoomsAsync の実装メモ

`GetRoomsAsync` は `QuerySessionsAsync` の結果を `LobbyInfo` 一覧に変換して返す。

- **取得できなかったときは `null` を返す**（「本当に 0 件」と区別するため）。呼び出し側（`MatchingPresenter.RefreshRoomsAsync`）は `null` のとき表示を更新せず据え置く。`null` になるのは次の 2 ケース:
  - クエリが既に実行中（`_isQuerying` ガード）— 自動更新と手動更新の競合時など
  - `SessionException`（UGS SDK がセッション離脱直後の過渡期に投げる NullRef の回避。次のリフレッシュで再試行）
- 変換ロジックは純メソッド `MatchingService.MapSessionsToRooms(IList<ISessionInfo>)` に分離してある。満室（`AvailableSlots == 0`）を除外し、`PlayerCount = MaxPlayers - AvailableSlots` を算出する。EditMode テスト（`MatchingServiceTests`）の対象。

---

## WaitForPlayerAsync の実装メモ

`WaitForPlayerAsync` は `CancellationTokenSource(timeout)` と外部 `ct` をリンクし、`PlayerJoined` イベントを主経路にしつつ `AvailableSlots` を 500ms 間隔でポーリングして待機する（ハンドラ登録の直前に相手が参加した取りこぼしへの保険）。

```csharp
using CancellationTokenSource timeoutCts = new(timeout);
using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, ct);

while (true)
{
    linked.Token.ThrowIfCancellationRequested();
    if (joined || session.AvailableSlots == 0) { return true; }   // joined は PlayerJoined で立つフラグ
    await UniTask.Delay(TimeSpan.FromMilliseconds(500), cancellationToken: linked.Token);
}
```

`PlayerJoined` はメインスレッド外で発火し得るため、`joined` フラグの読み書きは `Volatile.Read` / `Volatile.Write` で行いポーリング側との可視性を保証する。

**注意: タイムアウト起因のキャンセルはスレッドプールスレッドで継続され得る**

`new CancellationTokenSource(TimeSpan)` のタイマーは .NET のスレッドプールで発火するため、タイムアウトでキャンセルされたときの catch ブロックがスレッドプールスレッドに到達することがある（スタックトレースに `System.Threading._ThreadPoolWaitCallback:PerformWaitCallback()` が現れることで確認できる）。

そのため catch ブロック内で Unity API を触る前に `await UniTask.SwitchToMainThread()` を入れる。既にメインスレッドなら no-op なので、正常完了パス（`PlayerJoined` 経由）への影響はない。

---

## エディター MPM テスト

`Window → Multiplayer → Multiplayer Play Mode` で Virtual Player を追加して Enter Play Mode。

メインエディターとバーチャルプレイヤーの両方で「クイックマッチ」ボタンを押すとマッチングする。
先に起動した側がルームを作成して待機し、後から起動した側がルームを見つけて参加する。

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
    MatchingModel.cs
    MatchingPresenter.cs
    MatchingService.cs
    MatchingState.cs
Assets/Scenes/
  Matching.unity
```

---

## 未決事項

- [x] Main シーン側の NGO 同期実装（NetworkSessionStartup / NgoMessenger）
- [x] オフライン時のフォールバック
