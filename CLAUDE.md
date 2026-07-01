# CLAUDE.md

このファイルはリポジトリで作業する Claude Code (claude.ai/code) へのガイダンスを提供します。

## プロジェクト概要

オンライン対戦すごろく。**縦画面（ポートレート）**。Unity 6 (6000.3.18f1) 製。BGM/SE 再生機能と、Commonシーンをベースとしたアディティブシーン管理、UGS Multiplayer Services によるオンラインマッチングを備える。Home で「一人用モード」「オンラインプレイ」を選択でき、一人用モードはネットワーク非依存で、キャラクター選択（CharacterSelect）を経て Main へ進む。移動マス数は円盤ルーレットで決定する（ボタンを長押し中は回転し、離すと減速して止まった位置のセクターが出目）。外周マスを並べたループ盤面のコマが出目ぶん進む（1周してゴール到達でクリア）。ミニゲーム（タップ連打）は Main を残したまま MiniGame シーンを Additive で重ねて起動し、結果を盤面に反映する（中身は Addressables 差し替え式で将来最大5種類。現状はローカル完結・テスト用ボタン起動）。

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
| モード選択（Home の一人用/オンライン分岐。一人用は CharacterSelect へ。クレジットモーダルの開閉も担当） | [Assets/Scripts/Home/Presenter/HomePresenter.cs](Assets/Scripts/Home/Presenter/HomePresenter.cs) |
| キャラ識別子・カタログ・選択状態（Common。シーンをまたいで保持） | [Assets/Scripts/Common/Character/CharacterId.cs](Assets/Scripts/Common/Character/CharacterId.cs) / [CharacterCatalog.cs](Assets/Scripts/Common/Character/CharacterCatalog.cs) / [CharacterSessionModel.cs](Assets/Scripts/Common/Character/CharacterSessionModel.cs) |
| キャラ選択 UI（立ち絵を全画面背景・アイコンの選択スロットを下部に配置。画面上部のタイトルに選択中キャラ名を表示。画像は Addressables ロード、未配置は色面プレースホルダ） | [Assets/Scripts/CharacterSelect/Presenter/CharacterSelectPresenter.cs](Assets/Scripts/CharacterSelect/Presenter/CharacterSelectPresenter.cs) |
| マッチングサービス | [Assets/Scripts/Matching/MatchingService.cs](Assets/Scripts/Matching/MatchingService.cs) |
| マッチング DI 登録 | [Assets/Scripts/Matching/Injector/MatchingLifetimeScope.cs](Assets/Scripts/Matching/Injector/MatchingLifetimeScope.cs) |
| NGO 起動・接続待機 | [Assets/Scripts/Main/NetworkSessionStartup.cs](Assets/Scripts/Main/NetworkSessionStartup.cs) |
| NGO メッセージ送受信 | [Assets/Scripts/Main/NgoMessenger.cs](Assets/Scripts/Main/NgoMessenger.cs) |
| ルーレットの停止角度→セクター変換・状態（出目は止まった位置で決まる） | [Assets/Scripts/Main/Roulette/RouletteMath.cs](Assets/Scripts/Main/Roulette/RouletteMath.cs) / [RouletteModel.cs](Assets/Scripts/Main/Roulette/RouletteModel.cs) |
| ルーレット UI（Painter2D で虹色円盤・区切り線・中心ハブを描画。長押し中は加速・離すと減速する角速度回転を `Update` で駆動。すぐ離しても最低 1.5〜2.5 秒（ランダム）は回るよう、離した後は目標時間まで等速コーストしてから減速する。針のカチカチ反応・当たりセクター強調・結果ポップなどの演出） | [Assets/Scripts/Main/Roulette/RoulettePresenter.cs](Assets/Scripts/Main/Roulette/RoulettePresenter.cs) |
| 盤面ロジック（位置前進・周回判定・リング→グリッド座標の純粋関数） | [Assets/Scripts/Main/Board/BoardMath.cs](Assets/Scripts/Main/Board/BoardMath.cs) |
| 盤面状態（コマ位置・移動中・クリア） | [Assets/Scripts/Main/Board/BoardModel.cs](Assets/Scripts/Main/Board/BoardModel.cs) |
| 盤面 UI（外周マス描画・コマ移動演出。ルーレット出目とミニゲームのボーナスを共用する `AdvanceAsync`） | [Assets/Scripts/Main/Board/BoardPresenter.cs](Assets/Scripts/Main/Board/BoardPresenter.cs) |
| ミニゲーム起動（Main を残して MiniGame シーンを Additive で重ね・終了後に単独アンロード。Transit は使わない） | [Assets/Scripts/Common/MiniGame/MiniGameLauncher.cs](Assets/Scripts/Common/MiniGame/MiniGameLauncher.cs) |
| ミニゲーム種別・結果・起動側↔ホストの仲介 | [Assets/Scripts/Common/MiniGame/MiniGameId.cs](Assets/Scripts/Common/MiniGame/MiniGameId.cs) / [MiniGameResult.cs](Assets/Scripts/Common/MiniGame/MiniGameResult.cs) / [MiniGameSessionModel.cs](Assets/Scripts/Common/MiniGame/MiniGameSessionModel.cs) |
| ミニゲームホスト（CurrentGame に応じた UXML を Addressables ロードして進行） | [Assets/Scripts/MiniGame/MiniGameHostPresenter.cs](Assets/Scripts/MiniGame/MiniGameHostPresenter.cs) |
| タップ連打ロジック（フェーズ・タップ数・残り時間の純粋ロジック） | [Assets/Scripts/MiniGame/TapGame/TapGameModel.cs](Assets/Scripts/MiniGame/TapGame/TapGameModel.cs) |
| ミニゲーム起動トリガー（テスト用ボタン・しきい値判定で盤面にボーナス） | [Assets/Scripts/Main/MiniGameTriggerPresenter.cs](Assets/Scripts/Main/MiniGameTriggerPresenter.cs) |
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
