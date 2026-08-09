# 設計ドキュメント

## 設計方針

新しいゲームを作り始めるときの「土台」として機能するテンプレート。以下を目標としている。

- **DI・リアクティブを標準** とし、`Find()` や static は使わない
- **非同期を UniTask** で統一し、キャンセル処理を明示的に行う
- **アセットは Addressables** で遅延ロードし、`Resources.Load` は使わない
- **UI は UI Toolkit（UXML）** で構築し、uGUI は使わない

---

## シーン構成

```
Common (常駐)
  ├── SoundPlayer
  ├── SceneTransitioner
  ├── TransitionPresenter
  ├── OptionPresenter / OptionModalPresenter / OptionModel
  ├── GameSessionModel
  ├── CharacterSessionModel / BoardSessionModel
  ├── MiniGameLauncher / MiniGameSessionModel
  ├── NetworkManager（NGO・ルートオブジェクト。Relay 接続は Matching で張るので Main では遅い）
  └── Store 群（SoundStore / ModalStore ← AssetStoreBase を継承）

Title → Home ┬─（一人用モード）→ CharacterSelect → MapSelect ──────────→ Main
             └─（オンライン）→ Matching → OnlineCharacterSelect（満室後）→ Main
                                                                          └─（ミニゲーム）→ MiniGame（Main を残して重ねる）

（開発用）MiniGameTest → MiniGame（テストシーンを残して重ねる）  ※本番フロー外・エディタで直接起動
```

（すべて `Common` の上にアディティブでロード。画面は縦向き＝ポートレート固定）

- `Common` シーンは起動時にロードされ、以降アンロードされない
- 他シーンは `Common` の上にアディティブでロード・アンロードされる
- シーン遷移は `SceneTransitioner.Transit(Scenes next)` を呼ぶだけでよい
- 遷移時は `TransitionPresenter` が画面をフェードアウト→ロード→フェードインの演出を行う
- **Home で2モードを分岐**する。「一人で遊ぶ」（一人用モード）は `GameSessionModel.SetSinglePlayer()` を呼んで `CharacterSelect`（キャラ選択）へ遷移し、キャラ確定後に `MapSelect`（マップ選択＝マップとプレイ人数 2〜4 を選ぶ）へ、マップ確定後に `Main` へ進み、**CPU とのすごろく対戦**を行う。「オンラインプレイ」は `Matching` を経由し、**満室になったら `OnlineCharacterSelect`（キャラ選択ロビー・被り防止）を経て `Main` へ進む**（**マップはホストがルーム作成時に選び**＝`Matching` の全画面マップ選択オーバーレイ〔MapSelect と共通の `MapPickerView`〕・`CharacterLobbySync` の共有状態 `lobbyState.board` で全員へ同期する）
- **CharacterSelect** は選択中キャラの立ち絵を全画面背景に、カード絵の選択スロットを画面下部に表示する。各キャラは Addressables に 5 系統の画像アドレスを持ち、**アドレスは 5 系統とも `Character/Character<N>/<系統名>` で統一する**（`/Card`＝選択カード絵・`/Icon`＝盤面コマの丸バッジ・`/Portrait`＝立ち絵・`/Run`＝2Dレースミニゲームの走行絵・`/Flag`＝陣地マス占拠時の旗絵）。素材のファイル名をアドレスに出さないので、キャラを足すときに系統ごとで別のキャラを指す取り違えが起きない。CharacterSelect は Card と Portrait をロードし、盤面（`BoardPresenter`）はコマに Icon・陣地の旗演出と占拠マスの塗りに Flag を、2Dレース（`RaceGamePlay`）は走者に Run を使う。未配置のアドレスは色面プレースホルダにフォールバックする。選択結果は Common シングルトンの `CharacterSessionModel` に保持し、`Main` でも参照できる（この一人用フローの CharacterSelect は自分 1 人ぶんの選択。オンラインは別途 `OnlineCharacterSelect` で全員ぶんを被らないよう選ぶ＝次項）
- **OnlineCharacterSelect** はオンラインのキャラ選択ロビー（マッチング満室後・`Main` の前）。全員が使うキャラを選び、**被らない仕組み**として UGS のプレイヤープロパティ（各自の希望キャラ・決定フラグ）とセッション共有プロパティ（ホストが書くロック表・開始ロースター）で同期する。**ホストが審判**となり `CharacterClaimResolver`（純粋ロジック）で先着ロックを決めて全員へ共有し、他プレイヤーがロックしたキャラはグレーアウトして選べない。同期は `CharacterLobbySync` がライブなセッションのポーリング（600ms・`MatchingService.WaitForPlayerAsync` と同方式で `RefreshAsync` 不要）で行い、NGO は使わない（キャラ選択は数百 ms の遅延で困らず、UGS のプロパティだけで完結するほうが単純なため）。全員が「決定」してユニークな割り当てが揃うと、席順（参加時刻昇順）→キャラの割り当てを Common の `OnlineRosterSessionModel` に保存して `Main` へ遷移する。`Main` の `CpuCharacterPicker`（各席のキャラ）と `BoardPresenter`（自分の席＝人間プレイヤー）はこのロースターを参照する。**ホストがルーム作成時に選んだマップも同じ共有プロパティ（`lobbyState.board`）に載せ**、ゲストは開始確定時に `BoardSessionModel.Select` で反映してから遷移するので、全員が同じ盤面で `Main` に入る。**入室時は自分の席順（参加順）ごとの初期キャラが選択済みで開く**（`CharacterCatalog.DefaultFor(seat)`＝カタログの表示順そのまま〔1P=のらどっく / 2P=ザニザニマン / 3P=D.O.M / 4P=アリマ〕。席ごとにずらすので初期状態から誰とも被らず、誰も操作しなくてもホストの集計が全員ぶんを別キャラでロックできる＝そのまま「決定」を押すだけでも成立する。自席は `CharacterClaimResolver.SeatIndexOf` で求め、確定ロースターを組む `BuildRoster` と同じ参加順に揃える）。UI は選択で立ち絵を全画面背景表示（オフラインの CharacterSelect と同じ遅延ロード＋キャッシュ。初期キャラの立ち絵は表示前にロードする）・自分の選択＝黄枠／「決定」で緑枠（往復を待たず自分の画面は即時＝楽観表示）。**全員がロビーに到着するまでは選択・決定させない**（`CharacterLobbySync.AllPresent`＝自分のプレイヤープロパティを 1 度でも書いた人数が定員に達したか。シーン遷移とカード絵のロードで到着に差が出るため、先に選ぶとロックを集計してもらえないまま待ち続けることになる。待っている間は「他のプレイヤーの参加を待っています...（◯/◯人）」を出す）
- **MapSelect** は複数の盤面（`BoardDefinition`）から対戦マップを選ぶ。マップ一覧は `BoardCatalog`（ScriptableObject。`BoardDefinition` は SO 資産で静的クラスから参照できないため、キャラの `CharacterCatalog` とは異なりカタログ自身も SO にして資産参照のリストを持つ）が持ち、各マップは盤面の形を Painter2D で描く簡易サムネイル（`BoardSchematicView`〔`Main/Board/`・Main 型のみ依存〕・画像アセット不要。**各マスをイベント種別ごとの色〔`BoardEventColors`・盤面エディタと共通の配色〕で塗り分ける**ので、どんなイベント構成のマップかをサムネイルだけで見分けられる）とマップ名（`BoardDefinition.DisplayName`。空なら資産名にフォールバック）でカード表示する。**サムネイルは「実際にマスが占めている範囲」（`BoardSchematicView.BoundsOf`）で正規化する**（方眼キャンバスの寸法〔`GridColumns`／`GridRows`〕基準にすると、マスを置いていない行・列のぶん盤面が片寄って空白になる）。その範囲の縦横比を保って要素の中央へ内接させるので、横長マップが正方形の枠に引き伸ばされない。**大プレビューの枠自体もマップの形に合わせて可変**で、USS が与えた寸法を基準ボックスとして縦横比を内接させた大きさに `MapPickerView` が設定する（一直線のマップで枠が潰れないよう縦横比は 1:4〜4:1 に丸める）。**画面の背景には固定画像 `Image/StageBackground` を全画面で敷き**、上に暗いスクリムを重ねて前面 UI の視認性を確保する（全画面 1 枚の均一なスクリム。Home は同じ構成から「薄い全面幕＋下へ向かって濃くなる帯」の疑似グラデーションへ発展させてある＝[design-system.md](design-system.md)「背景イラストの上に UI を載せる」。`ISceneReady.ReadyAsync` でロードを待ってからフェードインする）。**選択中マップは大プレビューの下にイベント内訳〔総マス数＋「色ドット＋ラベル ×N」のチップ〕を出す**（陣地・アイテム・お金…がどれだけあるかをひと目で把握。集計は純粋関数 `BoardEventTally.Summarize`＝スタート/通常マスを除外し陣地を先頭に表示順で並べる。色・ラベルはランタイム共通の `BoardEventColors`／`BoardEventLabel` が単一の情報源で、盤面エディタ〔`EventColor`／`EventLabel`〕と同じ配色・文言を使う）。**カード一覧・大プレビュー・イベント内訳・選択状態は共通の `MapPickerView`〔`Main/Board/`〕に切り出し、`MapSelect` シーンとオンラインのルーム作成マップ選択〔`Matching` の全画面オーバーレイ〕で共用する**（USS クラスは共通なので埋め込む側の USS に同じクラスを定義する）。選択結果は Common シングルトンの `BoardSessionModel` に**識別子（マップ資産名）**として保持し、`Main` の `BoardPresenter` が `BoardCatalog.Find(識別子)` で実体を解決する（`CharacterSessionModel` と同型だが、Common から Main の `BoardDefinition` を参照できないため文字列 ID だけを持つ）。`BoardCatalog` 資産は `MapSelect` シーンの `MapSelectPresenter`・`Matching` シーンの `MatchingPresenter`・`Main` の `BoardPresenter` にインスペクタで割り当てる。マップ未選択や未割り当て時は `BoardPresenter._definition` にフォールバックする（オンラインはホストの選択が `CharacterLobbySync` で全員に同期されるため未選択にはならない）
- **NGO は Relay 経由で繋がる**（`SessionOptions.WithRelayNetwork()`）。接続の確立は `Matching` シーンでのセッション作成/参加時で、停止は `ISession.LeaveAsync()` のとき＝**NGO のライフサイクルは UGS セッションが握り、シーンの寿命とは無関係**になる。そのため `NetworkManager` は `Common` に常駐させ（ルートオブジェクト）、アプリ側からは `StartHost` / `StartClient` / `Shutdown` を呼ばない。`Main` の `NetworkSessionStartup` は接続が整うのを**待つだけ**で、整ったら `Connected` 通知の**前**に `OnlineGameSync.OnConnected()` を呼んでメッセージハンドラを永続登録する（最初のアクションから取りこぼさないため）。一人用モードは NGO を使わないので即 `Connected` 扱いにする。詳細は [networking.md](networking.md)「Relay 経由の接続」
- **ゲームを抜けるときは必ずセッションを離脱する**。NGO の寿命がシーンから切り離されたので、離脱しないとルームに残ったままになる。`BoardPresenter.ReturnHomeAsync`（「ホームに戻る」）と `GameSessionModel.SetSinglePlayer`（一人用モードの選択）が離脱を担う
- **`Main` の盤面進行は `OnlineGameSync`（`Main/Online/`）のアクションストリームで同期する**。進行を進める「決定」を `GameAction` にして、決めた 1 人が発行 → ホストが唯一の順序付け役として全員へ再配信 → **決めた本人も含め受信したアクションだけを適用**する。乱数（お金マスの増減額）・モーダル操作（アイテムショップの購入）・アイテム効果はすべて「決定（1 人）」と「適用（全員）」に分かれる。コマ移動・陣地占拠・勝敗判定は決定論的に導けるので配らない。**モーダル操作やミニゲームで 1 人だけが操作している間は、他のクライアントに待機表示（`WaitingBanner`＝「〔キャラ名〕が◯◯中…」）を出す**（相手から見えない操作＝ミニゲーム・陣地選択だけ `GameAction.Busy` で知らせ、相手の手番のルーレット待ち・アイテムショップは手番・ルーレット状態・着地マスから導けるので配らずローカルで出す）。一人用モードも同じストリームを通す（発行が即ローカルのキューへ積まれるだけ）ので進行のコードパスが一本化する。詳細は [networking.md](networking.md)「ゲーム進行の同期」・[patterns.md](patterns.md) #14
- **手番進行は `GameFlowController`（`Main/Turn/`）が統括する**。参加者は `GameParticipants` が `GameMode` から決める（一人用＝`[Human, Cpu×(人数-1)]`＝人数は `PlayerCountSessionModel`〔MapSelect で 2〜4 を選ぶ〕、オンライン＝`[Human×N]`＝ルーム定員ぶん〔2〜4・ホストがルーム作成時に選ぶ・`GameSessionModel.SessionMaxPlayers`（=`ISession.MaxPlayers`）を `GameParticipants.OnlinePlayerCountFrom` で下限2にクランプ・単独プレイは廃止。キャラの割り当ては `OnlineCharacterSelect` ロビーで同期して各席を被らないキャラで描画し、盤面の進行は `OnlineGameSync` のアクションストリームで同期する＝上記〕）。CPU のキャラは `CpuCharacterPicker` が人間とも他 CPU とも被らないよう配る（`CpuCharacterPicker` は DI で Scoped 共有し、盤面のコマ・ネームプレートとルーレットのセクターで同じ CPU キャラを使う）。**ルーレットは「止まったキャラが進む」方式**で、円盤のセクターに参加者（自分＋CPU）をラウンドロビンで均等配置し（`RouletteMath.ParticipantForSector`・ゲーム開始時に固定）、各参加者が同じ数字セット 1〜K を 1 枚ずつ持つよう番号を振る（`RouletteMath.StepsForSector`・K＝`RoulettePresenter._numbersPerCharacter` 既定 3・セクター総数＝人数×K）。`GameFlowController` は接続完了を待ってから「手番プレイヤー（＝スピンする人）を見る → **その席を担当するクライアントだけ**が回して（人間なら手動スピンの停止を待つ／CPU なら円盤を自動で回す）**回し始め（`SpinStart`）と停止位置（`Spin`）を `OnlineGameSync` へ発行する**（停止位置は円盤が止まるのを待たず「押下を離した瞬間」に確定する＝`SpinDecision`） → **全員が受信したセクターから**進む人とマス数を復元し（**止まったセクターのキャラ＝進む人 + マス数**。自分で回していないクライアントは `SpinStart` で一緒に円盤を回し始め〔`RoulettePresenter.BeginRemoteSpin`〕、`Spin` を受けて同じセクター・同じ減速時間で止める〔`RoulettePresenter.PlaySpinToAsync`〕ので、相手が回している間も画面が止まらず結果もほぼ同時に出る）、ルーレットが消える（`WaitForHideAsync`）のを待ってから**進む人**のコマをマス数ぶん進める → 勝者が出るまで `TurnModel.Next()` で交代」というループを回す（手番＝スピンする人だが、進むのはルーレット任せで自分にも CPU にも当たり得る）。**手番が移るたびに `BoardPresenter` が `TurnModel.CurrentPlayer` を購読して「〔キャラ名〕の番」（自分＝人間プレイヤーの手番は「あなたの番」）のアナウンス帯（`TurnBanner`／`.turn-banner`）を画面中央上寄りに約 1.4 秒フェード表示する**（`SetupBannersIfReady` で購読を 1 度だけ張り、購読時発火で初手番も出す。キャラ名は `CpuCharacterPicker.ResolveCharacter` で解決）。**勝敗は陣地マスの占拠で決まる**（周回ゴール勝利は廃止）ため、コマは 1 周で止まらず出目ぶんそのままスタート＝ゴールを通過してループし続ける。**コマ移動中はカメラ（盤面のズーム領域）が動くコマに追従する**：`BoardPresenter.AdvanceAsync` が移動開始時にズームを既定へ戻して動くコマを画面中央に据え、1 マス進むごとに少し間（`_panFollowDelay`）を置いて `BoardZoomController.CenterOn` でそのマスへパンする（横長マップで拡大・パンして見ている最中に手番が来ても、自分のコマを追ってくれる）。コマ位置は `BoardModel` がプレイヤーごとに保持し、勝者は着地イベントが `BoardModel.SetWinner` で確定する（`BoardModel.Winner` / `IsFinished`）。**勝者が確定すると `BoardPresenter` が勝敗テキスト（`ClearLabel`）とともに盤面下部へ「ホームに戻る」ボタン（`GameOverActions`／`HomeReturnButton`・既定は USS で非表示）を出し、押すと `ReturnHomeAsync` がオンラインセッションを離脱してから `SceneTransitioner.Transit(Scenes.Home)` で Home シーンへ戻る**（連打・多重遷移は `_returningHome` フラグでガード）。**決着 SE も自分の勝敗で鳴らし分ける**（自分の勝ち＝`SoundStore.DecisionSE`、自分以外のプレイヤーが勝利条件を満たしたとき＝`SoundStore.LoseSE`。判定はエフェクトと同じ `winner == _humanPlayer`）。**決着時は `ScreenEffectPlayer`（`Main/Board/`＝任意のパーティクル Prefab を前面再生する汎用プレイヤー）が AssetStore の Prefab を画面前面に再生する：勝者が人間プレイヤーなら花火（既定は CFXR の花火）、敗北＝CPU の勝利なら雨（既定は CFXR4 Rain Falling）**（UI Toolkit の ScreenSpaceOverlay はワールド空間のパーティクルを覆い隠すため、専用の `Effect` レイヤーだけを映すエフェクトカメラ→`RenderTexture`→加算ブレンド Canvas の `RawImage` で前面合成する＝[docs/effects.md](effects.md) の方式。勝利用・敗北用で別インスタンスを持ち、合成シェーダー〔`Sugoroku/AdditiveUI`〕は共通。Prefab 未設定ならその側は再生しない）。これまで各 Presenter に散在していた「ルーレット停止→コマ前進」「移動完了→ボタン再有効化」の購読チェーンを、このオーケストレータに集約した
- **陣地マスの占拠で勝敗が決まる**。マスのイベントに陣地マス（`BoardCellEvent.Territory`）があり、止まったプレイヤーがそのマスを占拠する（相手の陣地でも上書きで奪える）。占拠状態は `TerritoryModel`（`Main/Board/`・Scoped）が陣地マスの盤面 index ごとに保持し、盤面の陣地マス総数を**プレイヤー数で割った数（端数切り上げ）**（`RequiredToWin` = ceil(総数 / プレイヤー数)・プレイヤー数は `GameParticipants` を注入して保持）を先に占拠したプレイヤーが勝つ。着地時に `BoardPresenter` が旗演出（後述）の中で `TerritoryModel.Claim(player, index)` で占拠し、`HasReachedGoal(player)` なら `BoardModel.SetWinner(player)` を呼ぶ。マスの表示替えは各陣地マスの `Owner(index)` を Presenter が購読し（`ApplyTerritoryOwner`）、占拠プレイヤーの**旗画像**（`_flagIcons`）でマスを塗り替える（占拠者色〔YOU＝金・CPU＝青緑〕は枠線で残す・旗未ロード時は色クラスのみ）。占拠後そのマスは territory 画像には戻さず旗画像のまま。陣地マスの index 一覧は DI に無いため `BoardPresenter` が盤面データから集めて `TerritoryModel.Initialize` に渡す。**陣地マスが 0 個の盤面は勝者が出ない**ため、盤面には陣地マスを配置しておくこと
- **所持金は `MoneyModel`（`Main/Money/`）がプレイヤーごとに保持する**（Scoped・初期 1000・マイナス＝借金も許容）。マスのお金イベント（`BoardCellEvent.MoneyUp` / `MoneyDown`）にコマが止まると `BoardPresenter` が `MoneyModel.Add(player, ±magnitude)` で増減させる（**増減額はマスごとの固定値ではなく着地のたびに `MoneyCellRule.Amount` が `n×100`〔n=1〜5〕のランダム額を出し、`CellEventResolver.TryGetMoneyDelta` が MoneyUp なら +・MoneyDown なら − の符号を付ける**。**乱数を引くのは着地した本人のクライアントだけ**で、決めた額を発行して全員が受信した額を適用するので、オンラインでも所持金が食い違わない）。所持金は盤面上部のネームプレート（`PlayerNameplateView`＝**全プレイヤー**ぶん〔最大 4 人〕を横 1 行に並べ、各プレートは縦型でキャラの丸アイコンとキャラ名だけを置き〔自分＝人間プレイヤーのプレートには「（あなた）」を添える〕、上辺をプレイヤー色〔`PlayerColors`〕で色分け）ではなく、**プレートをクリックして開く詳細モーダル**（`PlayerDetailPresenter`）に出す（所持金・占領地〔「占拠数 / 勝利に必要な数〔`RequiredToWin`＝総数÷プレイヤー数の切り上げ〕」〕・所持アイテムを表示し、所持金と占領地は開いている間だけ購読してリアルタイムに追従する。自分だけでなく CPU・他プレイヤーのぶんも見られる）。ミニゲームの賞金も `MoneyModel.Add`（`BoardPresenter.AwardMiniGameAsync`・額は `MiniGamePrize`＝順位別の賞金（1位500／2位300／3位100・4位以下は0））で加算する。お金よこどりアイテムでは相手の所持金の一部を `MoneyModel.Add` で相手から引いて使用者に足す（`BoardPresenter.ApplyMoneyStealAsync`）。**進む/戻るのマスイベントは着地で発動する**（**動くマス数はマスごとの固定値ではなく着地のたびに `MoveCellRule.Steps` が 1〜3 のランダムな値を出し**、`CellEventResolver.TryGetMoveSteps` が進む＝+／戻る＝− の符号を付ける。`BoardPresenter.AdvanceAsync` がそのマス数ぶん続けて動かす＝**連鎖**。連鎖先の着地イベントも通常どおり発動し、上限は `MaxChainedMoves`＝8 回。**乱数を引くのは着地した本人のクライアントだけ**で、決めたマス数を `GameAction.MoveLanding` で発行して全員が受信した値を適用するので、オンラインでも移動先が食い違わない。演出で見せた数字と実際に動くマス数をずらさないため、連鎖の値は盤面データから引き直さず受信値をそのまま使う〔`TryGetChainedSteps`〕）。**ミニゲームマス（`BoardCellEvent.MiniGame`）も着地で発動する**（**遊ぶゲームは着地のたびの抽選**〔`MiniGameCatalog.RandomGame`〕で、マスには設定しない。`BoardPresenter.PlayMiniGameCellSequenceAsync` が起動し、順位に応じた賞金（`MiniGamePrize`＝順位別の賞金（1位500／2位300／3位100・4位以下は0））が入る。着地した人が配るのは**ゲームの内容を組み立てる種**だけ〔`GameAction.MiniGameLanding`〕で、オンラインは全員が同じ内容を同時に遊んで結果値を持ち寄る。**一人用モードは着地したのが自分でも CPU でも自分が CPU 相手に遊ぶ**〔`RunLocalMiniGameAsync`〕。報酬の加算と勝者発表の帯はオンライン・一人用の共通処理〔`AwardMiniGameAsync`〕）
- **取得アイテムは `ItemModel`（`Main/Item/`）がプレイヤーごとに保持する**（Scoped・`MoneyModel` と同様）。アイテム取得マス（`BoardCellEvent.Item`）に止まると `BoardPresenter` が**アイテムショップ**（`ItemShopPresenter`）を開く。`ItemCatalog.RandomLineup(rng, 2, 4)` でランダムな枚数・重複なしのラインナップを抽選して「絵・名前・効果説明・価格」の商品カードで見せ（一度に 2 枚のカルーセル）、**タイトルの下には買う前の判断材料として現在の所持金を出す**（`ShowWallet`＝`SelectAsync` に渡された `budget` を開くたびに書き換える。買えるかどうかの判定と同じ値なのでカードの無効表示と食い違わない。コイン画像は `Image/Icon/CoinIcon`〔プレイヤー詳細モーダルと同じ絵〕・未配置なら USS 描画のコインバッジのままで行の幅は変わらない）。プレイヤーが 1 つ選ぶと代金（`ItemDefinition.Price`）を `MoneyModel.Add(player, -price)` で支払い、`ItemModel.Add(player, item)` で手札に加える（CPU は `BoardPresenter.PickCpuPurchase` で買える範囲からランダムに 1 つ自動購入）。アイテムの種別・表示名・画像アドレス・価格は静的な `ItemCatalog`（`CharacterCatalog` と同じ静的カタログ方式）が持つ。`ItemModel.Gained`（R3 の `Observable<ItemGain>`）を `BoardPresenter` が購読し、人間プレイヤーのぶんだけ画面右下の手札（`.item-hand`）にサムネイルを足す（CPU のアイテムも内部では貯まるが非表示）。同じアイテムを重ねて取ったときはカードを増やさず、既存カード右下の枚数バッジ（`.item-hand__count`）を「x2」のように更新する（1 枚しか持っていない間はバッジ非表示。まとめるのは表示だけで、`ItemModel` の手札リストは取得順のまま重複を保持する）。手札のカードをクリックすると**アイテム詳細モーダル**（`ItemModalPresenter`・`Main/Item/`・`BoardPresenter` が `new` する協調クラス）が開き、アイテム絵・名前・効果説明（`ItemDefinition.Description`）と「使用する」「閉じる」を表示する。「使用する」は `ItemModalPresenter` 自身では消費せず、生成側から渡された**効果ハンドラ `Action<ItemId> onUse`（＝`BoardPresenter.HandleItemUse`）を呼んで閉じる**（消費〔`ItemModel.Use`〕と効果発動のタイミングはハンドラ側に委ねる。陣地獲得のようにマス選択のキャンセルで消費しない効果があるため）。「使用する」ボタンは**自分の手番かつルーレット未回転（`RouletteState.Idle`）でアイテム効果の実行中でないときだけ有効**にする（`BoardPresenter.CanUseItem` を `Func<bool>` で渡し、モーダルを開くたびに `SetEnabled` で評価。回した後・コマ移動中・相手の手番中・効果実行中は無効）。モーダルを開いている間だけ Board の `UIDocument.sortingOrder` を一時的に 100 へ持ち上げ（閉じたら元へ戻す）、回転中のルーレット（Sorting=10）より前面に表示する。
- **アイテムの効果は「決定」と「適用」の 2 段で走る**（オンライン同期のため。上記アクションストリーム参照）。決定＝`BoardPresenter.HandleItemUse` がアイテム種別で分岐し、効果のパラメータ（対象マス・奪取額・ミニゲーム報酬）を決めて発行するだけ。適用＝`BoardPresenter.ApplyActionAsync` が受信して `ItemModel.Use` で**消費**し、**まず全アイテム共通の「使った」演出**（`PlayItemUsePresentationAsync`＝アイテム絵の中央ポップ〔0.7 秒〕＋「〔キャラ名〕が「〔アイテム名〕」を使用！」の帯＋`ItemGetSE`。絵は効果の演出を覆わないよう必ず消してから戻る・絵が未配置なら帯と SE だけ）を挟んでから、種別ごとの効果と演出を反映する（全クライアントで走るので相手の画面にも同じ演出が出る）。キャンセル・対象なしのときは発行されないのでそもそも消費されない。実行中の再使用は `_itemEffectRunning`（`BeginItemEffect`／`EndItemEffect`）で防ぐ。**「陣地獲得」（`StealTerritory`）は実装済み**：`TerritoryModel.CellsNotOwnedBy(human)` で「自分以外が持つ陣地マス（未占拠＋相手占拠）」を出し（0 個なら消費せず何もしない）、そのマスを金枠（`board-cell--selectable`）＋キラキラのリング（`board-cell__glow` を `AnimateSelectableGlowAsync` が opacity/scale の ping パルスで毎フレーム駆動）で強調し、上部にガイドバナー（`TerritorySelectBanner`＋キャンセル）を出す。マス選択は `BoardZoomController.BeginCellSelection`（選択中はドラッグ層を常時反応させ、押下位置からほぼ動かず離せば**タップ＝選択**、動かせば**ドラッグ＝パン**に振り分ける＝盤面タップとパンを両立）経由で、タップ位置を `cell.worldBound.Contains` で対象マスに当てて確定する。確定したら選んだマスを発行し、適用側（`ApplyTerritoryStealAsync`）が `ItemModel.Use` で消費して着地時と同じ旗演出（`PlayTerritoryFlagSequenceAsync`）→ `ApplyTerritoryLanding` で占拠（相手陣地も上書きで奪う）→ 必要数なら勝利。**キャンセル・シーン破棄では発行しない＝消費しない**（`UniTaskCompletionSource<int>` に -1）。**効果はターンを消費せず**（使用後も自分の手番のまま通常どおりルーレットを回せる）、選択・演出の間だけ `RoulettePresenter.SetInteractable(false)` でスピンボタンを無効化する（`BoardPresenter` に `RoulettePresenter` を注入）。**「ミニゲーム」（`MiniGame`）も実装済み**：`DecideMiniGameAsync` が `MiniGameSelectPresenter.SelectAsync` で遊ぶミニゲームを選ばせ（`MiniGameCatalog` をサムネイル画像＋ゲーム名のカード一覧・キャンセル/暗幕/破棄では消費せず終了）、**「遊ぶゲーム」と「内容を組み立てる種」だけを発行**する。起動（`MiniGameLauncher.PlayAsync`）・勝敗判定・報酬はすべて適用側（`RunMiniGameAsync`）が担い、オンラインは全員が同じ内容を同時に遊んで結果値を持ち寄り `MiniGameRanking.Resolve` で勝者を決め、一人用は自分が CPU 相手に遊ぶ（`RunLocalMiniGameAsync`）。勝敗は `DetermineMiniGameWin`（各ゲームがスコア 1=勝ちで報告する共通判定＝`Score==1`。2Dレースは先着・タップ連打は連打数1位〔CPU もゲーム内で自動連打しスコアボードにライブ表示〕・被っちゃやーよは獲得）で判定し、賞金の加算（`MoneyModel.Add`）・勝者発表の帯・中央の浮遊テキストは `AwardMiniGameAsync` が出す。陣地獲得と同じく**ターン非消費**で、選択・プレイの間は `RoulettePresenter.SetInteractable(false)` でスピンを止める。選択モーダルは開いている間だけ Board の `UIDocument.sortingOrder` を 100 へ持ち上げるため、`DecideMiniGameAsync` は開く前に 1 フレーム待ってアイテム詳細モーダルの Close（sortingOrder の復元）を先に完了させる。**「お金よこどり」（`StealMoney`）も実装済み**：`DecideMoneySteal` が自分以外の参加者それぞれの所持金から `MoneyStealRule.Amount`（相手の所持金が正のとき 20〜50％をランダムに奪う・端数切り捨て・最低1・上限＝相手の所持金）で奪う額を集計し、合計が 0 なら発行せず終了（奪える相手がいない）。合計が正なら**席 index 順の奪取額の配列**を発行し、適用側（`ApplyMoneyStealAsync`）が `ItemModel.Use` で消費 → 相手から `MoneyModel.Add(seat, -amount)` で引いて使用者に合計を足し、増額を中央の浮遊テキストで見せる。ユーザー操作を挟まない即時効果だが、陣地獲得・ミニゲームと同じくターン非消費で、演出の間は `RoulettePresenter.SetInteractable(false)` でスピンを止める（**浮遊テキストは画面の持ち主から見た増減を出す**＝使った本人は奪った合計をプラス、奪われた席は自分が失った額をマイナスで見て、そこに「〔キャラ名〕にお金を奪われた！」の帯を添える〔奪われた側の画面にはアイテムを使う操作が出ていないのでマイナスの理由が分からないため。購入の知らせ〔`ApplyShopResultAsync`〕と同じ考え方〕。どちらでもない席〔所持金 0 以下で奪われなかった人〕には出さない。一人用モードは奪われるのが CPU なので自分のプラスだけになる）。**「勝利」（`InstantWin`）も実装済み**：使用すると効果パラメータなしで発行し、適用側が `ItemModel.Use` で消費して `BoardModel.SetWinner(seat)` で使用者の勝ちを確定する（`SetWinner` は確定済みなら上書きしない）。以降は既存の `BoardModel.Winner` 購読が勝者テキスト・「ホームに戻る」ボタン・花火エフェクト（人間の勝利なので `ScreenEffectPlayer` の勝利インスタンス）まで自動で走らせるため、この効果自体は追加の演出・非同期処理を持たない（決定側は `HandleItemUse` の `default` 分岐、適用側は 1 行という最小の効果例）
- **着地演出は `BoardPresenter.PlayLandingSequenceAsync` が統括する**。ビューの実体（マス画像ポップアップ・マスの文言・お金の浮遊テキスト・旗トゥイーン＝以下の `ShowCellPopupAsync` / `ShowCellMessageAsync` / `ShowMoneyFloatAsync` / `PlayTerritoryFlagSequenceAsync` など）は `BoardLandingPresentation`（`Main/Board/`・Presenter が `new` する協調クラス）が担い、Model 更新（お金加算・占拠確定・勝者判定）は `BoardPresenter` 側に残す。コマの移動完了後、止まったマスの種別で分岐する。**どのマスに止まっても、まず「そのマスの文言」を 1 つ抽選して見せる**（`PickCellMessage`＝イベント種別ごとに数件用意した `BoardCellMessageCatalog` 資産から抽選。文言の編集は `Window > Sugoroku > Cell Message Editor`＝資産なので直しても再コンパイルは走らない。`BoardPresenter._messageCatalog` にインスペクタで割り当てる（未割り当てなら `BoardCellMessageDefaults` の既定文言へ静かにフォールバックするので、資産を編集しても一切ゲームに出ない）。「お金を落とした！」のようなフレーバーテキストで、スタート＝経路 index 0 だけは位置で決まるので専用プールを使う）。**文言は見た目だけで盤面の状態に影響しないので、各クライアントがローカルに抽選する**（演出専用の `_messageRng`。アクションストリームには載せないので食い違っても進行は一致する）。専用演出へ分岐するマス（アイテム・ミニゲーム）は画像ポップアップを出さないため、分岐の手前で `ShowCellMessageOnlyAsync` が文言だけを画面中央に見せてから進む。**陣地マスだけは文言と旗の占拠演出を同時に走らせる**（`UniTask.WhenAll`。順に見せると陣地マスだけ着地が長くなるため。中央は旗ポップが使うので、文言は画面中央へ寄せず画像ありの着地と同じ「中央の少し下」に置いて重ねない＝`ShowCellMessageAsync(message, centered: false, ct)`）。**文言を見せる長さはマスの種類に依らず `CellMessageSeconds`＝2 秒にそろえてある**（画像ありの着地はこの長さ見せてから消し、お金・進む/戻るマスは浮遊テキストと同時に消すので浮遊テキストの時間をここから逆算する＝`FloatSeconds = CellMessageSeconds - PreHoldSeconds`。フェードイン 0.18 秒・フェードアウト 0.2 秒はこれとは別に前後へ付く）。**陣地マスは専用の旗演出**（`PlayTerritoryFlagSequenceAsync`）：手番プレイヤーのキャラの旗（`_flagIcons`）を専用要素 `FlagPopup` に貼って画面中央にポップイン → **1 秒ホールド** → 対象マス中心へ縮小移動（`cell.worldBound` から root ローカル座標を算出し、毎フレーム `AnimateFlagAsync` で位置・拡大率・不透明度を補間）→ 重なった瞬間に `ApplyTerritoryLanding` で占拠を確定（マスが旗画像に替わる）→ 旗をフェードアウト。旗が未ロードのときは演出をスキップして占拠だけ行う。**すでに手番プレイヤー自身が占拠している陣地マスに止まったときは占拠状態が変わらないため、`TerritoryModel.Owner(position)` を見て旗演出ごとスキップする**（他人の陣地・未占拠マスに止まったときだけ演出→占拠が走る）。**アイテム取得マスは `PlayItemShopSequenceAsync`**：`ItemCatalog.RandomLineup(_itemRng, 2, 4)` でランダムな枚数・重複なしのラインナップを抽選し、人間プレイヤーは `ItemShopPresenter.SelectAsync(lineup, budget, ct)` で商品情報（絵・名前・効果説明・価格）を見て 1 つ購入する（`budget`＝その時点の所持金。買えないカードは無効・買わずに閉じてもよい）。CPU は `PickCpuPurchase` で買える範囲からランダムに 1 つ選ぶ。購入が確定したら代金を `MoneyModel.Add(player, -price)` で支払い、`ItemModel.Add` で手札へ加え（`ItemModel.Gained` 購読で右下手札へ反映）、**お金を払う演出なので取得音ではなく `MoneySE` を鳴らす**。**支払いは所持金の数字が変わるだけでは伝わらないので、お金マスと同じ浮遊テキスト（`ShowMoneyFloatAsync(-price, …)`・赤）で見せる**。さらに**買ったのが自分以外（CPU・他プレイヤー）のときは、こちらの画面にショップが出ていない＝何を買ったのか分からないので、アイテム絵を `ShowCellPopupAsync` で中央にポップし「〇〇が「××」を購入！」の帯（`ShowBannerText`）も出す**（自分の買い物はショップモーダルで見えているので出さない）。これらは決定側ではなく適用側（`ApplyShopResultAsync`）にあるので、オンラインでも全員が同じ順で同じ演出を見る。買わなければ何もしない。ショップモーダルは全画面暗幕（sortingOrder 100）でスピンボタン等を覆うため着地中の再操作は起きない（このぶん手番進行は `SelectAsync` を `await` して待つ）。**ミニゲームマスは `PlayMiniGameCellSequenceAsync`**：**起動の前に「どのミニゲームを遊ぶか」を見せ、参加者全員の「はじめる」を待つ**（`ShowMiniGameAnnounceAsync`。**抽選で当たったゲームのサムネイル**〔マスの絵は全マス共通なので、当たったゲームを見せられるのはこの絵だけ＝`LoadMiniGameThumbnailAsync`〕と文言を中央に出し、「ミニゲーム「〇〇」！」のアナウンス帯と「はじめる」ボタン〔`MiniGameStart`／`.minigame-start*`〕を添える。遊ぶゲームはマスごとに決まっていて選択モーダルが出ないため、何が始まるのか分からないまま MiniGame シーンへ切り替わってしまうのを防ぐ。押した人は `GameAction.MiniGameReady` を 1 通配り、**全席ぶん受信した時点で全クライアントが起動する**＝`WaitForAllMiniGameReadyAsync`〔押した後はボタンを無効にして「n/N人が準備完了」を出す。一人用モードは CPU が押せないので自分が押した時点で全席ぶんを配る〕。**告知は種〔`MiniGameLanding`〕を受け取ってから出す**＝`MiniGameReady` 待ちの最中に種が届くと `WaitForActionAsync` が読み飛ばしてハングするため）。着地した人が**遊ぶゲームと内容を組み立てる種**を `GameAction.MiniGameLanding` で配り〔どちらも着地のたびの抽選で盤面データからは導けない〕、それを受けた `RunMiniGameAsync` が `MiniGameLauncher.PlayAsync` で起動する。**オンラインは部屋の全員が同じ内容を同時に遊び**、各自の結果値を持ち寄って `MiniGameRanking.Resolve` が勝者を決める。**一人用モードは着地したのが自分でも CPU でも自分が CPU 相手に遊び**（`RunLocalMiniGameAsync`）、**賞金は自分も CPU も順位別**で、CPU の順位はゲーム側が `MiniGameResult.Ranks` で返す（相手をシミュレートするのはゲームの中なので、全員の順位を知っているのはゲーム側だけ）。順位が付かない被っちゃやーよだけは `DetermineMiniGameWin` で判定し、自分が勝てば自分に、負ければ着地した CPU に 500 が入る（自分が着地して負けたときだけ勝者なし）。報酬の加算と勝者発表の帯（「ミニゲーム 〇〇 の勝ち！」）はオンラインと共通の `AwardMiniGameAsync`（アイテム版と違い遊ぶゲームは抽選なので選択モーダルは出ない）。**それ以外のマス**は止まったマスの画像（`_cellIcons`）を画面中央にカードで拡大表示し、**その真下に文言（`CellMessage`／薄黒い長方形の地に白抜き太字）を重ねて**（`ShowCellPopupAsync(sprite, message, ct)`／`CellPopup`＋`CellMessage`）、着地イベントを `ApplyLandingEventAsync` で反映する（陣地は上の旗演出側で確定済みのためここには来ない）。**画像と文言は常に足並みをそろえて出入りする**（表示状態を `_popupShown`／`_messageShown` で別々に覚え、`BeginHideCellPopup`／`FinishHideCellPopup` がまとめて消す）ので、片方だけ（画像が未配置のマス・文言だけ見せるマス）でも成立する。お金マスでは増減額を「+ $n／- $n」の浮遊テキスト（`ShowMoneyFloatAsync`／`MoneyFloat`・増額は緑・減額は赤）でポップ画像から上へ浮かび上がらせ、**進む/戻るマスでは同じ演出で動くマス数「+ n マス／- n マス」を見せてから連鎖の移動へ入る**（`ShowMoveFloatAsync`・進むは緑・戻るは赤。浮遊テキストの本体は `ShowFloatTextAsync` に共通化してあり、お金と移動は文言と色分けの元になる値だけが違う。**n は着地のたびのランダムなので、お金マスと同じく発行〔`MoveLanding`〕を待ってから表示する**＝表示した数字がそのまま連鎖で動くマス数になる）。いずれも**画像を浮遊テキストと同じタイミングで消す**（`CellPopup` の USS transition を、テキストのフェードアウト完了に合わせて開始）。既定タイミングはお金マスが「画像 0.5 秒（`PreHoldSeconds`）→ テキスト 1.5 秒浮遊（`FloatSeconds`）で計 2 秒」、お金・陣地以外のマス（スタート等）も画像と文言を計 2 秒（`CellMessageSeconds`）表示してから消す（**どのマスでも文言を読める時間は同じ**）。画像が未配置（未ロード）のマスは画像ポップアップをスキップして文言だけ画面中央に出す。この演出ぶん手番進行が待つ（`AdvanceAsync` が `await`）
- **ミニゲームは `Transit` を使わない**。`Transit` は Common 以外の全シーンをアンロードするため、Main を経由すると盤面状態・NGO 接続が破棄される。`MiniGameLauncher.PlayAsync` が `MiniGame` シーンを **今のシーン（Main や動作確認用の `MiniGameTest`）を残したまま Additive で重ね**、終了後にミニゲームシーンだけをアンロードする（ランチャーは Common 依存のみでシーン非依存）。起動側（`MiniGameLauncher`）とミニゲームシーンのホスト（`MiniGameHostPresenter`）は Common シングルトンの `MiniGameSessionModel` を介して「遊ぶゲームの指定」と「結果の受け渡し」を行う。ミニゲームの中身（UXML）は `MiniGameId` の種別に対応する Addressable アドレスを `MiniGameCatalog` から引いてロードする（将来最大5種類。現状はタップ連打・2Dレース・被っちゃやーよの3種）。`MiniGameHostPresenter` は `CurrentGame` で分岐するディスパッチャで、タップ連打は `TapGamePlay`、2Dレースは `RaceGamePlay`、被っちゃやーよは `OverlapGamePlay`（いずれも DI Scoped のプレーンクラス）へ委譲する。被っちゃやーよは Main アセンブリの `ItemCatalog`（アイテム絵）を再利用するため、`MiniGame` asmdef に `Main` 参照を追加している（`Main` は `MiniGame` を参照しないので循環しない）。**参加者数は `MiniGameLauncher.PlayAsync` の `playerCount` 引数（本番の盤面ミニゲームは参加者全員）が `MiniGameSessionModel.PlayerCount` に載り、被っちゃやーよのカード数・2Dレースのレーン数に反映される**（MiniGame シーンは別スコープで `GameParticipants` を直接注入できないためセッション経由で渡す）。**同様に参加者ごとのキャラ（`characters` 引数→`MiniGameSessionModel.Characters`）も渡し、各ミニゲームは走者・カード・ラベルに YOU/CPU でなくそのキャラ（名前・絵）を使う**（本番は実参加者のキャラ＝人間の選択キャラ＋CPU の盤面キャラ、MiniGameTest はランダムな重複なしキャラ。未指定時は各ゲームが従来解決＝選択キャラ／YOU・CPU へフォールバック）。**オンライン対戦では参加者全員が同時に遊ぶ**：`PlayAsync` に `simulateOpponents: false`（各ゲームが CPU の自動連打・自走・自動選択を止める）と全クライアント共通の `seed`（被っちゃやーよの提示カードなど、内容を揃えるため）を渡し、各自の結果値（`MiniGameResult.Value`）を持ち寄って `MiniGameRanking.Resolve` で勝者を決める。**プレイ中の途中経過も配り合う**（`MiniGameProgressChannel`＝送信関数＋参加者ごとの最新値の配列。ミニゲームシーンは別スコープで `OnlineGameSync` を注入できないためセッション経由で渡し、ゲーム側は配列をポーリングする）＝タップ連打は互いの連打数をスコアボードに、2Dレースは互いの走者の位置と、ゴール後はゴールタイムを流して結果パネルの順位表に出す。**被っちゃやーよは選んだカードの index を流し、全員ぶん揃ってから開示する**（0 が「未送信」を意味する経路なので `+2` の下駄を履かせて送り、無効票＝未選択も区別して運ぶ。届くまでは「ほかのプレイヤーを待っています...」で待ち、`OverlapGamePlay.OpponentWaitSeconds`＝8 秒で打ち切る＝取りこぼしても正式な勝敗は結果値を突き合わせる盤面側が決めるので、欠けるのは見た目だけ）。動作確認は `MiniGameTest` シーン（カタログを自動でボタン一覧・**−／＋ の人数ステッパーで 2〜4 人を選んで起動できる**）から行い、本番フローには出さない

### なぜアディティブか

シーン単位で DontDestroyOnLoad を使わず、Common シーンを「永続レイヤー」として扱うことで
サウンド・オプション・シーン遷移を全シーンで共有できる。

---

## 依存性注入（VContainer）

```
CommonLifeTimeScope   全シーン共通のシングルトンを登録
  ├── GameSessionModel
  ├── CharacterSessionModel
  ├── BoardSessionModel
  ├── MiniGameSessionModel
  ├── MiniGameLauncher
  ├── ModalStore
  ├── OptionPresenter
  ├── OptionModel
  ├── SoundPlayer
  ├── SoundStore
  ├── TransitionPresenter
  └── SceneTransitioner

TitleLifetimeScope            Title シーン固有のサービスを登録
HomeLifetimeScope             Home シーン固有のサービスを登録
CharacterSelectLifetimeScope  CharacterSelect シーン固有のサービスを登録
MapSelectLifetimeScope        MapSelect シーン固有のサービスを登録
MatchingLifetimeScope         Matching シーン固有のサービスを登録
OnlineCharacterSelectLifetimeScope  OnlineCharacterSelect シーン固有のサービスを登録
MainLifetimeScope             Main シーン固有のサービスを登録
MiniGameLifetimeScope         MiniGame シーン固有のサービスを登録
MiniGameTestLifetimeScope     MiniGameTest シーン（開発用）固有のサービスを登録
```

- 各シーンの `Injector/` フォルダに `*LifetimeScope.cs` を置く
- 新しいサービスは LifetimeScope に登録してコンストラクタでインジェクト
- シーンロード後の LifetimeScope 構築は `SceneExtensions.BuildLifetimeScopes()` 拡張メソッドが担う（BootLoader / CommonSceneLoader / SceneTransitioner から呼ばれる）

---

## 状態管理（R3）

Model → Presenter の単方向データフロー + 双方向バインディング。

```
OptionModel
  BGMVolume: ReactiveProperty<float>
  SEVolume:  ReactiveProperty<float>

OptionPresenter
  → BGMVolume.Subscribe で Slider を更新
  → Slider の ValueChanged で SetBGMVolume() を呼ぶ
```

- サブスクリプションは `AddTo(_disposables)` または `AddTo(destroyCancellationToken)` で管理
- Model は PlayerPrefs を通じて永続化する

---

## 非同期処理（UniTask）

- `IAsyncStartable` を実装したクラスは VContainer が StartAsync を呼ぶ
- `Store` 系クラスは起動時に Addressables ロードを行い、`UniTask Loaded` プロパティで完了を通知する
- 使う側は `await _store.Loaded` で待機してから使用する

```csharp
// 例: AudioManager (Title シーン)。動画と同時に Cheer を鳴らし、鳴り終わってから
// タイトル BGM（光晴イズム）へ移る。
public async UniTask StartAsync(CancellationToken cancellation = default)
{
    await _soundStore.Loaded;
    AudioClip cheer = _soundStore.CheerSE;
    if (cheer != null)
    {
        _soundPlayer.PlaySE(cheer);
        await UniTask.Delay(TimeSpan.FromSeconds(cheer.length), cancellationToken: cancellation);
    }
    _soundPlayer.PlayBGM(_soundStore.TitleBGM);
}
```

### MonoBehaviour のインジェクションタイミング

`CommonSceneLoader.Awake()` は `async void` で、`await UniTask.NextFrame()` の後に `BuildLifetimeScopes()` を呼ぶ。そのため **MonoBehaviour の `Awake/OnEnable/Start` が呼ばれる時点ではインジェクションが完了していない**。

| コールバック | インジェクト済みフィールドを使えるか |
|---|---|
| `Awake` / `OnEnable` / `Start` | **不可**（injection 前） |
| `[Inject] Construct(...)` | 可（injection と同時に呼ばれる） |
| `IAsyncStartable.StartAsync()` | 可（Build 完了後に VContainer が呼ぶ） |
| ユーザー操作イベントコールバック | 可（injection 完了後に発火） |

「シーン起動時にインジェクト済みフィールドを使って初期化したい」場合は、`Start()` ではなく `[Inject] Construct(...)` メソッド内で行うか、`IAsyncStartable` を実装した純粋 C# サービスを `RegisterEntryPoint` で登録してそこから MonoBehaviour の public メソッドを呼ぶ。

### シーン遷移のキャンセル処理

`SceneTransitioner` は `SemaphoreSlim` で同時遷移を防ぎ、
連打された場合は最後のリクエストのみ実行する（前の遷移は CancellationToken でキャンセル）。

### ISceneReady — シーン準備完了の通知

`RevealAsync`（フェードイン）の前に、`SceneTransitioner` は次シーンの root GameObject を検索し、`ISceneReady` を実装した**全ての**コンポーネントの `ReadyAsync(ct)` を `UniTask.WhenAll` で並行待機する。

これにより、Addressables の非同期ロードなど「表示前に完了させたい初期化」がフェードイン前に終わり、背景や要素が空白のまま画面が現れるのを防ぐ。

新しいシーンで表示前に待ちたい非同期処理がある場合は、そのシーンの Presenter に `ISceneReady` を実装し、準備完了時に `ReadyAsync` を完了させるだけでよい（実装が無いシーンは素通りする任意フック）。

`ReadyAsync` がキャンセル以外の例外を投げても、暗幕が残り続けないよう `SceneTransitioner` 側で例外をログ出力して握りつぶし、フェードインは必ず実行する（`WaitReadySafelyAsync`）。実装側で初期化失敗を扱いたい場合は `ReadyAsync` 内で完結させること。

ただし `ReadyAsync` を呼ぶのは `Transit` だけなので、`Title` のように `BootLoader` の素の `LoadSceneAsync` で直接開かれるシーンでは呼ばれない。直接起動もあり得るシーンは `Start` でも初期化を起動し `ReadyAsync` は完了待ちだけにする（[patterns.md](patterns.md) の「シーン表示前に非同期初期化を待つ」を参照）。

---

## サウンド設計

- BGM: `AudioSource.loop = true`、`PlayBGM()` で差し替え
- SE（単発）: `PlayOneShot()` で重ね再生
- SE（ループ）: 専用の `loop = true` な AudioSource を持ち、`PlaySELoop()` で鳴らし `StopSELoop()` で止める（コマ移動中の走行音 `RunSE` などに使う。移動開始で鳴らし移動完了・キャンセルで停止）
- 音量は `OptionModel.BGMVolume / SEVolume` (0–1) を ReactiveProperty で管理（単発・ループの両 SE AudioSource に反映）
- `SoundPlayer` は音量変化を Subscribe して AudioSource に即時反映

> `_bgmAudioSource.volume = v / 2` としているのは、
> OptionModel の値 1.0 がデフォルトの AudioSource 最大音量の半分に相当するようにしているため。

---

## UI 設計（UI Toolkit）

### ファイル配置

```
Assets/Scripts/<Scene>/<Feature>/
  ├── *Presenter.cs   （UI ロジック）
  └── *.uxml          （見た目 / Addressables 経由でロードするものは AddressableAssets/ に配置）
```

### PanelSettings

`Assets/Scripts/Panel Settings.asset` の Scale Mode を **Scale With Screen Size**、基準解像度を **540×960（縦）**、Screen Match Mode を **Match Width Or Height（Match=0＝幅基準）** に設定済み（ゲームは縦画面固定）。
基準解像度（幅）に対して UI 全体がスケールするため、固定 px 値で指定したサイズが解像度によらず適切な比率で表示される。**基準解像度を小さくするほど UI 全体（文字・ボタン・余白）が一律に大きくなる**ため、スマホで読みやすいよう幅 540 まで下げてある（実画面 1080 幅なら 2 倍表示）。全体の大きさを変えたいときは個々の USS の font-size ではなく、この基準解像度の X を調整する（小さく＝大きく表示）。新しい UI を px で組むときはこの幅 540 基準で考える。

### オプションモーダル

- アイコンクリックで表示、Close ボタンで非表示
- 「タイトルに戻る」ボタンでモーダルを閉じつつ Title シーンへ遷移。**遷移の前にオンラインセッションを離脱する**（`OptionPresenter.BackToTitleAsync`＝`OnlineRosterSessionModel.Clear()` → `GameSessionModel.LeaveCurrentSessionAsync()` → `Transit`）。オプションアイコンは Common 常駐でマッチング中・キャラ選択ロビー・対戦中のどこからでも押せるが、`NetworkManager` も Common 常駐なのでシーンを移るだけでは接続もルームも残ってしまう（[docs/networking.md](networking.md)「Relay 経由の接続」）。連打・多重遷移は `_backingToTitle` でガードする
- オーバーレイ（`rgba(0,0,0,0.55)`）がゲーム画面を暗幕
- モーダルカードは画面中央に配置（`align-items: center; justify-content: center`）
- UIDocument の SortingOrder を 1000 にして他 UI より手前に表示
- モーダル内 UI バインド（スライダー・ボタン）は `OptionModalPresenter`（plain C# クラス）が担い、`OptionPresenter.SetupAsync()` 内で `new` して使う

---

## アセンブリ構成

スクリプトは 8 つのランタイム Assembly Definition と、1 つのエディタ専用アセンブリに分割されている。

| アセンブリ | パス | 依存 |
|---|---|---|
| `Common` | `Assets/Scripts/Common/` | VContainer / R3 / UniTask / DOTween |
| `Title` | `Assets/Scripts/Title/` | VContainer / UniTask / Common |
| `Home` | `Assets/Scripts/Home/` | VContainer / UniTask / Common |
| `CharacterSelect` | `Assets/Scripts/CharacterSelect/` | VContainer / R3 / UniTask / Addressables / Common |
| `MapSelect` | `Assets/Scripts/MapSelect/` | VContainer / R3 / UniTask / Addressables / Common / **Main**（`BoardCatalog` / `BoardDefinition` を参照するため Main にも依存する唯一のシーンアセンブリ） |
| `Matching` | `Assets/Scripts/Matching/` | VContainer / R3 / UniTask / Common / Unity.Services.Multiplayer / Unity.Netcode |
| `OnlineCharacterSelect` | `Assets/Scripts/OnlineCharacterSelect/` | VContainer / R3 / UniTask / Addressables / Common / Unity.Services.Multiplayer（キャラ選択ロビーの UGS プロパティ同期） |
| `Main` | `Assets/Scripts/Main/` | VContainer / R3 / UniTask / Common / Unity.Netcode / DOTween |
| `MiniGame` | `Assets/Scripts/MiniGame/` | VContainer / R3 / UniTask / Addressables / Common |
| `Main.Editor` | `Assets/Scripts/Main/Editor/` | Main / Common（`includePlatforms: ["Editor"]`＝ビルド非対象。盤面エディタ・マスの文言エディタ用） |

- `Title` / `Home` / `CharacterSelect` / `MapSelect` / `Matching` / `OnlineCharacterSelect` / `Main` / `MiniGame` は `Common` に依存し、逆方向の依存は禁止
- `BoardSessionModel`（選択マップの識別子）は `Common` に置き文字列 ID だけを持つ。`BoardCatalog` / `BoardDefinition` は `Main` にあるため、それらを扱う `MapSelect` だけが `Common` に加えて `Main` にも依存する
- `Main.Editor` はエディタ専用（`Window > Sugoroku > Board Editor`＝盤面の形とイベント、`Window > Sugoroku > Cell Message Editor`＝マスに止まったときの文言）。参照は推移解決されないため対象ランタイム asmdef の GUID を明示する（[patterns.md](patterns.md) #11）
- `autoReferenced: true` のため既存コードへの影響なし

---

## アセット管理（Addressables）

```
Assets/AddressableAssets/
  ├── Icon/        SVG アイコン
  ├── Image/       キャラのカード絵/コマ用バッジ/立ち絵/走行絵/旗絵（Character/Character<N>/Card・/Icon・/Portrait・/Run・/Flag）・アイテム絵（Image/Item/*）
  ├── MiniGame/    ミニゲームの UXML / USS
  ├── Modal/       Modal.uxml / Modal.uss
  └── Sound/       AudioClip
```

- `SoundStore` / `ModalStore` はともに `AssetStoreBase` を継承し、ボイラープレート（`UniTask Loaded`・`Start()`・try-catch）を共有
- `AssetStoreBase` は `IStartable` を実装し、`LoadAssetsCore()` をサブクラスに委譲する
- ロード完了は `UniTask Loaded` プロパティで通知

### 例外: 動画は StreamingAssets

WebGL は `VideoClip` アセットをサポートしないため、**動画だけは Addressables ではなく `Assets/StreamingAssets/` に置き、`VideoPlayer` を `VideoSource.Url`（`Application.streamingAssetsPath` 配下）で再生する**。これは WebGL / Standalone 共通で動く唯一の方式。タイトル背景動画（`TitleVideoPresenter` / `Assets/StreamingAssets/Video/TitleMovie.mp4`）がこれ。動画は Media Foundation / ブラウザ双方で確実に再生できるよう **H.264 baseline profile・`yuv420p`・BT.709 タグ付き**でエンコードしておく（main profile の B フレームや色情報未指定だと警告や色ズレ・タイムスタンプ補正が出る）。`VideoPlayer` は `RenderTexture` に描画し、UI Toolkit の背景要素（`background-image`）に貼る。

> **既知のエディタ専用症状**: エディタで Play を繰り返す（または `VideoPlayer` を2個目以降生成する）と `WindowsVideoMedia error 0x887a0005`（`DXGI_ERROR_DEVICE_REMOVED`）でデコードに失敗し、エディタを再起動するまで復帰しないことがある。これはエディタがプロセスを使い回すことによる D3D デバイス喪失で、**ビルドした実機（起動ごとに新プロセス）では再現しない**（Standalone / WebGL ビルドで毎回再生されることを確認済み）。`TitleVideoPresenter` は再生不可・準備タイムアウト時に文言だけ表示するフォールバックを持つので、黒画面で固まることはない。
