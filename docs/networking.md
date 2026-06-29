# NGO + MPM ネットワーク実装ノウハウ

Unity 6 + NGO (Netcode for GameObjects) + UGS Multiplayer Services + MPM (Multiplayer Play Mode) の組み合わせで発生したハマりポイントと解決策。

---

## テンプレート適用状況

| # | 問題 | 適用先 | 済み |
|---|---|---|---|
| 1 | NGO が Common シーンを破壊 | `MatchingService.CreateRoomAsync` / `JoinRoomAsync` | ✅ |
| 2 | MPM で VContainer 親スコープが見つからない | `SceneExtensions.BuildLifetimeScopes` | ✅ |
| 3 | MPM でロード済みシーンへの遷移が壊れる | `SceneTransitioner.Transit` | ✅ |
| 4 | `CustomMessagingManager` が null | Main シーン実装時に適用 | ⬜ |
| 5 | `IsConnectedClient=true` でもメッセージが届かない | Main シーン実装時に適用 | ⬜ |
| 6 | `PlayerJoined` イベントの競合 | `MatchingService.WaitForPlayerAsync` | ✅ |
| 7 | MPM でフォーカスを失った画面の BGM・時間が止まる | `ProjectSettings` の `runInBackground` | ✅ |
| 8 | 遅延ハンドラ登録によるメッセージロスト（恒久対策） | Main シーン実装時に適用 | ⬜ |

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

**Main シーン実装時に適用**（テンプレートへの組み込み不要）

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

**Main シーン実装時に適用**（テンプレートへの組み込み不要）

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

### 6. `PlayerJoined` イベントの競合

**適用先**: `MatchingService.WaitForPlayerAsync`

**症状**: クライアントがルーム参加を完了した後にホストが `WaitForPlayerAsync` を呼ぶと、イベントが既に発火済みで永久に待ち続ける。

**原因**: `CreateRoomAsync` が返った直後にクライアントが参加した場合、`session.PlayerJoined` への登録前にイベントが発火して失われる。

**対処**: ハンドラを先に登録してから `AvailableSlots` で既に埋まっていないかを確認する。「登録 → 状態確認」の順を守ることで競合ウィンドウを狭める。さらに、待機ループ中も `AvailableSlots` を定期ポーリング（500ms 間隔）して、イベント取りこぼしの保険にする。

```csharp
session.PlayerJoined += OnPlayerJoined;  // 先に登録

if (session.AvailableSlots == 0)         // 後から確認
{
    session.PlayerJoined -= OnPlayerJoined;
    return true;  // 既に参加済み
}

// 待機ループ: イベント発火フラグ or AvailableSlots==0 のどちらかで成立
while (true)
{
    linked.Token.ThrowIfCancellationRequested();
    if (joined || session.AvailableSlots == 0)
    {
        session.PlayerJoined -= OnPlayerJoined;
        return true;
    }
    await UniTask.Delay(TimeSpan.FromMilliseconds(500), cancellationToken: linked.Token);
}
```

「登録 → 状態確認」の一度きりチェックだけでは、ハンドラ登録の直前に相手が参加した狭い窓を取りこぼし得る。待機ループ内でも `AvailableSlots` を監視し続けることで、イベントが来なくても参加を検知できる。

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

## デッキ交換プロトコルの設計メモ

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
