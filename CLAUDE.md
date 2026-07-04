# CLAUDE.md

このファイルはリポジトリで作業する Claude Code (claude.ai/code) へのガイダンスを提供します。

## プロジェクト概要

オンライン対戦すごろく。**縦画面（ポートレート）**。Unity 6 (6000.3.18f1) 製。BGM/SE 再生機能と、Commonシーンをベースとしたアディティブシーン管理、UGS Multiplayer Services によるオンラインマッチングを備える。Home で「一人用モード」「オンラインプレイ」を選択でき、一人用モードはネットワーク非依存で、キャラクター選択（CharacterSelect）を経て Main へ進み、**CPU と 1 対 1 のすごろく対戦**を行う（あなたが先攻、以降交互）。移動マス数は円盤ルーレットで決定する（ボタンを長押し中は回転し、離すと減速して止まった位置のセクターが出目。CPU の番は同じ円盤が自動で回る）。外周マスを並べたループ盤面で、手番プレイヤーのコマが出目ぶん進み、**先に 1 周ゴールした方が勝ち**。手番進行は `GameFlowController` が統括する（オンラインは参加者 1 人で従来どおり単独プレイ）。プレイヤーは**所持金**を持ち（`MoneyModel`・初期 1000・マイナス可）、お金アップ/ダウンのマスに止まると増減する（盤面上部の自分ネームプレートに表示）。ミニゲーム（タップ連打／CPU と競うタイミングメーター式の2Dレース）は Main を残したまま MiniGame シーンを Additive で重ねて起動する仕組み（中身は Addressables 差し替え式で将来最大5種類・ローカル完結）。動作確認は専用の **MiniGameTest シーン**で行う（本番フロー＝Title→Home→… には出さず、エディタで直接開いて Play する。カタログのミニゲームがボタンで自動一覧される）。盤面の特殊マスや手番との正式なゲーム内連携は未実装。

## Unity 開発

ビルドと実行は Unity Editor (Unity 6000.3.18f1) を通じて行う。独立したビルドスクリプトは存在しない。

- **テスト実行 (EditMode)**: Unity Editor → Window → General → Test Runner → EditMode タブ → Run All
- **テスト実行 (PlayMode)**: Unity Editor → Window → General → Test Runner → PlayMode タブ → Run All
- **ビルド**: Unity Editor → File → Build Settings → Build

## テスト構成

| ディレクトリ | 種別 | 内容 |
|---|---|---|
| [Assets/Tests/PlayMode/](Assets/Tests/PlayMode/) | PlayMode | シーンロードを伴う統合テスト |
| [Assets/Tests/EditMode/](Assets/Tests/EditMode/) | EditMode | 純粋ロジックの単体テスト |

**PlayMode テストの注意点:**
- `CommonSceneLoader` が `static bool _loaded` を持つため、`[UnityTearDown]` で reflection リセットが必要
- `IAsyncStartable.StartAsync` は VContainer からキャンセルトークンを受け取るため `catch (OperationCanceledException)` で正常終了させること
- ボタンクリック模擬は `NavigationSubmitEvent`（`ClickEvent` では Clickable が反応しない）
- 動画を再生するシーン（Title 等）を丸ごとロードするテストは、再生不可の環境で `VideoPlayer` がネイティブに吐く `Error` ログだけでテストが失敗扱いになる。動画の成否を検証しないテストでは `SetUp`/`TearDown` で `LogAssert.ignoreFailingMessages` を切り替えて回避する（本来の不具合は各アサーションで検出される）

**EditMode テストの注意点:**
- asmdef の `references` に、テスト対象クラスのアセンブリ GUID とその直接依存アセンブリ GUID を追加する（推移的参照は自動解決されない）
- `R3.dll` を `precompiledReferences` に追加が必要
- `ReadOnlyReactiveProperty<T>` の値は `.CurrentValue`（`.Value` は不可）。`ReactiveProperty<T>` は `.Value` で読み書き可
- R3 の Subscribe 拡張メソッドには `using R3;` が必要
- UniTask の同期完了タスクは `.GetAwaiter().GetResult()` でテスト可能（null ガード等の即完了ケース）

## アーキテクチャ

詳細は [docs/architecture.md](docs/architecture.md) を参照。

要点:
- `Common` シーンが常駐し、他シーンはアディティブでロード
- DI は VContainer（`Find()` / static 禁止）
- 状態管理は R3 の `ReactiveProperty<T>`（Model → Presenter の単方向フロー）
- アセットは Addressables（`Resources.Load` 禁止）。例外として**動画は StreamingAssets に置き `VideoPlayer` の URL で再生する**（WebGL は `VideoClip` アセット非対応のため。タイトル動画がこの方式）
- UI は UI Toolkit / UXML + USS（uGUI 禁止）。スタイルはインラインでなく USS ファイルに定義してクラスで適用する

  - 新しいシーンを作成するときは右上エリアに UI 要素を配置しない（Common シーンのオプションアイコンが `right:0 / top:0` に重なるため）。詳細は [docs/design-system.md](docs/design-system.md) を参照

## コーディング規約 (.editorconfig でエラーとして強制)

- **命名**: 型・メソッド・プロパティ・定数は `PascalCase`、フィールドは `_camelCase`、引数・ローカル変数は `camelCase`
- **明示的な型を優先** (`var` は使用しない。`csharp_style_var_*` はすべて false)
- **アクセス修飾子必須** (インターフェースメンバー以外のすべてのメンバー)
- **readonly フィールド**を可能な限り使用
- すべてのブロックに波括弧必須、開き波括弧は新しい行に配置
- `using` ディレクティブは名前空間の外側に記述し、System ディレクティブを先頭に並べる

## 主要ファイルの場所

| 用途 | パス |
|---|---|
| Common DI 登録 | [Assets/Scripts/Common/Injector/CommonLifeTimeScope.cs](Assets/Scripts/Common/Injector/CommonLifeTimeScope.cs) |
| シーン遷移ロジック | [Assets/Scripts/Common/SceneManagement/SceneTransitioner.cs](Assets/Scripts/Common/SceneManagement/SceneTransitioner.cs) |
| シーン準備完了通知（任意実装。非同期初期化の完了を待ってからフェードイン） | [Assets/Scripts/Common/SceneManagement/ISceneReady.cs](Assets/Scripts/Common/SceneManagement/ISceneReady.cs) |
| 遷移演出 | [Assets/Scripts/Common/Transition/TransitionPresenter.cs](Assets/Scripts/Common/Transition/TransitionPresenter.cs) |
| サウンド再生 | [Assets/Scripts/Common/SoundManagement/SoundPlayer.cs](Assets/Scripts/Common/SoundManagement/SoundPlayer.cs) |
| ボリューム状態モデル | [Assets/Scripts/Common/Option/OptionModel.cs](Assets/Scripts/Common/Option/OptionModel.cs) |
| オプションモーダル UI バインド | [Assets/Scripts/Common/Option/OptionModalPresenter.cs](Assets/Scripts/Common/Option/OptionModalPresenter.cs) |
| Store 共通基底クラス | [Assets/Scripts/Common/Store/AssetStoreBase.cs](Assets/Scripts/Common/Store/AssetStoreBase.cs) |
| セッション保持・ゲームモード（Common） | [Assets/Scripts/Common/GameSession/GameSessionModel.cs](Assets/Scripts/Common/GameSession/GameSessionModel.cs) / [GameMode.cs](Assets/Scripts/Common/GameSession/GameMode.cs) |
| 参加者リスト（GameMode から生成。一人用=[Human,Cpu]、オンライン=[Human]）・プレイヤー種別 | [Assets/Scripts/Main/Turn/GameParticipants.cs](Assets/Scripts/Main/Turn/GameParticipants.cs) / [PlayerKind.cs](Assets/Scripts/Main/Turn/PlayerKind.cs) |
| 手番状態（現在の手番プレイヤーの巡回） | [Assets/Scripts/Main/Turn/TurnModel.cs](Assets/Scripts/Main/Turn/TurnModel.cs) |
| ターン進行の統括（手番に応じてルーレットを手動待機／CPU 自動スピン→**ルーレットが消えるのを待って**手番プレイヤーのコマ前進→勝者が出るまで交代。散在していた「ルーレット停止→前進」の連鎖をここへ集約） | [Assets/Scripts/Main/Turn/GameFlowController.cs](Assets/Scripts/Main/Turn/GameFlowController.cs) |
| モード選択（Home の一人用/オンライン分岐。一人用は CharacterSelect へ。クレジットモーダルの開閉も担当。カタログからランダムに選んだ1キャラのカード画像（`CardAddress`）を全画面背景に表示し、上に暗いスクリムを重ねて前面 UI の視認性を確保。画像は Addressables ロード・未配置は色面プレースホルダ。表示前にロードを終えるため `ISceneReady` を実装） | [Assets/Scripts/Home/Presenter/HomePresenter.cs](Assets/Scripts/Home/Presenter/HomePresenter.cs) |
| キャラ識別子・カタログ・選択状態（Common。シーンをまたいで保持。各キャラは Card（選択カード絵）/Icon（盤面コマの丸バッジ）/Portrait（立ち絵）/Run（2Dレースの走行絵）の4系統の Addressable アドレスを持つ） | [Assets/Scripts/Common/Character/CharacterId.cs](Assets/Scripts/Common/Character/CharacterId.cs) / [CharacterCatalog.cs](Assets/Scripts/Common/Character/CharacterCatalog.cs) / [CharacterSessionModel.cs](Assets/Scripts/Common/Character/CharacterSessionModel.cs) |
| キャラ選択 UI（立ち絵を全画面背景・カード絵の選択スロットを下部に配置。戻る／決定ボタンは画面上部（右上のオプションアイコンを避けて中央寄せ）。キャラ名は各カード内に表示。画像は `CardAddress`（カード）と `PortraitAddress`（立ち絵）を Addressables ロード、未配置は色面プレースホルダ） | [Assets/Scripts/CharacterSelect/Presenter/CharacterSelectPresenter.cs](Assets/Scripts/CharacterSelect/Presenter/CharacterSelectPresenter.cs) |
| マッチングサービス | [Assets/Scripts/Matching/MatchingService.cs](Assets/Scripts/Matching/MatchingService.cs) |
| マッチング DI 登録 | [Assets/Scripts/Matching/Injector/MatchingLifetimeScope.cs](Assets/Scripts/Matching/Injector/MatchingLifetimeScope.cs) |
| NGO 起動・接続待機 | [Assets/Scripts/Main/NetworkSessionStartup.cs](Assets/Scripts/Main/NetworkSessionStartup.cs) |
| NGO メッセージ送受信 | [Assets/Scripts/Main/NgoMessenger.cs](Assets/Scripts/Main/NgoMessenger.cs) |
| ルーレットの停止角度→セクター変換・状態（出目は止まった位置で決まる）・セクター→キャラ割り当て（`CharacterForSector`。カタログ表示順で巡回） | [Assets/Scripts/Main/Roulette/RouletteMath.cs](Assets/Scripts/Main/Roulette/RouletteMath.cs) / [RouletteModel.cs](Assets/Scripts/Main/Roulette/RouletteModel.cs) |
| ルーレット UI（Painter2D で虹色円盤・区切り線・中心ハブ・各セクターのキャラコイン下地を描画。セクター数は `_sectorCount`（既定 8＝出目1〜8）。各セクターにキャラアイコンをコイン（ゴールド枠＋白座面）で表示し、出目の数字はアイコンの子バッジとしてコイン下部に重ねる。アイコンは円盤と一緒に周回しつつ逆回転で常に正立。コイン/アバターの寸法はセクター数から弦長ベースで自動計算し重なりを防ぐ。キャラ画像は Addressables ロード・未配置は色面プレースホルダ。長押し中は加速・離すと減速する角速度回転を `Update` で駆動。離した後は離した瞬間の速度に依らず一定時間（2.5〜3.5 秒・ランダム）かけて ease-out で減速して止めるため、すぐ離しても長押しから離しても止まり方の印象が揃う。針の反応（セクター境界を通過するたびに Roulet のティック SE を鳴らす）・当たりセクター強調・結果ポップなどの演出。円盤本体（タイトル・出目ラベル含む）は `RouletteState` に連動して**回しているときだけ表示**し（`Spinning` で表示・`Stopped` で `_hideAfterStopSeconds` 秒後に非表示・`Idle`＝手番リセットで即非表示）、隠している間は `visibility` で透明化して背後の盤面（Sorting Order Board:0 の下層）を見せる（スピンボタンは手番トリガーとして常に残す）。手番制御 `SetInteractable`／人間の停止待ち `WaitForManualSpinAsync`／CPU の自動スピン `AutoSpinAsync`／非表示になるまで待つ `WaitForHideAsync` を公開し `GameFlowController` から駆動される） | [Assets/Scripts/Main/Roulette/RoulettePresenter.cs](Assets/Scripts/Main/Roulette/RoulettePresenter.cs) |
| 盤面データ（ScriptableObject。方眼キャンバス上にマスを**経路順**に並べて保持＝盤面の形・経路。各マスはイベント（進む/戻る/休み/ミニゲーム・お金アップ/ダウン。お金は着地で発動・それ以外は現状も表示のみで未発動）と見た目（色・アイコンアドレス）を持つ。`Amount` は数値パラメータ（進む/戻るマス数・休みターン数・お金の金額）。`CreateRectangular` で従来の矩形リングをメモリ生成しフォールバックに使う） | [Assets/Scripts/Main/Board/BoardDefinition.cs](Assets/Scripts/Main/Board/BoardDefinition.cs) / [BoardCellDefinition.cs](Assets/Scripts/Main/Board/BoardCellDefinition.cs) / [BoardCellEvent.cs](Assets/Scripts/Main/Board/BoardCellEvent.cs) |
| 盤面エディタ（`Window > Sugoroku > Board Editor`。方眼をクリックして経路順にマスを置き、選択マスのイベント・数値（お金マスは「金額」）・色・アイコンアドレスを編集。`BoardDefinition` アセットの新規作成／読込／保存） | [Assets/Scripts/Main/Editor/BoardEditorWindow.cs](Assets/Scripts/Main/Editor/BoardEditorWindow.cs) |
| 盤面ロジック（位置前進・周回判定・矩形リング→グリッド座標の純粋関数。座標は `BoardDefinition` データが持つのが基本で、これは矩形フォールバック生成に使う） | [Assets/Scripts/Main/Board/BoardMath.cs](Assets/Scripts/Main/Board/BoardMath.cs) |
| 盤面状態（コマ位置を**プレイヤーごと**に保持・移動中・勝者 index／`IsFinished`） | [Assets/Scripts/Main/Board/BoardModel.cs](Assets/Scripts/Main/Board/BoardModel.cs) |
| 所持金（プレイヤーごとの所持金を保持。初期 1000・マイナス（借金）可。`Money(player)` 購読・`Add(player, delta)` で増減。お金マス着地（`BoardPresenter`）と将来のミニゲーム報酬から呼ぶ） | [Assets/Scripts/Main/Money/MoneyModel.cs](Assets/Scripts/Main/Money/MoneyModel.cs) |
| 盤面 UI（`BoardDefinition`（未割り当てなら `_columns`/`_rows` から矩形リング生成）を読んで盤面を描画。既定は縦長リング 5列×7行＝周回20マス。リング領域はグリッドの `(列-1):(行-1)` のアスペクト比を保ってピクセルで中央配置（`LayoutBoardArea`・`GeometryChangedEvent` でリサイズ追従）し、画面比に依らずマスを均等に並べる。マス中心間隔はマスの実寸（端の最外周マスは領域端から半マスはみ出す）まで含めて利用可能領域に収まるよう決め、最外周マスが余白へ食い込まず画面端に隙間を残す。各マスの一辺はマス中心間隔の `_cellFillRatio` 倍にして、マス間に隙間を作る（`ResizeCells`）。隙間はマス中心を経路順に結ぶ接続線でつなぐ（`board-lines` オーバーレイに `generateVisualContent`＋Painter2D で描画・最後のマスからスタートへ戻ってループを閉じる）。マス描画（データの色・アイコン画像＝Addressables ロード・イベント記号 ▲進む/▼戻る/休/MG を反映）・参加者ぶんのコマ描画（キャラの丸バッジ画像＝`PieceIconAddress` を Addressables ロードして貼付、YOU＝選択キャラ・CPU＝人間と別のキャラをランダム選択。画像未配置のキャラは色＋YOU/CPU ラベルにフォールバック・同マスの重なり回避）・画面上部に自分（人間プレイヤー）のネームプレート（YOU ロールタグ＋選択キャラ名＋所持金を金アクセント＝自分のコマ `--p0` と同色で表示・所持金は `MoneyModel` を購読しコイン＋金額でリアルタイム更新・マイナスは赤字・右上のオプションアイコンを避けて中央寄せ・相手は出さない＝`BuildPlayerHeaderIfReady`）・コマ移動演出。ルーレット出目とミニゲームのボーナスを共用する `AdvanceAsync(player, steps)`（着地マスのお金イベントを `ApplyLandingEvent` で発動して所持金を増減）・勝敗メッセージ表示） | [Assets/Scripts/Main/Board/BoardPresenter.cs](Assets/Scripts/Main/Board/BoardPresenter.cs) |
| ミニゲーム起動（Main を残して MiniGame シーンを Additive で重ね・終了後に単独アンロード。Transit は使わない） | [Assets/Scripts/Common/MiniGame/MiniGameLauncher.cs](Assets/Scripts/Common/MiniGame/MiniGameLauncher.cs) |
| ミニゲーム種別・カタログ（種別→表示名・UXML アドレス。新規追加はここに1行）・結果・起動側↔ホストの仲介 | [Assets/Scripts/Common/MiniGame/MiniGameId.cs](Assets/Scripts/Common/MiniGame/MiniGameId.cs) / [MiniGameCatalog.cs](Assets/Scripts/Common/MiniGame/MiniGameCatalog.cs) / [MiniGameResult.cs](Assets/Scripts/Common/MiniGame/MiniGameResult.cs) / [MiniGameSessionModel.cs](Assets/Scripts/Common/MiniGame/MiniGameSessionModel.cs) |
| ミニゲームホスト（`CurrentGame` でゲームごとに分岐するディスパッチャ。UXML を Addressables ロードして、タップ連打は自前で進行し、2Dレースは `RaceGamePlay` へ委譲する。タップ連打では選択中キャラのカード絵（`CardAddress`）を中央に表示し、タップのたびにカードだけを「がたがた」振動＋「パンチ」拡大で弾ませる（ボタン本体は固定）。カード画像は Addressables ロード・未配置は色面プレースホルダ） | [Assets/Scripts/MiniGame/MiniGameHostPresenter.cs](Assets/Scripts/MiniGame/MiniGameHostPresenter.cs) |
| タップ連打ロジック（フェーズ・タップ数・残り時間の純粋ロジック） | [Assets/Scripts/MiniGame/TapGame/TapGameModel.cs](Assets/Scripts/MiniGame/TapGame/TapGameModel.cs) |
| 2Dレース ロジック（進捗0→1・メーター判定（`Judge`）・タップ加算（`ApplyTap`）・時間経過（`Tick`）・勝敗の純粋ロジック。CPU はプレイヤーと同じベース速度で進み、ランダム間隔で Great/Good/Miss を抽選して前進（Great は低確率）。`System.Random` で決定的。速度・ブースト量・判定帯幅・CPU 抽選確率は `RaceGameConfig` に定数化） | [Assets/Scripts/MiniGame/RaceGame/RaceGameModel.cs](Assets/Scripts/MiniGame/RaceGame/RaceGameModel.cs) / [RaceGameConfig.cs](Assets/Scripts/MiniGame/RaceGame/RaceGameConfig.cs) / [RaceGamePhase.cs](Assets/Scripts/MiniGame/RaceGame/RaceGamePhase.cs) / [MeterJudgement.cs](Assets/Scripts/MiniGame/RaceGame/MeterJudgement.cs) / [RaceRunner.cs](Assets/Scripts/MiniGame/RaceGame/RaceRunner.cs) |
| 2Dレース UI・進行（ホストから委譲され、走者スプライト（`RunAddress`）・往復メーター・カウントダウン・判定表示・結果を毎フレーム駆動。メーターのアニメと入力は Presenter、判定/前進/勝敗は Model。走者は右→左へ進み先着で勝ち。スコアは勝ち=1／負け=0。画像未配置は色面プレースホルダ） | [Assets/Scripts/MiniGame/RaceGame/RaceGamePlay.cs](Assets/Scripts/MiniGame/RaceGame/RaceGamePlay.cs) |
| ミニゲーム動作確認シーン（`MiniGameCatalog` の各ミニゲームをボタンで一覧し、押すと `MiniGameLauncher` で起動→結果スコアを表示。本番フローには出さず、エディタで `MiniGameTest` シーンを直接開いて Play する） | [Assets/Scripts/MiniGame/Test/MiniGameTestPresenter.cs](Assets/Scripts/MiniGame/Test/MiniGameTestPresenter.cs) / [MiniGameTestLifetimeScope.cs](Assets/Scripts/MiniGame/Test/MiniGameTestLifetimeScope.cs) |
| タイトル背景動画＋タイトル文言演出（StreamingAssets の動画を `VideoPlayer`→`RenderTexture` で全画面背景に再生し、終了後に「ドラゴンファミリー/すごろく」を3行・1文字ずつ上から降らせる。初回再生開始から30秒おきに文言を隠して最初から再生し直すループ。直接起動でも初回再生されるよう `Start` と `ReadyAsync` の両方で初期化。準備タイムアウト・再生エラー時は文言のみ表示） | [Assets/Scripts/Title/Video/Presenter/TitleVideoPresenter.cs](Assets/Scripts/Title/Video/Presenter/TitleVideoPresenter.cs) |
| タイトル動画ファイル（StreamingAssets。H.264 baseline / BT.709 タグ付き mp4） | [Assets/StreamingAssets/Video/TitleMovie.mp4](Assets/StreamingAssets/Video/TitleMovie.mp4) |
| 日本語フォント（アセット） | [Assets/Font/](Assets/Font/) |
| 既定フォント設定（全 UI へ NotoSansJP Bold を適用） | [Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss](Assets/UI%20Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss) |

## ドキュメント

- [docs/architecture.md](docs/architecture.md): アーキテクチャドキュメント
- [docs/design-system.md](docs/design-system.md): UIデザインシステム（カラー・タイポグラフィ・コンポーネント）
- [docs/patterns.md](docs/patterns.md): よく触る実装パターン集（Presenter追加・DI登録・destroyCancellationToken・DOTween×UI Toolkit・DOTween×R3 の AddTo 衝突・RegisterComponentInHierarchy の前提・Button の押し続け判定はトリクルダウン登録）
- [docs/effects.md](docs/effects.md): パーティクル・VFX 実装ノウハウ（UI Toolkit との共存・加算ブレンド・worldBound 変換・再生時間調整）
- [docs/product.md](docs/product.md): プロダクトドキュメント
- [docs/matchmaking.md](docs/matchmaking.md): マッチメイキング設計（UGS Multiplayer Services）
- [docs/Live2D.md](docs/Live2D.md): Live2D Cubism SDK のアニメーション実装ノウハウ（**Live2D 関連の実装前に必読**）
- [docs/networking.md](docs/networking.md): NGO + MPM ネットワーク実装ノウハウ（**NGO 関連の実装前に必読**）

## Asset Store アセット

- Asset Store からダウンロードしたものは `Assets/AssetStore/` に配置する。このディレクトリは Git の管理対象外。
- DoTween (Demigiant) は `Assets/Plugins/` に配置済み（Git 管理対象）。
- Live2D Cubism SDK は `Assets/Live2D/` に配置済み（Git 管理対象）。
  - `Assets/csc.rsp` / `Assets/mcs.rsp` に `-unsafe` フラグが必要（Cubism Core が unsafe コードを使用するため）。
