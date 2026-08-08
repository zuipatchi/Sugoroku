# マッチメイキング設計ドキュメント

## 概要

Unity Gaming Services (UGS) の **`com.unity.services.multiplayer`** を使ったオンラインマッチング機能。
ホストがルームを作成（定員 2〜4 人をステッパーで選び、対戦マップも選ぶ）し、他プレイヤーはルーム一覧から手動参加する方式。Relay による NAT 越え対応。

---

## 使用パッケージ

| パッケージ | バージョン | 用途 |
|---|---|---|
| `com.unity.services.multiplayer` | 2.2.3 | Session / Authentication / Relay 統合 SDK |
| `com.unity.netcode.gameobjects` | 2.12.0 | NGO ネットワーク通信 |
| `com.unity.multiplayer.playmode` | 2.0.2 | エディター MPM テスト |

---

## 事前セットアップ（必須）

1. `dashboard.unity3d.com` でプロジェクトを作成し **Lobby** と **Relay** の両サービスをプロジェクトに追加する（Relay が未追加だと `CreateSessionAsync` が `SessionException` で落ちる。ダッシュボードのサービス一覧で Relay が「設定中」に居れば追加済み＝そのまま使える。「アクティブ」の判定は過去 30 日に実際に使ったかどうかなので、初回接続までは「設定中」のままでよい）
2. **Edit → Project Settings → Services** でプロジェクト ID を紐付け
3. ⚠️ 動作確認は Windows / Mac ビルドで行う（WebGL は QoS がサポート外で Relay のリージョン選択が既定へフォールバックする。接続自体は WSS で成立するが未検証。プロトコルと `UnityTransport` の整合は `MatchingService.AlignTransportToRelayProtocol` が自動でとる＝[networking.md](networking.md)「Relay 経由の接続」）

---

## シーン構成

```
Title → Home →（オンラインプレイ）→ Matching → OnlineCharacterSelect（満室後）→ Main
```

- `Matching` は Home の「オンラインプレイ」ボタンから遷移する（「一人で遊ぶ」＝一人用モードは `Matching` を経由せず `CharacterSelect`→`MapSelect`→`Main` へ進む）
- `Matching` シーンでルーム選択・接続を完了させ、満室になったら `OnlineCharacterSelect`（キャラ選択ロビー・被り防止）を経て `Main` へ遷移する（ロビーの詳細は CLAUDE.md／[architecture.md](architecture.md)）
- `Common` シーンは常駐（既存の構成を維持）。**`NetworkManager` は `Common` に置く**（セッション作成時に存在している必要があるため。詳細は [networking.md](networking.md)「Relay 経由の接続」）

---

## フロー

```
1. Matching シーン起動
   → 匿名認証（UnityServices.Initialize + SignInAnonymously）
   → ルーム一覧を表示

2a. ルームを作成（ホスト）
   → 人数ステッパーで定員 2〜4 を選ぶ
   → マップを選ぶ（「変更」で MapSelect 風の全画面マップ選択オーバーレイ〔共通の `MapPickerView`〕。既定はカタログ先頭）
   → CreateSessionAsync(Name="Room", MaxPlayers=選んだ人数, WithRelayNetwork())。選んだマップは `BoardSessionModel` に保持
   ※ この時点で SDK が Relay を割り当てて NGO も StartHost する（NGO の起動/停止は以降セッションが握る）
   → 定員が埋まるまで相手待ち（120秒タイムアウト・待機中は「◯/◯人」をライブ表示）
   → 全員揃った（AvailableSlots==0）→ OnlineCharacterSelect（キャラ選択ロビー）→ 全員決定で Main へ
   ※ ホストだけ遷移前に MatchingFlow.HostStartDelayDuration（3秒）待つ（ホストの方が先に満室を検知しがちなのでゲストを先に到着させる）
   ※ 選んだマップはキャラ選択ロビーの共有プロパティ（`lobbyState.board`）でゲストへ同期し、全員同じ盤面で Main に入る
   → タイムアウト → 作成したセッションを退出（一覧から削除）→ リトライ確認ダイアログ

2b. ルームに手動参加
   → 一覧のルーム（「Room 1/4」＋「マップ：〇〇」）をタップ → JoinSessionByIdAsync(sessionId)
   ※ ホストが公開した join code で SDK が自動的に Relay へ参加し NGO も StartClient する（参加側にオプションは不要）
   ※ ルーム作成時にホストが選んだマップ識別子を公開セッションプロパティ（キー `board`）に載せるので、
     参加前でも一覧でマップ名を確認できる（`MatchingService.BoardPropertyKey`・`LobbyInfo.BoardId`）
   → 参加側も定員が埋まるまで相手待ち（ホストと同じ WaitForPlayerAsync・「◯/◯人」表示）
   → 全員そろった（AvailableSlots==0）→ OnlineCharacterSelect（キャラ選択ロビー）→ 全員決定で Main へ
   ※ 参加してすぐ開始しない（そうしないとゲストだけ 2 人目参加の時点で先に始まる）

3. OnlineCharacterSelect（キャラ選択ロビー）
   → 入室時点で席順（参加順）ごとの初期キャラが選択済み（1P=のらどっく / 2P=ザニザニマン / …）
   ※ 席ごとにずらすので初期状態から被らない＝そのまま「決定」を押すだけでも成立する
   → 全員が被らないキャラを選ぶ（UGS プロパティでホスト審判の先着ロック）
   → 全員「決定」→ 席順→キャラの割り当てを OnlineRosterSessionModel に保存 → Main へ

4. Main シーン開始
```

---

## アーキテクチャ

### 主要クラス

| クラス | 責務 |
|---|---|
| `MatchingModel` | マッチング状態を `ReactiveProperty` で管理 |
| `MatchingPresenter` | UI とマッチング状態のバインド（`IStartable` 実装）。入力を `MatchingFlow` へ転送する |
| `MatchingFlow` | フロー制御（認証・2秒ごとの自動ルーム更新ループ・ルーム作成〔定員2〜4＋選んだマップを `BoardSessionModel` に保持〕/参加・相手待ち〔参加人数を Model に通知〕・ゲーム開始〔`StartGameAsync`。ホストだけ `HostStartDelayDuration`＝3秒待ってからキャラ選択ロビーへ遷移する〕） |
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
| `Starting` | キャラ選択ロビー（OnlineCharacterSelect）へ遷移中 |
| `TimedOut` | タイムアウト（リトライ確認中） |
| `Error` | エラー発生 |

---

## GetRoomsAsync の実装メモ

`GetRoomsAsync` は `QuerySessionsAsync` の結果を `LobbyInfo` 一覧に変換して返す。

- **取得できなかったときは `null` を返す**（「本当に 0 件」と区別するため）。呼び出し側（`MatchingFlow.RefreshRoomsAsync`）は `null` のとき表示を更新せず据え置く。`null` になるのは次の 2 ケース:
  - クエリが既に実行中（`_isQuerying` ガード）— 自動更新と手動更新の競合時など
  - `SessionException`（UGS SDK がセッション離脱直後の過渡期に投げる NullRef の回避。次のリフレッシュで再試行）
- 変換ロジックは純メソッド `MatchingService.MapSessionsToRooms(IList<ISessionInfo>)` に分離してある。満室（`AvailableSlots == 0`）を除外し、`PlayerCount = MaxPlayers - AvailableSlots` を算出する。EditMode テスト（`MatchingServiceTests`）の対象。
- **マップ名の表示**: ホストは `CreateRoomAsync` 時に選んだマップ識別子（資産名）を公開セッションプロパティ（`VisibilityPropertyOptions.Public`・キー `MatchingService.BoardPropertyKey="board"`）に載せる。`QuerySessionsAsync` の結果（`ISessionInfo.Properties`）にも含まれるので、`MapSessionsToRooms` が `LobbyInfo.BoardId` へ取り出し、`MatchingPresenter` が `BoardCatalog` で表示名に解決してルーム一覧のボタンに「マップ：〇〇」と表示する（未設定・未登録なら省略）。

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
そのルームをタップして参加する。定員が埋まると全員が OnlineCharacterSelect（キャラ選択ロビー）へ遷移し、全員がキャラを決定すると Main シーンへ進む。

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
    MatchingService.cs          # UGS Session API（WithRelayNetwork / 転送設定の整合）
    MatchingState.cs
  OnlineCharacterSelect/        # 満室後のキャラ選択ロビー（被り防止）
    Injector/
      OnlineCharacterSelectLifetimeScope.cs
    Presenter/
      OnlineCharacterSelectPresenter.cs
    Sync/
      CharacterLobbySync.cs     # UGS プロパティでの同期・到着人数・開始判定
      CharacterClaimResolver.cs # 先着ロック・開始条件・席順の純粋ロジック
Assets/Scenes/
  Matching.unity
  OnlineCharacterSelect.unity
```

---

## 未決事項

- [x] Main シーン側の NGO 同期実装（NetworkSessionStartup / NgoMessenger）
- [x] ゲーム進行（手番・出目・コマ移動・お金・アイテム・勝敗）の同期＝`OnlineGameSync` のアクションストリーム → [networking.md](networking.md)「ゲーム進行の同期」
- [x] Relay 経由の接続（`SessionOptions.WithRelayNetwork()`・`NetworkManager` は Common 常駐・NGO の起動/停止は UGS セッションが握る）→ [networking.md](networking.md)「Relay 経由の接続」
- [x] オフライン時のフォールバック
