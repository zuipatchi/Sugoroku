# プロダクトドキュメント

実装済み機能の索引。各機能の詳細な挙動・仕様は右側のリンク先ドキュメントに集約する（このファイルは「何が実装されているか」の一覧に徹し、仕様の本文は持たない）。

新しいゲームを立ち上げたら、このテンプレートが提供する基盤機能の上に、ゲーム固有の機能を「ゲーム固有機能」セクションへ追記していく。

## 概要

新しいゲームを素早くセットアップするための Unity 6 ゲームテンプレート。
共通基盤（シーン管理・サウンド・オプション・オンライン対戦の土台）を提供し、ゲーム固有の機能はこの土台の上に実装する。

## テンプレートが提供する基盤・共通機能

- BGM / SE 再生（音量調整・永続化）→ [architecture.md](architecture.md)「サウンド設計」
- Common シーンを常駐させたアディティブシーン管理・フェード画面遷移演出 → [architecture.md](architecture.md)「シーン構成」
- オプションモーダル（音量設定など）→ [architecture.md](architecture.md)「オプションモーダル」
- オンラインマッチングの土台（クイックマッチ・ルーム一覧から手動参加）→ [matchmaking.md](matchmaking.md)
- NGO によるネットワーク同期の土台（セッション接続・メッセージ送受信のハマりポイントと定石）→ [networking.md](networking.md)

## ゲーム固有機能（プロジェクトごとに追記）

このテンプレートをコピーして作るゲームでは、実装した機能をここに列挙し、詳細は各ドキュメントへのリンクに集約する。

- タイトル画面の演出（背景動画を再生し、終了後に「ドラゴンファミリー/すごろく」を3行・1文字ずつ上から降らせて表示。初回再生開始から30秒おきに文言を隠して動画を最初から再生し直すループ。画面全体が「Press start」ボタンで、文言は点滅して入力を促す。動画は WebGL 対応のため StreamingAssets を `VideoPlayer` の URL 再生）→ [Assets/Scripts/Title/Video/](../Assets/Scripts/Title/Video/)
- 一人用 / オンラインの2モード選択（Home で分岐。一人用はネットワーク非依存）→ [architecture.md](architecture.md)「シーン構成」
- クレジット表示（Home のクレジットボタンでモーダルを開き、制作・イラスト・使用技術などを表示）→ [Assets/Scripts/Home/](../Assets/Scripts/Home/)
- キャラクター選択（一人用は Main の前に CharacterSelect で選ぶ。立ち絵を全画面背景、アイコンの選択スロットを下部に表示。画面上部のタイトルには選択中キャラの名前を表示。選択は `CharacterSessionModel` に保持。画像は Addressables、現状オンライン非対応）→ [Assets/Scripts/CharacterSelect/](../Assets/Scripts/CharacterSelect/)
- 円盤ルーレット（ボタンを長押し中は加速して回転し、離すと減速。すぐ離しても最低 1.5〜2.5 秒（ランダム）は回ってから、自然に止まった位置のセクターが出目になり移動マス数を決定。Painter2D で描画・`Update` で角速度を加減速）→ [Assets/Scripts/Main/Roulette/](../Assets/Scripts/Main/Roulette/)
- すごろくのループ盤面（外周にマスを並べたループ盤。ルーレットの出目ぶんコマを1マスずつ移動し、1周してゴール＝スタートに到達するとクリア。1コマ・普通マスのみ）→ [Assets/Scripts/Main/Board/](../Assets/Scripts/Main/Board/)
- ミニゲーム（タップ連打。5秒間のタップ数を競う。Main を残したまま MiniGame シーンを Additive で重ねて起動し、勝利なら盤面にボーナス前進。中身は `MiniGameId` に応じて Addressables で差し替え、将来最大5種類）→ [architecture.md](architecture.md)「シーン構成」・[Assets/Scripts/MiniGame/](../Assets/Scripts/MiniGame/)

## 未実装（今後の課題）

- ミニゲームのネットワーク同期（現状はローカル完結。ホスト権威での開始合図・全員のスコア集約による順位判定は未実装。勝者判定は暫定的にローカルのしきい値で代用）
- ミニゲームの起動トリガー（現状は Main のテスト用ボタン。盤面の特殊マスやターン連携は未実装）
- タップ連打以外のミニゲーム（最大5種類を想定。`MiniGameId` への追加と対応 UXML・進行ロジックの実装で増やす）
