# CLAUDE.md

このファイルはリポジトリで作業する Claude Code (claude.ai/code) へのガイダンスを提供します。

## プロジェクト概要

オンライン対戦すごろく。**縦画面（ポートレート）**。Unity 6 (6000.3.18f1) 製。BGM/SE 再生機能と、Commonシーンをベースとしたアディティブシーン管理、UGS Multiplayer Services によるオンラインマッチングを備える。Home で「一人用モード」「オンラインプレイ」を選択でき、一人用モードはネットワーク非依存で、キャラクター選択（CharacterSelect）→ マップ選択（MapSelect）を経て Main へ進み、**CPU と 1 対 1 のすごろく対戦**を行う（あなたが先攻、以降交互）。**盤面（マップ）は複数用意でき、`BoardCatalog` にまとめて MapSelect で 1 つ選ぶ**（選択は Common の `BoardSessionModel` に識別子で保持し `BoardPresenter` がカタログから解決。オンラインは既定＝カタログ先頭）。移動マス数は円盤ルーレットで決定する（ボタンを長押し中は回転し、離すと減速して止まった位置のセクターが出目。CPU の番は同じ円盤が自動で回る）。外周マスを並べたループ盤面で、手番プレイヤーのコマが出目ぶん進む（周回勝利は廃止したのでスタート＝ゴールを通過して回り続ける）。盤面は左下の虫眼鏡 +／− ボタンで**表示列数を段階ズーム**でき（既定4列・拡大で3→2列・縮小で6→8列）、画面外へはみ出したぶんは**ドラッグでパン**して見る（横長マップ向け・`BoardZoomController`）。**勝敗は陣地マス（`BoardCellEvent.Territory`）の占拠で決まる**：止まったマスを占拠し（相手の陣地でも上書きで奪える）、盤面の陣地マス総数の**過半数を占拠した方が勝ち**（`TerritoryModel` が状態と過半数判定を持つ）。陣地マス着地時は**そのコマのキャラの旗を画面中央に 1 秒表示→縮小しながらマスへ重ねて占拠する演出**を再生し、占拠後そのマスは territory 画像ではなく所有者の旗画像のままになる。手番進行は `GameFlowController` が統括する（オンラインは参加者 1 人で従来どおり単独プレイ）。プレイヤーは**所持金**を持ち（`MoneyModel`・初期 1000・マイナス可）、お金アップ/ダウンのマスに止まると増減する（盤面上部の自分ネームプレートに表示）。**アイテム取得マス（`BoardCellEvent.Item`）**に止まると `ItemCatalog` からランダムに1枚もらえ、抽選したアイテム絵を画面中央に約1秒見せてから**画面右下の手札**にサムネイル追加する（`ItemModel` がプレイヤーごとの手札を保持・表示は自分＝人間プレイヤーのぶんのみ・CPU は非表示。**同じアイテムを重ねて取ったときはカードを増やさず、既存カード右下に「x2」の枚数バッジ**を出す）。手札のアイテムは**クリックで詳細モーダル**（絵・名前・効果説明＋「使用する」「閉じる」）が開き、「使用する」で効果ハンドラ（`onUse`）が呼ばれる（`ItemModalPresenter` は消費せず `onUse` を呼ぶだけで、消費〔`ItemModel.Use`〕と効果発動は `BoardPresenter.HandleItemUse` が担う。**「使用する」は自分の手番かつルーレット未回転〔`Idle`〕でアイテム効果の実行中でないときだけ有効**で、回した後・コマ移動中・相手の手番中は無効）。**「陣地獲得」は効果実装済み**＝使用すると自分以外が持つ陣地マス（未占拠＋相手占拠）が金枠＋キラキラで強調され、盤面タップ〔ドラッグでパンも可・上部にガイド＋キャンセル〕で 1 つ選んで占拠する（既存の旗演出→占拠確定→過半数なら勝利。選択のキャンセルでは消費しない。効果はターンを消費せず使用後もルーレットを回せる。選択・演出中はスピンボタンを無効化）。**「ミニゲーム」も効果実装済み**＝使用すると遊ぶミニゲームを選ぶモーダル（`MiniGameSelectPresenter`＝`MiniGameCatalog` をボタン一覧）が開き、選んだミニゲームを `MiniGameLauncher` で起動して勝てば所持金報酬（`MoneyModel.Add`・既定 +500）をもらえる（2Dレースは先着で勝ち・タップ連打はタップ数を CPU 想定値と比べて 1 位なら勝ち・被っちゃやーよは他の誰とも被らないアイテムを選べば勝ち。キャンセルでは消費しない。ターン非消費で選択・プレイ中はスピン無効）。残る「お金よこどり」は消費のみで効果発動は未実装。ミニゲーム（タップ連打／CPU と競うタイミングメーター式の2Dレース／参加者数ぶんのアイテムから被らない1枚を選ぶ「被っちゃやーよ」）は Main を残したまま MiniGame シーンを Additive で重ねて起動する仕組み（中身は Addressables 差し替え式で将来最大5種類・ローカル完結）。動作確認は専用の **MiniGameTest シーン**で行う（本番フロー＝Title→Home→… には出さず、エディタで直接開いて Play する。カタログのミニゲームがボタンで自動一覧される）。盤面の特殊マスや手番との正式なゲーム内連携は未実装。

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
| ターン進行の統括（手番巡回のオーケストレータ。散在していた「ルーレット停止→前進」の連鎖をここへ集約。進行フローの詳細は [docs/architecture.md](docs/architecture.md)「シーン構成」） | [Assets/Scripts/Main/Turn/GameFlowController.cs](Assets/Scripts/Main/Turn/GameFlowController.cs) |
| モード選択（Home の一人用/オンライン分岐。一人用は CharacterSelect へ。クレジットモーダルの開閉も担当。カタログからランダムに選んだ1キャラのカード画像（`CardAddress`）を全画面背景に表示し、上に暗いスクリムを重ねて前面 UI の視認性を確保。画像は Addressables ロード・未配置は色面プレースホルダ。表示前にロードを終えるため `ISceneReady` を実装） | [Assets/Scripts/Home/Presenter/HomePresenter.cs](Assets/Scripts/Home/Presenter/HomePresenter.cs) |
| キャラ識別子・カタログ・選択状態（Common。シーンをまたいで保持。各キャラは Card（選択カード絵）/Icon（盤面コマの丸バッジ）/Portrait（立ち絵）/Run（2Dレースの走行絵）/Flag（陣地マス占拠時の旗絵・`Character/<名前>/Flag`）の5系統の Addressable アドレスを持つ） | [Assets/Scripts/Common/Character/CharacterId.cs](Assets/Scripts/Common/Character/CharacterId.cs) / [CharacterCatalog.cs](Assets/Scripts/Common/Character/CharacterCatalog.cs) / [CharacterSessionModel.cs](Assets/Scripts/Common/Character/CharacterSessionModel.cs) |
| キャラ選択 UI（立ち絵を全画面背景・カード絵の選択スロットを下部に配置。戻る／決定ボタンは画面上部（右上のオプションアイコンを避けて中央寄せ）。キャラ名は各カード内に表示。画像は `CardAddress`（カード）と `PortraitAddress`（立ち絵）を Addressables ロード、未配置は色面プレースホルダ。フェードイン前（`ReadyAsync`）はカード絵を全キャラぶん `UniTask.WhenAll` で**並列ロード**し、立ち絵は**選択時に遅延ロード＋キャッシュ**（フェードイン前に待つのは初期選択キャラの立ち絵1枚だけ＝遷移待ちを短縮。ロード中に選択が変わったら結果は破棄）。決定でキャラを保存し MapSelect へ遷移） | [Assets/Scripts/CharacterSelect/Presenter/CharacterSelectPresenter.cs](Assets/Scripts/CharacterSelect/Presenter/CharacterSelectPresenter.cs) |
| マップ一覧カタログ（`BoardDefinition` は SO 資産で静的クラスから参照できないため、キャラの `CharacterCatalog` と違いカタログ自身も SO。`List<BoardDefinition>` を持ち `All`/`Default`/`Find(識別子)` を公開。識別子＝マップ資産名（`Object.name`）。`BoardCatalog.asset` を作り MapSelect の Presenter と Main の `BoardPresenter` の両方へインスペクタで割り当てる） | [Assets/Scripts/Main/Board/BoardCatalog.cs](Assets/Scripts/Main/Board/BoardCatalog.cs) |
| 選択マップ保持（Common。シーンをまたいで保持する `CharacterSessionModel` のマップ版。Common から Main の `BoardDefinition` を参照できないため識別子＝マップ資産名の文字列だけを持つ。実体解決はカタログ側） | [Assets/Scripts/Common/Board/BoardSessionModel.cs](Assets/Scripts/Common/Board/BoardSessionModel.cs) |
| マップ選択 UI（`BoardCatalog` のマップを盤面サムネイル付きカードで一覧し、選ぶと大プレビューに拡大。決定で `BoardSessionModel` に保存して Main へ・戻るで CharacterSelect へ。サムネイルは画像を使わず `BoardSchematicView` が Painter2D で盤面の形を描く。マップ名は `BoardDefinition.DisplayName`〔空なら資産名〕。UI 構築は注入後に走る `ISceneReady.ReadyAsync` で行う） | [Assets/Scripts/MapSelect/Presenter/MapSelectPresenter.cs](Assets/Scripts/MapSelect/Presenter/MapSelectPresenter.cs) / [MapSelectLifetimeScope.cs](Assets/Scripts/MapSelect/Injector/MapSelectLifetimeScope.cs) / [BoardSchematicView.cs](Assets/Scripts/MapSelect/View/BoardSchematicView.cs) / [MapSelect.uxml](Assets/Scripts/MapSelect/View/MapSelect.uxml) |
| マッチングサービス | [Assets/Scripts/Matching/MatchingService.cs](Assets/Scripts/Matching/MatchingService.cs) |
| マッチング DI 登録 | [Assets/Scripts/Matching/Injector/MatchingLifetimeScope.cs](Assets/Scripts/Matching/Injector/MatchingLifetimeScope.cs) |
| NGO 起動・接続待機 | [Assets/Scripts/Main/NetworkSessionStartup.cs](Assets/Scripts/Main/NetworkSessionStartup.cs) |
| NGO メッセージ送受信 | [Assets/Scripts/Main/NgoMessenger.cs](Assets/Scripts/Main/NgoMessenger.cs) |
| ルーレットの停止角度→セクター変換・状態（出目は止まった位置で決まる）・セクター→キャラ割り当て（`CharacterForSector`。カタログ表示順で巡回） | [Assets/Scripts/Main/Roulette/RouletteMath.cs](Assets/Scripts/Main/Roulette/RouletteMath.cs) / [RouletteModel.cs](Assets/Scripts/Main/Roulette/RouletteModel.cs) |
| ルーレット UI（Painter2D で虹色円盤・区切り線・中心ハブ・各セクターのキャラコイン下地を描画。セクター数は `_sectorCount`（既定 8＝出目1〜8）。各セクターにキャラアイコンをコイン（ゴールド枠＋白座面）で表示し、出目の数字はアイコンの子バッジとしてコイン下部に重ねる。アイコンは円盤と一緒に周回しつつ逆回転で常に正立。コイン/アバターの寸法はセクター数から弦長ベースで自動計算し重なりを防ぐ。キャラ画像は Addressables ロード・未配置は色面プレースホルダ。長押し中は加速・離すと減速する角速度回転を `Update` で駆動。離した後は離した瞬間の速度に依らず一定時間（2.5〜3.5 秒・ランダム）かけて ease-out で減速して止めるため、すぐ離しても長押しから離しても止まり方の印象が揃う。針の反応（セクター境界を通過するたびに Roulet のティック SE を鳴らす）・当たりセクター強調・結果ポップなどの演出。**確定した出目は円盤中央のハブに大きい数字（`ResultLabel`）だけを重ねて表示**する（回転開始で空にし、停止＝`Stopped` 時に `RouletteModel.Result` の現在値から反映するので、CPU の手番や前回と同じ出目でも確実に出る）。円盤本体（中央の出目数字含む）は `RouletteState` に連動して**回しているときだけ表示**し（`Spinning` で表示・`Stopped` で `_hideAfterStopSeconds` 秒後に非表示・`Idle`＝手番リセットで即非表示）、隠している間は `visibility` で透明化して背後の盤面（Sorting Order Board:0 の下層）を見せる（スピンボタンは手番トリガーとして常に残す）。スピンボタンは**画面左下のズームピルの右隣**（left:80px / bottom:16px）に絶対配置で固定し、文字ではなく画像（`Image/Roulette`＝`Roulette.png`）を貼って**円形**（border-radius でくり抜き）に表示する（`RouletteIconLoader.LoadSpinButtonAsync`・画像クラスは定数 `SpinButtonImageClass`・未配置なら「長押しで回す」の文字ボタンにフォールバック）。画像表示のときは**押下判定も内接円**にし、円の外＝四隅の押下は `OnPointerDown` の TrickleDown で弾く（`IsOutsideCircularButton`）。スピンボタンは**押し続けている間は有効のまま**（離す＝`PointerUp` を受け取る必要があるため）だが、**指を離してスピンが確定した瞬間に無効化**して惰性回転中の再押下を防ぐ（`_spinReleased` フラグを `UpdateSpinEnabled` の有効条件に組み込み、次の手番開始＝`SetInteractable(true)` でクリア）。手番制御 `SetInteractable`／人間の停止待ち `WaitForManualSpinAsync`／CPU の自動スピン `AutoSpinAsync`／非表示になるまで待つ `WaitForHideAsync` を公開し `GameFlowController` から駆動される。円盤の Painter2D 描画は `RouletteWheelRenderer`・加減速の回転物理は `RouletteSpinPhysics`・当たり強調などの演出は `RouletteEffects`・画像ロードは `RouletteIconLoader` に分担） | [Assets/Scripts/Main/Roulette/RoulettePresenter.cs](Assets/Scripts/Main/Roulette/RoulettePresenter.cs) / [RouletteWheelRenderer.cs](Assets/Scripts/Main/Roulette/RouletteWheelRenderer.cs) / [RouletteSpinPhysics.cs](Assets/Scripts/Main/Roulette/RouletteSpinPhysics.cs) / [RouletteEffects.cs](Assets/Scripts/Main/Roulette/RouletteEffects.cs) / [RouletteIconLoader.cs](Assets/Scripts/Main/Roulette/RouletteIconLoader.cs) |
| 盤面データ（ScriptableObject。方眼キャンバス上にマスを**経路順**に並べて保持＝盤面の形・経路。各マスはイベント（進む/戻る/休み/ミニゲーム・お金アップ/ダウン・陣地・アイテム。お金・陣地・アイテムは着地で発動・それ以外は現状も表示のみで未発動）と見た目（色）を持つ。`Amount` は数値パラメータ（進む/戻るマス数・休みターン数・お金の金額。陣地・アイテムは未使用）。マスの画像は**イベント種別ごとに全マップ共通**（`BoardEventArtCatalog`＝静的カタログ・`Board/<イベント名>` 規約で解決）で、盤面ごとの画像設定は持たない（同一イベントのマスすべてに同じ画像を貼る／`CharacterCatalog`・`ItemCatalog` と同じ静的カタログ方式）。全マス共通で画像の上に重ねる**枠画像**（`FrameAddress`）は盤面ごとに持つ。画像は Addressables アドレスで、画像を用意していないイベント（進む/戻る/休み/ミニゲーム等）は記号表示にフォールバック。スタート＝ゴール（経路 index 0）は固定アドレス `Board/Start`（`BoardEventArtCatalog.StartAddress`）を優先。陣地マスは占拠後、所有者の旗画像へ差し替わる。マップ選択に出す**表示名**（`DisplayName`・`SetDisplayName`）も持つ。`CreateRectangular` で従来の矩形リングをメモリ生成しフォールバックに使う） | [Assets/Scripts/Main/Board/BoardDefinition.cs](Assets/Scripts/Main/Board/BoardDefinition.cs) / [BoardCellDefinition.cs](Assets/Scripts/Main/Board/BoardCellDefinition.cs) / [BoardCellEvent.cs](Assets/Scripts/Main/Board/BoardCellEvent.cs) / [BoardEventArtCatalog.cs](Assets/Scripts/Main/Board/BoardEventArtCatalog.cs) |
| 盤面エディタ（`Window > Sugoroku > Board Editor`。方眼をクリックして経路順にマスを置き、選択マスのイベント・数値（お金マスは「金額」）・色を編集。グリッドのマスは**イベント種別ごとの色で塗り分け**（カスタム色未設定時。色定義は `BoardEditorGridView.EventColor` に一元化）、色→イベントの対応表（凡例）をグリッド下に表示してどのマスに何のイベントが設定されているか一目で分かるようにする。ツールバーで**マップ名**（マップ選択に表示）・盤面共通の**枠画像アドレス**・方眼サイズを設定（マスのイベント画像はイベント種別ごとに全マップ共通＝`BoardEventArtCatalog` なので盤面ごとの設定は無い）。`BoardDefinition` アセットの新規作成／読込／保存。方眼描画は `BoardEditorGridView`・右パネルは `BoardCellInspector` に委譲） | [Assets/Scripts/Main/Editor/BoardEditorWindow.cs](Assets/Scripts/Main/Editor/BoardEditorWindow.cs) |
| 盤面ロジック（位置前進（`Advance`・スタートを越えるとループ）・矩形リング→グリッド座標の純粋関数。座標は `BoardDefinition` データが持つのが基本で、これは矩形フォールバック生成に使う） | [Assets/Scripts/Main/Board/BoardMath.cs](Assets/Scripts/Main/Board/BoardMath.cs) |
| 盤面状態（コマ位置を**プレイヤーごと**に保持・移動中・勝者 index／`IsFinished`。移動完了は `EndMove`、勝者確定は `SetWinner`＝陣地の過半数占拠時に `BoardPresenter` から呼ぶ） | [Assets/Scripts/Main/Board/BoardModel.cs](Assets/Scripts/Main/Board/BoardModel.cs) |
| 陣地マスの占拠状態（陣地マスの盤面 index ごとに所有者を保持・-1=未占拠。`Claim(player, index)` で占拠＝上書きで奪える・`Owner(index)` 購読で色替え・`RequiredToWin`＝過半数（総数/2+1）・`HasMajority(player)` で勝利判定・`CellsNotOwnedBy(player)` で自分以外が持つ陣地マス一覧（陣地獲得アイテムの選択対象）。陣地 index 一覧は `BoardPresenter` が `Initialize` で渡す。陣地マス 0 個の盤面は勝者が出ない） | [Assets/Scripts/Main/Board/TerritoryModel.cs](Assets/Scripts/Main/Board/TerritoryModel.cs) |
| 所持金（プレイヤーごとの所持金を保持。初期 1000・マイナス（借金）可。`Money(player)` 購読・`Add(player, delta)` で増減。お金マス着地（`BoardPresenter`）と将来のミニゲーム報酬から呼ぶ） | [Assets/Scripts/Main/Money/MoneyModel.cs](Assets/Scripts/Main/Money/MoneyModel.cs) |
| アイテム種別・カタログ（`ItemId` enum〔StealTerritory/StealMoney/MiniGame〕と静的な `ItemCatalog`＝`ItemId`→表示名＋効果説明文（`Description`・モーダル本文に表示）＋アイテム絵アドレス〔`Image/Item/*`〕。`All`/`Find(id)`/`RandomItem(System.Random)` を公開。新アイテムは enum とカタログに1行足す。`CharacterCatalog` と同じ静的カタログ方式。効果の発動は使用側〔`BoardPresenter.HandleItemUse`〕が担う＝`StealTerritory`〔陣地獲得〕・`MiniGame`〔ミニゲーム〕は実装済み・`StealMoney`〔お金よこどり〕は消費のみ） | [Assets/Scripts/Main/Item/ItemId.cs](Assets/Scripts/Main/Item/ItemId.cs) / [ItemCatalog.cs](Assets/Scripts/Main/Item/ItemCatalog.cs) |
| 取得アイテム（手札）（プレイヤーごとに取得アイテムの `List<ItemId>` を保持。`Add(player, item)` で1枚加え取得を `Gained`〔R3 の `Observable<ItemGain>`〕で通知・`Use(player, item)` で1枚消費し `Used`〔`Observable<ItemUse>`〕で通知〔未所持なら false で何もしない。`ItemModel` 自体は消費と通知だけで、効果の発動は使用側〔`BoardPresenter.HandleItemUse`〕が担う〕・`Items(player)` で手札取得。アイテム取得マス着地・アイテム効果の確定〔`BoardPresenter`〕から呼ぶ。`MoneyModel` と同じ Scoped DI・参加者ごと） | [Assets/Scripts/Main/Item/ItemModel.cs](Assets/Scripts/Main/Item/ItemModel.cs) |
| アイテム詳細モーダル（手札のカードをクリックすると開く。アイテム絵・名前・効果説明〔`ItemDefinition.Description`〕と「使用する」「閉じる」を表示し、「使用する」は生成側から渡された効果ハンドラ〔`Action<ItemId> onUse`＝`BoardPresenter.HandleItemUse`〕を呼んで閉じる〔消費〔`ItemModel.Use`〕・効果発動・手札 UI の減算はハンドラ側と `Used` 購読側が担う。選択キャンセルで消費しない効果があるため消費タイミングを効果側へ委ねる〕・「閉じる」/暗幕クリックで閉じる。`BoardLandingPresentation` と同じく `BoardPresenter` が `new` する協調クラスで、アイテム絵はロード済みキャッシュから引く〔未ロードは絵なしで名前と説明のみ〕。開いている間だけ Board の `UIDocument.sortingOrder` を一時的に 100 へ持ち上げ〔閉じたら元へ戻す〕、回転中のルーレット〔Sorting=10〕より前面に表示する。UI は Board.uxml の `ItemModal`＋`.item-modal*` クラス） | [Assets/Scripts/Main/Item/ItemModalPresenter.cs](Assets/Scripts/Main/Item/ItemModalPresenter.cs) |
| ミニゲーム選択モーダル（ミニゲームアイテム使用時に「どのミニゲームを遊ぶか」を選ばせる。`MiniGameCatalog` の各ゲームをボタンで並べ〔`MiniGameSelectList`〕、選んだ ID を `SelectAsync` で返す〔キャンセル・暗幕クリック・破棄では null＝消費しない〕。`ItemModalPresenter` と同じく `BoardPresenter` が `new` する協調クラスで、開いている間だけ Board の `UIDocument.sortingOrder` を 100 へ持ち上げてスピンボタン等より前面に出す。UI は Board.uxml の `MiniGameSelectModal`＋`.minigame-select*` クラス〔暗幕・カードは `.item-modal*` を流用〕） | [Assets/Scripts/Main/Item/MiniGameSelectPresenter.cs](Assets/Scripts/Main/Item/MiniGameSelectPresenter.cs) |
| 盤面 UI（描画する `BoardDefinition` は `ResolveDefinition` が「(1) MapSelect で選ばれたマップ〔`_catalog.Find(BoardSessionModel.SelectedId)`〕→ (2) インスペクタ割り当ての `_definition` → (3) `_columns`/`_rows` から矩形リング生成」の順で解決。選択マップは注入後に参照するため `BuildCells` は Construct と OnEnable の両方がそろってから走るようゲートする。既定は縦長リング 5列×7行＝周回20マス。リング領域はグリッドの `(列-1):(行-1)` のアスペクト比を保ってピクセルで中央配置（`LayoutBoardArea`・`GeometryChangedEvent` でリサイズ追従）し、画面比に依らずマスを均等に並べる。マス中心間隔はマスの実寸（端の最外周マスは領域端から半マスはみ出す）まで含めて利用可能領域に収まるよう決め、最外周マスが余白へ食い込まず画面端に隙間を残す。各マスの一辺はマス中心間隔の `_cellFillRatio` 倍にして、マス間に隙間を作る（`ResizeCells`）。隙間はマス中心を経路順に結ぶ接続線でつなぐ（`board-lines` オーバーレイに `generateVisualContent`＋Painter2D で描画・最後のマスからスタートへ戻ってループを閉じる）。マス描画（データの色・イベント種別ごとの画像＝全マップ共通の `BoardEventArtCatalog` で解決して Addressables ロード・その上に盤面共通の枠画像を各マスへオーバーレイ＝`AddFrameOverlay`/`LoadFrameAsync`・イベント記号 ▲進む/▼戻る/休/MG/陣/カ を反映・陣地マスは占拠者の**旗画像**に塗り替え＝`SetupTerritoriesIfReady` で `TerritoryModel.Owner` を購読し `ApplyTerritoryOwner` が占拠者の旗画像〔`_flagIcons`〕を貼る〔`board-cell--flag`＝scale-to-fit・所有者色は枠線で残す〕・旗未ロード時は色クラスのみ）・参加者ぶんのコマ描画（キャラの丸バッジ画像＝`PieceIconAddress` を Addressables ロードして貼付、YOU＝選択キャラ・CPU＝人間と別のキャラをランダム選択。画像未配置のキャラは色＋YOU/CPU ラベルにフォールバック・同マスの重なり回避）・画面上部に自分（人間プレイヤー）のネームプレート（YOU ロールタグ＋選択キャラ名＋所持金を金アクセント＝自分のコマ `--p0` と同色で表示・所持金は `MoneyModel` を購読しコイン＋金額でリアルタイム更新・マイナスは赤字・右上のオプションアイコンを避けて中央寄せ・相手は出さない＝`BuildPlayerHeaderIfReady`）・コマ移動演出。ルーレット出目とミニゲームのボーナスを共用する `AdvanceAsync(player, steps)`（**移動開始時にズームを既定へ戻して動くコマを画面中央に据え**〔`FocusCameraOnPlayer`→`BoardZoomController.CenterOn`〕、周回で止めず出目ぶん進み、**1マス進むごとに少し間〔`_panFollowDelay`・既定0.09秒〕を置いてカメラをそのマスへパン追従させる**。移動完了後に `PlayLandingSequenceAsync` で着地演出を再生）・着地演出（`PlayLandingSequenceAsync`＝止まったマスの種別で分岐：陣地マスは旗演出→占拠確定・アイテムマスは抽選絵の中央ポップ→右下の手札〔`.item-hand`〕へ追加＝同じアイテムはカードを増やさず既存カードの枚数バッジ〔`.item-hand__count`〕を「x2」のように更新（`AppendItemToHand`）・手札カードはクリックでアイテム詳細モーダル〔`ItemModalPresenter`〕を開き、使用（`ItemModel.Used` 購読）で枚数バッジを減算＝最後の1枚ならカード削除（`RemoveItemFromHand`）・お金マスは増減額の浮遊テキスト・それ以外はマス画像の中央ポップ。演出フロー・秒数の詳細は [docs/architecture.md](docs/architecture.md)「シーン構成」の着地演出の項に集約）・アイテム効果の統括（`HandleItemUse`＝アイテム種別で分岐。陣地獲得〔`StealTerritory`〕は `TerritoryModel.CellsNotOwnedBy` で対象マスを出し、金枠〔`board-cell--selectable`〕＋キラキラのリング〔`board-cell__glow` を `AnimateSelectableGlowAsync` で ping パルス駆動〕で強調・ガイドバナー〔`TerritorySelectBanner`〕を出し、盤面タップ〔`BoardZoomController.BeginCellSelection` 経由でタップ/パン両立〕またはキャンセルを待って占拠→旗演出→過半数なら勝利。ミニゲーム〔`MiniGame`〕は `MiniGameSelectPresenter` で遊ぶゲームを選ばせ〔キャンセルで消費せず終了〕、`MiniGameLauncher` で起動して勝てば `MoneyModel.Add` で所持金報酬〔勝敗は `DetermineMiniGameWin`＝レースは先着・タップ連打はタップ数を CPU 想定値と比較〕。選択・演出中は `RoulettePresenter.SetInteractable(false)` でスピン無効・確定時のみ `ItemModel.Use` で消費・効果はターン非消費）・勝敗メッセージ表示。ズーム/パンは `BoardZoomController`、着地演出のビュー（ポップアップ・浮遊テキスト・旗トゥイーン）は `BoardLandingPresentation` に委譲・リング領域のレイアウトとズーム用の寸法（`Area`）は `BoardLayoutCalculator`） | [Assets/Scripts/Main/Board/BoardPresenter.cs](Assets/Scripts/Main/Board/BoardPresenter.cs) / [BoardLandingPresentation.cs](Assets/Scripts/Main/Board/BoardLandingPresentation.cs) / [BoardLayoutCalculator.cs](Assets/Scripts/Main/Board/BoardLayoutCalculator.cs) |
| 盤面のズーム・ドラッグパン（横長マップ向け。虫眼鏡 +／− ボタンで「画面幅に収める列数」を段階切替＝`_zoomColumnLevels`〔既定 `{2,3,4,6,8}`〕を行き来し、各段階のスケールを基準列数レイアウトに掛ける〔`ScaleForColumns`〕。段階は盤面の実列数を上限に丸める〔`BuildColumnLevels`〕。既定は `_visibleColumns`＝4 列で `BoardLayoutCalculator` が盤面を横へはみ出させ、はみ出したぶんを全画面の透明ドラッグ層〔`BoardDragLayer`・盤面がはみ出しているときだけ picking 有効〕でドラッグしてパン〔`ClampPan` で盤面が画面を覆う範囲にクランプ〕。ズームは**カメラ（画面）中心**で、段階切替の直前に画面中央に見えている盤面上の点が動かないようパンを補正してから拡大縮小する〔`ZoomAroundViewportCenter`→`ClampPan`。右上を見ている状態でズームすればそのまま右上が拡大される〕。起動時はスタート＝左上へ寄せる。**コマ移動中はカメラがコマに追従する**＝`BoardPresenter` が正規化位置を渡して呼ぶ `CenterOn`（対象マスが画面中央に来るようパンを解き `ClampPan` で盤面が画面を覆う範囲へ収める・端付近では寄り切らない・移動開始時は先にズームを基準列数へ戻す）。ボタン UI は **+ と − を1つの縦長カプセルに連結した「ズームピル」**（金の極細枠＋暗いガラス地・`.zoom-pill`）で、記号は金色・間を区切り線で分ける。虫眼鏡画像 `Image/lupe` は Addressables ロードできたときだけ**ピル中央の飾りアイコン**として表示する〔`LoadMagnifierIconAsync`・未配置なら + と − が直接隣り合う〕。**陣地獲得アイテムのマス選択モード**〔`BeginCellSelection(Func<Vector2,bool>)`／`EndCellSelection`〕も持ち、選択中はドラッグ層を常時反応させて、押下からほぼ動かず離せば**タップ＝選択**・動かせば**ドラッグ＝パン**に振り分ける〔盤面タップとパンを両立・`BoardPresenter` から駆動〕） | [Assets/Scripts/Main/Board/BoardZoomController.cs](Assets/Scripts/Main/Board/BoardZoomController.cs) |
| ミニゲーム起動（Main を残して MiniGame シーンを Additive で重ね・終了後に単独アンロード。Transit は使わない。MiniGameTest シーンとミニゲームアイテムの効果〔`BoardPresenter.RunMiniGameAsync`〕から起動される） | [Assets/Scripts/Common/MiniGame/MiniGameLauncher.cs](Assets/Scripts/Common/MiniGame/MiniGameLauncher.cs) |
| ミニゲーム種別・カタログ（種別→表示名・UXML アドレス。新規追加はここに1行）・結果・起動側↔ホストの仲介 | [Assets/Scripts/Common/MiniGame/MiniGameId.cs](Assets/Scripts/Common/MiniGame/MiniGameId.cs) / [MiniGameCatalog.cs](Assets/Scripts/Common/MiniGame/MiniGameCatalog.cs) / [MiniGameResult.cs](Assets/Scripts/Common/MiniGame/MiniGameResult.cs) / [MiniGameSessionModel.cs](Assets/Scripts/Common/MiniGame/MiniGameSessionModel.cs) |
| ミニゲームホスト（`CurrentGame` でゲームごとに分岐するディスパッチャ。UXML を Addressables ロードして、タップ連打は `TapGamePlay`、2Dレースは `RaceGamePlay`、被っちゃやーよは `OverlapGamePlay` へ委譲する） | [Assets/Scripts/MiniGame/MiniGameHostPresenter.cs](Assets/Scripts/MiniGame/MiniGameHostPresenter.cs) |
| タップ連打ロジック（フェーズ・タップ数・残り時間の純粋ロジック） | [Assets/Scripts/MiniGame/TapGame/TapGameModel.cs](Assets/Scripts/MiniGame/TapGame/TapGameModel.cs) |
| タップ連打 UI・進行（ホストから委譲され、選択中キャラのカード絵（`CardAddress`）をタップボタンの上に表示し、タップのたびにカードだけを「がたがた」振動＋「パンチ」拡大で弾ませる（ボタン本体は固定）。タップのたびにパンチ SE（`SoundStore.RandomPunchSE`＝Punch1〜3 をランダム）を鳴らす。タップ数はボタン上に重ねて表示し、カウントダウン中（計測開始前）は非表示。残り時間は「残り時間：」付きで表示。カード画像は Addressables ロード・未配置は色面プレースホルダ） | [Assets/Scripts/MiniGame/TapGame/TapGamePlay.cs](Assets/Scripts/MiniGame/TapGame/TapGamePlay.cs) |
| 2Dレース ロジック（進捗0→1・メーター判定（`Judge`）・タップ加算（`ApplyTap`）・時間経過（`Tick`）・勝敗の純粋ロジック。CPU はプレイヤーと同じベース速度で進み、ランダム間隔で Great/Good/Miss を抽選して前進（Great は低確率）。`System.Random` で決定的。速度・ブースト量・判定帯幅・CPU 抽選確率は `RaceGameConfig` に定数化） | [Assets/Scripts/MiniGame/RaceGame/RaceGameModel.cs](Assets/Scripts/MiniGame/RaceGame/RaceGameModel.cs) / [RaceGameConfig.cs](Assets/Scripts/MiniGame/RaceGame/RaceGameConfig.cs) / [RaceGamePhase.cs](Assets/Scripts/MiniGame/RaceGame/RaceGamePhase.cs) / [MeterJudgement.cs](Assets/Scripts/MiniGame/RaceGame/MeterJudgement.cs) / [RaceRunner.cs](Assets/Scripts/MiniGame/RaceGame/RaceRunner.cs) |
| 2Dレース UI・進行（ホストから委譲され、走者スプライト（`RunAddress`）・往復メーター・カウントダウン・判定表示・結果を毎フレーム駆動。メーターのアニメと入力は Presenter、判定/前進/勝敗は Model。走者は右→左へ進み先着で勝ち。スコアは勝ち=1／負け=0。画像未配置は色面プレースホルダ） | [Assets/Scripts/MiniGame/RaceGame/RaceGamePlay.cs](Assets/Scripts/MiniGame/RaceGame/RaceGamePlay.cs) |
| 被っちゃやーよ ロジック（参加者数ぶんの重複なしアイテムを `ItemCatalog` から抽選して提示し、各参加者が1つ選ぶ。プレイヤー＝index 0 の選択が他の誰とも被らなければ獲得＝勝ち。CPU の選択は `Setup(playerCount, seed)` 時点で `System.Random` により確定＝決定的。提示枚数は参加者数（`OverlapGameConfig.DefaultPlayerCount`＝現状2。将来 `GameParticipants.Count` を供給）とカタログ総数の小さい方にクランプ。フェーズ Ready/Countdown/Choosing/Revealed/Finished。制限時間内に選べなければ `TimeOut()` で無効票＝未選択のまま Revealed へ進み `IsPlayerWin` は false。時間・入力は Presenter 駆動でここは純粋ロジック） | [Assets/Scripts/MiniGame/OverlapGame/OverlapGameModel.cs](Assets/Scripts/MiniGame/OverlapGame/OverlapGameModel.cs) / [OverlapGameConfig.cs](Assets/Scripts/MiniGame/OverlapGame/OverlapGameConfig.cs) / [OverlapGamePhase.cs](Assets/Scripts/MiniGame/OverlapGame/OverlapGamePhase.cs) |
| 被っちゃやーよ UI・進行（ホストから委譲され、提示アイテム数（＝参加者数）に応じて選択カードを動的生成し、アイテム絵〔`Image/Item/*`〕を並列ロードして貼る。カウントダウン → 選択（`OverlapGameConfig.ChoiceSeconds`＝3秒の制限時間を「のこりN」で表示・時間切れは `TimeOut`）→ 全員オープン（YOU/CPU バッジ表示・被り＝赤枠／セーフ＝緑枠）→ 結果（獲得＝ItemGetSE・かぶり/時間切れ＝Cancel1SE）を駆動。スコアは獲得=1／被り・無効票=0。画像未配置は色面プレースホルダ） | [Assets/Scripts/MiniGame/OverlapGame/OverlapGamePlay.cs](Assets/Scripts/MiniGame/OverlapGame/OverlapGamePlay.cs) |
| ミニゲーム動作確認シーン（`MiniGameCatalog` の各ミニゲームをボタンで一覧し、押すと `MiniGameLauncher` で起動→結果スコアを表示。本番フローには出さず、エディタで `MiniGameTest` シーンを直接開いて Play する） | [Assets/Scripts/MiniGame/Test/MiniGameTestPresenter.cs](Assets/Scripts/MiniGame/Test/MiniGameTestPresenter.cs) / [MiniGameTestLifetimeScope.cs](Assets/Scripts/MiniGame/Test/MiniGameTestLifetimeScope.cs) |
| タイトル背景動画＋タイトル文言演出（StreamingAssets の動画を `VideoPlayer`→`RenderTexture` で全画面背景に再生し、終了後に「ドラゴンファミリー/すごろく」を3行・1文字ずつ上から降らせる。初回再生開始から30秒おきに文言を隠して最初から再生し直すループ。初回起動時は動画の再生準備が完了するまで画面右下に「Now Loading」を表示し、準備の成否が確定した時点で隠す。直接起動でも初回再生されるよう `Start` と `ReadyAsync` の両方で初期化。準備タイムアウト・再生エラー時は文言のみ表示） | [Assets/Scripts/Title/Video/Presenter/TitleVideoPresenter.cs](Assets/Scripts/Title/Video/Presenter/TitleVideoPresenter.cs) |
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
- DOTween (Demigiant) は `Assets/Plugins/` に配置済み（Git 管理対象）。
- Live2D Cubism SDK は `Assets/Live2D/` に配置済み（Git 管理対象）。
  - `Assets/csc.rsp` / `Assets/mcs.rsp` に `-unsafe` フラグが必要（Cubism Core が unsafe コードを使用するため）。
