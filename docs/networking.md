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
| `SpinStart` | （なし） | 手番の人 |
| `Spin` | 停止セクター index ＋ 減速時間（ミリ秒） | 手番の人 |
| `MoneyLanding` | 所持金の増減額（符号付き） | 着地した人 |
| `MoveLanding` | 進む/戻るで続けて動くマス数（進む＝正・戻る＝負） | 着地した人 |
| `ShopResult` | 買ったアイテム（負値＝買わなかった） | 着地した人 |
| `MiniGameLanding` | ミニゲームの内容を組み立てる種 | 着地した人 |
| `MiniGameScore` | ミニゲームの自分の結果値 | 参加者全員（各自 1 通） |
| `ItemUse` | アイテム＋効果パラメータ | 使用者 |
| `Busy` | 待機理由（`BusyReason`・`None`＝解除） | 待たせる操作をする人 |
| `Leave` | （なし） | 自分から抜ける人／ホスト（中継・猶予切れ） |

**送らないもの**: コマ移動・陣地占拠・勝敗判定は「誰が何マス進むか」と盤面データから決定論的に導けるので配らない。ルーレットの停止セクターも、`(進む人, 出目)` との割り当てが 1 対 1（`RouletteMath.ParticipantForSector` / `StepsForSector`）なので **1 つの整数だけ**で足りる。

### 制御メッセージ（ストリームには載せない）

一時停止と復帰は「盤面を進める決定」ではないので、**アクションストリームとは別のチャンネル**（`SGRK_Control`）で送って受信側が即座に処理する。進行の待ち受け（`GameFlowController.WaitForSpinAsync` / `BoardPresenter.WaitForActionAsync`）と順序を取り合わないので、待ち受け側のコードを一切増やさずに済む。

| 種別 | ペイロード | 送る人 |
|---|---|---|
| `Pause` | 復帰を待つ猶予（秒） | 切断を検知したホスト |
| `Resume` | （なし） | ホスト（差分を送り終えた後） |
| `Resync` | 受信済みの最後の通し番号 | 復帰したクライアント |

`Resync` は初回接続時にも送る。まだ何も受け取っていないので送り直しは起きず、**ホストが「この NGO クライアント id はこの席」と覚えるための挨拶**として働く（切断時に誰が落ちたかを名前で出すのに使う）。

### ルーレットは「回り終わってから」ではなく 2 段で配る

結果（`Spin`）だけを円盤が止まってから配ると、相手が回している数秒間こちらの画面は無反応で、結果もそのぶん遅れて出る。そこでスピンは **2 段**で配る。

| タイミング | 配るもの | 受信側の動き |
|---|---|---|
| 手番の人が押した（円盤が回り始めた） | `SpinStart` | `RoulettePresenter.BeginRemoteSpin()` で自分の円盤も回し始め、`Spin` が届くまで回し続ける |
| 手番の人が指を離した（**まだ回っている**） | `Spin`（セクター＋減速時間） | `RoulettePresenter.PlaySpinToAsync(sector, stopSeconds)` でそのまま減速へ入り、同じセクターの中心で止まる |

離した瞬間に停止位置を確定できるのは、ease-out 減速で進む角度が「離した瞬間の速度 × 停止時間 ÷ 3」に定まるため（`RouletteSpinPhysics.PredictStopRotation`）。その予測角をいちばん近いセクター中心へ寄せ（`RouletteMath.NearestRotationForSectorCenter`）、`RouletteSpinPhysics.ReleaseTo` で目標角ちょうどに止めるので、**先に配った結果と実際に止まる位置が必ず一致する**。減速時間も一緒に配ることで、全員の円盤がほぼ同時に止まる。

`SpinStart` を取りこぼした場合（着地待ちに割り込んだ等）でも `PlaySpinToAsync` が自分で回し始めてから止めるので、結果がずれることはない（演出が遅れるだけ）。

### 決定と適用を分ける

乱数・モーダル操作を含む処理は **「決定（1 人だけ）」と「適用（全員）」に分ける**。

| 処理 | 決定 | 適用 |
|---|---|---|
| お金マスの増減額 | `MoneyCellRule.Amount` を着地者だけが引く | 受信した額を `MoneyModel.Add` |
| 進む/戻るのマス数 | `MoveCellRule.Steps` を着地者だけが引く | 受信したマス数ぶん連鎖して動く（浮遊テキストも同じ値） |
| アイテムショップ | 着地者だけがラインナップ抽選＋モーダル選択 | 代金支払い＋`ItemModel.Add`＋購入の演出（代金の浮遊テキスト・自分以外の購入はアイテム絵と帯） |
| ミニゲーム | 着地／使用者だけが「遊ぶゲーム」と「内容の種」を決める | **全員が同じ内容を遊び**、各自の結果値を持ち寄って勝者へ `MoneyModel.Add` |
| アイテム効果 | 使用者だけが対象マス・奪取額・ミニゲーム結果を決める | `ItemModel.Use` ＋共通の使用演出（誰が何を使ったかの中央ポップ＋帯）＋効果の反映（**演出は画面の持ち主から見た向きで出す**＝お金よこどりなら使用者に「+ 合計」・奪われた席に「− 失った額」＋帯・無関係な席には出さない） |

**アイテムの消費（`ItemModel.Use`）は適用側で行う**。キャンセルされた使用は発行されないので、そもそも消費されない。

### 待たせる操作は「待っている」ことを見せる

モーダル操作やミニゲームは決める人の手元で数秒〜数十秒かかる。その間、他のクライアントは次のアクションを待つだけなので、**画面が固まったように見える**。そこで待たせる側が「いま何をしているか」を配り、待つ側は待機表示（`WaitingBanner`＝「〔キャラ名〕が◯◯中…」）を出す。

| 待つ対象 | 知らせ方 | 表示 |
|---|---|---|
| 相手がルーレットを回すまで | **配らない**（手番とルーレット状態はどちらも全員が持っているので導ける） | 「〇〇のルーレット待ち…」 |
| アイテムショップ | **配らない**（着地したマスは全員が同じデータから導けるので、非決定者が自分で表示する） | 「〇〇が買い物中…」 |
| ミニゲームの結果集め | **配らない**（全員が同時に遊ぶので待つ相手は 1 人に定まらない） | 「他のプレイヤーの結果を待っています…」 |
| ミニゲーム（アイテム効果） | `Busy(MiniGame)` を選択モーダルを開く前に配る | 「〇〇がミニゲーム中…」 |
| 陣地獲得のマス選択（アイテム効果） | `Busy(TerritorySelect)` を選択開始時に配る | 「〇〇が陣地を選んでいます…」 |

**円盤は回している間しか表示されない**ので、相手の手番が始まってから相手が押すまでは画面に動きが無い。ここは `TurnModel.CurrentPlayer` と `RouletteState` の購読だけで導けるため配らずローカルで出す（相手が押す＝`SpinStart` を受けて自分の円盤も回り出した時点で消える）。表示の優先度は **`Busy` ＞ ルーレット待ち**で、コマ移動中・着地演出中・アイテム効果の演出中・決着後は出さない（画面が動いているので待たされている感が無い）。

`Busy` は**盤面を進めないお知らせ**なので、受信側は表示を切り替えて次のアクションを待ち続ける（`BoardPresenter.ApplyActionAsync` が扱い、`GameFlowController.WaitForSpinAsync` / `BoardPresenter.WaitForActionAsync` の両方から流れてくる）。解除は次の 2 通り。

- **成功した**: 結果のアクション（`ItemUse` など）が届く。`Busy` 以外のアクションを受け取ったら待機表示は消す（＝待っていた操作が済んだ）ので、解除を別途配る必要はない
- **キャンセルされた**: 結果が発行されないので、決めた人が `Busy(None)` を配って消す

自分が決める席の `Busy` は無視する（待たされる側ではない）。一人用モードは全席をローカルが決めるので、待機表示は出ない。

### 見た目だけの情報はストリームに載せない

タップ連打の「相手のいまの連打数」・2Dレースの「相手のいまの位置」のような**進行を進めない情報**は、アクションストリームではなく専用の名前付きメッセージ（`SGRK_MiniGameProgress`）で流す。理由は 2 つ。

- **ミニゲーム中は誰もストリームを読んでいない**（`MiniGameLauncher.PlayAsync` を await 中）。載せてもバッファに溜まるだけで、遊んでいる間には届かない
- **順序保証も再送も要らない**。取りこぼしても次の値が 200ms 後に来る

送受信は `OnlineGameSync.PublishProgress` / `ProgressReceived` で、ゲスト→ホスト→残り全員の中継はアクションと同じ形（ゲスト同士は繋がっていないため）。送信失敗は表示が一瞬遅れるだけなので黙って捨てる。

ミニゲームシーンは別スコープで `OnlineGameSync` を注入できないので、`MiniGameProgressChannel`（送信関数＋参加者ごとの最新値の配列）を `MiniGameSessionModel` 経由で渡し、**ゲーム側は配列を毎フレーム読むだけ**にする。イベント購読より取りこぼしに強く、受信スレッドを気にしなくてよい。

整数しか運べない経路なので、**小数の値は倍率をかけて送る**（2Dレースの進捗 0〜1 は `RaceGamePlay.ProgressScale`＝10000 倍）。また 200ms 間隔でしか届かないため、**そのまま位置に反映するとカクついて見える**。連打数のような数値表示はそのままでよいが、**位置を動かすものは表示側で目標へ寄せて滑らかにする**（`RaceGamePlay.UpdateRunnerPositions`＝自分の走者は Model の値そのまま、相手だけ指数補間）。

**0 は「まだ届いていない」を意味する**（配列の初期値）ので、0 が正当な値になりうるものは下駄を履かせて送る。被っちゃやーよは選んだカード index（0 始まり・無効票は -1）を `OverlapGamePlay.ChoiceValueOffset`＝2 だけずらして送り、受信側は 0 以下を「未着」として扱う。

**相手がまだ受信を始めていない時期に配ったぶんは届かない**（ハンドラの登録は `MiniGameLauncher.PlayAsync` の直前で、シーンのロードやフェードのぶんクライアント間にずれがある）。ポーリング前提の経路なので、**揃うまで同じ値を配り続ける**のが対処になる（2Dレースは 200ms ごとに現在位置を送り続け、被っちゃやーよは選択が全員ぶん揃うまで自分の選択を `PublishIntervalSeconds` 間隔で送り直す）。

**届いた値でゲームの決着を左右しない**のも大事。2Dレースは相手の進捗を表示にだけ使い、決着（`RaceGameModel.ResolveFinish`）は自分のゴールだけで判定する（相手の到達で自分のレースを打ち切ると、まだ走っている途中なのに未ゴール扱いで結果値を出すことになる）。順位はあくまで各自のゴールタイムを持ち寄って決める（次項）。被っちゃやーよの開示も同じで、届いた選択はバッジ表示に使うだけ。届かないまま `OverlapGamePlay.OpponentWaitSeconds` で打ち切ったときは「かぶらなかった」と断定せず結果の発表を盤面側へ委ねる（**逆に「誰かと被った」は 1 人ぶん届けば確定する**ので、揃うのを待たずに負けを見せてよい）。

### 勝敗は「値を持ち寄って全員が同じ関数にかける」

ミニゲームの勝者はホストが決めない。各自が自分の結果値（連打数・ゴールタイム・選んだカード index）を `MiniGameScore` で出し、全員ぶんが揃ったら純粋関数 `MiniGameRanking.Resolve` にかける。入力が同じなら出力も同じなので、**判定役を置かなくても全クライアントが同じ勝者に至る**（EditMode テストで固められるのも利点）。

ゲームを起動できなかった場合でも `MiniGameRanking.WorstValue` を必ず配る。黙って抜けると、結果を待っている他のクライアントが永久に進めなくなる。

### ストリームを待てるのは同時に 1 箇所だけ

`ActionStream.NextAsync` を同時に 2 箇所から待つと `InvalidOperationException` になる。進行は次のように所有権を受け渡す設計になっている。

- 手番の待機（`GameFlowController.WaitForSpinAsync`）— `SpinStart` は円盤を回し始めて、アイテム使用が割り込んで来たら `BoardPresenter.ApplyActionAsync` へ流して、それぞれ待ち続ける
- 着地の待機（`BoardPresenter.WaitForActionAsync`）— `GameFlowController` は `AdvanceAsync` を待っている間ストリームを触らない

アイテムは「自分の手番かつルーレット未回転（`RouletteState.Idle`）」のときしか使えないので、着地の待機中にアイテム使用が発生することは通常ない。

### 切断 — まず一時停止して復帰を待つ

`NetworkManager.OnClientDisconnectCallback` を `OnlineGameSync` が監視する。切断は**即座に打ち切らず、まず `SessionReconnector.GraceSeconds`（60 秒）だけ復帰を待つ**。

| 誰が | 何をする |
|---|---|
| ホスト（ゲストの切断を検知） | `Pause(席)` を全員へ配って猶予タイマーを回す。戻れば `Resume`、猶予切れなら `Leave` |
| 切れた本人 | `SessionReconnector` でセッションへ入り直し、`Resync` で取りこぼしを送り直してもらう |
| 残りの全員 | 入力（スピン・アイテム）を閉じて待機バナーを出し、次のアクションを待ち続ける |

猶予切れ・復帰失敗なら従来どおり `SessionLost` が立ち、待機中の `NextAsync` がキャンセルされて進行が止まり、`BoardPresenter` が「相手が退出しました」と「ホームに戻る」を出す。ゲスト同士には切断が伝わらない（NGO はクライアント同士を繋がない）ため、`Leave` の配布・中継はホストが担う。

#### 復帰は「盤面の再送」ではなく「取りこぼした決定の再送」

ホストは再配信するアクションに通し番号（`GameAction.Seq`）を振って `ActionLog` に残す。復帰したクライアントは受信済みの最後の番号を `Resync` で申告し、ホストは `ActionLog.Since(seq)` で**それ以降だけ**を送り直してから `Resume` を配る。

- 盤面・所持金・アイテム・陣地をまるごとシリアライズする**スナップショットが要らない**。適用は通常と同じ受信経路（`ActionStream`）を通るので、演出や消費のコードパスを二重に持たずに済む
- 通し番号は**二重適用の防止**にも使う（`OnlineGameSync.Accept` が受信済みの番号以下を捨てる）
- 前提は「アプリが生きている」こと。ローカルの Model が残っているから差分だけで追いつける。アプリを落とした場合は復帰できない（スナップショットが別途必要）
- 切断中に自分が発行したアクションは送信キューへ積み、復帰後に発行順で流す（ミニゲームの結果値を取りこぼさないため）
- ホストが切れた場合は Relay の割り当てごと消えるので、たいてい復帰できない（猶予切れと同じく終了に倒れる）

#### 自分から抜けるときの扱い（通信断と区別する）

`ISession.LeaveAsync()` は NGO も閉じるので、**自分で抜けたときも自分の `OnClientDisconnectCallback` が発火する**。通信断と同じ扱いにすると「自分の画面に退出通知が出る」「相手が 60 秒も復帰を待つ」「自分が再接続を試し始める」といった誤動作になるので、両者を区別する。

| 見分け方 | 使う場所 |
|---|---|
| **相手へ**: 接続を閉じる前に `Leave` を送る | `GameSessionModel.LeaveCurrentSessionAsync` が `Leaving` イベントを発火 → `OnlineGameSync` が送信（ホストは残りへ中継）。オプションの「タイトルへ戻る」のように Common 側から抜ける経路もこれで拾える |
| **自分側**: `GameSessionModel.HasSession` が false か | 離脱は await の前に `Session` を手放すので、`HandleSessionLost` はこれで自発的な離脱と判定できる。`SessionLost` を立てず、待機中の進行を打ち切る（`_abortCts.Cancel()`）だけにする。再接続も始めない |

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
- **ゲームを抜けるときは必ずセッションを離脱する**。NGO の寿命がシーンから切り離されたので、離脱を忘れると「Main を出たのにルームに残っていて、一人用で遊んでいる間も Relay に繋がったまま」になる。相手からは在席して見えるので、退出に気づけないまま待たされ続ける。本プロジェクトでは `BoardPresenter.ReturnHomeAsync`（ホームに戻る）・`OptionPresenter.BackToTitleAsync`（オプションの「タイトルへ戻る」）・`GameSessionModel.SetSinglePlayer`（一人用モード選択）で離脱している。**とくにオプションアイコンは Common 常駐でマッチング中・キャラ選択ロビー・対戦中のどこからでも押せる**ので、シーンを移すだけの遷移を書かないこと。
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
