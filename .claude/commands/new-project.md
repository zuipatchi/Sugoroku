---
description: テンプレートから新規プロジェクトをセットアップする
---

# new-project: 新規プロジェクトセットアップ

このリポジトリを新しいゲームプロジェクトとして初期化する。

## 手順

1. ユーザーに**新しいプロジェクト名（例: MyGame）**を聞く

2. 以下のファイルを更新する:

   ### ProjectSettings/ProjectSettings.asset
   `productName: Template` を `productName: <新プロジェクト名>` に変更

   ### Template.slnx
   ファイル名を `<新プロジェクト名>.slnx` にリネーム（`mv Template.slnx <新プロジェクト名>.slnx`）

   ### README.md
   - 1行目の `# Template` を `# <新プロジェクト名>` に変更
   - 2行目のプロジェクト説明をユーザーに確認して書き換える

   ### CLAUDE.md
   - 「プロジェクト概要」セクションの説明を新しいゲームに合わせて書き換える

3. Librasyフォルダを削除してもらう。VsCodeには表示されないので注意 。UnityHubも一度閉じないと削除できない

4. UGS（マッチング機能）を使うかユーザーに確認する。
   使う場合: `ProjectSettings/ProjectSettings.asset` の `cloudProjectId` と `organizationId` を差し替える必要があることを伝え、Unity Editor → Edit → Project Settings → Services で紐づけ直す手順を案内する。

5. 完了後、ユーザーに以下を伝える:
   - 変更した内容の一覧
   - .gitを再生成してもらう
