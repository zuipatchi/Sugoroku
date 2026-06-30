# 実装パターン集

よく触る実装パターンのレシピ。新機能を追加するときはここを起点にする。

---

## 1. 新しい Presenter を追加する（シーン単位）

### 手順

**① Presenter クラスを作る**

```csharp
// IAsyncStartable を実装してエントリポイントにする場合
public sealed class YourPresenter : IAsyncStartable, IDisposable
{
    public async UniTask StartAsync(CancellationToken ct)
    {
        try { /* 初期化・購読 */ }
        catch (OperationCanceledException) { }
    }

    public void Dispose() { /* 購読解除など */ }
}
```

MonoBehaviour として配置する場合は `RegisterComponentInHierarchy<YourPresenter>()` を使う。

**② LifetimeScope に登録する**

対象シーンの `LifetimeScope`（例: `Assets/Scripts/Main/Injector/MainLifetimeScope.cs`）の `Configure` に追加:

```csharp
// 純粋 C# クラス（エントリポイント）
builder.RegisterEntryPoint<YourPresenter>().AsSelf();

// MonoBehaviour（シーン内に配置済み）
builder.RegisterComponentInHierarchy<YourPresenter>().AsSelf().AsImplementedInterfaces();

// 依存を注入するだけで自動起動不要な場合
builder.Register<YourService>(Lifetime.Scoped);
```

> シーン起動時の初期化を MonoBehaviour の `Start()` で書かないこと。インジェクション完了前に呼ばれるため。`IAsyncStartable.StartAsync()` か `[Inject] Construct(...)` を使う（[architecture.md](architecture.md)「MonoBehaviour のインジェクションタイミング」）。

---

## 2. async MonoBehaviour での destroyCancellationToken の扱い（Unity 6）

Unity 6 では `destroyCancellationToken` を **一度も参照しないまま MonoBehaviour が破棄される** と
`MissingReferenceException` が発生する（"DestroyCancellation token should be called atleast once before destroying the monobehaviour object"）。

### 対処パターン

async メソッド内で最初の `await` の後に `destroyCancellationToken` を参照する場合、
`await` 中に MonoBehaviour が破棄されると例外が出る。以下の2点を必ず守る:

**① `await` の直後に `this == null` ガードを入れる**

```csharp
private async UniTaskVoid BuildAsync()
{
    try
    {
        await _someTask;

        if (this == null) { return; }   // ← await 後は必ずガード

        CancellationToken ct = destroyCancellationToken;  // ← ガード後に一度だけキャプチャ
        // 以降は ct を使う
    }
    catch (OperationCanceledException) { }
}
```

**② キャプチャした `ct` を以降のすべての箇所で使う**

メソッド内で `destroyCancellationToken` を直接参照するのは最初のキャプチャ時のみ。
`CancellationTokenSource.CreateLinkedTokenSource` や他のメソッドへの引数も `ct` を渡す。

---

## 3. DOTween + UI Toolkit でのスタイル値ゲッター（フリーズ対策）

UI Toolkit のスタイルプロパティを DOTween ゲッターに直接渡すと、シーケンス開始フレームでの
値読み取りが不定になり `OnComplete` が発火しないケースがある。

### NG パターン

```csharp
DOTween.To(() => _overlay.style.opacity.value, v => _overlay.style.opacity = v, 1f, 0.25f)
```

スタイルプロパティの `.value` を毎フレーム読み取るため、前フレームの状態に依存して初期値が不正になることがある。

### OK パターン（ローカル float 変数）

```csharp
float opacity = 0f;
DOTween.To(
    () => opacity,
    v => { opacity = v; _overlay.style.opacity = v; },
    1f, 0.25f
)
```

ローカル float 変数を「仲介」として使うことで初期値が確定し、`OnComplete` が確実に発火する。
`TransitionPresenter`（フェード演出）はこのパターンで実装済み。同様の Tween を新たに書く場合も必ずこの形式を使う。

> あわせて、フェードの Tween には `.OnKill(() => tcs.TrySetResult())` を付ける。途中で `Kill()` されたとき `OnComplete` は呼ばれないため、`await` している `UniTaskCompletionSource` を `OnKill` でも完了させないとデッドロックする（シーン破棄・連続遷移で発生）。

---

## 4. シーン表示前に非同期初期化を待つ（ISceneReady）

Addressables ロードやネットワーク初期化など「フェードイン前に終わらせたい処理」がある場合、
そのシーンの Presenter（や任意の MonoBehaviour）に `ISceneReady` を実装する。
`SceneTransitioner` がフェードイン前に、次シーン内の **全** `ISceneReady` 実装の `ReadyAsync` を
`UniTask.WhenAll` で待機する（実装が無いシーンは素通り）。

```csharp
public sealed class YourPresenter : IAsyncStartable, ISceneReady
{
    private readonly UniTaskCompletionSource _ready = new();

    public async UniTask StartAsync(CancellationToken ct)
    {
        try
        {
            await LoadAssetsAsync(ct);   // 表示前に終わらせたい初期化
            _ready.TrySetResult();        // 完了を通知 → フェードイン開始
        }
        catch (OperationCanceledException) { }
    }

    // SceneTransitioner がフェードイン前にこれを await する
    public UniTask ReadyAsync(CancellationToken ct) => _ready.Task.AttachExternalCancellation(ct);
}
```

> `ReadyAsync` がキャンセル以外の例外を投げても、`SceneTransitioner` 側でログ出力して握りつぶしフェードインは継続する（暗幕が残らない）。初期化失敗の扱いは `ReadyAsync` 内で完結させること。

> **落とし穴: 直接起動されるシーンでは `ReadyAsync` が呼ばれない。** `ReadyAsync` を呼ぶのは `SceneTransitioner.Transit` だけ。`Title` のように `BootLoader` の素の `LoadSceneAsync`（やエディタで直接 Play）で開かれるシーンは「遷移」が発生しないため `ReadyAsync` が一度も呼ばれず、`ReadyAsync` 内だけで初期化していると**初回だけ動かない**（他シーンから戻ると `Transit` 経由で動く）。直接起動もあり得るシーンでは、`Start` でも初期化を起動し、`ReadyAsync` ではその完了を待つだけにする（初期化はフラグで一度きり）。`TitleVideoPresenter` がこの形:

```csharp
private UniTask _initTask;
private bool _initStarted;

private void Start() => EnsureInitStarted();                 // 直接起動でも初期化する
public async UniTask ReadyAsync(CancellationToken ct)         // 遷移時は完了を待ってフェードイン
{
    EnsureInitStarted();
    await _initTask.AttachExternalCancellation(ct);
}
private void EnsureInitStarted()
{
    if (_initStarted) { return; }
    _initStarted = true;
    _initTask = InitializeAsync(destroyCancellationToken).Preserve();  // fire-and-forget でも await でも安全
}
```

---

## 5. DOTween と R3 を同じファイルで使うと `.AddTo(CancellationToken)` が壊れる

`using DG.Tweening;` と `using R3;` を**同じファイルで併用**すると、`.AddTo(destroyCancellationToken)` が
`error CS1620: Argument 2 must be passed with the 'ref' keyword` でコンパイル失敗する。
DOTween.dll がグローバルな `AddTo<T>(this T, ...)` 拡張メソッドを公開していて、R3 の
`AddTo(this IDisposable, CancellationToken)` よりそちらに解決されてしまうため（DOTween を import しない
ファイルでは正常に通る）。

### 対処：`CompositeDisposable` のインスタンスメソッド `Add()` で購読を管理する

```csharp
private readonly CompositeDisposable _disposables = new();

// 拡張メソッドではなくインスタンスメソッドなので衝突しない
_disposables.Add(_model.State.Subscribe(ApplyState));

private void OnDestroy() => _disposables.Dispose();
```

インスタンスメソッドは拡張メソッドより優先される。`SoundPlayer` と同じ方式。実例は
[RoulettePresenter.cs](../Assets/Scripts/Main/Roulette/RoulettePresenter.cs)。

---

## 6. `RegisterComponentInHierarchy<T>` はシーン内に有効な GameObject が必須

`builder.RegisterComponentInHierarchy<T>()` は LifetimeScope 構築時にシーン内を検索し、
**対象が無い／GameObject が無効だと `VContainerException: T is not in this scene` で構築ごと失敗する**
（`AsSelf()` だけでもビルド時に解決されるため、依存元が無くても例外になる）。

- 新しい MonoBehaviour Presenter を `RegisterComponentInHierarchy` で登録したら、**対象シーンに
  その component を持つ有効な GameObject を必ず配置**する（UI Toolkit なら UIDocument に
  Panel Settings と Source Asset(uxml) を割り当てた GameObject）
- 配置先が `Common` ではなく対象シーン直下であること、GameObject が有効（チェック ON）であることを確認する

---

## 7. 同一シーンに複数の UIDocument を重ねるときは Sorting Order でイベントを整理する

UI Toolkit のポインタイベントは **Sorting Order が最も高いパネルから順に**ヒットテストされる。フルスクリーンの UIDocument を複数重ねると、上のパネルのルートが全面を覆ってイベントを奪い、下のパネルのボタンが**ホバーもクリックも反応しなくなる**（描画は見えているのに無反応）。

- 上に乗せたいパネル（例: ミニゲーム起動ボタン）の UIDocument の **Sorting Order を、奪っている側より大きく**する（Main では Board=0 / Roulette=10 なので、トリガーは 20 にした）
- そのパネルのルート要素は `picking-mode="Ignore"` にし、**ボタン等の操作要素だけがイベントを拾う**ようにする。これで「ボタン以外は下のパネルへ素通り」になり、共存できる
- 参考の Sorting Order: Transition=2000 / Option=1000 / MiniGame シーン=100。新しい前面 UI はこれらと衝突しない値にする

---

## 8. 新しいミニゲームを追加する

ミニゲームは `MiniGame` シーンを Main の上に Additive で重ねて動かす（`Transit` は使わない。詳細は [architecture.md](architecture.md)「シーン構成」）。新しい種類を足す手順:

1. [MiniGameId.cs](../Assets/Scripts/Common/MiniGame/MiniGameId.cs) に種別を追加する（最大5種類想定）
2. その種別の UI を `Assets/AddressableAssets/MiniGame/` に `.uxml` / `.uss` で作り、**Addressable アドレスを `MiniGame/<名前>`** に設定する
3. [MiniGameHostPresenter.cs](../Assets/Scripts/MiniGame/MiniGameHostPresenter.cs) の `AddressFor` に分岐を足し、進行ロジック（カウントダウン→計測→結果）を実装する。状態は純粋ロジックの Model（[TapGameModel.cs](../Assets/Scripts/MiniGame/TapGame/TapGameModel.cs) に倣う）に分け、EditMode テストを書く
4. 起動は `MiniGameLauncher.PlayAsync(MiniGameId.<種別>, ct)`。結果は `MiniGameResult.Score` で受け取り、呼び出し側（例: [MiniGameTriggerPresenter.cs](../Assets/Scripts/Main/MiniGameTriggerPresenter.cs)）で盤面反映などを行う
5. ホストは表示前に UXML をロードするため `ISceneReady` を実装している（ロード完了まで暗幕を維持）。`Report` で結果を返すとランチャーがシーンをアンロードする

> ローカル完結のため、現状「勝者」はしきい値で暫定判定している。全員同時プレイのスコア同期は今後の課題（[networking.md](networking.md) の永続ハンドラ方式に乗せる）。

---

## 9. Button で「押し続け／離す」を取りたいときはトリクルダウンで登録する

UI Toolkit の `Button` は内部に `Clickable` マニピュレータを持ち、`PointerDownEvent` を処理した後に **`StopImmediatePropagation()` を呼ぶ**。`Clickable` は Button 生成時にバブリング段階へ登録済みなので、後から同じ `Button` に `RegisterCallback<PointerDownEvent>` を**バブリング段階（既定）で**足しても、Clickable に伝播を止められて**呼ばれない**（`clicked` は Clickable 自身のイベントなので動くため、原因が分かりにくい）。

押し続け中だけ処理したい・押下と離しを別々に扱いたい場合（例: ルーレットの長押し回転 [RoulettePresenter.cs](../Assets/Scripts/Main/Roulette/RoulettePresenter.cs)）は、**トリクルダウン（キャプチャ）段階で登録**して Clickable より先に走らせる。

```csharp
// Clickable より先に実行させる。Unregister も同じ TrickleDown を渡す。
button.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
button.RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
```

- **ポインタ捕捉は Clickable に任せる**（`CapturePointer` を自前で呼ばない）。Clickable が押下時に捕捉するため、**ボタン外で指を離しても `PointerUp` は届く**
- 回転・連続処理の最中はボタンを `SetEnabled(false)` に**しない**（無効化すると押下中の `PointerUp` を受け取れない）。再入のガードは状態（`RouletteState.Spinning` など）でチェックする
- 保険として `PointerCaptureOutEvent` も購読しておくと、何らかの理由で捕捉が外れたときに「離した」扱いへフォールバックできる

---

## 共通ルール（抜粋）

- `var` は使わない。型を明示する
- フィールドは `_camelCase`、型・メソッドは `PascalCase`
- `Find()` / static 状態は使わない。DI で解決する
- UI は UXML + USS で構築。uGUI 禁止
- アセットロードは Addressables。`Resources.Load` 禁止
- USS では `gap` 禁止 → 子要素の `margin` で代替
