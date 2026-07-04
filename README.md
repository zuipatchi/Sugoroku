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
