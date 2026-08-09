# Sugoroku
オンライン対戦すごろく（縦画面）
ここには開発者向けのメモを記載する

## 開発の進め方
1. `/feature` を実行して新機能を実装する（ヒアリング→実装→テストまで自動で進む）
2. PlayMode: Window → General → Test Runner → PlayMode タブ → Run All で自動テストを実行する
3. Unity Editor で Play して動作確認する
4. 問題なければ `/ship` を実行してコミット・ドキュメント更新まで行う

## テストプレイの仕方
- 一人用モード: Unity Editor で `Title` シーンから Play し、Home →「一人で遊ぶ」→ キャラ選択 → マップ選択（マップ＋人数 2〜4）→ Main で CPU 対戦（勝敗が決まると盤面下部の「ホームに戻る」ボタンで Home へ戻れる。自分が勝つと花火、負けると雨のエフェクトと負けの SE が再生される）
- オンライン: Multiplayer Play Mode で複数プレイヤーを起動し、Home →「オンラインプレイ」からマッチング（ホストはルーム作成時に定員 2〜4 とマップを選ぶ）→ 満室後にキャラ選択ロビー（全員が被らないキャラを選ぶ）→ Main（詳細は [docs/matchmaking.md](docs/matchmaking.md)）。Main の進行（手番・出目・コマ移動・お金・アイテム・勝敗）は全プレイヤーで同期する（[docs/networking.md](docs/networking.md)「ゲーム進行の同期」）。相手の操作を待っている間は盤面上部に待機表示（「〇〇のルーレット待ち…」「〇〇が買い物中…」など）が出る。ミニゲームは**部屋の全員が同時に遊んで実スコアで順位を決める**（賞金は順位別で 1位500／2位300／3位100。タップ連打は互いの連打数が見えるうえ全員のキャラが並んで叩いた本人のカードが震え、2Dレースは互いの走者の位置がリアルタイムに見え、被っちゃやーよは開示時に誰がどのカードを選んだかが分かる）。対戦中に通信が切れても即終了せず、最大60秒だけ全員で復帰を待つ（戻れば取りこぼしたぶんを送り直して同じ盤面から再開。アプリ再起動やホストの切断は復帰できず対戦終了＝[docs/networking.md](docs/networking.md)「切断 — まず一時停止して復帰を待つ」）。NGO は Relay 経由で繋がるので離れた相手とも対戦できる（Unity Dashboard で **Relay サービスをプロジェクトに追加**しておくこと＝[docs/networking.md](docs/networking.md)「Relay 経由の接続」）
- ミニゲーム単体: `MiniGameTest` シーンをエディタで直接開いて Play（本番フロー外の動作確認用）

## 使用 Package
- Addressables
- R3
- UniTask
- VContainer
- DOTween
- Unity Gaming Services (UGS)
- Netcode for GameObjects (NGO)
- Live2D Cubism SDK（`Assets/Live2D/`、Git 管理対象）

## プラットフォーム
- オンライン対戦の動作確認は Windows / Mac ビルドで行う（WebGL は UGS の QoS がサポート外で Relay のリージョン選択が既定へフォールバックする＝警告は出るが接続自体は WSS で成立する。ただし未検証。[docs/networking.md](docs/networking.md)「Relay 経由の接続」）
- タイトル動画は WebGL / Standalone 両対応の StreamingAssets 方式（[docs/architecture.md](docs/architecture.md)「例外: 動画は StreamingAssets」）

## 日本語フォント
- 既定フォントは NotoSansJP Bold (SDF) を `PanelSettings.textSettings`（`PanelTextSettings` 資産）で全 UI に適用済み。**テーマ（`.tss`）からフォント資産を参照してはいけない**（再インポートの連鎖でテーマが崩れる）。セットアップは `Window > Sugoroku > Setup Panel Text Settings`・詳細と差し替え方法は [docs/design-system.md](docs/design-system.md)「日本語フォント」を参照

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
