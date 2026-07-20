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

- タイトル画面の演出（背景動画＋タイトル文言の降下演出をループ再生。画面全体が「Press start」ボタン。動画は WebGL 対応のため StreamingAssets を `VideoPlayer` の URL 再生。演出の詳細は [CLAUDE.md](../CLAUDE.md)「主要ファイルの場所」TitleVideoPresenter の行）→ [Assets/Scripts/Title/Video/](../Assets/Scripts/Title/Video/)
- 一人用 / オンラインの2モード選択（Home で分岐。一人用はネットワーク非依存で CPU とのすごろく対戦〔人数 2〜8 を MapSelect で選ぶ〕。背景演出の詳細は [CLAUDE.md](../CLAUDE.md)「主要ファイルの場所」HomePresenter の行）→ [architecture.md](architecture.md)「シーン構成」
- クレジット表示（Home のクレジットボタンでモーダルを開き、制作・イラスト・使用技術などを表示）→ [Assets/Scripts/Home/](../Assets/Scripts/Home/)
- キャラクター選択（一人用は Main の前に CharacterSelect で選ぶ。全8種。立ち絵を全画面背景、カード絵の選択スロットを下部に表示。戻る／決定ボタンは画面上部（右上のオプションアイコンを避けて中央寄せ）。キャラ名は各カード内に表示。選択は `CharacterSessionModel` に保持。画像は Addressables、現状オンライン非対応）→ [Assets/Scripts/CharacterSelect/](../Assets/Scripts/CharacterSelect/)
- マップ選択＋人数選択（一人用はキャラ選択の後、Main の前に MapSelect で対戦マップと**プレイヤー人数〔自分＋CPU・2〜8〕**を選ぶ。複数の盤面 `BoardDefinition` を `BoardCatalog` にまとめておき、盤面の形の簡易サムネイル＋マップ名でカード一覧。人数は −／＋ ステッパーで選び `PlayerCountSessionModel` に保存。選んだマップの盤面・人数で対戦する。選択は `BoardSessionModel` に識別子で保持し Main の `BoardPresenter` がカタログから実体を解決。オンラインは既定マップ＝カタログ先頭・人数選択は不使用）→ [Assets/Scripts/MapSelect/](../Assets/Scripts/MapSelect/)
- 円盤ルーレット（8分割・出目1〜8。ボタンを長押し中は回転し、離すと減速して止まった位置のセクターが出目になり移動マス数を決定。CPU の番は同じ円盤が自動で回る。減速・コイン表示・SE などの演出仕様は [CLAUDE.md](../CLAUDE.md)「主要ファイルの場所」RoulettePresenter の行）→ [Assets/Scripts/Main/Roulette/](../Assets/Scripts/Main/Roulette/)
- CPU 対戦のターン進行（一人用モード。あなたが先攻で以降交互。**勝敗は陣地マスの占拠数で決まる**（下記）。`GameFlowController` が統括し、オンラインは接続した実プレイヤーぶん＝最低 2 人（単独プレイは廃止・盤面のターン同期は未実装）。進行フローの詳細は [architecture.md](architecture.md)「シーン構成」）→ [Assets/Scripts/Main/Turn/](../Assets/Scripts/Main/Turn/)
- すごろくのループ盤面（盤面データ `BoardDefinition` を読んで外周マスのループ盤面を描画し、手番プレイヤーのコマをルーレットの出目ぶん移動。周回勝利は廃止したのでスタート＝ゴールを通過して回り続ける。各マスはイベント（進む/戻る/休み/ミニゲーム・お金アップ/ダウン・陣地・アイテム）と見た目（色）を持ち、マスの画像はイベント種別ごとに**全マップ共通**（`BoardEventArtCatalog`・`Board/<イベント名>` 規約）で解決する（全マス共通の枠画像は盤面ごとに重ねられる）。スタート＝ゴール（先頭マス）は固定で `Board/Start` の画像を使う。お金マス・陣地マス・アイテムマスが着地で発動。**盤面は左下のズームボタンで表示列数を段階ズーム**でき、画面外へはみ出したぶんはドラッグでパンして見る〔横長マップ向け・`BoardZoomController`。段階・操作の詳細は [CLAUDE.md](../CLAUDE.md)「主要ファイルの場所」BoardZoomController の行〕。描画・レイアウト・ネームプレートの詳細は [CLAUDE.md](../CLAUDE.md)「主要ファイルの場所」BoardPresenter の行）→ [Assets/Scripts/Main/Board/](../Assets/Scripts/Main/Board/)
- 陣地マスの占拠と勝利判定（陣地マスに止まるとそのマスを占拠し＝占拠者のキャラの旗画像に塗り替え、相手の陣地でも上書きで奪える。盤面の陣地マス総数を**プレイヤー数で割った数（端数切り上げ）**を先に占拠したプレイヤーが勝ち〔例：4人・陣地8マスなら 2 マスで勝利〕。占拠状態と勝利判定は `TerritoryModel` が持つ。陣地マスは盤面エディタでイベント「Territory」を選んで配置する〔0 個だと勝者が出ない〕。**勝敗が確定すると盤面下部に「ホームに戻る」ボタンが出て、押すと Home へ戻れる**〔勝ち・負けどちらでも表示。`BoardPresenter` が `SceneTransitioner.Transit(Scenes.Home)`〕）→ [Assets/Scripts/Main/Board/](../Assets/Scripts/Main/Board/)
- 所持金（お金）（プレイヤーごとに所持金を持つ。初期 1000・マイナス＝借金も可。お金アップ/ダウンのマスに止まると増減し、盤面上部の自分ネームプレートにコインアイコン＋金額で表示・マイナスは赤字。金額は盤面エディタでマスごとに設定。ミニゲームアイテムでミニゲームに勝ったときの報酬〔既定 +500〕でも増える）→ [Assets/Scripts/Main/Money/](../Assets/Scripts/Main/Money/)
- プレイヤー情報のネームプレート（盤面上部に**全プレイヤー**を横 1 行で表示・1 画面最大 2 人で超えるぶんは左右端の三角ボタンでページ送り〔4 人＝2 ページ〕。各プレートは横型で左にキャラの丸アイコン、右に選択キャラ名・所持金・占領地数〔「占拠数 / 勝利に必要な数」＝総数÷プレイヤー数の切り上げを分母にして勝利までの進捗を示す・陣地マスが無い盤面では非表示〕を並べ、上辺をそのプレイヤー色で色分け〔盤面の色分けの凡例〕。所持金・占領地はリアルタイム更新。行頭アイコンは `Image/Icon` の画像を使用）→ [Assets/Scripts/Main/Board/PlayerNameplateView.cs](../Assets/Scripts/Main/Board/PlayerNameplateView.cs)
- プレイヤーの色分け（コマ・陣地占拠マス・ネームプレート上辺を p0〜p7 の 8 色で色分けし、3 人以上でも各プレイヤーを見分けられる。共通ヘルパ `PlayerColors`。同じマスに複数コマが乗ったときは円状にずらして全員見えるようにする〔`BoardPresenter.RefreshPieceOffsets`〕）→ [Assets/Scripts/Main/Board/PlayerColors.cs](../Assets/Scripts/Main/Board/PlayerColors.cs)
- アイテム取得（アイテム取得マス〔イベント「Item」〕に止まると `ItemCatalog` からランダムに1枚もらえる。取得したアイテムは画面右下に手札としてサムネイル表示〔自分＝人間プレイヤーのぶんのみ。CPU は内部的に貯まるが非表示。同じアイテムはカードを増やさず「x2」の枚数バッジでまとめる〕。手札のアイテムはクリックで詳細モーダル〔絵・名前・効果説明＋「使用する」「閉じる」〕が開き、「使用する」で効果が発動して手札から 1 枚消費される〔x2 バッジ減算・最後の 1 枚ならカード消滅〕。**「使用する」は自分の手番かつルーレット未回転のときだけ有効**〔回した後・コマ移動中・相手の手番中・別のアイテム効果の実行中は無効〕。**「陣地獲得」は効果実装済み**〔使用すると自分以外が持つ陣地マスが金枠＋キラキラで強調され、盤面タップ〔ドラッグでパンも可〕で 1 つ選んで占拠する。既存の旗演出→占拠→必要数なら勝利。選択のキャンセルでは消費しない。ターンは消費せず使用後もルーレットを回せる〕。**「ミニゲーム」も効果実装済み**〔使用すると遊ぶミニゲームを選ぶモーダル〔`MiniGameCatalog` をサムネイル画像＋ゲーム名のカード一覧〕が開き、選んだミニゲームを起動して勝てば所持金報酬〔既定 +500〕をもらえる。2Dレースは先着で勝ち・タップ連打はタップ数を CPU 想定値と比べて 1 位なら勝ち。キャンセルでは消費しない。ターン非消費で選択・プレイ中はスピン無効〕。**「お金よこどり」も効果実装済み**〔使用すると相手（人間以外の参加者＝CPU）の所持金の一部＝現在の所持金の20〜50％をランダムに奪って自分に足し、増額を中央の浮遊テキストで見せる。相手の所持金が正のときだけ発動〔奪える額が無ければ消費しない〕。相手の所持金もネームプレートに出るので減るのは見えるが演出は自分側のみ。ターン非消費で演出中はスピン無効。奪う額の計算は `MoneyStealRule`〕。アイテム種別・画像・効果説明は `ItemId`／`ItemCatalog`、手札の保持・消費は `ItemModel`、モーダルは `ItemModalPresenter`、効果の発動は `BoardPresenter.HandleItemUse`。アイテム画像は Addressables〔`Image/Item/*`〕、未配置ならアイテム名の文字で代替）→ [Assets/Scripts/Main/Item/](../Assets/Scripts/Main/Item/)
- マス着地の演出（コマが止まると、止まったマスの種別に応じた演出＝マス画像の中央ポップ・お金の増減額浮遊テキスト・陣地の旗演出・アイテムの抽選絵表示を再生する。実装は `BoardPresenter.PlayLandingSequenceAsync`。演出フロー・秒数の詳細は [architecture.md](architecture.md)「シーン構成」の着地演出の項）→ [Assets/Scripts/Main/Board/](../Assets/Scripts/Main/Board/)
- 盤面エディタ（`Window > Sugoroku > Board Editor`。方眼をクリックして経路順にマスを置き＝盤面の形・経路を自作、マップ名（マップ選択に表示）・選択マスのイベント・数値・色を編集し、盤面共通の枠画像アドレス・方眼サイズを設定して `BoardDefinition` アセットとして保存（マスのイベント画像はイベント種別ごとに全マップ共通＝`BoardEventArtCatalog` なので盤面ごとの設定は無い）。グリッドのマスはイベント種別ごとの色で塗り分けられ〔カスタム色未設定時〕、色→イベントの対応表もグリッド下に出るので、どのマスに何のイベントを置いたか一目で分かる。作った盤面は `BoardCatalog` にまとめて MapSelect で選ばせる〔単発なら `BoardPresenter` の Definition 欄に割り当ててフォールバック使用も可〕）→ [Assets/Scripts/Main/Editor/](../Assets/Scripts/Main/Editor/)
- ミニゲーム（現状3種。いずれも Main を残したまま MiniGame シーンを Additive で重ねて起動し、中身は `MiniGameId`／`MiniGameCatalog` で差し替える〔将来最大5種類〕。動作確認は専用の MiniGameTest シーンから行う＝人数ステッパー〔2〜8〕で参加者数を選んで起動できる）→ [architecture.md](architecture.md)「シーン構成」・[Assets/Scripts/MiniGame/](../Assets/Scripts/MiniGame/)
  - タップ連打：5秒間のタップ数を競う。選択中キャラのカード絵をタップボタンの上に表示し、タップのたびにカードが「がたがた」振動＋「パンチ」拡大で弾む。タップ数はボタン上に表示（カウントダウン中は非表示）
  - 2Dレース：選択キャラ（YOU）＋CPU の複数人レース（参加者数に応じてレーンを動的生成。本番の盤面ミニゲームは自分＋CPU の2人、MiniGameTest シーンは 2〜8 人を選べる。CPU は互いにもプレイヤーとも被らないキャラを配布）。走者が右から左へ進み先着で勝ち。全員ベース速度でゆっくり進み、プレイヤーは高速往復するメーターをタップで止め、Great（大きく前進）／Good（少し前進）／Miss（進まない）の判定で前へ（タップ後は一瞬フリーズして自動再開）。各 CPU はプレイヤーと同じベース速度で進み、独立したタイマーでランダムに Great/Good/Miss を抽選して前進（Great は低確率）。スコアは勝ち=1／負け=0。各キャラの走行絵は動物 Run 画像（`RunAddress`）
  - 被っちゃやーよ：参加者数ぶんのランダムなアイテム（`ItemCatalog` から重複なしで抽選・アイテム絵は `Image/Item/*`）から1枚を選び、他の誰とも被らなければ獲得＝勝ち。CPU の選択は seed で確定（決定的）。アイテム選択は3秒の制限時間があり、時間内に選べなければ無効票（獲得できない）。提示枚数は参加者数と一致（本番の盤面ミニゲームは自分＋CPU の2枚、MiniGameTest シーンは 2〜8 人を選べる＝`MiniGameSessionModel.PlayerCount` 経由。カタログ総数が上限）。スコアは獲得=1／被り・無効票=0

## 未実装（今後の課題）

- ミニゲームのネットワーク同期（現状はローカル完結。ホスト権威での開始合図・全員のスコア集約による順位判定は未実装。勝者判定は暫定的にローカルのしきい値で代用）
- ミニゲームの起動トリガー（動作確認用の MiniGameTest シーンと、**ミニゲームアイテム**〔手札から「ミニゲーム」を使用〕から起動できる。盤面の特殊マス〔`BoardCellEvent.MiniGame`〕や手番との正式なゲーム内連携は未実装）
- 盤面マスのイベント発動（お金アップ/ダウン・陣地・アイテム取得は着地で発動する。進む/戻る/休み/ミニゲームは `BoardDefinition` で編集・盤面に記号表示できるが、止まったときに実際に発動させる処理は未実装。発動には `GameFlowController` / `TurnModel` への組み込みが必要）
- オンライン対戦の手番・キャラ同期（参加者リストは最低 2 人〔`GameParticipants` の Human×2〕になったが、`GameFlowController` の手番進行はローカル駆動のまま。各プレイヤーのキャラ選択・出目・手番を NGO 経由で同期する実装が未対応で、現状は各クライアントがローカルで両プレイヤーを操作する暫定状態）
- 4種類目以降のミニゲーム（最大5種類を想定。現状はタップ連打・2Dレース・被っちゃやーよの3種。`MiniGameId`／`MiniGameCatalog` への追加と対応 UXML・進行ロジックの実装で増やす。MiniGameTest シーンにボタンが自動で並ぶ）
