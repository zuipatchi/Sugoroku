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

**Presenter が `new` する協調クラスが Model を購読するときは、そのクラス自身を `IDisposable` にして
Presenter の `CompositeDisposable` に載せる**（協調クラスは MonoBehaviour ではないので `OnDestroy` が無く、
購読を自分で畳めない）。内部に自前の `CompositeDisposable` を持ち、`Dispose` で落とすだけでよい。
実例は [PlayerNameplateView.cs](../Assets/Scripts/Main/Board/PlayerNameplateView.cs)（プレートと同じ寿命の購読）と
[PlayerDetailPresenter.cs](../Assets/Scripts/Main/Board/PlayerDetailPresenter.cs)（開いている間だけの購読）。

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

- 上に乗せたいパネル（前面に出したいボタンなど）の UIDocument の **Sorting Order を、奪っている側より大きく**する（Main では Board=0 / Roulette=10 なので、その上に重ねるパネルは 20 にした）
- そのパネルのルート要素は `picking-mode="Ignore"` にし、**ボタン等の操作要素だけがイベントを拾う**ようにする。これで「ボタン以外は下のパネルへ素通り」になり、共存できる
- 参考の Sorting Order: Transition=2000 / Option=1000 / MiniGame シーン=100。新しい前面 UI はこれらと衝突しない値にする
- **下のパネルに載っているモーダルを一時的に最前面へ出したいときは、開いている間だけそのパネルの `UIDocument.sortingOrder` を上げて閉じたら戻す。** アイテム詳細モーダルは Board パネル（Sorting=0）にあるため、回転中のルーレット（Sorting=10）が前面に来て隠れていた。`ItemModalPresenter` が開くとき Board の `sortingOrder` を 100（ルーレット/トリガより上・Option/Transition より下）へ退避付きで持ち上げ、閉じるときに元へ戻す（閉→開の遷移でだけ退避するので二重オープンでも基準値を失わない）。Board パネルのモーダルはすべてこの規約に従う（`ItemShopPresenter`／`MiniGameSelectPresenter`／`BoardCellInfoPresenter`／`PlayerDetailPresenter`）

**逆に、下のパネルへ操作要素を足すときは、上の全画面パネルすべてのルートを `picking-mode="Ignore"` にする。** Board パネル（Sorting=0・最下層）に盤面ズームの虫眼鏡ボタンとドラッグ層を足したとき、上に乗る Roulette パネル（Sorting=10）のルートが**全画面 picking 有効**でイベントを奪っており、下の Board のボタンが無反応だった。Roulette 側の**ルートと円盤ビジュアルを `picking-mode="Ignore"`**（[Roulette.uxml](../Assets/Scripts/Main/Roulette/View/Roulette.uxml)）にし、**スピンボタンだけ操作可能**にすることで、下の Board パネルのボタン・ドラッグ層へ入力が通るようになった。「下のパネルに操作 UI を置く」場合は、それより上の全画面パネルが素通し設定になっているか必ず確認する。

---

## 8. 新しいミニゲームを追加する

ミニゲームは `MiniGame` シーンを Main（や動作確認用の `MiniGameTest`）の上に Additive で重ねて動かす（`Transit` は使わない。詳細は [architecture.md](architecture.md)「シーン構成」）。新しい種類を足す手順:

1. [MiniGameId.cs](../Assets/Scripts/Common/MiniGame/MiniGameId.cs) に種別を追加する（最大5種類想定）
2. その種別の UI を `Assets/AddressableAssets/MiniGame/` に `.uxml` / `.uss` で作り、**Addressable アドレスを `MiniGame/<名前>`** に設定する
3. [MiniGameCatalog.cs](../Assets/Scripts/Common/MiniGame/MiniGameCatalog.cs) の `All` に 1 行足す（`MiniGameId` → 表示名・UXML アドレス・**選択カードのサムネイル画像アドレス** `Image/MiniGame/<名前>`・**遊び方の 1 行説明（`Description`）**。画像は Addressables に登録し、未配置なら名前のみのプレースホルダになる。`Description` は Home のルール説明（[#17](#17-ルール説明はゲーム側のカタログから組み立てる)）に出る唯一の情報源なので、空のままだと EditMode テストが落ちる）。**ゲーム内で使う絵も同じ `Image/MiniGame/` に置く**〔2Dレースのコース＝`Image/MiniGame/Track`・タップ連打のサンドバッグ＝`Image/MiniGame/SandBag`。カタログに載るのは選択カードのサムネイルだけで、ゲーム内の絵は各 GamePlay がアドレス定数を持って自分でロードする。画面全体に敷く背景の絵だけは `Image/` 直下に置く＝下の「背景画像を貼るときは…」参照〕`MiniGameHostPresenter.AddressFor` はカタログ引きなので分岐追加は不要で、**動作確認用の `MiniGameTest` シーンにもボタンが自動で並ぶ**
4. 進行ロジックを実装する。状態は純粋ロジックの Model（[TapGameModel.cs](../Assets/Scripts/MiniGame/TapGame/TapGameModel.cs) / [RaceGameModel.cs](../Assets/Scripts/MiniGame/RaceGame/RaceGameModel.cs) に倣う）に分け、EditMode テストを書く。[MiniGameHostPresenter.cs](../Assets/Scripts/MiniGame/MiniGameHostPresenter.cs) は `CurrentGame` で分岐するディスパッチャなので、UI が異なるゲームは**専用の `<名前>GamePlay` クラス**（プレーンクラス。[RaceGamePlay.cs](../Assets/Scripts/MiniGame/RaceGame/RaceGamePlay.cs) 参照。`BuildAsync`＝表示前ロード／`RunAsync`＝入力待ち・進行してスコアを返す）に切り出し、ホストの `ReadyAsync` に 1 分岐足して委譲する（タップ連打も [TapGamePlay.cs](../Assets/Scripts/MiniGame/TapGame/TapGamePlay.cs) に切り出して同じ構造にしている）
5. Play クラスと Model を [MiniGameLifetimeScope.cs](../Assets/Scripts/MiniGame/Injector/MiniGameLifetimeScope.cs) に `Lifetime.Scoped` で登録する（DI が生成・破棄する。Addressables ハンドルの解放は各クラスの `Dispose` に書く）
6. 起動は `MiniGameLauncher.PlayAsync(MiniGameId.<種別>, ct)`（参加者数に依存するゲームは第3引数 `playerCount` も渡す＝省略時は `MiniGameLauncher.DefaultPlayerCount`＝2。本番の盤面ミニゲームは参加者全員ぶんを明示的に渡す）。結果は `MiniGameResult.Score` で受け取る（意味はゲームごと。**勝敗系は勝ち1/負け0で報告し `DetermineMiniGameWin` が `Score==1` で判定**する。タップ連打＝連打数1位で1、2Dレース＝先着で1、被っちゃやーよ＝獲得で1）。動作確認は `MiniGameTest` シーン（[MiniGameTestPresenter.cs](../Assets/Scripts/MiniGame/Test/MiniGameTestPresenter.cs)）をエディタで直接開いて Play する（人数ステッパーで参加者数を選べる）
7. ホストは表示前に UXML をロードするため `ISceneReady` を実装している（ロード完了まで暗幕を維持）。`Report` で結果を返すとランチャーがシーンをアンロードする

> **ゲーム内からの起動は 2 経路**：ミニゲームマス（`BoardCellEvent.MiniGame`・**遊ぶゲームは着地のたびの抽選**＝`MiniGameCatalog.RandomGame`）への着地と、ミニゲームアイテム（遊ぶゲームをモーダルで選ぶ）。報酬は**順位が付くゲーム（タップ連打・2Dレース）が順位別の賞金（1位500／2位300／3位100・4位以下は0）**、順位が定義できない被っちゃやーよは賞金ではなく**誰とも被らなかった人が選んだアイテムそのもの**（手札に入る。ルールは `MiniGamePrize`・配るのは `BoardPresenter.AwardMiniGameItemsAsync`）。
> **マスへの着地は遊ぶゲームを選べないので、起動の前に告知して全員の「はじめる」を待つ**（`BoardPresenter.ShowMiniGameAnnounceAsync` → `WaitForAllMiniGameReadyAsync`）。アイテム経由は選択モーダルで何を遊ぶか分かっているので待たない。
> **一人用モードでも、ミニゲームマスに止まったのが CPU のときは自分が CPU 相手に遊ぶ**（`BoardPresenter.RunLocalMiniGameAsync`）。**賞金はオンラインと同じく順位別**で、自分も CPU もミニゲーム内の順位ぶんもらう（CPU の順位を知っているのはゲーム側だけなので `MiniGameResult.Ranks` で受け取る）。順位が定義できない被っちゃやーよは、誰とも被らなかった人（自分でも CPU でも）が自分の選んだアイテムを受け取る（CPU が何を選んだかを知っているのもゲーム側だけなので `MiniGameResult.Values`＝参加者ごとの結果値で受け取り、勝敗はオンラインと同じ純粋関数 `MiniGameRanking.Resolve` で決める）。報酬の加算と結果発表の帯（自分の結果＝順位／獲得したアイテム）はオンラインと共通（賞金＝`AwardMiniGameAsync`／アイテム＝`AwardMiniGameItemsAsync`）。

**オンライン対戦に対応させるとき**（新しいゲームを足したら必ず通す）:

1. **相手をシミュレートしない道を作る**。`MiniGameSessionModel.SimulateOpponents` が false のとき、CPU の自動操作（連打・自走・自動選択）を止める。分岐は Model 側に 1 つ持たせるのが素直
2. **`RunAsync` が生の結果値を返す**（`MiniGameOutcome`）。`Score` は一人用の勝敗判定、`Value` はオンラインで順位を決める素材（連打数・ゴールタイム ms・選んだアイテムの `ItemId`。**同じ判定ができるなら、盤面が意味を読み取れる値で返す**＝被っちゃやーよは「カードの index」でなく「選んだアイテムの `ItemId`」を返すので、被り判定〔値の一致〕は変わらないまま盤面が結果の帯にアイテム名を出せる＝`OverlapGamePlay.ChosenItemValue`）、`Ranks` は**参加者ごとの 1 始まりの順位**（一人用の順位別の賞金に使う。相手をシミュレートするのはゲームの中なので、CPU の順位を知っているのはゲーム側だけ。結果パネルの順位表を組むときに一緒に返すのが楽＝`TapGamePlay.RefreshStandings`／`RaceStandingsView.Refresh`。順位が定義できないゲームは空でよい）、`Values` は**参加者ごとの生の結果値**（`Ranks` と同じ理由で一人用のために返す。**報酬に「相手が何を出したか」まで要るゲームだけ**でよい＝被っちゃやーよは勝った人が選んだアイテムをそのまま配るので、CPU のぶんも要る＝`OverlapGamePlay.ChosenItemValues`。盤面側は `MiniGameRanking.Resolve` にかけてオンラインと同じ勝敗を出す）
3. **勝敗ルールを `MiniGameRanking.Resolve` に足す**（純粋関数・EditMode テスト対象）。全クライアントが同じ入力から同じ勝者に至るので判定役が要らない。「起動できなかったとき」用に `WorstValue` も足す
4. **ランダムな内容は共有の種で組む**。`MiniGameSessionModel.ResolveSeed()` から種を取る（オンラインでは起動側が配った共通の種が入っている）。その場で `Random` を引くとクライアント間で内容が食い違う
5. **持っていない値で結果を断定しない**。相手の最終値が届いていないうちに「1位！」と出すと後の発表と食い違う。出すなら 6 の途中経過で全員ぶんを集め、**揃うまでは暫定と明示する**（2Dレースの順位表は自分のゴール直後から出るが、全員のゴールタイムが届くまでは「（暫定）」＋「他のプレイヤーが走行中…」を添える＝`RaceStandingsView`／`RaceGamePlay.RefreshStandings`。タップ連打も同じで、届いている連打数で順位表を出しつつ「ほかのプレイヤーの結果を集計中…（暫定）」を添える＝`TapGamePlay.RefreshStandings`）。並べ方は勝者判定（`MiniGameRanking.Resolve`）と同じルールにして、盤面の発表とずれないようにする。**「断定できる方向」は片側だけのことがある**ので、そこは待たずに見せてよい（被っちゃやーよの「かぶった」は 1 人ぶん届けば確定するのでその場で赤枠にし、「かぶらなかった」は全員ぶん揃ったときだけ言う＝`OverlapGamePlay.RevealResult`）
6. **プレイ中に相手の状況を見せたいなら `MiniGameSessionModel.Progress`**（`MiniGameProgressChannel`）。自分の値を `Publish` し、`Values` を毎フレーム読んで表示に反映する。アクションストリームには載せない（[networking.md](networking.md)「見た目だけの情報はストリームに載せない」）。運べるのは整数 1 つなので**小数は倍率をかけて送り**（2Dレースの進捗 0〜1 は 10000 倍）、**値の意味を切り替えたいときは値域で分ける**（レースは 1000000 以上＝ゴール済みで、差がゴールタイム ms＝`RaceGamePlay.FinishedValueOffset`）、**0 は「まだ届いていない」を意味する**（`Values` の初期値）ので 0 が正当な値になりうるものは下駄を履かせ（被っちゃやーよの選んだカード index は +2＝`OverlapGamePlay.ChoiceValueOffset`。無効票 -1 も一緒に運べる）、**相手がまだ受信を始めていない時期に配ったぶんは届かない**ので揃うまで送り直す（`OverlapGamePlay.CollectChoicesAsync` は選択が全員ぶん揃うまで 200ms ごとに自分の選択を送り直し、`OpponentWaitSeconds`＝8秒で打ち切る）、**位置を動かすものは表示側で目標へ補間する**（200ms 間隔の値をそのまま置くとカクつく＝`RaceGamePlay.UpdateRunnerPositions`）。**届いた値でゲームの決着を左右しない**（2Dレースは相手がゴールしても自分のレースを打ち切らない＝`RaceGameModel.ResolveFinish`。順位は 3 の持ち寄りで決める）。自分が先に終わっても相手はまだ遊んでいるので、**結果画面を出している間も配り／反映を続ける**と相手が最後まで動いて見える（`RaceGamePlay.WaitForCloseAsync`）

**結果パネルに全参加者ぶんの成績を出すときは [MiniGameStandingsView.cs](../Assets/Scripts/MiniGame/MiniGameStandingsView.cs) を使う**。参加者と同じ並びで行を作っておき（`AddParticipant`）、表示のたびに並べ替え済みの `StandingLine`（参加者 index ＋左の列＋右の列）を渡して入れ直す（`Refresh`）。**順位の決め方と文言はゲームごとに違うので呼び出し側が決める**（タップ連打＝`ScoreRanking.Order` の連打数順／2Dレース＝`RaceRanking.Order` のゴール順／被っちゃやーよは順位が付かないので「獲得！／かぶり／時間切れ」の区分順）。USS クラスは接頭辞から `<prefix>` / `--you` / `__rank` / `__name` / `__value` を組み立てるので、そのゲームの `.uss` に同じクラスを定義する（`tap-standing`／`race-standing`／`overlap-standing`）。行が潰れないよう最小幅を持たせるが、**パネルの左右パディングを足しても基準解像度 540px に収まる幅**にする。

**参加者を「並べて見せる」ときは 1 つあたりの寸法を枠の実寸から決める**。全員ぶんの要素を横に並べるゲーム（タップ連打のキャラカード・2Dレースのレーン・被っちゃやーよの選択カード）は人数で 1 つあたりの大きさが変わる。基準解像度（540px）と USS の幅から px を計算して定数で持つと、USS 側の幅を変えたときに静かにはみ出すので、**並べる枠の `resolvedStyle.width` から計算して入れ、定数で持つのは上限・下限だけにする**（`TapGamePlay.LayoutCards`）。**レイアウトが決まる前は寸法が読めない**（`BuildAsync` は表示前に走るので `resolvedStyle` が 0/NaN）ので、生成直後に 1 度呼ぶだけでなく枠の `GeometryChangedEvent` でも呼ぶ（[MapPickerView](../Assets/Scripts/Main/Board/MapPickerView.cs) の大プレビューと同じ形。子の寸法を変えても枠自身の寸法は変わらないのでループしない）。

**背景画像を貼るときは「名前で引いた UXML のルート要素」へ貼る**。`MiniGameHostPresenter` は `UIDocument.rootVisualElement` に `CloneTree` するので、`BuildAsync` に渡ってくる `root` は **UXML のルート要素（`.overlap-root` 等）の親**。親へ `backgroundImage` を貼っても、全画面を覆う不透明な地色を持つ子に隠れて見えない（`OverlapGamePlay.ApplyBackgroundAsync` は `root.Q("OverlapRoot")` に貼る／タップ連打は `TapRoot`／2Dレースは `Track` に貼る）。拡大縮小は USS 側（`background-size: cover`）に任せ、文字が読めるよう `-unity-background-image-tint-color` で絵だけを暗くする。**画面全体に敷く背景の絵の資産は `Assets/AddressableAssets/Image/Background/` にまとめる**（ホーム・マップ選択・ショップ・ジム・レース会場）。アドレスは資産パスと独立なので、新しく足すぶんはフォルダに合わせて `Image/Background/<名前>`（2Dレース＝`Image/Background/RaceBackground`）、フォルダを作る前から使っている 4 枚は移行前のまま（`Image/HomeBackGround`／`Image/StageBackground`／`Image/GymBackground`＝タップ連打／`Image/Shop`＝被っちゃやーよ〔アイテムショップと共用〕）。`Image/MiniGame/` はゲームの中身の絵（サムネイル・サンドバッグ・路面）の置き場として使い分ける。

**「無効だから薄く」を画像に効かせない**。`-unity-background-image-tint-color` を `:disabled` に書くと、まだ操作できない準備中（カウントダウン中）にも効いて絵が薄く見える。**操作できないことは入力を切るだけで伝わる**ので、薄くするのは画像を貼っていないときの地色と文字にとどめる（`.tap-button:disabled`＝サンドバッグ／`.overlap-card:disabled`＝アイテムのカード。どちらも既定テーマが無効要素へ掛ける半透明化を打ち消すため `opacity: 1` を明示する）。要素ごと薄くする `opacity` は中の絵まで巻き込むので同じ理由で使わない。押下中（`:active`）の tint は手応えの演出なので残してよい。

**Main のカタログ（`ItemCatalog` 等）を再利用するとき**は `MiniGame` asmdef に `Main` 参照を足す（被っちゃやーよがアイテム絵の再利用で追加済み）。`Main` は `MiniGame` を参照しないので循環しない。**参加者数に依存するミニゲーム**（被っちゃやーよは提示枚数＝参加者数、2Dレースはレーン数＝参加者数）は、人数を `MiniGameSessionModel.PlayerCount` から取る。MiniGame シーンは別スコープで `GameParticipants` を直接注入できないため、起動側が `MiniGameLauncher.PlayAsync(id, ct, playerCount)` で渡した値を Common シングルトンの `MiniGameSessionModel` に載せ、各 GamePlay がそれを参照する（本番の盤面ミニゲームは参加者全員＝2〜4、`MiniGameTest` シーンは人数ステッパーで 2〜4）。セッション未設定時のフォールバックだけ Config の定数（`OverlapGameConfig.DefaultPlayerCount`）に残す。**参加者ごとのキャラ**も同じ経路で運ぶ：`PlayAsync(id, ct, playerCount, characters)` の `characters`（index 0＝プレイヤー）が `MiniGameSessionModel.Characters` に載り、各 GamePlay が走者・カード・ラベルに YOU/CPU でなくそのキャラを使う（本番＝`BoardPresenter` が実参加者のキャラ、`MiniGameTest`＝ランダムな重複なしキャラ。未指定時は選択キャラ／YOU・CPU へフォールバック）。

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

- **状態変化の待受は R3 の `FirstAsync` を `await` する**。`ReactiveProperty` は購読時に現在値を流すため、`Where` で目的の状態だけ通し、`FirstAsync(ct)` でその瞬間まで待つ。ボタン長押しのような「ユーザー操作の完了」も、Model に通知（`Observable<T>`）を生やしてループ側で `await` すれば分岐なく書ける。**待受の結果が複数値なら小さな struct（`SpinDecision`＝止まるセクター＋減速時間）でまとめて流す**とループ側が組み立てずに済む。

```csharp
// 回し始める前に待受を張ってから回す（操作が先に完了しても取りこぼさない）
Task<SpinDecision> decided = _rouletteModel.Decided.FirstAsync(ct);
_roulette.SetInteractable(true);
SpinDecision decision = await decided;
```

- **「結果が確定する瞬間」は「演出が終わる瞬間」より早いことがある**。ルーレットは離した瞬間に停止位置が決まる（[#14](#14-オンライン同期は決定と適用を分けてアクションストリームで配る)）ので、演出の完了を待たずに確定値を流したほうが、オンラインの相手を待たせずに済む。

- **前回の状態が残る点に注意**。`FirstAsync` は購読時の現在値も評価するので、前手番の `Stopped` を「今回の停止」と誤検知しないよう、手番の開始時に Model を `Reset()`（`Idle` へ戻す）してから待つ。
- **人間と CPU は同じ流れの分岐にする**。人間＝入力を許可する、CPU＝同じ UI（円盤）をコードから回す（`AutoSpinAsync`）、と**入口だけ変えて後段（確定の待受・コマ前進・勝敗判定）は共通**にすると、演出コードを二重化せずに済む。
- **キャンセルは握る**。ループの `await` はシーン破棄でキャンセルされる。`StartAsync` 全体を `try { ... } catch (OperationCanceledException) { }` で囲む（VContainer 由来のトークンなので [#4](#4-シーン表示前に非同期初期化を待つisceneready) と同じ扱い）。
- **接続待ちを最初に置く**とオンライン/オフラインを同じループで扱える（`NetworkModel.State` が `Connected` になるまで `FirstAsync` で待ってから進行を始める。一人用は即 `Connected`）。
- **進行そのものはアクションストリームで駆動する**（[#14](#14-オンライン同期は決定と適用を分けてアクションストリームで配る)）。ループは「手番の席を担当するクライアントだけが決めて発行 → 全員が受信したアクションを適用」の形になり、オンライン/一人用が同じコードパスになる。

---

## 11. 自作コンテンツは ScriptableObject データ＋専用 EditorWindow で作れるようにする

盤面のように「非プログラマーがビジュアルに量産したいデータ」は、**データを ScriptableObject アセットにして、専用の `EditorWindow` で編集する**構成にする（例: [BoardDefinition.cs](../Assets/Scripts/Main/Board/BoardDefinition.cs) ＋ [BoardEditorWindow.cs](../Assets/Scripts/Main/Editor/BoardEditorWindow.cs)）。実行時の Presenter はこのデータを読んで描画するだけにし、計算生成はフォールバックに回す（`BoardPresenter` は選択マップも `_definition` も無いときだけ矩形リングを生成）。

- **エディタ専用コードは `Editor/` サブフォルダの Editor 専用 asmdef に分ける**。`includePlatforms: ["Editor"]` にし、`references` に対象ランタイム asmdef の GUID を並べる（**推移的に参照されないので**、`BoardDefinition`（Main）だけでなく `MiniGameId`（Common）のように編集画面で型名を出すものは Common の GUID も足す）。ビルドには含まれない。
- **ScriptableObject の編集は `Undo.RecordObject(target, "…")` → 値を変更 → `EditorUtility.SetDirty(target)`** の順で行い、保存は `AssetDatabase.SaveAssets()`（またはユーザーの Ctrl+S）に任せる。`[Serializable]` な子クラス（`BoardCellDefinition` など）を `List<T>` で持たせれば、そのリストを丸ごと Undo/シリアライズできる。
- **エディタは UI Toolkit（`CreateGUI` で構築）で書く**とランタイム同様にクラスで組める。ただし `UnityEditor.UIElements` のフィールド（`IntegerField` 等）は**ラベルの最小幅が広く、フィールド全体幅を小さく固定すると入力欄が潰れて操作できなくなる**。`field.labelElement.style.width` を絞ってから十分な幅を与える。数値入力は `isDelayed = true` にして Enter／フォーカスアウトで確定させると桁の途中で反応せず打ちやすい。
- **作業コピー（`_working` のような編集用のリスト）を持つエディタは `Undo.undoRedoPerformed` を購読する**（`OnEnable` で購読・`OnDisable` で解除し、ハンドラで資産から読み直して UI を組み直す）。Ctrl+Z が巻き戻すのは資産だけなので、購読しないと作業コピーが古いまま残り、**次の編集で古いコピーが資産へ書き戻されて取り消した変更が復活する**（`CellMessageEditorWindow` がこれを踏んだ）。`CreateGUI` より前に呼ばれることがあるので UI 要素の null ガードを入れる。
- **アセット未割り当てでも壊れないフォールバックを実行時側に持たせる**（`BoardDefinition.CreateRectangular` を `CreateInstance` で生成し、`OnDestroy` で `Destroy` する）。既存シーンは無改変で従来動作、データを割り当てたときだけ差し替わる。ただし**フォールバックは静かに効くので「資産を作って編集したのに割り当て忘れ」に気づけない**（`BoardCellMessageCatalog` がこれで、エディタで編集した文言が一度もゲームに出ていなかった）。資産を新設したら、**消費側の Presenter へインスペクタで割り当ててシーンをコミットするまでがワンセット**。
- **同種の SO 資産を複数から選ばせるには「カタログ SO ＋ Common に文字列 ID」で分ける**。`CharacterCatalog` のような静的クラスは Addressable アドレス（文字列）しか持てず SO 資産参照を持てないので、資産を並べるカタログ自体も `ScriptableObject` にする（例: [BoardCatalog.cs](../Assets/Scripts/Main/Board/BoardCatalog.cs) が `List<BoardDefinition>` を持ち `All`/`Default`/`Find` を公開）。選択状態をシーンをまたいで持つ Common シングルトン（[BoardSessionModel.cs](../Assets/Scripts/Common/Board/BoardSessionModel.cs)）は、**Common から Main の SO 型（`BoardDefinition`）を参照できない**ため識別子（資産名 `Object.name`）だけを文字列で持ち、消費側（`BoardPresenter`）が `catalog.Find(id)` で実体を解決する。カタログ資産は選択シーンと消費シーンの両方の Presenter にインスペクタで割り当てる。未選択・未割り当て時は単発フォールバック（`_definition`）に落ちる。

---

## 12. 新しいアイテムを追加する

アイテム取得マス（`BoardCellEvent.Item`）でもらえるアイテムは、ミニゲームと同じ「enum ＋ 静的カタログ」方式で増やす。

1. [ItemId.cs](../Assets/Scripts/Main/Item/ItemId.cs) に種別を 1 つ足す
2. アイテム絵を `Assets/AddressableAssets/Image/Item/` に置き、**Addressable アドレスを `Image/Item/<名前>`** に設定する（未配置でも動く。その場合は手札にアイテム名の文字が出る）。**アイテム絵はどこでも正方形の枠に切り抜いて出す**（[design-system.md](design-system.md)「アイテム絵は正方形の枠に切り抜いて出す」）ので、**正方形の絵なら既存の絵を流用してもよい**（お金アップはお金アップのマスと同じ `Board/MoneyUp`）
3. [ItemCatalog.cs](../Assets/Scripts/Main/Item/ItemCatalog.cs) の `All` に 1 行足す（`ItemId` → 表示名・**効果説明文（`Description`・アイテムモーダル/ショップの本文に出る）**・画像アドレス・**購入価格（`Price`）**）。ショップのラインナップ抽選（`RandomLineup`）は `Purchasable` から選ぶので分岐追加は不要

**どこにどれだけ出すか**は 2 つの指定で決める（独立しているので「買えないがカードには出る」もできる）。抽選の入口が分かれているので、出す側に分岐は要らない。

| 出す先 | 抽選の入口 | 指定 |
|---|---|---|
| アイテムショップ | `ItemCatalog.RandomLineup`（`Purchasable` から） | `purchasable`（既定 true） |
| 被っちゃやーよの選択カード | `ItemCatalog.RandomCards`（`All` から重み付き） | `cardWeight`（既定 1） |

- **ショップに並べない**なら `purchasable: false`（価格は 0）。買えないだけで、**被っちゃやーよで誰とも被らずに選べば報酬として手札に入る**ので効果は実装する（`MoneyUp`＝お金アップ）。
- **カードに出にくくする**なら `cardWeight` を下げる（`InstantWin`＝勝利は 0.5＝他の半分）。`RandomCards` は重みに比例した確率で 1 枚ずつ引いては候補から外すので、1 枚だけ引くときは重みの比がそのまま出やすさの比になる。0 にすれば実質出ない。
- カードは**種類が参加者数より多いほど毎回の顔ぶれが変わって選択の駆け引きが生まれる**（並ぶのは参加者数ぶんだけなので、種類＝人数だと毎回全部出てしまう）。

**説明文（`Description`）は効果の実装と読み比べて書く**。実装より狭く読める書き方をしない（「相手の所持金の一部を奪う」は 1 人から奪うように読めるが、実際は自分以外の全員が対象＝「全員からいくらかのお金を適当に奪う」）。**数値まで書くならルール側の定数から組み立てる**（ミニゲームの順位別の賞金＝`MiniGamePrize.RankPrizeText()`。マスの説明〔`BoardEventDescription`〕と同じ方針で、賞金の並びはそちらと共用する）ので、ルールを変えれば説明文も一緒に変わる。**あえて数値を書かない**選択もある（お金よこどりの奪う割合はぼかす＝使ってからのお楽しみ）。書いた内容は EditMode テストで押さえる（`ItemCatalogTests`）。**説明文と価格は Home のルール説明にもそのまま出る**（[#17](#17-ルール説明はゲーム側のカタログから組み立てる)）ので、カタログに足せば説明を書く場所は増えない。

- **アイテムは着地時に開くアイテムショップで購入する**。アイテム取得マスに止まると `BoardPresenter.PlayItemShopSequenceAsync` が動く。**買い物は「決定」と「適用」に分かれている**（[#14](#14-オンライン同期は決定と適用を分けてアクションストリームで配る)）：着地した本人のクライアントだけが `DecidePurchaseAsync` で `ItemCatalog.RandomLineup(_itemRng, 2, 4)`（ランダムな枚数・重複なし）を抽選し、人間は `ItemShopPresenter.SelectAsync(lineup, budget, ct)`（一度に 2 枚のカルーセル・買えないカードは無効・「買わずに閉じる」でスキップ）、CPU は `PickCpuPurchase` で買える範囲からランダムに 1 つ選ぶ。結果を `GameAction.ShopResult` で発行し、**全クライアントが `ApplyShopResult` で代金の支払い（`MoneyModel.Add(player, -price)`）と `ItemModel.Add`（購入音＝`MoneySE`）を行う**。カルーセルは `flex-shrink:0` のカードを `overflow:hidden` のビューポートに収め、`ShowPage` で `PageWidthPx`（カード142px×2）ぶん translate する（ビューポート幅・`CardSlotWidthPx` は USS と一致させる）。
- 取得の保持は [ItemModel.cs](../Assets/Scripts/Main/Item/ItemModel.cs)（`MoneyModel`／`TerritoryModel` と同じ Scoped DI・参加者ごと）。右下手札への反映は `ItemModel.Gained` 購読→`BoardPresenter.AppendItemToHand`（同じアイテムはカードを増やさず「x2」の枚数バッジで表示をまとめる。`ItemModel` 側の手札リストは重複を保持）。
- 手札カードのクリックで [ItemModalPresenter.cs](../Assets/Scripts/Main/Item/ItemModalPresenter.cs) の詳細モーダル（絵・名前・効果説明＋「使用する」「閉じる」）が開く。**「使用する」はモーダル自身では消費せず、生成側から渡された効果ハンドラ `Action<ItemId> onUse`（＝`BoardPresenter.HandleItemUse`）を呼んで閉じるだけ**（消費〔`ItemModel.Use`〕のタイミングは効果側に委ねる。マス選択のキャンセルで消費しない効果があるため）。手札 UI の減算は `ItemModel.Used` を購読する `BoardPresenter.RemoveItemFromHand`。
- 「使用する」ボタンは**自分の手番かつルーレット未回転（`RouletteState.Idle`）でアイテム効果の実行中でないときだけ有効**にする。`BoardPresenter.CanUseItem`（`Func<bool>`＝`!_itemEffectRunning && _turn.CurrentPlayer.CurrentValue == _humanPlayer && _rouletteModel.State.CurrentValue == RouletteState.Idle`）を `ItemModalPresenter` へ渡し、モーダルを開くたびに `SetEnabled` で評価する（回した後・コマ移動中・相手の手番中・効果実行中は無効）。

### アイテムの効果を実装する（決定＝`HandleItemUse` / 適用＝`ApplyActionAsync`）

効果は**「決定（1 人だけ）」と「適用（全員）」の 2 段**で書く（理由と全体像は [#14](#14-オンライン同期は決定と適用を分けてアクションストリームで配る)）。新しい効果を足すときは、`HandleItemUse` の `switch` に決定側を、`ApplyActionAsync` の `switch` に適用側を 1 本ずつ足す。

- **決定側（`BoardPresenter.HandleItemUse(ItemId)`）はパラメータを決めて `_sync.Publish(GameAction.ItemUse(...))` するだけ**。`ItemModel.Use` も効果の反映もここでは行わない。マス選択などユーザー操作を挟む効果は、**確定できたときだけ発行する**（キャンセル・対象なし・シーン破棄では発行しない＝消費もされない）。効果パラメータは `int[]` で運ぶ（陣地獲得＝対象マス index、お金よこどり＝**席 index 順**の奪取額、ミニゲーム＝所持金報酬）。
- **適用側（`BoardPresenter.ApplyActionAsync`）が `ItemModel.Use(seat, item)` で消費してから効果を反映する**。使用者だけでなく全クライアントで走るので、演出も含めてここに書けば相手の画面にも同じものが出る。**種別ごとの `switch` に入る前に、全アイテム共通の「使った」演出（`PlayItemUsePresentationAsync`＝アイテム絵の中央ポップ＋「〔キャラ名〕が「〔アイテム名〕」を使用！」の帯＋SE）が走る**ので、新しい効果を足すときに「使った感」を自前で用意する必要は無い（効果側の演出は共通演出が終わってから始まる）。
- **多重起動と実行中の再使用は `_itemEffectRunning` で防ぐ**（`CanUseItem` にも組み込み済み）。`BeginItemEffect()`（フラグを立て `RoulettePresenter.SetInteractable(false)`）／`EndItemEffect()`（フラグを戻し、自分の手番かつ `Idle` ならスピンを再有効化）を使う。**決定側で発行できたらフラグは戻さない**（続けて走る適用側の `finally` が `EndItemEffect` するため）。効果はターンを消費しないので、終わればそのままルーレットを回せる。
- **実装例＝陣地獲得（`StealTerritory`）**。決定＝`DecideTerritoryStealAsync`（`async UniTaskVoid`・`_destroyCt` で `Forget`）が `TerritoryModel.CellsNotOwnedBy(_humanPlayer)` で対象マス（未占拠＋相手占拠）を出し（0 個なら発行せず終了）、`SelectTerritoryCellAsync` でマス選択を待って選んだ index を発行する。対象マスは `board-cell--selectable`（金枠）＋重ねた `board-cell__glow` を `AnimateSelectableGlowAsync` が ping パルスで駆動して強調し、ガイドバナー（`TerritorySelectBanner`）を出す。選択入力は `BoardZoomController.BeginCellSelection(Func<Vector2,bool>)`／`EndCellSelection` に委ね、**ドラッグ層でタップ（ほぼ動かず離す）とパン（動かす）を振り分ける**（盤面タップとドラッグパンを両立）。タップ位置は `cell.worldBound.Contains(screenPos)` で対象マスに当てる。選択結果は `UniTaskCompletionSource<int>`（キャンセル・破棄で -1）で受け取る。適用＝`ApplyTerritoryStealAsync` が既存の旗演出（`PlayTerritoryFlagSequenceAsync`）→ `ApplyTerritoryLanding`（占拠・必要数〔総数÷プレイヤー数の切り上げ〕到達で勝利）を再利用する。
- **実装例＝ミニゲーム（`MiniGame`）**。決定＝`DecideMiniGameAsync` が `MiniGameSelectPresenter.SelectAsync`（`MiniGameCatalog` をサムネイル画像＋ゲーム名のカード一覧にした選択モーダル・`UniTaskCompletionSource<MiniGameId?>`＝キャンセル/暗幕/破棄で null）で遊ぶミニゲームを選ばせ（null なら発行せず終了）、**「遊ぶゲーム」と「内容を組み立てる種」だけを発行**する。プレイと報酬の反映は適用側（`RunMiniGameAsync`）＝`MiniGameLauncher.PlayAsync`（Additive 起動）で遊び、賞金（`MiniGamePrize`）・結果発表の帯（自分の結果＝順位／獲得したアイテム）・浮遊テキストを `AwardMiniGameAsync` が出す（**被っちゃやーよだけは賞金ではなく勝った人が選んだアイテムを手札へ配る**＝`AwardMiniGameItemsAsync`）。**オンラインでは全員が同時に遊ぶ**ので各自の結果値を持ち寄って `MiniGameRanking.Resolve` で勝者を決め、**一人用は自分が CPU 相手に遊ぶ**（`RunLocalMiniGameAsync`・勝敗は `DetermineMiniGameWin`＝各ゲームがスコア 1=勝ちで報告する共通判定＝`Score==1`）。
- **実装例＝お金よこどり（`StealMoney`／ユーザー操作の無い抽選）**。決定＝`DecideMoneySteal`（同期メソッド）が、自分以外の参加者ごとに `MoneyStealRule.Amount(相手の所持金, _itemRng)`（相手の所持金が正のとき 20〜50％をランダムに奪う純粋ロジック・端数切り捨て・最低1・上限＝相手の所持金）で奪う額を集計し、**席 index をそのまま添字にした `int[]`** で発行する（合計 0＝奪える相手がいないなら発行せず終了）。適用＝`ApplyMoneyStealAsync` が相手から `MoneyModel.Add(seat, -amount)` で引いて使用者に合計を足し（**足すのは決めた額ではなく `Add` の返り値＝実際に減った額の合計**。所持金は 0 より下がらない〔`MoneyModel.MinMoney`〕ので、決定と適用の間に相手の所持金が動いていても奪った額と失われた額が食い違わない）、増減を `BoardLandingPresentation.ShowMoneyFloatAsync` で見せる（**演出は画面の持ち主から見た向きで出す**＝使った本人は「+ 合計」、奪われた席は「− 失った額」＋「〔キャラ名〕にお金を奪われた！」の帯、どちらでもない席には出さない。上の「適用側の演出は『画面の持ち主から見た向き』で出す」を参照）。**奪う額の乱数ルールはモデルに持たせず純粋クラス（`MoneyStealRule`）へ切り出す**と `System.Random` で seed 固定して EditMode テストできる（`MoneyCellRule`／`RouletteMath` と同じ方針）。
- **実装例＝お金アップ（`MoneyUp`／値を 1 つ決めるだけの効果）**。決定＝`DecideMoneyUp`（同期メソッド）が `MoneyCellRule.Amount(_itemRng)`（お金アップのマスと同じルール）で増える額を抽選して 1 つの効果パラメータで発行し、適用＝`ApplyMoneyUpAsync` が `MoneyModel.Add` で足して浮遊テキストを出す（お金よこどりと同じく**画面の持ち主から見た増減**だけ）。**ショップに並ばないアイテムでも手札には入る**（被っちゃやーよで誰とも被らずに選んだときの報酬）ので、`purchasable: false` でも効果は実装する。
- **実装例＝勝利（`InstantWin`／最小の効果）**。決めることが何も無い効果は `HandleItemUse` の `default` 分岐（`BeginItemEffect()` → 効果パラメータなしで発行）だけで済む。適用側は `_model.SetWinner(seat)` を呼ぶ 1 行（`BoardModel.SetWinner` は確定済みなら上書きしない）。勝者テキスト・「ホームに戻る」ボタン・決着エフェクトはすべて既存の `BoardModel.Winner` 購読が担うため、効果側で演出を書く必要はない。
- **選択モーダルの sortingOrder 罠**：`ItemModalPresenter`／`MiniGameSelectPresenter`／`ItemShopPresenter` はいずれも開いている間だけ Board の `UIDocument.sortingOrder` を 100 へ持ち上げて元へ戻す。詳細モーダルの「使用する」から効果を起こすと、効果側が別のモーダルを開くのと詳細モーダルの `Close`（sortingOrder 復元）が同フレームで競合し、持ち上げ済みの値を base として取り込んで戻らなくなる。効果側で選択モーダルを開く前に `await UniTask.Yield(PlayerLoopTiming.Update, ct)` を 1 回挟み、詳細モーダルの `Close` を先に完了させてから開く（`DecideMiniGameAsync` 参照）。
- **盤面タップを拾えるようにする**（`BoardZoomController`）：`BeginCellSelection` 中はドラッグ層を常に `PickingMode.Position` にして（盤面が画面内に収まっていてもタップを拾う）、`OnPointerUp` で押下からの最大移動量が閾値以下ならタップとしてコールバックを呼ぶ。`UpdateInteractivity` も選択中は反応を切らない。`EndCellSelection` で通常（盤面がはみ出すときだけ有効）へ戻す。

---

## 13. 複数シーンで同じ UI を出すときは「共通 UI コントローラ」を Main に切り出す

同じ見た目・操作の UI を別シーンでも出したくなったら、Presenter に丸ごと再実装せず、**渡された要素を組み立てる plain C# コントローラ**に切り出して共用する（`MapPickerView`＝マップ選択のカード一覧＋大プレビュー＋イベント内訳＋選択状態。`MapSelect` シーンとオンラインのルーム作成マップ選択オーバーレイ〔`Matching`〕が共用）。

### 手順・注意
- **置き場所は Main**（`Main/Board/`）。ゲームデータ型（`BoardCatalog`／`BoardDefinition` 等）にしか依存しないなら Main が正しい住所。使う側のアセンブリ（`MapSelect`／`Matching`）は Main を参照する（`Matching` は `BoardCatalog`／`MapPickerView` 参照のため Main 参照を追加した。`Main` は両者を参照しないので循環しない）。
- **コンストラクタで要素（グリッド・プレビュー・ラベル群）を受け取り**、`Build(catalog, initialId)` で組み、`SelectedId`／`HasSelection`／`Selected`（選択変化イベント）を公開する。SE・確定/キャンセル・シーン遷移の配線は**呼び出し側の Presenter が持つ**（コントローラは UI 構築と選択状態だけに専念）。
- **Presenter からロジックを消して委譲に置き換える**（`MapSelectPresenter` の `BuildCards`／`UpdateSelection`／`UpdateStats` を削除しコントローラへ）。純粋な描画ヘルパ（`BoardSchematicView`）も Main 型のみ依存なら MapSelect 等のシーンアセンブリから Main へ移す。
- **USS クラス名は共通**（`map-card`／`map-thumb`／`map-name`／`map-card--selected`／`ms-stat*`）。コントローラが同じクラス名で要素を組むので、**埋め込む各シーンの USS に同じクラスを定義する**（スタイルシートはシーンごとに別なので、クラス定義は各 USS に複製する＝USS の重複は許容）。
- **コンテンツに合わせて寸法を変えたいときは「USS の寸法＝基準ボックス」にする**。埋め込む側ごとに適切な大きさは違う（`.ms-preview` は 320px・`.mp-preview` は 220px）ので、コントローラが px を直書きせず、**初回の `GeometryChangedEvent` で `resolvedStyle` の寸法を 1 度だけ覚えて**、その中へ内接させた値をインラインの `style.width`／`style.height` に設定する（`MapPickerView.ApplyPreviewAspect`＝大プレビュー枠を選択マップの縦横比に合わせる）。覚えるのは 1 度きりにしないと、自分が設定した寸法を基準として読み直して縮み続ける。オーバーレイは開くまで `display:none` で寸法が 0 なので、**0 のときは覚えずに次の geometry を待つ**。極端な比率（一直線のマップ＝縦横比 10:1 等）で枠が潰れないよう `Mathf.Clamp` で丸める。
- **全画面オーバーレイに埋め込むとき**は、そのシーンの UXML に「プレビュー＋ラベル＋グリッド（`ScrollView` でも可＝`Add`/`Clear` は contentContainer に効く）＋確定/閉じるボタン」を用意し、Presenter が `display` トグルで開閉する。開くたびに `Build(catalog, 確定中ID)` で選択状態を作り直せば「キャンセルを引きずらない」挙動になる。

---

## 14. オンライン同期は「決定」と「適用」を分けてアクションストリームで配る

盤面の進行を全クライアントで一致させるとき、**各クライアントが自分でゲームを進めて結果だけ突き合わせる**設計にすると、乱数・モーダル操作・演出タイミングのどれか 1 つがズレただけで盤面が食い違う。代わりに**「ゲームを進める決定」を 1 本のストリームに流し、全員が受信した順にだけ適用する**（実装: [Assets/Scripts/Main/Online/](../Assets/Scripts/Main/Online/)、設計の全体像は [networking.md](networking.md)「ゲーム進行の同期」）。

```
決める人（手番の人／着地した人／アイテムを使った人）
   └─ OnlineGameSync.Publish(GameAction)
         ├─ ホスト : 自分以外へ再配信してから自分のキューへ積む
         └─ ゲスト : ホストへ送るだけ（適用しない）
        全員（決めた本人も含む）: 受信したアクションだけを適用
```

- **決めた本人も一度ネットワークを往復させてから適用する**。これが肝で、ホストが唯一の順序付け役になるので全クライアントの適用順が必ず一致する。「自分の分だけ先に適用する」最適化をすると順序が崩れる。
- **一人用モードも同じストリームを通す**（`Publish` が即ローカルのキューへ積まれるだけ）。オンライン用の `if` が進行コードに散らず、片方だけ壊れる事故が減る。`OnlineGameSync.IsLocalDecider(seat)` が「その席の決定を自分がするか」を吸収する（オンライン＝自席のみ／一人用＝全席）。
- **決定と適用を必ず分ける**。乱数を引く・モーダルで選ばせる・ミニゲームを遊ぶといった「1 人しかできないこと」は決定側に置き、`Model` の更新と演出は適用側に置く。適用側は全クライアントで走るので、相手の画面にも同じ演出が出る。
- **適用側の演出は「画面の持ち主から見た向き」で出す**。適用が全クライアントで走るということは、**決めた人の視点で書いた演出がそのまま相手の画面にも出てしまう**ということでもある。誰かの得が誰かの損になる効果では、`_humanPlayer`（自分の席）と照らして出す内容を変える。例＝お金よこどり（`ApplyMoneyStealAsync`）は、使った本人には「+ 奪った合計」、奪われた席には「− 自分が失った額」、どちらでもない席には何も出さない（`Model` の更新＝送金は全員が同じように行い、**演出だけを視点で分ける**のがポイント）。相手の操作が画面に映らない側には、何が起きたのかを帯（`ShowBannerText`）で添える（購入の知らせ＝`ApplyShopResultAsync` と同じ）。
- **決める人が席に紐づかないならホストに決めさせる**。「誰が着地したか」と関係なく 1 人が引けば足りる抽選は、着地した人ではなく**ホスト**が決めて配る（`OnlineGameSync.IsHost`＝一人用モードは自分がホスト扱いなので同じ経路を通る）。例＝マスの文言（`GameAction.CellMessage`・`BoardPresenter.ResolveCellMessageAsync`）。見た目だけの値でも、同じマスに止まったのに席ごとに違う文言が出ると同じ画面を見ている感じが崩れるので配る。**この待ちは着地演出の先頭に置き**、着地した人が配る `MoneyLanding` などより必ず先に流れるようにする（下の「同時に待つのは 1 箇所」「想定外のアクション」と同じ、交差させない話）。演出ごとスキップする分岐（すでに自分が占拠している陣地マス）は**配ってもらう前に**判定して、誰も受け取らないアクションをストリームに残さない。
- **配るのは復元できない情報だけ**。コマ移動・陣地占拠・勝敗判定は「誰が何マス進むか」と盤面データから決定論的に導けるので送らない。ルーレットも `(進む人, 出目)` とセクターの割り当てが 1 対 1 なら **整数 1 つ**で足りる。ペイロードは小さいほど食い違いの余地が減る。
- **乱数でも「種を共有できる」なら配らずに済む**。着地の乱数（お金の増減額・進む/戻るのマス数・マスの文言）は引いた人が配るが、**ルーレットの出目の割り当て（スピンのたびに 1〜6 から重複なしで引き直す）は配っていない**。全員が同じ値を持つ「セッション ID（＝基準種）」と「スピン回数（＝手番ループを回った数）」から各クライアントが同じ表を組めるため（[RouletteNumberLayout.cs](../Assets/Scripts/Main/Roulette/RouletteNumberLayout.cs)）。**導出に倒してよいのは次の 2 つを両方満たすときだけ**：(1) 種の材料が全クライアントで必ず同じ値になる（`string.GetHashCode` は実行ごとに変わり得るので使えない＝`StableHash` / `MixSeed` で自前計算する）、(2) 引く回数と順序がそろう（＝アクションストリームで駆動される 1 本のループから引く）。どちらか怪しいなら素直に配る——**乱数の食い違いは誰も気づけないまま「進むマス数だけがずれる」という最悪の壊れ方をする**。
- **「1 人しか操作できない時間」は待っていることを画面に出す**。モーダルやミニゲームの間、他のクライアントは次のアクションを待つだけなので画面が固まって見える。ここでも配る／導出するの線引きは同じで、**相手の手番のルーレット待ち・アイテムショップのように全員が同じ値（手番・ルーレット状態・着地マス）から導けるものはローカルで導出**し、モーダル操作のように相手から見えないものだけ `GameAction.Busy` を配る。`Busy` は盤面を進めないお知らせなので、受信側は表示を切り替えて次のアクションを待ち続ける。解除は「結果のアクションが届いたら自動／キャンセル時だけ `Busy(None)` を配る」の 2 通りで、**成功パスで解除を配らない**のがポイント（結果と解除が二重に流れると順序を気にする羽目になる）。
- **受信は必ずキューにバッファする**（`ActionStream`）。「待つ直前にハンドラを登録」だと受信が先に来たぶんを取りこぼす（[networking.md](networking.md) 8）。接続確立時に 1 度だけ永続登録し、待機側は「キューにあれば即取得、無ければ待つ」にする。
- **同時に待つのは 1 箇所だけにする**。`ActionStream.NextAsync` は 2 箇所から待つと `InvalidOperationException` を投げる（進行の組み立て違いを早期に検出するため）。所有権は「手番待ち＝`GameFlowController`」→「着地待ち＝`BoardPresenter`」と受け渡す。
- **想定外のアクションが来ても止まらないようにする**。期待と違う種別が届いたら、取りこぼすと困るもの（アイテム使用）は適用し、それ以外は警告ログを残して読み飛ばす。ハングよりログのほうが原因を追える。
- **「自分か」は参加者種別ではなく席で判定する**。オンラインは `GameParticipants` が**全席を `PlayerKind.Human`** で作るので、`KindOf(player) == PlayerKind.Human` は相手の席でも真になる（一人用は Human が自分だけなので表面化せず、オンラインで初めて壊れる）。自分の席は `OnlineGameSync.MySeat`（オンライン＝ロビーで確定した席／一人用＝0）で見る。`PlayerKind` は「手動で操作するか CPU が自動で回すか」の区別に使い、「自分／相手」の区別には使わない。
- **切断は「まず一時停止、猶予切れで打ち切り」として扱う**。`NetworkManager.OnClientDisconnectCallback` を監視し、猶予（60 秒）のあいだ復帰を待ってから、内部 `CancellationTokenSource` を `Cancel` して待機中の `NextAsync` を解く（各 `async` ループの `catch (OperationCanceledException)` がそのまま受け止める）。**ゲスト同士には切断が伝わらない**（NGO はクライアント同士を繋がない）ので、一時停止（`GameAction.Pause`）も退出通知（`GameAction.Leave`）もホストが残り全員へ配る。復帰の仕組みは後述の「切断からの復帰」。
- **接続のライフサイクルはシーンではなくセッションに預ける**。Relay を使うと NGO の起動・停止は UGS セッション（`WithRelayNetwork()` / `ISession.LeaveAsync`）が握るので、`NetworkManager` は `Common` に常駐させ、アプリから `StartHost` / `Shutdown` を呼ばない。代わりに**ゲームを抜けるとき必ずセッションを離脱する**責務が生まれる（忘れるとルームに残り続ける）。**対象は「ホームに戻る」のような対戦画面の導線だけではない**——`Common` 常駐のオプションモーダル（「タイトルへ戻る」）のように全シーンから押せる導線も、オンライン中に押されうる以上は同じ後始末が要る。また `LeaveAsync` は NGO も閉じるので**自分で抜けたときも自分の切断コールバックが発火する**。「相手が退出しました」のような通知を出しているなら、`GameSessionModel.HasSession`（離脱は await の前に `Session` を手放す）で自発的な離脱と見分けて抑える。詳細は [networking.md](networking.md)「Relay 経由の接続」。

### 参加者が非同期に集まる画面は「全員そろってから」操作させる

シーン遷移やアセットロードの時間差で、**同じ画面に全員が同時に到着するとは限らない**。到着前の相手を巻き込む操作（キャラの取り合いなど）を許すと、集計してくれる相手がいないまま待ちに入って進行が止まる。

- **「到着した」の判定は、画面が出たことではなく同期ループが動き出したこと**にする。オンラインのキャラ選択ロビーでは「自分のプレイヤープロパティを 1 度でも書いたか」で数える（`CharacterLobbySync.CountPresent`）。画面表示だけを条件にすると、まだ通信していない相手を到着扱いしてしまう。
- **ロジック側と UI 側の両方で塞ぐ**。`CharacterLobbySync.Select` / `Confirm` が `AllPresent` を見て弾き、Presenter もカードと確定ボタンを `SetEnabled(false)` にする。UI だけだと別経路（テスト・将来の入力）から抜けられる。
- **待っていることを画面に出す**（「他のプレイヤーの参加を待っています...（2/4人）」）。無言で操作を受け付けないと、固まったのか待ちなのか区別できない。
- **取り合いになる初期値は席順でずらす**。全員に同じ初期選択を与えると到着した瞬間から取り合いが起きるので、`CharacterCatalog.DefaultFor(seat)` のように「席 index → 選択肢 index」で配る（[CharacterLobbySync.cs](../Assets/Scripts/OnlineCharacterSelect/Sync/CharacterLobbySync.cs) の `ApplyInitialSelection`）。初期状態で衝突しないので、誰も操作しなくても集計だけで成立する＝「決定」を押すだけで先へ進める。**席の求め方は確定後の並び（ロースター）と同じ比較で共有する**（`CharacterClaimResolver.SeatIndexOf` と `BuildRoster` が `CompareJoin` を共有）。ここがずれると、ロビーで見せた席と本番の席が食い違う。

### 切断からの復帰は「スナップショット」より「取りこぼした決定の再送」

アクションストリームで進行を組んでおくと、再接続の実装がぐっと軽くなる。**盤面・所持金・アイテム・陣地をまるごとシリアライズして送る必要がない**——ホストが配信時に通し番号を振って台帳に残しておけば（`GameAction.Seq` / [ActionLog.cs](../Assets/Scripts/Main/Online/ActionLog.cs)）、復帰したクライアントは「seq N まで受け取った」と申告するだけで、N+1 以降を送り直してもらえば追いつける。

- **適用が通常の受信経路を通る**のが最大の利点。スナップショット専用の「状態を流し込む」コードパスを作ると、演出・消費・購読の更新が本流と二重管理になり、片方だけ壊れる。
- **通し番号は二重適用の防止にも使う**（受信済みの番号以下は捨てる）。再送が重複しても壊れない。
- **前提は「アプリが生きている」こと**。ローカルの Model が残っているから差分だけで足りる。アプリ再起動からの復帰まで求めるなら、結局スナップショットが要る。
- **切断中に自分が発行したぶんは送信キューへ**積み、復帰後に発行順で流す（送信失敗もキューへ回す）。捨てると全員が待ち続ける。
- **復帰までは進行を止める**。「切断＝即終了」ではなく「一時停止 → 猶予 → 終了」にして、待っている側は入力を閉じて理由を表示する。猶予切れの振る舞いは従来の終了処理をそのまま使えばよい。
- **一時停止・復帰・申告のような「進行を進めない連絡」はストリームに載せない**。専用チャンネルで送って受信側が即処理すれば、進行の待ち受け側（`WaitForSpinAsync` / `WaitForActionAsync`）を一切変更せずに済む。順序付けが要るのは盤面を動かす決定だけ。
- **自発的な離脱は通信断と区別する**（前項の「接続のライフサイクル」参照）。区別しないと、抜けた人を猶予いっぱい待ってしまう。

### 新しいアクションを足す手順

1. [GameActionType.cs](../Assets/Scripts/Main/Online/GameActionType.cs) に種別を 1 つ足す（引数の意味を doc コメントに書く）
2. [GameAction.cs](../Assets/Scripts/Main/Online/GameAction.cs) に静的ファクトリと名前付きプロパティを足す（引数の添字を呼び出し側に散らさない）
3. 決定側（1 人だけ通る場所）で `_sync.Publish(...)`、適用側（全員が通る場所）で受信して反映
4. [GameActionCodecTests.cs](../Assets/Tests/EditMode/GameActionCodecTests.cs) に往復テストを 1 本足す（JSON 化はここだけの責務なので純粋にテストできる）

進行を進めない連絡（一時停止・復帰など）は 1〜2 だけ行い、3 の代わりに制御チャンネル（`SGRK_Control`）で送って `OnlineGameSync.OnControlReceived` で処理し切る。

---

## 15. 新しいマスイベントを追加する

盤面のマスに割り当てるイベント（`BoardCellEvent`）は、色・ラベル・説明・文言・画像・記号・内訳がそれぞれ別のファイルに散っている。**1 つ足す／消すときの触る場所はこの 8 か所**。

1. [BoardCellEvent.cs](../Assets/Scripts/Main/Board/BoardCellEvent.cs) に種別を 1 つ足す（**値は末尾に追加し、既存の値は絶対に動かさない**。`BoardDefinition` アセットに int で保存されているので、詰めると保存済み盤面のイベントが全部ずれる。廃止するときも値は欠番のまま残す）
2. [BoardEventColors.cs](../Assets/Scripts/Main/Board/BoardEventColors.cs) に色を足す（盤面エディタのグリッド・凡例と、マップ選択のサムネイル・内訳が同じ配色を共有する）
3. [BoardEventLabel.cs](../Assets/Scripts/Main/Board/BoardEventLabel.cs) に日本語名を足す（凡例・内訳チップ・マス説明モーダルの見出し）
4. [BoardEventDescription.cs](../Assets/Scripts/Main/Board/BoardEventDescription.cs) に説明文を足す（マスをタップしたときの説明モーダル本文）。**金額やマス数のような「ルール側で決まっている数値」は各ルールの定数から組み立てる**（お金マスは `MoneyCellRule.Unit`/`MinN`/`MaxN`、進む/戻るマスは `MoveCellRule.MinSteps`/`MaxSteps`、ミニゲームマスは `MiniGamePrize`）ので、ルールを変えれば説明も一緒に変わる。**文章は「このマスに止まると、〜」で始めて他のマスとそろえる**。足し忘れると通常マスと同じ文言のままになる（EditMode テストが検出する）
5. [BoardCellMessageDefaults.cs](../Assets/Scripts/Main/Board/BoardCellMessageDefaults.cs) に着地時の既定文言プールを足す（止まったときにマス画像の下へ出すフレーバーテキスト。**1 種別につき 10 件並べて着地のたびに 1 つ抽選する**ので、単数形の「説明文」ではなく配列で用意する。書き方の決まりはファイル冒頭のコメントにある＝**文末に「。」は使わない**〔言い切り・「！」・「…」・「？」で終える。EditMode テストが検出する〕・**全角 14 文字以内なら 1 行に収まる**〔`.cell-message` の `max-width: 94%` ／ `font-size: 28px` ／ `-unity-text-outline-width: 2px` から決まる＝縁取りは字送りに効くので全角 1 文字は 32px 占める。超えても折り返して出せるが 2 行になる。Cell Message Editor が行ごとに文字数を出す〕）。足し忘れると通常マスの文言が出る（EditMode テストが検出する）。**実際に使う文言は `BoardCellMessageCatalog` 資産**（`Window > Sugoroku > Cell Message Editor`）なので、既存の資産にはウィンドウで足した種別の欄が空で現れる＝そこにも文言を入れる（空のままだとそのマスでは文言が出ない）
6. [BoardEventTally.cs](../Assets/Scripts/Main/Board/BoardEventTally.cs) の `DisplayOrder` に足す（マップ選択の内訳と **Home のルール説明の「マスの種類」**に出す順。入れないとどちらにも出ない＝EditMode テストが検出する）
7. マス画像を使うなら [BoardEventArtCatalog.cs](../Assets/Scripts/Main/Board/BoardEventArtCatalog.cs) の `Address` にアドレスを足し、画像を `Assets/AddressableAssets/Image/Board/` に置いて **Addressable アドレスを `Board/<イベント名>`**（＝enum 名。通常マス〔`None`〕だけは素材名のままの `Image/Board/Glass`＝`NoneAddress` という例外）に設定する。画像を用意しないイベントは空文字のままで、`BoardPresenter.EventMarker` の記号表示にフォールバックする。**マスごとのデータで絵を変えたいときは `AddressFor` に分岐を足す**（いまは分岐なし＝どのイベントも種別ごとの共通画像。ミニゲームマスはかつてマスに設定したゲームのサムネイルを使っていたが、遊ぶゲームを抽選にした時点で「マスの絵で特定のゲームを指せない」＝共通画像に戻した）
8. マスごとに設定する値が要るなら [BoardCellInspector.cs](../Assets/Scripts/Main/Editor/BoardCellInspector.cs) に入力欄を足す。**ただし今は入力欄が「イベント種別」と「色」しかない**：数値パラメータ `Amount` も遊ぶミニゲーム `MiniGame` も、着地のたびのランダムに移行して誰も読まなくなった（フィールドは保存済みアセットとの互換で残してある）。増やす前に「本当にマスごとに固定したいのか、着地のたびの抽選でよくないか」を確かめる

盤面エディタの凡例は `Enum.GetValues` で自動生成されるので、1〜3 を足せば勝手に並ぶ。

**イベント種別ごとの静的カタログはこの並び（色・ラベル・説明・文言・画像）で揃えてある**（文言だけは、再コンパイルなしで直せるよう静的な既定値の上に `BoardCellMessageCatalog` 資産を重ねてある）ので、新しい「種別ごとに変わる見せ方」を足すときも `switch` 1 つの静的クラスにして同じ場所に並べる（`BoardDefinition` 側にフィールドを増やさない＝盤面ごとに設定させると全マップぶん手で埋める羽目になる）。マスの絵と同じく**全マップ共通**なのが既定で、盤面ごとに変えたいものだけ `BoardDefinition` が持つ（枠画像・背景画像がその例）。

### 着地時の効果を実装する

着地の分岐は `BoardPresenter.PlayLandingSequenceAsync`（マス種別ごとの演出）と `ApplyLandingEventAsync`（Model 更新）にある。**判定は `CellEventResolver` の純粋関数に置いて EditMode テストで固める**（`TryGetMoneyDelta`／`TryGetMoveSteps`）。

- **盤面データから導ける効果はオンラインで配らない**（[#14](#14-オンライン同期は決定と適用を分けてアクションストリームで配る)）。着地マスも「そのマスが何のイベントか」も全員が同じ盤面から分かるので、`GameAction` を増やさずに全クライアントが同じ結果になる。**乱数やモーダル操作を含む効果だけ**が「決定（1 人）＋発行」と「適用（全員）」に分かれる（お金マスの増減額・進む/戻るのマス数・アイテムショップ）
- **効果をランダム化したら、その値は配る側へ移す**。進む/戻るのマス数はもともと `Amount`（盤面データ）だったので配らずに済んでいたが、着地のたびのランダム（`MoveCellRule`）にした時点で全クライアントで一致しなくなり、`GameAction.MoveLanding` が必要になった。**あわせて「演出で見せた値」と「実際に適用する値」を同じ 1 つの値にする**（受信値を覚えておいて連鎖に使う＝`BoardPresenter.TryGetChainedSteps`）。盤面データから引き直すと、浮遊テキストは「+3 マス」なのに 5 マス動く、といった食い違いになる
- **効果がコマを動かすなら連鎖の上限を決める**。進む／戻るは `AdvanceAsync` が「移動 → 着地 → また移動」を繰り返し、`MaxChainedMoves`（8 回）で打ち切る。上限は定数なので全クライアントで一致し、打ち切っても結果はずれない
- 演出は使い回す。「+ n マス」の浮遊テキストはお金の増減額と同じ `ShowFloatTextAsync`（[BoardLandingPresentation.cs](../Assets/Scripts/Main/Board/BoardLandingPresentation.cs)）で、文言と色分けの元になる値だけを差し替えている
- **順に見せる必要がない演出は `UniTask.WhenAll` で並走させる**。着地は 1 マスごとに待つので、演出を直列に足すたびに手番が延びる（陣地マスは「文言 2 秒 → 旗演出 1.85 秒」で 4 秒近くあった）。並走させるときは**画面中央の取り合いを配置で避ける**：陣地マスの旗ポップは中央 200px を占めるので、文言は `.cell-message-row--alone`（中央寄せ）を掛けず、画像ありの着地と同じ「中央の少し下」（`.cell-message-row`）に置く＝`ShowCellMessageAsync(message, centered: false, ct)`。**片方だけ位置を変えたいときは USS クラスの付け外しを引数で選べるようにする**（`BoardLandingPresentation` の `centerWhenAlone`）ので、既存の呼び出し側の見た目は変わらない

---

## 16. 同じ実体を複数の系統で参照するアセットは、アドレスを「識別子＋系統名」で機械的に決める

キャラは Card / Icon / Portrait / Run / Flag の 5 系統の画像を持ち、[CharacterCatalog.cs](../Assets/Scripts/Common/Character/CharacterCatalog.cs) が 1 行に 5 つのアドレスを並べて持つ。ここでアドレスの付け方が系統ごとにバラバラだと、**行の中の 1 つだけ別のキャラを指していても誰も気付けない**（走行絵だけ `Image/<動物名>Run` 規約で、素材名のアルファベット順に並べたせいで 8 キャラ全員ぶんズレていた実績がある。表示は出るので画像の欠落としても検出されない）。

- **アドレスは `<識別子>/<系統名>`（キャラなら `Character/Character<N>/Run`）に統一する**。素材のファイル名をアドレスに出さないので、行内の 5 つが同じ `Character<N>` を指しているか目視で照合できる
- キャラを 1 人足すときは、画像 5 枚を `Assets/AddressableAssets/Image/Character/` 配下に置いて Addressables のアドレスをこの規約で付け、[CharacterId.cs](../Assets/Scripts/Common/Character/CharacterId.cs) に enum 値を 1 つ、`CharacterCatalog.All` に 1 行足すだけでよい（表示順＝席順ごとの初期キャラ `DefaultFor(seat)` にもそのまま効く）
- **アセットの実体名とアドレスがズレる規約は避ける**。Addressables のアドレスはリネームしてもファイルの GUID を保つので、後からでも揃え直せる（アセット参照は切れない）

---

## 17. ルール説明はゲーム側のカタログから組み立てる

Home の「ルール説明」モーダル（[RuleBook.cs](../Assets/Scripts/Home/Presenter/RuleBook.cs)）は、遊び方の文章を**自分では持たない**。マス・アイテム・ミニゲームの名前や効果は、ゲーム内の説明モーダルが使っているのと同じ情報源から引く。

| 出すもの | 引く先 |
|---|---|
| マスの名前・色・効果・並び順 | `BoardEventLabel` / `BoardEventColors` / `BoardEventDescription` / `BoardEventTally.DisplayOrder` |
| アイテムの名前・効果・価格 | `ItemCatalog.Purchasable`（`ItemDefinition.Description` / `Price`） |
| ミニゲームの名前・遊び方 | `MiniGameCatalog.All`（`MiniGameDefinition.Description`） |
| ミニゲームの報酬（順位別の賞金・被っちゃやーよの獲得アイテム） | `MiniGamePrize.RankPrizeText()` / `MiniGamePrize.OverlapRewardText()` |
| お金の増減額・初期所持金・プレイ人数 | `MoneyCellRule.RangeText()` / `MoneyModel.InitialMoney` / `PlayerCountSessionModel.Min`・`Max` |
| ルーレットの数字の範囲 | `RouletteNumberLayout.MinNumber`・`MaxNumber` |

- **ルールを変えたらルール説明も一緒に変わる**。賞金額や進むマス数を書き写さないので、「ゲーム内の説明は直したがルール説明が古いまま」が起きない。`RuleBook` に直接書くのは、**どのクラスも単独では持っていない「遊びの流れ」だけ**（勝ち方・手番とルーレット・画面の見かた）
- **カタログに足したものは自動で並ぶ**。マスイベント（[#15](#15-新しいマスイベントを追加する)）・アイテム（[#12](#12-新しいアイテムを追加する)）・ミニゲーム（[#8](#8-新しいミニゲームを追加する)）を足すと、それぞれの追加手順を踏むだけでルール説明にも載る。**書き忘れは `RuleBookTests` が検出する**（説明が空・`DisplayOrder` への追加漏れ・買えないアイテムが並んでいる）
- Home アセンブリはこの参照のため **Main を参照する**（Matching が `BoardCatalog` / `MapPickerView` のために参照しているのと同じ。`Main` は Home を参照しないので循環しない）
- **モーダルの開閉は [HomeModal.cs](../Assets/Scripts/Home/Presenter/HomeModal.cs) が持つ**（ルール説明・クレジットで共有）。`display` とフェードクラスの順序は [design-system.md](design-system.md)「アニメーション」の定石そのままで、Home にモーダルを増やすときは `.home-overlay` / `.home-modal-card` を付けて `HomeModal` を 1 つ作るだけでよい

---

## 共通ルール（抜粋）

- `var` は使わない。型を明示する
- フィールドは `_camelCase`、型・メソッドは `PascalCase`
- `Find()` / static 状態は使わない。DI で解決する
- UI は UXML + USS で構築。uGUI 禁止
- アセットロードは Addressables。`Resources.Load` 禁止
- USS では `gap` 禁止 → 子要素の `margin` で代替
