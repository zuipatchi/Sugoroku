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

## 共通ルール（抜粋）

- `var` は使わない。型を明示する
- フィールドは `_camelCase`、型・メソッドは `PascalCase`
- `Find()` / static 状態は使わない。DI で解決する
- UI は UXML + USS で構築。uGUI 禁止
- アセットロードは Addressables。`Resources.Load` 禁止
- USS では `gap` 禁止 → 子要素の `margin` で代替
