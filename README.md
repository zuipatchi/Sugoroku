# Sugoroku
オンライン対戦すごろく（縦画面）
ここには開発者向けのメモを記載する

## 開発の進め方
1. `/feature` を実行して新機能を実装する（ヒアリング→実装→テストまで自動で進む）
2. PlayMode: Window → General → Test Runner → PlayMode タブ → Run All で自動テストを実行する
3. Unity Editor で Play して動作確認する
4. 問題なければ `/ship` を実行してコミット・ドキュメント更新まで行う

## テストプレイの仕方
決まったら記載する

## 機能一覧
- タイトル画面（背景動画を1回再生→終了後に「ドラゴンファミリー/すごろく」を3行・1文字ずつ上から降らせて表示。画面全体が「Press start」ボタンで、文言は点滅して入力を促す。動画は WebGL 対応のため StreamingAssets を `VideoPlayer` の URL 再生）
- BGMとSEの再生機能
- シーン管理機能（Commonシーンをベースに他のシーンを使用する）
  - フェードイン/アウトによる画面遷移演出
- オプション機能
  - BGM音量
  - SE音量
  - タイトルに戻る
- オンラインマッチング機能（UGS Multiplayer Services）
  - クイックマッチ（空きルームへ自動参加 or 作成して待機）
  - ルーム一覧から手動参加
  - タイムアウト＋リトライ確認（クイックマッチ 30 秒 / 手動ルーム 120 秒）
- モード選択（Home）
  - 一人用モード（ネットワーク非依存。キャラ選択を経て Main へ）
  - オンラインプレイ（Matching 経由）
  - クレジット表示（モーダルで制作・イラスト・使用技術などを表示）
- キャラクター選択（一人用は Main の前に選択。立ち絵を全画面背景・アイコンの選択スロットを下部に表示。選択をシーンをまたいで保持。現状オンライン非対応）
- 円盤ルーレット（ボタンを長押し中は回転し、離すと減速して止まった出目で移動マス数を決定）
- すごろくのループ盤面（外周マスをコマが出目ぶん移動。1周してゴール到達でクリア）
- ミニゲーム（5秒間のタップ連打。Main を残したまま MiniGame シーンを重ねて起動し、勝利なら盤面にボーナス前進。中身は Addressables 差し替え式で将来最大5種類。現状はローカル完結・テスト用ボタン起動）

## 使用 Package
- Addressables
- R3
- UniTask
- VContainer
- DoTween
- UnityGamingService
- Netcode for GameObjects (NGO)
- Live2D Cubism SDK（`Assets/Live2D/`、Git 管理対象）

## プラットフォーム
決まったら記載する

## 日本語フォント
- ゲーム全体の既定フォントは NotoSansJP Bold (SDF)。テーマ（`Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss`）で全テキスト要素に適用済みなので、uxml ごとの個別指定は不要
- 別の太さ・別フォントに変えたい場合は、新しい SDF を作ってテーマの `-unity-font-definition` の url を差し替える（詳細は [docs/design-system.md](docs/design-system.md)）

## GitHub 連携（Claude Code GitHub Action）
- `.github/workflows/claude.yml` で、GitHub の Issue / PR コメントに `@claude` とメンションすると Claude が動く
- 認証は **Pro/Max サブスク枠の OAuth トークン**を使用（API 従量課金ではない）。リポジトリの Secret に `CLAUDE_CODE_OAUTH_TOKEN` を登録する（`claude setup-token` で生成）
- `anthropic_api_key` は設定しないこと（設定すると API 課金が優先される）

## gitignore
- Asset Storeからダウンロードした物は AssetStore ディレクトリに入れるとGitに管理されない

## このテンプレートから新規プロジェクトを作る手順

フォルダをコピーした後、Claude Code で以下を実行する:

```
/new-project
```

プロジェクト名を聞かれるので答えると、必要な箇所を自動で書き換えてくれる。
