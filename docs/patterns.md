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
- **下のパネルに載っているモーダルを一時的に最前面へ出したいときは、開いている間だけそのパネルの `UIDocument.sortingOrder` を上げて閉じたら戻す。** アイテム詳細モーダルは Board パネル（Sorting=0）にあるため、回転中のルーレット（Sorting=10）が前面に来て隠れていた。`ItemModalPresenter` が開くとき Board の `sortingOrder` を 100（ルーレット/トリガより上・Option/Transition より下）へ退避付きで持ち上げ、閉じるときに元へ戻す（閉→開の遷移でだけ退避するので二重オープンでも基準値を失わない）

**逆に、下のパネルへ操作要素を足すときは、上の全画面パネルすべてのルートを `picking-mode="Ignore"` にする。** Board パネル（Sorting=0・最下層）に盤面ズームの虫眼鏡ボタンとドラッグ層を足したとき、上に乗る Roulette パネル（Sorting=10）のルートが**全画面 picking 有効**でイベントを奪っており、下の Board のボタンが無反応だった。Roulette 側の**ルートと円盤ビジュアルを `picking-mode="Ignore"`**（[Roulette.uxml](../Assets/Scripts/Main/Roulette/View/Roulette.uxml)）にし、**スピンボタンだけ操作可能**にすることで、下の Board パネルのボタン・ドラッグ層へ入力が通るようになった。「下のパネルに操作 UI を置く」場合は、それより上の全画面パネルが素通し設定になっているか必ず確認する。

---

## 8. 新しいミニゲームを追加する

ミニゲームは `MiniGame` シーンを Main（や動作確認用の `MiniGameTest`）の上に Additive で重ねて動かす（`Transit` は使わない。詳細は [architecture.md](architecture.md)「シーン構成」）。新しい種類を足す手順:

1. [MiniGameId.cs](../Assets/Scripts/Common/MiniGame/MiniGameId.cs) に種別を追加する（最大5種類想定）
2. その種別の UI を `Assets/AddressableAssets/MiniGame/` に `.uxml` / `.uss` で作り、**Addressable アドレスを `MiniGame/<名前>`** に設定する
3. [MiniGameCatalog.cs](../Assets/Scripts/Common/MiniGame/MiniGameCatalog.cs) の `All` に 1 行足す（`MiniGameId` → 表示名・UXML アドレス）。`MiniGameHostPresenter.AddressFor` はカタログ引きなので分岐追加は不要で、**動作確認用の `MiniGameTest` シーンにもボタンが自動で並ぶ**
4. 進行ロジックを実装する。状態は純粋ロジックの Model（[TapGameModel.cs](../Assets/Scripts/MiniGame/TapGame/TapGameModel.cs) / [RaceGameModel.cs](../Assets/Scripts/MiniGame/RaceGame/RaceGameModel.cs) に倣う）に分け、EditMode テストを書く。[MiniGameHostPresenter.cs](../Assets/Scripts/MiniGame/MiniGameHostPresenter.cs) は `CurrentGame` で分岐するディスパッチャなので、UI が異なるゲームは**専用の `<名前>GamePlay` クラス**（プレーンクラス。[RaceGamePlay.cs](../Assets/Scripts/MiniGame/RaceGame/RaceGamePlay.cs) 参照。`BuildAsync`＝表示前ロード／`RunAsync`＝入力待ち・進行してスコアを返す）に切り出し、ホストの `ReadyAsync` に 1 分岐足して委譲する（タップ連打も [TapGamePlay.cs](../Assets/Scripts/MiniGame/TapGame/TapGamePlay.cs) に切り出して同じ構造にしている）
5. Play クラスと Model を [MiniGameLifetimeScope.cs](../Assets/Scripts/MiniGame/Injector/MiniGameLifetimeScope.cs) に `Lifetime.Scoped` で登録する（DI が生成・破棄する。Addressables ハンドルの解放は各クラスの `Dispose` に書く）
6. 起動は `MiniGameLauncher.PlayAsync(MiniGameId.<種別>, ct)`。結果は `MiniGameResult.Score` で受け取る（意味はゲームごと。タップ連打＝タップ数、2Dレース＝勝ち1/負け0、被っちゃやーよ＝獲得1/被り・無効票0）。動作確認は `MiniGameTest` シーン（[MiniGameTestPresenter.cs](../Assets/Scripts/MiniGame/Test/MiniGameTestPresenter.cs)）をエディタで直接開いて Play する
7. ホストは表示前に UXML をロードするため `ISceneReady` を実装している（ロード完了まで暗幕を維持）。`Report` で結果を返すとランチャーがシーンをアンロードする

> ローカル完結のため、盤面反映やゲーム内トリガー（特殊マス・手番連携）はまだ Main に組み込んでいない。全員同時プレイのスコア同期も今後の課題（[networking.md](networking.md) の永続ハンドラ方式に乗せる）。

**Main のカタログ（`ItemCatalog` 等）を再利用するとき**は `MiniGame` asmdef に `Main` 参照を足す（被っちゃやーよがアイテム絵の再利用で追加済み）。`Main` は `MiniGame` を参照しないので循環しない。**参加者数に依存するミニゲーム**（被っちゃやーよは提示枚数＝参加者数）は、値をハードコードせず Config の定数（`OverlapGameConfig.DefaultPlayerCount`）に置く。MiniGame シーンは別スコープで `GameParticipants` を直接注入できないため、将来プレイヤー数を増やすときはセッション経由で供給する差し替え1点で済むようにしておく。

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
- **押し続けている間（`PointerUp` 待ち）はボタンを `SetEnabled(false)` にしない**（無効化すると押下中の `PointerUp` を受け取れない）。ただし**指を離した後（＝押下が完了した後）は無効化してよい**：捕捉中の `PointerUp` はこの後 Clickable が処理して捕捉を解放するため、`OnPointerUp` 内で同期的に `SetEnabled(false)` しても離し操作は壊れない。ルーレットは「離した瞬間に無効化して惰性回転中の再押下を防ぐ」ため、`_spinReleased` フラグを立てて `UpdateSpinEnabled` の有効条件に組み込み、次の手番開始（`SetInteractable(true)`）でクリアする（再入のガード自体は `RouletteState.Spinning` などの状態でもチェックする）
- 保険として `PointerCaptureOutEvent` も購読しておくと、何らかの理由で捕捉が外れたときに「離した」扱いへフォールバックできる

---

## 10. ターン進行など「順序のある流れ」はエントリポイントの async ループに集約する

「ルーレットが止まったらコマを進める → 移動が終わったらボタンを戻す → 次の手番へ」のような**順序のある進行**を、各 Presenter の R3 購読（`State.Subscribe(...)` で次を呼ぶ）に散らすと、手番・CPU・勝敗判定が絡んだ瞬間に「誰のコマを動かすか」「今は押していい番か」が追えなくなる。こうした流れは、`RegisterEntryPoint` で登録した純粋 C# サービス（`IAsyncStartable`）の **1 本の async ループ**に集約すると読みやすい（例: [GameFlowController.cs](../Assets/Scripts/Main/Turn/GameFlowController.cs)）。

- **状態変化の待受は R3 の `FirstAsync` を `await` する**。`ReactiveProperty` は購読時に現在値を流すため、`Where` で目的の状態だけ通し、`FirstAsync(ct)` でその瞬間まで待つ。ボタン長押しのような「ユーザー操作の完了」も、Presenter に `UniTask<int> WaitForManualSpinAsync(ct)` を生やして中で `await` すればループ側は分岐なく書ける。

```csharp
// Stopped になるまで待って、その時の出目を返す（Presenter 側）
public async UniTask<int> WaitForManualSpinAsync(CancellationToken ct)
{
    await _model.State.Where(s => s == RouletteState.Stopped).FirstAsync(ct);
    return _model.Result.CurrentValue;
}
```

- **前回の状態が残る点に注意**。`FirstAsync` は購読時の現在値も評価するので、前手番の `Stopped` を「今回の停止」と誤検知しないよう、手番の開始時に Model を `Reset()`（`Idle` へ戻す）してから待つ。
- **人間と CPU は同じ流れの分岐にする**。人間＝入力の完了を待つ、CPU＝同じ UI（円盤）をコードから回して（`AutoSpinAsync`）結果を待つ、と**入口だけ変えて後段（コマ前進・勝敗判定）は共通**にすると、演出コードを二重化せずに済む。
- **キャンセルは握る**。ループの `await` はシーン破棄でキャンセルされる。`StartAsync` 全体を `try { ... } catch (OperationCanceledException) { }` で囲む（VContainer 由来のトークンなので [#4](#4-シーン表示前に非同期初期化を待つisceneready) と同じ扱い）。
- **接続待ちを最初に置く**とオンライン/オフラインを同じループで扱える（`NetworkModel.State` が `Connected` になるまで `FirstAsync` で待ってから進行を始める。一人用は即 `Connected`）。

---

## 11. 自作コンテンツは ScriptableObject データ＋専用 EditorWindow で作れるようにする

盤面のように「非プログラマーがビジュアルに量産したいデータ」は、**データを ScriptableObject アセットにして、専用の `EditorWindow` で編集する**構成にする（例: [BoardDefinition.cs](../Assets/Scripts/Main/Board/BoardDefinition.cs) ＋ [BoardEditorWindow.cs](../Assets/Scripts/Main/Editor/BoardEditorWindow.cs)）。実行時の Presenter はこのデータを読んで描画するだけにし、計算生成はフォールバックに回す（`BoardPresenter` は選択マップも `_definition` も無いときだけ矩形リングを生成）。

- **エディタ専用コードは `Editor/` サブフォルダの Editor 専用 asmdef に分ける**。`includePlatforms: ["Editor"]` にし、`references` に対象ランタイム asmdef の GUID を並べる（**推移的に参照されないので**、`BoardDefinition`（Main）だけでなく `MiniGameId`（Common）のように編集画面で型名を出すものは Common の GUID も足す）。ビルドには含まれない。
- **ScriptableObject の編集は `Undo.RecordObject(target, "…")` → 値を変更 → `EditorUtility.SetDirty(target)`** の順で行い、保存は `AssetDatabase.SaveAssets()`（またはユーザーの Ctrl+S）に任せる。`[Serializable]` な子クラス（`BoardCellDefinition` など）を `List<T>` で持たせれば、そのリストを丸ごと Undo/シリアライズできる。
- **エディタは UI Toolkit（`CreateGUI` で構築）で書く**とランタイム同様にクラスで組める。ただし `UnityEditor.UIElements` のフィールド（`IntegerField` 等）は**ラベルの最小幅が広く、フィールド全体幅を小さく固定すると入力欄が潰れて操作できなくなる**。`field.labelElement.style.width` を絞ってから十分な幅を与える。数値入力は `isDelayed = true` にして Enter／フォーカスアウトで確定させると桁の途中で反応せず打ちやすい。
- **アセット未割り当てでも壊れないフォールバックを実行時側に持たせる**（`BoardDefinition.CreateRectangular` を `CreateInstance` で生成し、`OnDestroy` で `Destroy` する）。既存シーンは無改変で従来動作、データを割り当てたときだけ差し替わる。
- **同種の SO 資産を複数から選ばせるには「カタログ SO ＋ Common に文字列 ID」で分ける**。`CharacterCatalog` のような静的クラスは Addressable アドレス（文字列）しか持てず SO 資産参照を持てないので、資産を並べるカタログ自体も `ScriptableObject` にする（例: [BoardCatalog.cs](../Assets/Scripts/Main/Board/BoardCatalog.cs) が `List<BoardDefinition>` を持ち `All`/`Default`/`Find` を公開）。選択状態をシーンをまたいで持つ Common シングルトン（[BoardSessionModel.cs](../Assets/Scripts/Common/Board/BoardSessionModel.cs)）は、**Common から Main の SO 型（`BoardDefinition`）を参照できない**ため識別子（資産名 `Object.name`）だけを文字列で持ち、消費側（`BoardPresenter`）が `catalog.Find(id)` で実体を解決する。カタログ資産は選択シーンと消費シーンの両方の Presenter にインスペクタで割り当てる。未選択・未割り当て時は単発フォールバック（`_definition`）に落ちる。

## 12. 新しいアイテムを追加する

アイテム取得マス（`BoardCellEvent.Item`）でもらえるアイテムは、ミニゲームと同じ「enum ＋ 静的カタログ」方式で増やす。

1. [ItemId.cs](../Assets/Scripts/Main/Item/ItemId.cs) に種別を 1 つ足す
2. アイテム絵を `Assets/AddressableAssets/Image/Item/` に置き、**Addressable アドレスを `Image/Item/<名前>`** に設定する（未配置でも動く。その場合は手札にアイテム名の文字が出る）
3. [ItemCatalog.cs](../Assets/Scripts/Main/Item/ItemCatalog.cs) の `All` に 1 行足す（`ItemId` → 表示名・**効果説明文（`Description`・アイテムモーダルの本文に出る）**・画像アドレス）。`ItemCatalog.RandomItem` はカタログ全体から抽選するので分岐追加は不要

- 取得の保持は [ItemModel.cs](../Assets/Scripts/Main/Item/ItemModel.cs)（`MoneyModel`／`TerritoryModel` と同じ Scoped DI・参加者ごと）。着地演出・右下手札への反映は `BoardPresenter.PlayItemSequenceAsync`／`AppendItemToHand`（同じアイテムはカードを増やさず「x2」の枚数バッジで表示をまとめる。`ItemModel` 側の手札リストは重複を保持）。
- 手札カードのクリックで [ItemModalPresenter.cs](../Assets/Scripts/Main/Item/ItemModalPresenter.cs) の詳細モーダル（絵・名前・効果説明＋「使用する」「閉じる」）が開く。**「使用する」はモーダル自身では消費せず、生成側から渡された効果ハンドラ `Action<ItemId> onUse`（＝`BoardPresenter.HandleItemUse`）を呼んで閉じるだけ**（消費〔`ItemModel.Use`〕のタイミングは効果側に委ねる。マス選択のキャンセルで消費しない効果があるため）。手札 UI の減算は `ItemModel.Used` を購読する `BoardPresenter.RemoveItemFromHand`。
- 「使用する」ボタンは**自分の手番かつルーレット未回転（`RouletteState.Idle`）でアイテム効果の実行中でないときだけ有効**にする。`BoardPresenter.CanUseItem`（`Func<bool>`＝`!_itemEffectRunning && _turn.CurrentPlayer.CurrentValue == _humanPlayer && _rouletteModel.State.CurrentValue == RouletteState.Idle`）を `ItemModalPresenter` へ渡し、モーダルを開くたびに `SetEnabled` で評価する（回した後・コマ移動中・相手の手番中・効果実行中は無効）。

### アイテムの効果を実装する（`BoardPresenter.HandleItemUse` で分岐）

- **効果の発動は使用側 `BoardPresenter.HandleItemUse(ItemId)` がアイテム種別で分岐して担う**。効果未実装のアイテムは従来どおり `_items.Use(_humanPlayer, item)` で消費のみ。効果を足すときはこのメソッドに `if (item == ...)` を 1 本足して、そこから `TerritoryModel`／`MoneyModel`／`MiniGameLauncher` などにつなぐ。
- **消費のタイミングは効果側で決める**。即時発動なら `_items.Use` を呼んでから効果を出す。マス選択などユーザー操作を挟んでキャンセルできる効果は、**確定できたときだけ `_items.Use` を呼ぶ**（キャンセル・シーン破棄では消費しない）。多重起動と実行中の再使用を防ぐため `_itemEffectRunning` フラグを立て（`CanUseItem` にも組み込む）、`finally` で必ず戻す。ターンを消費しない効果なら、選択・演出の間だけ `RoulettePresenter.SetInteractable(false)` でスピンを止め、終わったら自分の手番かつ `Idle` のとき `true` に戻す。
- **実装例＝陣地獲得（`StealTerritory`）**。`RunTerritoryStealAsync`（`async UniTaskVoid`・`_destroyCt` で `Forget`）が、`TerritoryModel.CellsNotOwnedBy(_humanPlayer)` で対象マス（未占拠＋相手占拠）を出し（0 個なら消費せず終了）、`SelectTerritoryCellAsync` でマス選択を待つ。対象マスは `board-cell--selectable`（金枠）＋重ねた `board-cell__glow` を `AnimateSelectableGlowAsync` が ping パルスで駆動して強調し、ガイドバナー（`TerritorySelectBanner`）を出す。選択入力は `BoardZoomController.BeginCellSelection(Func<Vector2,bool>)`／`EndCellSelection` に委ね、**ドラッグ層でタップ（ほぼ動かず離す）とパン（動かす）を振り分ける**（盤面タップとドラッグパンを両立）。タップ位置は `cell.worldBound.Contains(screenPos)` で対象マスに当てる。選択結果は `UniTaskCompletionSource<int>`（キャンセル・破棄で -1）で受け取り、確定したら `ItemModel.Use` → 既存の旗演出（`PlayTerritoryFlagSequenceAsync`）→ `ApplyTerritoryLanding`（占拠・過半数勝利）を再利用する。
- **盤面タップを拾えるようにする**（`BoardZoomController`）：`BeginCellSelection` 中はドラッグ層を常に `PickingMode.Position` にして（盤面が画面内に収まっていてもタップを拾う）、`OnPointerUp` で押下からの最大移動量が閾値以下ならタップとしてコールバックを呼ぶ。`UpdateInteractivity` も選択中は反応を切らない。`EndCellSelection` で通常（盤面がはみ出すときだけ有効）へ戻す。

---

## 共通ルール（抜粋）

- `var` は使わない。型を明示する
- フィールドは `_camelCase`、型・メソッドは `PascalCase`
- `Find()` / static 状態は使わない。DI で解決する
- UI は UXML + USS で構築。uGUI 禁止
- アセットロードは Addressables。`Resources.Load` 禁止
- USS では `gap` 禁止 → 子要素の `margin` で代替
