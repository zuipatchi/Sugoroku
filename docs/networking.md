# NGO + MPM ネットワーク実装ノウハウ

Unity 6 + NGO (Netcode for GameObjects) + UGS Multiplayer Services + MPM (Multiplayer Play Mode) の組み合わせで発生したハマりポイントと解決策。

---

## テンプレート適用状況

| # | 問題 | 適用先 | 済み |
|---|---|---|---|
| 1 | NGO が Common シーンを破壊 | `MatchingService.CreateRoomAsync` / `JoinRoomAsync` | ✅ |
| 2 | MPM で VContainer 親スコープが見つからない | `SceneExtensions.BuildLifetimeScopes` | ✅ |
| 3 | MPM でロード済みシーンへの遷移が壊れる | `SceneTransitioner.Transit` | ✅ |
| 4 | `CustomMessagingManager` が null | `NetworkSessionStartup.StartAsync` | ✅ |
| 5 | `IsConnectedClient=true` でもメッセージが届かない | 該当せず（接続直後のハンドシェイクを持たない設計にした） | — |
| 6 | 相手待ちは `AvailableSlots` ポーリングで判定（`PlayerJoined` 完了は使わない・N人部屋対応） | `MatchingService.WaitForPlayerAsync` | ✅ |
| 7 | MPM でフォーカスを失った画面の BGM・時間が止まる | `ProjectSettings` の `runInBackground` | ✅ |
| 8 | 遅延ハンドラ登録によるメッセージロスト（恒久対策） | `OnlineGameSync` / `ActionStream` | ✅ |
| 9 | Relay のプロトコルと `UnityTransport` のドライバが食い違う（ビルドターゲット切替で発生） | `MatchingService.AlignTransportToRelayProtocol` | ✅ |

---

## ハマりポイントと対処法

### 1. NGO の NetworkSceneManager が Common シーンを破壊する

**適用先**: `MatchingService.CreateRoomAsync` / `JoinRoomAsync`

**症状**: クライアント側で Common シーンが消え、`SceneTransitioner` が `MissingReferenceException` で死ぬ。

**原因**: NGO はデフォルト (`EnableSceneManagement=true`) でホストのシーン操作をクライアントに同期する。ホストが Main シーンを Additive でロードすると、クライアント側には **Single モードでロード** される扱いになり、Common を含む既存シーンが全て破壊される。

**対処**: セッション作成・参加の前に `EnableSceneManagement` を無効化する。

```csharp
private static void DisableNgoSceneManagement()
{
    NetworkManager nm = NetworkManager.Singleton;
    if (nm != null)
    {
        nm.NetworkConfig.EnableSceneManagement = false;
    }
}

// CreateSessionAsync / JoinSessionByIdAsync の直前に呼ぶ
await _gameSessionModel.LeaveCurrentSessionAsync();
DisableNgoSceneManagement();
IHostSession session = await MultiplayerService.Instance.CreateSessionAsync(options)...;
```

---

### 2. MPM で VContainer の親スコープが見つからない (`VContainerParentTypeReferenceNotFound`)

**適用先**: `SceneExtensions.BuildLifetimeScopes`

**症状**: クライアント側のシーン遷移後に `VContainerParentTypeReferenceNotFound` 例外。

**原因**: VContainer の `LifetimeScope.FindAnyObjectByType` はデフォルトで **inactive なオブジェクトを除外** する。MPM では各プレイヤーが独立したシーンを持つため、別プレイヤーのシーンにある親スコープを誤って拾う・または見つけられないケースが発生する。

**対処**: 全シーンを直接走査し、`Container != null`（ビルド済み）のスコープだけを親候補にする。また `Container != null` のスコープは再 Build をスキップする（二重 Build 防止）。

```csharp
internal static void BuildLifetimeScopes(this Scene scene)
{
    foreach (GameObject root in scene.GetRootGameObjects())
    {
        foreach (LifetimeScope scope in root.GetComponentsInChildren<LifetimeScope>(true))
        {
            if (scope.Container != null) continue; // 二重 Build 防止
            ResolveParentReference(scope);
            scope.Build();
        }
    }
}

private static void ResolveParentReference(LifetimeScope scope)
{
    if (scope.parentReference.Object != null) return;
    if (scope.parentReference.Type == null) return;

    Type parentType = scope.parentReference.Type;
    for (int i = 0; i < SceneManager.sceneCount; i++)
    {
        Scene s = SceneManager.GetSceneAt(i);
        foreach (GameObject root in s.GetRootGameObjects())
        {
            LifetimeScope candidate = root.GetComponentInChildren(parentType, true) as LifetimeScope;
            if (candidate != null && candidate.Container != null)
            {
                scope.parentReference.Object = candidate;
                return;
            }
        }
    }
}
```

---

### 3. MPM でのシーン遷移（既にロード済みのシーンへの対応）

**適用先**: `SceneTransitioner.Transit`

**症状**: MPM では SceneManager がプレイヤー間で共有される。一方のプレイヤーが Main シーンをロード済みの状態でもう一方がロードしようとすると、ロードがスキップされるがスコープはビルドされていない。

**対処**:
- シーンのロードを条件付きにして、スコープのビルドは常に実行する（既ビルド済みは #2 の `Container != null` チェックでスキップ）
- アンロードは `activeScene` だけでなく **Common とターゲット以外の全シーン** を対象にする

```csharp
// ロードは未ロード時のみ
Scene nextScene = SceneManager.GetSceneByBuildIndex((int)next);
if (!nextScene.IsValid() || !nextScene.isLoaded)
{
    await SceneManager.LoadSceneAsync((int)next, LoadSceneMode.Additive).WithCancellation(ct);
    nextScene = SceneManager.GetSceneByBuildIndex((int)next);
}

// スコープビルドは常に実行（既ビルドはスキップ）
nextScene.BuildLifetimeScopes();

// アンロード: Common とターゲット以外を全て（MPM 対応）
List<Scene> toUnload = new();
for (int i = 0; i < SceneManager.sceneCount; i++)
{
    Scene s = SceneManager.GetSceneAt(i);
    if (s.buildIndex != (int)Scenes.Common && s.buildIndex != nextScene.buildIndex)
    {
        toUnload.Add(s);
    }
}
foreach (Scene s in toUnload)
{
    await SceneManager.UnloadSceneAsync(s).WithCancellation(ct);
}
```

---

### 4. `CustomMessagingManager` が `JoinSessionByIdAsync` 直後に null になる

**適用先**: `NetworkSessionStartup.StartAsync`（適用済み）

**症状**: `messaging.RegisterNamedMessageHandler(...)` で NullReferenceException。

**原因**: `JoinSessionByIdAsync` が返った時点では NGO の初期化が完了していない場合がある。

**対処**: 処理開始前に NGO の準備完了を待つ。ホストは `IsListening`、クライアントは `IsConnectedClient` で確認する（条件が異なることに注意）。

```csharp
NetworkManager nm = NetworkManager.Singleton;
bool isHost = _gameSessionModel.IsHost;

while (nm.CustomMessagingManager == null
       || (isHost ? !nm.IsListening : !nm.IsConnectedClient))
{
    await UniTask.NextFrame(cancellationToken: ct);
}
```

---

### 5. `IsConnectedClient=true` でもメッセージが届かない

**本プロジェクトでは該当せず**（接続直後のハンドシェイクを持たない設計にしたため。最初のメッセージは接続確立から数秒後の 1 手目のスピンになる）

**症状**: ホストが受信ハンドラを登録して待機中、クライアントが送信しても受信できない。

**原因**: `IsConnectedClient=true` になった瞬間は NGO の Relay トランスポートが完全に双方向通信可能な状態でないケースがある。最初のメッセージが輸送レイヤーで失われる。

**対処**: 受信確認が取れるまで 200ms 間隔でリトライ送信する。ホスト側のハンドラは `UnregisterNamedMessageHandler` で1回目受信後に解除するため、複数回届いても問題ない。

```csharp
bool requestReceived = false;

void OnRequestDeck(ulong senderId, FastBufferReader reader)
{
    messaging.UnregisterNamedMessageHandler(k_RequestDeck);
    requestReceived = true;
    requestTcs.TrySetResult();
}

messaging.RegisterNamedMessageHandler(k_RequestDeck, OnRequestDeck);

while (!requestReceived)
{
    using (FastBufferWriter writer = new FastBufferWriter(4, Allocator.Temp))
    {
        messaging.SendNamedMessage(k_ClientReady, NetworkManager.ServerClientId, writer);
    }
    await UniTask.Delay(200, cancellationToken: ct);
}
```

---

### 6. 相手待ちは `PlayerJoined` ではなく `AvailableSlots` のポーリングで判定する

**適用先**: `MatchingService.WaitForPlayerAsync`

**症状（過去バグ）**: 3〜4 人部屋を作っても、2 人目が参加した瞬間にゲームが始まってしまう。

**原因**: `session.PlayerJoined` は **1 人参加するたびに発火**する。これを完了トリガーにすると、定員に達していなくても最初の参加で待機が完了してしまう（2 人固定ルーム時代の名残）。加えて `PlayerJoined` はメインスレッド外で発火し得る／登録前に発火して失われる、といった競合もある。

**対処**: 完了条件を **`AvailableSlots == 0`（＝定員が全員埋まる）だけ**にし、`AvailableSlots` を 500ms 間隔でポーリングする。これで人数に依らず「全員そろってから」開始でき、`PlayerJoined` の競合も考えなくてよい（最大 500ms の検知遅延は許容）。併せて `session.PlayerCount` を通知して「◯/◯人」の待機表示を更新する。

```csharp
onPlayerCount?.Invoke(session.PlayerCount);
if (session.AvailableSlots == 0) { return true; }   // 既に満室

while (true)
{
    linked.Token.ThrowIfCancellationRequested();
    onPlayerCount?.Invoke(session.PlayerCount);
    if (session.AvailableSlots == 0) { return true; }   // 満室＝全員参加で成立
    await UniTask.Delay(TimeSpan.FromMilliseconds(500), cancellationToken: linked.Token);
}
```

`CreateRoomAsync` 返却直後に相手が参加した「取りこぼし」も、待機に入る前の初回チェックとその後のポーリングでカバーされる（`PlayerJoined` の登録競合を気にする必要がない）。

---

### 7. MPM でフォーカスを失った画面の BGM・時間が止まる

**適用先**: `ProjectSettings/ProjectSettings.asset`（`runInBackground: 1`）

**症状**: MPM で 2 画面テスト中、片方の画面を操作するともう片方の画面で BGM が止まり、時間の流れ（アニメーション・タイマー等）も停止する。

**原因**: Unity のデフォルト設定では `Run In Background = false` のため、アプリがフォーカスを失うとオーディオ・ゲームループが一時停止する。

**対処**: `Edit → Project Settings → Player → Resolution and Presentation → Run In Background` にチェックを入れる（または `ProjectSettings.asset` の `runInBackground: 1`）。

---

### 8. 遅延ハンドラ登録によるメッセージロスト（永続ハンドラ + 受信キューで構造的に防ぐ）

**症状**: 名前付きメッセージのやり取りで、片方のプレイヤーが進めなくなり永久にハングする。アニメーションや演出を挟むほど発生しやすい。

**原因**: 「待つ直前にハンドラを登録 → 受信 → 解除」という遅延登録パターンだと、受信側がハンドラを登録する前に送信側のメッセージが届いた場合、NGO は**未登録の名前付きメッセージを破棄**する。受信側は来ないメッセージを永久に待ち続ける。セクション 4・5 もこのクラスの問題で、リトライ送信で個別に回避していた。リクエスト/レスポンス型のやり取りを増やすたびに同じ罠を踏むリスクが残る。

**対処（恒久策）**: 接続確立時に、使う名前付きメッセージのハンドラを**一度だけ永続登録**し、受信値を**チャンネルごとのキューにバッファ**する。待機側はキューにあれば即取得、無ければ waiter を登録して待つ。これで送受信の前後関係に依存せず取りこぼさない。「待つ直前に登録 → 1回受信して解除」という遅延登録パターンは不要になる。

```csharp
// 1チャンネル分の受信バッファ。受信ハンドラと待機側をキューで仲介する。
private sealed class MessageChannel
{
    private readonly Queue<string> _queue = new();
    private UniTaskCompletionSource<string> _waiter;

    // 受信ハンドラから呼ぶ：待機中なら即解決、なければキューに積む
    public void OnReceived(string payload)
    {
        UniTaskCompletionSource<string> waiter = _waiter;
        _waiter = null;
        if (waiter != null && waiter.TrySetResult(payload)) { return; }
        _queue.Enqueue(payload);
    }

    // 待機側から呼ぶ：キューにあれば即返す、なければ待つ
    public async UniTask<string> WaitAsync(CancellationToken ct)
    {
        if (_queue.Count > 0) { return _queue.Dequeue(); }
        _waiter = new UniTaskCompletionSource<string>();
        try { return await _waiter.Task.AttachExternalCancellation(ct); }
        finally { _waiter = null; }
    }
}

// 接続確立時に一度だけ、使う全メッセージのハンドラを永続登録する
foreach (string messageName in messageNames)
{
    MessageChannel channel = new();
    _channels[messageName] = channel;
    messaging.RegisterNamedMessageHandler(messageName, (senderId, reader) =>
    {
        reader.ReadValueSafe(out string json);
        channel.OnReceived(json);
    });
}
```

ペイロードの無い通知も、空文字列を送ることで「一律に string を読む」形に統一できる。

**新メッセージ追加時の指針**: メッセージ名を登録リストに足し、送信は共通ヘルパー（`SendJson(messageName, json)`）、受信待機は `channel.WaitAsync(ct)` を使う。これだけでタイミング非依存になる。手動でのハンドラ登録・解除は不要・禁止。

> ハンドシェイク（接続直後の一度きりで明示的に順序付けされたやり取り）は、リトライ送信（セクション 5）で受信を保証しているため、この一般化の対象外。従来どおり都度登録・解除する。

---

## ゲーム進行の同期（アクションストリーム）

Main シーンの盤面進行を全クライアントで一致させる仕組み。実装は `Assets/Scripts/Main/Online/`。

### 原則

**ゲームを進める「決定」を 1 本のストリームに流し、全員が受信した順にだけ適用する。**

```
決める人（手番の人／着地した人／アイテムを使った人）
   └─ Publish(GameAction)
         ├─ ホスト  : 自分以外へ再配信してから自分のキューへ積む
         └─ ゲスト  : ホストへ送るだけ（適用しない）
                          ↓ ホストが再配信
        全員（決めた本人も含む）: 受信したアクションだけを適用
```

**決めた本人も一度ネットワークを往復させてから適用する**のがポイント。これで全クライアントの適用順が必ず一致する。ホストが唯一の順序付け役（sequencer）なので、順序の衝突は起きない。

一人用モード（`GameMode.SinglePlayer`）でも同じストリームを通す（`Publish` が即ローカルのキューへ積まれるだけ）。進行のコードパスがオンラインと一本化するので、片方だけ壊れる事故が減る。

### アクションの種類

| 種別 | ペイロード | 決める人 |
|---|---|---|
| `Spin` | 停止セクター index | 手番の人 |
| `MoneyLanding` | 所持金の増減額（符号付き） | 着地した人 |
| `ShopResult` | 買ったアイテム（負値＝買わなかった） | 着地した人 |
| `ItemUse` | アイテム＋効果パラメータ | 使用者 |
| `Leave` | （なし） | ホスト（ゲスト離脱時） |

**送らないもの**: コマ移動・陣地占拠・勝敗判定は「誰が何マス進むか」と盤面データから決定論的に導けるので配らない。ルーレットの停止セクターも、`(進む人, 出目)` との割り当てが 1 対 1（`RouletteMath.SectorFor` が逆変換）なので **1 つの整数だけ**で足りる。

受信側は `RoulettePresenter.PlaySpinToAsync(sector)` で同じセクターに止まる円盤演出を再生する（`RouletteSpinPhysics.ReleaseTo` が目標角ちょうどで止める ease-out 減速を担う）。

### 決定と適用を分ける

乱数・モーダル操作を含む処理は **「決定（1 人だけ）」と「適用（全員）」に分ける**。

| 処理 | 決定 | 適用 |
|---|---|---|
| お金マスの増減額 | `MoneyCellRule.Amount` を着地者だけが引く | 受信した額を `MoneyModel.Add` |
| アイテムショップ | 着地者だけがラインナップ抽選＋モーダル選択 | 代金支払い＋`ItemModel.Add` |
| アイテム効果 | 使用者だけが対象マス・奪取額・ミニゲーム結果を決める | `ItemModel.Use` ＋効果の反映 |

**アイテムの消費（`ItemModel.Use`）は適用側で行う**。キャンセルされた使用は発行されないので、そもそも消費されない。

### ストリームを待てるのは同時に 1 箇所だけ

`ActionStream.NextAsync` を同時に 2 箇所から待つと `InvalidOperationException` になる。進行は次のように所有権を受け渡す設計になっている。

- 手番の待機（`GameFlowController.WaitForSpinAsync`）— アイテム使用が割り込んで来たら `BoardPresenter.ApplyActionAsync` へ流して待ち続ける
- 着地の待機（`BoardPresenter.WaitForActionAsync`）— `GameFlowController` は `AdvanceAsync` を待っている間ストリームを触らない

アイテムは「自分の手番かつルーレット未回転（`RouletteState.Idle`）」のときしか使えないので、着地の待機中にアイテム使用が発生することは通常ない。

### 切断

`NetworkManager.OnClientDisconnectCallback` を `OnlineGameSync` が監視する。ゲスト同士には切断が伝わらない（NGO はクライアント同士を繋がない）ため、**ホストが `Leave` を残りの全員へ配る**。受け取ると `SessionLost` が立ち、待機中の `NextAsync` がキャンセルされて進行が止まり、`BoardPresenter` が「相手が退出しました」と「ホームに戻る」を出す。

---

## Relay 経由の接続（NGO のライフサイクルは UGS セッションが握る）

NGO を **Relay**（UGS の中継サーバー）経由にすることで NAT 越しに繋がる。ポイントは「**NGO の起動・停止を自分でやらない**」こと。

### 仕組み

`SessionOptions.WithRelayNetwork()` を付けてセッションを作ると、SDK（`GameObjectsNetcodeNetworkHandler`）が次を肩代わりする。

| | ホスト | ゲスト |
|---|---|---|
| セッション作成/参加時 | Relay の割り当て → `UnityTransport.SetRelayServerData` → **`StartHost()`** | ホストが公開した join code で Relay に参加 → **`StartClient()`** |
| `ISession.LeaveAsync()` 時 | **`NetworkManager.Shutdown()`** | 同左 |

つまり **NGO の接続は「Matching シーンでセッションを作った/参加した瞬間」に確立し、「セッションを離脱した瞬間」に閉じる**。Main シーンの寿命とは無関係になる。

### 守るべきルール

- **`NetworkManager` は `Common` シーンに常駐させる**。`WithRelayNetwork()` は `NetworkManager.Singleton` が無いと `SessionException` で落ちるが、セッションを作るのは Matching シーンなので Main に置いていては間に合わない。**ルートオブジェクトに置く**こと（NGO は親を持つ `NetworkManager` を許さない）。
- **自分で `StartHost()` / `StartClient()` を呼ばない**。SDK が起動済みの状態でもう一度呼ぶと接続が壊れる。`NetworkSessionStartup` は `CustomMessagingManager != null` と `IsListening`（ホスト）/ `IsConnectedClient`（ゲスト）が揃うのを**待つだけ**にする。
- **自分で `NetworkManager.Shutdown()` を呼ばない**。SDK も `"Do not call NetworkManager.Shutdown() when using a session. Use ISession.LeaveAsync instead."` と警告する。停止したいときは `ISession.LeaveAsync()`（＝`GameSessionModel.LeaveCurrentSessionAsync()`）を呼ぶ。
- **ゲームを抜けるときは必ずセッションを離脱する**。NGO の寿命がシーンから切り離されたので、離脱を忘れると「Main を出たのにルームに残っていて、一人用で遊んでいる間も Relay に繋がったまま」になる。本プロジェクトでは `BoardPresenter.ReturnHomeAsync`（ホームに戻る）と `GameSessionModel.SetSinglePlayer`（一人用モード選択）で離脱している。
- **`EnableSceneManagement` は切ったままにする**（セクション 1）。`NetworkManager` が常駐すると `MatchingService.DisableNgoSceneManagement()` が実際に効くようになるが、Common シーンのアセット側でも既定を `false` にしてある。

### 事前セットアップ

Unity Dashboard で **Relay サービスをプロジェクトに追加**しておくこと（Lobby だけでは足りない）。未追加だと `CreateSessionAsync` が `SessionException` を投げ、`MatchingFlow` が `MatchingState.Error` にしてログへ出す。

ダッシュボードのサービス一覧で Relay が「設定中」に居れば追加済みで、そのまま使える（「アクティブ」/「非アクティブ」は**過去 30 日に実際に使ったか**の区分なので、初めて Relay 経由で繋ぐまでは「設定中」のままで正常）。

### 落とし穴：Relay のプロトコルと `UnityTransport` のドライバが食い違う

**症状**（ルーム作成が `SessionException: [Error: NetworkManagerStartFailed]` で失敗する）:

```
Relay server data indicates usage of WebSockets, but "Use WebSockets" checkbox isn't checked under "Unity Transport" component.
Relay is configured to use WebSockets, but NetworkDriver uses UDP.
ArgumentException: Mismatched Relay configuration and network interface.
SessionException: [Error: NetworkManagerStartFailed] [Message: Failed to start NetworkManager: Object reference not set to an instance of an object]
```

**原因**: SDK は `RelayProtocol.Default` で Relay を確保するが、これは **`#if UNITY_WEBGL` で分岐**していて **WebGL は WSS（WebSocket）・それ以外は DTLS（UDP）**になる。一方 `UnityTransport` は `Use WebSockets` チェックボックスの状態でドライバを作る。**ビルドターゲットを WebGL に切り替えるとプロトコルだけが WSS に変わり、チェックボックスは UDP のまま**なので食い違う（逆にチェックだけ入れると今度は Standalone で壊れる）。

**対処**: インスペクタの設定に頼らず、セッション作成・参加の直前に SDK と同じ条件でそろえる（`MatchingService.AlignTransportToRelayProtocol`）。

```csharp
UnityTransport transport = NetworkManager.Singleton?.GetComponent<UnityTransport>();
if (transport == null) { return; }
#if UNITY_WEBGL
transport.UseWebSockets = true;   // RelayProtocol.Default == WSS
#else
transport.UseWebSockets = false;  // RelayProtocol.Default == DTLS
#endif
```

> `UNITY_WEBGL` はエディタでも**アクティブなビルドターゲット**で定義が決まるので、エディタ再生（MPM 含む）でもこの分岐で一致する。

なお WebGL ターゲットでは `Could not do Qos region selection. Will use default.`（QoS SDK が WebGL 非対応）の警告も出るが、これは**リージョン選択が既定にフォールバックするだけの警告**で接続は失敗しない。オンラインの動作確認は Windows / Mac ターゲットで行うのが確実。

### 参加側は何も足さなくてよい

`WithRelayNetwork()` は**作成側の `SessionOptions` にだけ**付ける。ゲストはセッションのネットワークメタデータ（join code）を読んで自動で Relay に参加するので、`JoinSessionByIdAsync` はそのままでよい。

---

## デッキ交換プロトコルの設計メモ

> **注**: このセクションは別プロジェクト（カードゲーム）由来の一般例。本プロジェクト（すごろく）にデッキ交換はないが、「ホスト⇔クライアントの初期ハンドシェイク設計」の参考として残している。

NGS_ClientReady ハンドシェイクを入れた理由は「ホストがリクエストを送るタイミングをクライアントのハンドラ登録完了に同期させるため」。

```
ホスト                              クライアント
  ├─ k_ClientReady 登録             ├─ k_RequestDeck 登録
  ├─ k_DeckSubmit  登録             ├─ k_InitialState 登録
  └─ 待機                           └─ NGS_ClientReady をリトライ送信
                                          ↓（200ms ごと）
  ← NGS_ClientReady 受信
  ├─ NGS_RequestDeck 送信 ─────────→ 受信・送信ループ停止
  ←──────────── NGS_DeckSubmit 受信
  ├─ シャッフル・手札決定
  └─ NGS_InitialState 送信 ────────→ 受信・ゲーム開始
```

メッセージは `JsonUtility` + `FastBufferWriter.WriteValueSafe(string)` で送受信する。JSON サイズを過小見積もりするとバッファ不足になるため、`json.Length * 2 + 8` でバッファを確保する（Unicode 文字の最大バイト数を考慮）。
