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

- タイトル画面の演出（背景動画を再生し、終了後に「ドラゴンファミリー/すごろく」を3行・1文字ずつ上から降らせて表示。初回再生開始から30秒おきに文言を隠して動画を最初から再生し直すループ。画面全体が「Press start」ボタンで、文言は点滅して入力を促す。動画は WebGL 対応のため StreamingAssets を `VideoPlayer` の URL 再生。サウンドは動画再生と同時に歓声（Cheer）を鳴らし、鳴り終わってからタイトル BGM（光晴イズム）へ移る）→ [Assets/Scripts/Title/Video/](../Assets/Scripts/Title/Video/)
- 一人用 / オンラインの2モード選択（Home で分岐。一人用はネットワーク非依存で CPU と 1 対 1 のすごろく対戦。背景演出の詳細は [CLAUDE.md](../CLAUDE.md)「主要ファイルの場所」HomePresenter の行）→ [architecture.md](architecture.md)「シーン構成」
- クレジット表示（Home のクレジットボタンでモーダルを開き、制作・イラスト・使用技術などを表示）→ [Assets/Scripts/Home/](../Assets/Scripts/Home/)
- キャラクター選択（一人用は Main の前に CharacterSelect で選ぶ。全8種。立ち絵を全画面背景、カード絵の選択スロットを下部に表示。戻る／決定ボタンは画面上部（右上のオプションアイコンを避けて中央寄せ）。キャラ名は各カード内に表示。選択は `CharacterSessionModel` に保持。画像は Addressables、現状オンライン非対応）→ [Assets/Scripts/CharacterSelect/](../Assets/Scripts/CharacterSelect/)
- 円盤ルーレット（8分割・出目1〜8。ボタンを長押し中は回転し、離すと減速して止まった位置のセクターが出目になり移動マス数を決定。CPU の番は同じ円盤が自動で回る。減速・コイン表示・SE などの演出仕様は [CLAUDE.md](../CLAUDE.md)「主要ファイルの場所」RoulettePresenter の行）→ [Assets/Scripts/Main/Roulette/](../Assets/Scripts/Main/Roulette/)
- CPU 対戦のターン進行（一人用モード。あなたが先攻で以降交互。人間の番は手動でルーレット、CPU の番は同じ円盤が自動で回る。ルーレットが消えてから手番プレイヤーのコマが出目ぶん進み、先に 1 周ゴールした方が勝ち。`GameFlowController` が統括し、オンラインは参加者 1 人で従来どおり単独プレイ）→ [Assets/Scripts/Main/Turn/](../Assets/Scripts/Main/Turn/)
- すごろくのループ盤面（盤面データ `BoardDefinition` を読んで外周マスのループ盤面を描画し、手番プレイヤーのコマをルーレットの出目ぶん移動。1周してゴール＝スタートに到達すると勝ち。各マスはイベント（進む/戻る/休み/ミニゲーム・お金アップ/ダウン）と見た目（色・アイコン）を持ち、お金マスのみ着地で発動。描画・レイアウト・ネームプレートの詳細は [CLAUDE.md](../CLAUDE.md)「主要ファイルの場所」BoardPresenter の行）→ [Assets/Scripts/Main/Board/](../Assets/Scripts/Main/Board/)
- 所持金（お金）（プレイヤーごとに所持金を持つ。初期 1000・マイナス＝借金も可。お金アップ/ダウンのマスに止まると増減し、盤面上部の自分ネームプレートにコイン＋金額で表示・マイナスは赤字。金額は盤面エディタでマスごとに設定。将来はミニゲーム勝利での獲得にも対応予定）→ [Assets/Scripts/Main/Money/](../Assets/Scripts/Main/Money/)
- 盤面エディタ（`Window > Sugoroku > Board Editor`。方眼をクリックして経路順にマスを置き＝盤面の形・経路を自作、選択マスのイベント・数値・色・アイコンアドレスを編集して `BoardDefinition` アセットとして保存。作った盤面は `BoardPresenter` の Definition 欄に割り当てて使う）→ [Assets/Scripts/Main/Editor/](../Assets/Scripts/Main/Editor/)
- ミニゲーム（現状2種。いずれも Main を残したまま MiniGame シーンを Additive で重ねて起動し、中身は `MiniGameId`／`MiniGameCatalog` で差し替える〔将来最大5種類〕。動作確認は専用の MiniGameTest シーンから行う）→ [architecture.md](architecture.md)「シーン構成」・[Assets/Scripts/MiniGame/](../Assets/Scripts/MiniGame/)
  - タップ連打：5秒間のタップ数を競う。選択中キャラのカード絵を中央に表示し、タップのたびにカードが「がたがた」振動＋「パンチ」拡大で弾む
  - 2Dレース：選択キャラ vs CPU の1対1。走者が右から左へ進み先着で勝ち。全員ベース速度でゆっくり進み、プレイヤーは高速往復するメーターをタップで止め、Great（大きく前進）／Good（少し前進）／Miss（進まない）の判定で前へ（タップ後は一瞬フリーズして自動再開）。CPU はプレイヤーと同じベース速度で進み、ランダム間隔で Great/Good/Miss を抽選して前進（Great は低確率）。スコアは勝ち=1／負け=0。各キャラの走行絵は動物 Run 画像（`RunAddress`）

## 未実装（今後の課題）

- ミニゲームのネットワーク同期（現状はローカル完結。ホスト権威での開始合図・全員のスコア集約による順位判定は未実装。勝者判定は暫定的にローカルのしきい値で代用）
- ミニゲームの起動トリガー（現状は動作確認用の MiniGameTest シーンから手動起動するのみ。盤面の特殊マスや手番との正式なゲーム内連携は未実装）
- 盤面マスのイベント発動（お金アップ/ダウンは着地で発動する。進む/戻る/休み/ミニゲームは `BoardDefinition` で編集・盤面に記号表示できるが、止まったときに実際に発動させる処理は未実装。発動には `GameFlowController` / `TurnModel` への組み込みが必要）
- ミニゲーム勝利での所持金獲得（`MoneyModel.Add` を呼ぶ拡張点は用意済みだが、ミニゲーム結果と所持金の連携は未実装）
- オンライン対戦の手番同期（現状 `GameFlowController` は CPU 対戦とローカル単独プレイのみ。NGO 経由での手番・出目の同期は未実装）
- 3種類目以降のミニゲーム（最大5種類を想定。現状はタップ連打・2Dレースの2種。`MiniGameId`／`MiniGameCatalog` への追加と対応 UXML・進行ロジックの実装で増やす。MiniGameTest シーンにボタンが自動で並ぶ）
