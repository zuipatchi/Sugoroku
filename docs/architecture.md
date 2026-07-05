# 設計ドキュメント

## 設計方針

新しいゲームを作り始めるときの「土台」として機能するテンプレート。以下を目標としている。

- **DI・リアクティブを標準** とし、`Find()` や static は使わない
- **非同期を UniTask** で統一し、キャンセル処理を明示的に行う
- **アセットは Addressables** で遅延ロードし、`Resources.Load` は使わない
- **UI は UI Toolkit（UXML）** で構築し、uGUI は使わない

---

## シーン構成

```
Common (常駐)
  ├── SoundPlayer
  ├── SceneTransitioner
  ├── TransitionPresenter
  ├── OptionPresenter / OptionModalPresenter / OptionModel
  ├── GameSessionModel
  ├── CharacterSessionModel / BoardSessionModel
  ├── MiniGameLauncher / MiniGameSessionModel
  └── Store 群（SoundStore / ModalStore ← AssetStoreBase を継承）

Title → Home ┬─（一人用モード）→ CharacterSelect → MapSelect ─→ Main
             └─（オンライン）→ Matching ─────────────────────→ Main
                                                                 └─（ミニゲーム）→ MiniGame（Main を残して重ねる）

（開発用）MiniGameTest → MiniGame（テストシーンを残して重ねる）  ※本番フロー外・エディタで直接起動
```

（すべて `Common` の上にアディティブでロード。画面は縦向き＝ポートレート固定）

- `Common` シーンは起動時にロードされ、以降アンロードされない
- 他シーンは `Common` の上にアディティブでロード・アンロードされる
- シーン遷移は `SceneTransitioner.Transit(Scenes next)` を呼ぶだけでよい
- 遷移時は `TransitionPresenter` が画面をフェードアウト→ロード→フェードインの演出を行う
- **Home で2モードを分岐**する。「一人用モード」は `GameSessionModel.SetSinglePlayer()` を呼んで `CharacterSelect`（キャラ選択）へ遷移し、キャラ確定後に `MapSelect`（マップ選択）へ、マップ確定後に `Main` へ進み、**CPU と 1 対 1 のすごろく対戦**を行う。「オンラインプレイ」は `Matching` を経由する（マップは既定＝カタログ先頭）
- **CharacterSelect** は選択中キャラの立ち絵を全画面背景に、カード絵の選択スロットを画面下部に表示する。各キャラは Addressables に 4 系統の画像アドレスを持つ（`Character/<名前>/Card`＝選択カード絵・`Character/<名前>/Icon`＝盤面コマの丸バッジ・`Character/<名前>/Portrait`＝立ち絵・`Image/<動物>Run`＝2Dレースミニゲームの走行絵）。CharacterSelect は Card と Portrait をロードし、盤面（`BoardPresenter`）はコマに Icon を、2Dレース（`RaceGamePlay`）は走者に Run を使う。未配置のアドレスは色面プレースホルダにフォールバックする。選択結果は Common シングルトンの `CharacterSessionModel` に保持し、`Main` でも参照できる（現状はオンライン非対応・一人用のみ）
- **MapSelect** は複数の盤面（`BoardDefinition`）から対戦マップを選ぶ。マップ一覧は `BoardCatalog`（ScriptableObject。`BoardDefinition` は SO 資産で静的クラスから参照できないため、キャラの `CharacterCatalog` とは異なりカタログ自身も SO にして資産参照のリストを持つ）が持ち、各マップは盤面の形を Painter2D で描く簡易サムネイル（`BoardSchematicView`・画像アセット不要）とマップ名（`BoardDefinition.DisplayName`。空なら資産名にフォールバック）でカード表示する。選択結果は Common シングルトンの `BoardSessionModel` に**識別子（マップ資産名）**として保持し、`Main` の `BoardPresenter` が `BoardCatalog.Find(識別子)` で実体を解決する（`CharacterSessionModel` と同型だが、Common から Main の `BoardDefinition` を参照できないため文字列 ID だけを持つ）。`BoardCatalog` 資産は `MapSelect` シーンの `MapSelectPresenter` と `Main` の `BoardPresenter` の両方にインスペクタで割り当てる。マップ未選択（オンライン等）や未割り当て時は `BoardPresenter._definition` にフォールバックする
- `Main` の `NetworkSessionStartup` は `GameSessionModel.Mode == SinglePlayer` のとき NGO を起動せず即 `Connected` 扱いにする（一人用モードはネットワーク非依存）
- **手番進行は `GameFlowController`（`Main/Turn/`）が統括する**。参加者は `GameParticipants` が `GameMode` から決める（一人用＝`[Human, Cpu]` の 1 対 1、オンライン＝`[Human]` の単独プレイ）。`GameFlowController` は接続完了を待ってから「手番プレイヤーを見る → 人間なら手動スピンの停止を待つ／CPU なら円盤を自動で回す → ルーレットが消える（`WaitForHideAsync`）のを待ってから出目ぶんそのプレイヤーのコマを進める → 勝者が出るまで `TurnModel.Next()` で交代」というループを回す。**勝敗は陣地マスの占拠で決まる**（周回ゴール勝利は廃止）ため、コマは 1 周で止まらず出目ぶんそのままスタート＝ゴールを通過してループし続ける。コマ位置は `BoardModel` がプレイヤーごとに保持し、勝者は着地イベントが `BoardModel.SetWinner` で確定する（`BoardModel.Winner` / `IsFinished`）。これまで各 Presenter に散在していた「ルーレット停止→コマ前進」「移動完了→ボタン再有効化」の購読チェーンを、このオーケストレータに集約した
- **陣地マスの占拠で勝敗が決まる**。マスのイベントに陣地マス（`BoardCellEvent.Territory`）があり、止まったプレイヤーがそのマスを占拠する（相手の陣地でも上書きで奪える）。占拠状態は `TerritoryModel`（`Main/Board/`・Scoped）が陣地マスの盤面 index ごとに保持し、盤面の陣地マス総数の**過半数**（`RequiredToWin` = 総数/2 + 1）を占拠したプレイヤーが勝つ。着地時に `BoardPresenter` が `TerritoryModel.Claim(player, index)` で占拠し、`HasMajority(player)` なら `BoardModel.SetWinner(player)` を呼ぶ。マスの色替えは各陣地マスの `Owner(index)` を Presenter が購読し、占拠プレイヤーの色（YOU＝金・CPU＝青緑）に塗り替える。陣地マスの index 一覧は DI に無いため `BoardPresenter` が盤面データから集めて `TerritoryModel.Initialize` に渡す。**陣地マスが 0 個の盤面は勝者が出ない**ため、盤面には陣地マスを配置しておくこと
- **所持金は `MoneyModel`（`Main/Money/`）がプレイヤーごとに保持する**（Scoped・初期 1000・マイナス＝借金も許容）。マスのお金イベント（`BoardCellEvent.MoneyUp` / `MoneyDown`）にコマが止まると `BoardPresenter` が `MoneyModel.Add(player, ±Amount)` で増減させる。盤面上部の自分ネームプレートが人間プレイヤーの所持金を購読して表示する（CPU の所持金も内部では増減するが表示は自分のみ）。将来のミニゲーム報酬も `MoneyModel.Add` を呼ぶだけの拡張点として空けてある。お金・陣地以外のマスイベント（進む/戻る/休み/ミニゲーム）は現状も表示のみで未発動
- **ミニゲームは `Transit` を使わない**。`Transit` は Common 以外の全シーンをアンロードするため、Main を経由すると盤面状態・NGO 接続が破棄される。`MiniGameLauncher.PlayAsync` が `MiniGame` シーンを **今のシーン（Main や動作確認用の `MiniGameTest`）を残したまま Additive で重ね**、終了後にミニゲームシーンだけをアンロードする（ランチャーは Common 依存のみでシーン非依存）。起動側（`MiniGameLauncher`）とミニゲームシーンのホスト（`MiniGameHostPresenter`）は Common シングルトンの `MiniGameSessionModel` を介して「遊ぶゲームの指定」と「結果の受け渡し」を行う。ミニゲームの中身（UXML）は `MiniGameId` の種別に対応する Addressable アドレスを `MiniGameCatalog` から引いてロードする（将来最大5種類。現状はタップ連打・2Dレースの2種）。`MiniGameHostPresenter` は `CurrentGame` で分岐するディスパッチャで、タップ連打は `TapGamePlay`、2Dレースは `RaceGamePlay`（いずれも DI Scoped のプレーンクラス）へ委譲する。動作確認は `MiniGameTest` シーン（カタログを自動でボタン一覧）から行い、本番フローには出さない。現状はローカル完結でスコア同期はしない

### なぜアディティブか

シーン単位で DontDestroyOnLoad を使わず、Common シーンを「永続レイヤー」として扱うことで
サウンド・オプション・シーン遷移を全シーンで共有できる。

---

## 依存性注入（VContainer）

```
CommonLifeTimeScope   全シーン共通のシングルトンを登録
  ├── GameSessionModel
  ├── CharacterSessionModel
  ├── MiniGameSessionModel
  ├── MiniGameLauncher
  ├── ModalStore
  ├── OptionPresenter
  ├── OptionModel
  ├── SoundPlayer
  ├── SoundStore
  ├── TransitionPresenter
  └── SceneTransitioner

TitleLifetimeScope            Title シーン固有のサービスを登録
HomeLifetimeScope             Home シーン固有のサービスを登録
CharacterSelectLifetimeScope  CharacterSelect シーン固有のサービスを登録
MapSelectLifetimeScope        MapSelect シーン固有のサービスを登録
MainLifetimeScope             Main シーン固有のサービスを登録
```

- 各シーンの `Injector/` フォルダに `*LifetimeScope.cs` を置く
- 新しいサービスは LifetimeScope に登録してコンストラクタでインジェクト
- シーンロード後の LifetimeScope 構築は `SceneExtensions.BuildLifetimeScopes()` 拡張メソッドが担う（BootLoader / CommonSceneLoader / SceneTransitioner から呼ばれる）

---

## 状態管理（R3）

Model → Presenter の単方向データフロー + 双方向バインディング。

```
OptionModel
  BGMVolume: ReactiveProperty<float>
  SEVolume:  ReactiveProperty<float>

OptionPresenter
  → BGMVolume.Subscribe で Slider を更新
  → Slider の ValueChanged で SetBGMVolume() を呼ぶ
```

- サブスクリプションは `AddTo(_disposables)` または `AddTo(destroyCancellationToken)` で管理
- Model は PlayerPrefs を通じて永続化する

---

## 非同期処理（UniTask）

- `IAsyncStartable` を実装したクラスは VContainer が StartAsync を呼ぶ
- `Store` 系クラスは起動時に Addressables ロードを行い、`UniTask Loaded` プロパティで完了を通知する
- 使う側は `await _store.Loaded` で待機してから使用する

```csharp
// 例: AudioManager (Title シーン)。動画と同時に Cheer を鳴らし、鳴り終わってから
// タイトル BGM（光晴イズム）へ移る。
public async UniTask StartAsync(CancellationToken cancellation = default)
{
    await _soundStore.Loaded;
    AudioClip cheer = _soundStore.CheerSE;
    if (cheer != null)
    {
        _soundPlayer.PlaySE(cheer);
        await UniTask.Delay(TimeSpan.FromSeconds(cheer.length), cancellationToken: cancellation);
    }
    _soundPlayer.PlayBGM(_soundStore.TitleBGM);
}
```

### MonoBehaviour のインジェクションタイミング

`CommonSceneLoader.Awake()` は `async void` で、`await UniTask.NextFrame()` の後に `BuildLifetimeScopes()` を呼ぶ。そのため **MonoBehaviour の `Awake/OnEnable/Start` が呼ばれる時点ではインジェクションが完了していない**。

| コールバック | インジェクト済みフィールドを使えるか |
|---|---|
| `Awake` / `OnEnable` / `Start` | **不可**（injection 前） |
| `[Inject] Construct(...)` | 可（injection と同時に呼ばれる） |
| `IAsyncStartable.StartAsync()` | 可（Build 完了後に VContainer が呼ぶ） |
| ユーザー操作イベントコールバック | 可（injection 完了後に発火） |

「シーン起動時にインジェクト済みフィールドを使って初期化したい」場合は、`Start()` ではなく `[Inject] Construct(...)` メソッド内で行うか、`IAsyncStartable` を実装した純粋 C# サービスを `RegisterEntryPoint` で登録してそこから MonoBehaviour の public メソッドを呼ぶ。

### シーン遷移のキャンセル処理

`SceneTransitioner` は `SemaphoreSlim` で同時遷移を防ぎ、
連打された場合は最後のリクエストのみ実行する（前の遷移は CancellationToken でキャンセル）。

### ISceneReady — シーン準備完了の通知

`RevealAsync`（フェードイン）の前に、`SceneTransitioner` は次シーンの root GameObject を検索し、`ISceneReady` を実装した**全ての**コンポーネントの `ReadyAsync(ct)` を `UniTask.WhenAll` で並行待機する。

これにより、Addressables の非同期ロードなど「表示前に完了させたい初期化」がフェードイン前に終わり、背景や要素が空白のまま画面が現れるのを防ぐ。

新しいシーンで表示前に待ちたい非同期処理がある場合は、そのシーンの Presenter に `ISceneReady` を実装し、準備完了時に `ReadyAsync` を完了させるだけでよい（実装が無いシーンは素通りする任意フック）。

`ReadyAsync` がキャンセル以外の例外を投げても、暗幕が残り続けないよう `SceneTransitioner` 側で例外をログ出力して握りつぶし、フェードインは必ず実行する（`WaitReadySafelyAsync`）。実装側で初期化失敗を扱いたい場合は `ReadyAsync` 内で完結させること。

ただし `ReadyAsync` を呼ぶのは `Transit` だけなので、`Title` のように `BootLoader` の素の `LoadSceneAsync` で直接開かれるシーンでは呼ばれない。直接起動もあり得るシーンは `Start` でも初期化を起動し `ReadyAsync` は完了待ちだけにする（[patterns.md](patterns.md) の「シーン表示前に非同期初期化を待つ」を参照）。

---

## サウンド設計

- BGM: `AudioSource.loop = true`、`PlayBGM()` で差し替え
- SE: `PlayOneShot()` で重ね再生
- 音量は `OptionModel.BGMVolume / SEVolume` (0–1) を ReactiveProperty で管理
- `SoundPlayer` は音量変化を Subscribe して AudioSource に即時反映

> `_bgmAudioSource.volume = v / 2` としているのは、
> OptionModel の値 1.0 がデフォルトの AudioSource 最大音量の半分に相当するようにしているため。

---

## UI 設計（UI Toolkit）

### ファイル配置

```
Assets/Scripts/<Scene>/<Feature>/
  ├── *Presenter.cs   （UI ロジック）
  └── *.uxml          （見た目 / Addressables 経由でロードするものは AddressableAssets/ に配置）
```

### PanelSettings

`Assets/Scripts/Panel Settings.asset` の Scale Mode を **Scale With Screen Size**、基準解像度を **540×960（縦）**、Screen Match Mode を **Match Width Or Height（Match=0＝幅基準）** に設定済み（ゲームは縦画面固定）。
基準解像度（幅）に対して UI 全体がスケールするため、固定 px 値で指定したサイズが解像度によらず適切な比率で表示される。**基準解像度を小さくするほど UI 全体（文字・ボタン・余白）が一律に大きくなる**ため、スマホで読みやすいよう幅 540 まで下げてある（実画面 1080 幅なら 2 倍表示）。全体の大きさを変えたいときは個々の USS の font-size ではなく、この基準解像度の X を調整する（小さく＝大きく表示）。新しい UI を px で組むときはこの幅 540 基準で考える。

### オプションモーダル

- アイコンクリックで表示、Close ボタンで非表示
- 「タイトルに戻る」ボタンでモーダルを閉じつつ Title シーンへ遷移
- オーバーレイ（`rgba(0,0,0,0.55)`）がゲーム画面を暗幕
- モーダルカードは画面中央に配置（`align-items: center; justify-content: center`）
- UIDocument の SortingOrder を 1000 にして他 UI より手前に表示
- モーダル内 UI バインド（スライダー・ボタン）は `OptionModalPresenter`（plain C# クラス）が担い、`OptionPresenter.SetupAsync()` 内で `new` して使う

---

## アセンブリ構成

スクリプトは 8 つのランタイム Assembly Definition と、1 つのエディタ専用アセンブリに分割されている。

| アセンブリ | パス | 依存 |
|---|---|---|
| `Common` | `Assets/Scripts/Common/` | VContainer / R3 / UniTask / DOTween |
| `Title` | `Assets/Scripts/Title/` | VContainer / UniTask / Common |
| `Home` | `Assets/Scripts/Home/` | VContainer / UniTask / Common |
| `CharacterSelect` | `Assets/Scripts/CharacterSelect/` | VContainer / R3 / UniTask / Addressables / Common |
| `MapSelect` | `Assets/Scripts/MapSelect/` | VContainer / R3 / UniTask / Addressables / Common / **Main**（`BoardCatalog` / `BoardDefinition` を参照するため Main にも依存する唯一のシーンアセンブリ） |
| `Matching` | `Assets/Scripts/Matching/` | VContainer / R3 / UniTask / Common / Unity.Services.Multiplayer / Unity.Netcode |
| `Main` | `Assets/Scripts/Main/` | VContainer / R3 / UniTask / Common / Unity.Netcode / DOTween |
| `MiniGame` | `Assets/Scripts/MiniGame/` | VContainer / R3 / UniTask / Addressables / Common |
| `Main.Editor` | `Assets/Scripts/Main/Editor/` | Main / Common（`includePlatforms: ["Editor"]`＝ビルド非対象。盤面エディタ用） |

- `Title` / `Home` / `CharacterSelect` / `MapSelect` / `Matching` / `Main` / `MiniGame` は `Common` に依存し、逆方向の依存は禁止
- `BoardSessionModel`（選択マップの識別子）は `Common` に置き文字列 ID だけを持つ。`BoardCatalog` / `BoardDefinition` は `Main` にあるため、それらを扱う `MapSelect` だけが `Common` に加えて `Main` にも依存する
- `Main.Editor` はエディタ専用（`Window > Sugoroku > Board Editor`）。参照は推移解決されないため対象ランタイム asmdef の GUID を明示する（[patterns.md](patterns.md) #11）
- `autoReferenced: true` のため既存コードへの影響なし

---

## アセット管理（Addressables）

```
Assets/AddressableAssets/
  ├── Icon/        SVG アイコン
  ├── Image/       キャラのカード絵/コマ用バッジ/立ち絵（Character/<名前>/Card・/Icon・/Portrait）・2Dレースの走行絵（Image/<動物>Run）
  ├── MiniGame/    ミニゲームの UXML / USS
  ├── Modal/       Modal.uxml / Modal.uss
  └── Sound/       AudioClip
```

- `SoundStore` / `ModalStore` はともに `AssetStoreBase` を継承し、ボイラープレート（`UniTask Loaded`・`Start()`・try-catch）を共有
- `AssetStoreBase` は `IStartable` を実装し、`LoadAssetsCore()` をサブクラスに委譲する
- ロード完了は `UniTask Loaded` プロパティで通知

### 例外: 動画は StreamingAssets

WebGL は `VideoClip` アセットをサポートしないため、**動画だけは Addressables ではなく `Assets/StreamingAssets/` に置き、`VideoPlayer` を `VideoSource.Url`（`Application.streamingAssetsPath` 配下）で再生する**。これは WebGL / Standalone 共通で動く唯一の方式。タイトル背景動画（`TitleVideoPresenter` / `Assets/StreamingAssets/Video/TitleMovie.mp4`）がこれ。動画は Media Foundation / ブラウザ双方で確実に再生できるよう **H.264 baseline profile・`yuv420p`・BT.709 タグ付き**でエンコードしておく（main profile の B フレームや色情報未指定だと警告や色ズレ・タイムスタンプ補正が出る）。`VideoPlayer` は `RenderTexture` に描画し、UI Toolkit の背景要素（`background-image`）に貼る。

> **既知のエディタ専用症状**: エディタで Play を繰り返す（または `VideoPlayer` を2個目以降生成する）と `WindowsVideoMedia error 0x887a0005`（`DXGI_ERROR_DEVICE_REMOVED`）でデコードに失敗し、エディタを再起動するまで復帰しないことがある。これはエディタがプロセスを使い回すことによる D3D デバイス喪失で、**ビルドした実機（起動ごとに新プロセス）では再現しない**（Standalone / WebGL ビルドで毎回再生されることを確認済み）。`TitleVideoPresenter` は再生不可・準備タイムアウト時に文言だけ表示するフォールバックを持つので、黒画面で固まることはない。
