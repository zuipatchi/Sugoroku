using System;
using System.Collections.Generic;
using System.Threading;
using Common.Board;
using Common.Character;
using Common.GameSession;
using Common.MiniGame;
using Common.SceneManagement;
using Common.SoundManagement;
using Common.Store;
using Cysharp.Threading.Tasks;
using Main.Item;
using Main.Money;
using Main.Online;
using Main.Roulette;
using Main.Turn;
using R3;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Main.Board
{
    /// <summary>
    /// すごろく盤（ループ）の UI。外周にマスを並べて参加者ぶんのコマを描画し、
    /// 出目に応じてコマを 1 マスずつ移動させる。手番進行は <see cref="Turn.GameFlowController"/> が担い、
    /// 位置・状態は <see cref="BoardModel"/> が持つ。
    /// レイアウト計算は <see cref="BoardLayoutCalculator"/>、画像ロードは <see cref="BoardIconLoader"/>、
    /// キャラ解決は <see cref="CpuCharacterPicker"/>、ネームプレートは <see cref="PlayerNameplateView"/>、
    /// お金イベント判定は <see cref="CellEventResolver"/>、着地演出のビュー（ポップアップ・お金浮遊テキスト・
    /// 旗トゥイーン）は <see cref="BoardLandingPresentation"/> に分担し、ここでは購読・構築・移動と
    /// 「どの演出をいつ呼ぶか」の統括（Model 更新含む）を担う。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class BoardPresenter : MonoBehaviour
    {
        // マップ一覧。MapSelect で選ばれたマップを識別子（BoardSessionModel）から解決するのに使う。
        // 未割り当て・未選択なら下の _definition にフォールバックする。
        [SerializeField] private BoardCatalog _catalog;
        // 盤面データ（形・経路・イベント・見た目）。カタログで解決できないときのフォールバック。
        // これも未割り当てなら下の _columns/_rows から矩形リングを生成する。
        [SerializeField] private BoardDefinition _definition;
        // _definition 未割り当て時のフォールバック用。縦画面向けに幅より高さの大きい縦長リング（列 < 行）。周回マス数は 2*列+2*行-4。
        [SerializeField] private int _columns = 5;
        [SerializeField] private int _rows = 7;
        [SerializeField] private float _stepInterval = 0.18f;
        // 1 マス移動してからカメラがそのマスへパン追従するまでの間（コマの着地を見せてから追う）。
        [SerializeField] private float _panFollowDelay = 0.09f;
        // マスの一辺をマス中心間隔の何割にするか。1 未満にすると隣接マスの間に隙間が空き、そこを接続線でつなぐ。
        [SerializeField, Range(0.3f, 1f)] private float _cellFillRatio = 0.62f;
        // 既定で画面幅に収める列数。列数がこれを超える横長盤面は、この列数ぶんを大きく表示し
        // 残りは画面外へはみ出させてドラッグでパンして見る（BoardZoomController）。列数がこれ以下なら全体表示。
        [SerializeField] private int _visibleColumns = 4;
        // 虫眼鏡ボタンで切り替えるズーム段階（画面幅に収める列数）。既定 4 列を中心に、拡大＝列を減らし
        // （3→2 列）、縮小＝列を増やす（6→8 列）。盤面の列数を超える値は自動で頭打ちにする。
        [SerializeField] private int[] _zoomColumnLevels = { 2, 3, 4, 6, 8 };

        [Header("勝利エフェクト（自分が勝ったときだけ再生）")]
        // 勝利時に再生する AssetStore のパーティクル Prefab（既定は CFXR の花火）。未設定なら再生しない。
        [SerializeField] private GameObject _victoryEffectPrefab;
        // ワールド空間のパーティクルを UI Toolkit の前面に合成するための加算ブレンドシェーダー（Sugoroku/AdditiveUI）。
        [SerializeField] private Shader _victoryEffectShader;
        // エフェクトカメラ前方に Prefab を置く距離・縦オフセット（負で下＝下から打ち上がって見える）・表示スケール。
        [SerializeField] private float _victoryEffectDistance = 8f;
        [SerializeField] private float _victoryEffectVerticalOffset = -1.5f;
        [SerializeField] private float _victoryEffectScale = 1f;
        // 打ち上げる発数と、1 発ごとの時間差（秒）。既定は 3 発を横に広げて少しずつ打ち上げる。
        [SerializeField] private int _victoryEffectCount = 3;
        [SerializeField] private float _victoryEffectStagger = 0.35f;

        [Header("敗北エフェクト（自分が負けたときだけ再生）")]
        // 敗北時に再生する AssetStore のパーティクル Prefab（既定は CFXR4 Rain Falling の雨）。未設定なら再生しない。
        // 合成用の加算ブレンドシェーダーは勝利エフェクトと共通（_victoryEffectShader）を使う。
        [SerializeField] private GameObject _defeatEffectPrefab;
        // エフェクトカメラ前方に Prefab を置く距離・縦オフセット・表示スケール。雨は画面中央付近に 1 つ置いて降らせる。
        [SerializeField] private float _defeatEffectDistance = 8f;
        [SerializeField] private float _defeatEffectVerticalOffset = 0f;
        [SerializeField] private float _defeatEffectScale = 1f;
        // 雨は連続系なので 1 つだけ。時間差は不要。
        [SerializeField] private int _defeatEffectCount = 1;
        [SerializeField] private float _defeatEffectStagger = 0f;

        private BoardModel _model;
        private TerritoryModel _territory;
        private SoundStore _soundStore;
        private SoundPlayer _soundPlayer;
        private MoneyModel _money;
        private ItemModel _items;
        // ミニゲームアイテムの効果でミニゲームシーンを Additive 起動するのに使う。
        private MiniGameLauncher _launcher;
        private TurnModel _turn;
        private RouletteModel _rouletteModel;
        // 陣地獲得アイテムの選択・演出中にスピンボタンを一時無効化するために保持する。
        private RoulettePresenter _roulette;
        private BoardSessionModel _boardSession;
        // 勝敗確定後に「ホームに戻る」で Home シーンへ遷移するのに使う。
        private SceneTransitioner _sceneTransitioner;
        private CpuCharacterPicker _characterPicker;
        private PlayerNameplateView _nameplateView;
        // ネームプレートのクリックで開くプレイヤー詳細モーダル（所持金・占領地・所持アイテム）。BuildCells で生成。
        private PlayerDetailPresenter _playerDetail;
        // 進行の決定を配るアクションストリーム。着地の乱数・アイテム効果は「決めた人が発行 → 全員が受信して適用」。
        private OnlineGameSync _sync;
        // ホームへ戻るときにオンラインセッションを離脱する（NGO の停止も UGS 側が一緒に行う）ために持つ。
        private GameSessionModel _gameSession;
        private OnlineRosterSessionModel _onlineRoster;
        // 手札を右下に出す人間プレイヤーの index（参加者リストから解決）。
        private int _humanPlayer;
        // 人間プレイヤーの勝利時にパーティクル Prefab（花火）を前面再生する。初回勝利確定時に遅延生成する。
        private ScreenEffectPlayer _victoryEffect;
        // 人間プレイヤーの敗北（CPU の勝利）時にパーティクル Prefab（雨）を前面再生する。初回敗北確定時に遅延生成する。
        private ScreenEffectPlayer _defeatEffect;

        private UIDocument _uiDocument;
        private VisualElement _boardBackground;
        private VisualElement _boardArea;
        private VisualElement _playerHeader;
        private VisualElement[] _cells;
        private VisualElement[] _pieces;
        private Sprite[] _pieceIcons;
        // 各プレイヤーの旗画像。陣地マス占拠の演出（中央表示→マスへ縮小）と占拠マスの塗りに使う。
        private Sprite[] _flagIcons;
        // 各マスに貼った画像。着地演出（ポップアップ拡大表示）で流用するのに保持する。
        private Sprite[] _cellIcons;
        // 着地演出のビュー（ポップアップ・お金浮遊テキスト・旗トゥイーン）。BuildCells で UI 要素とともに生成。
        private BoardLandingPresentation _landing;
        private Label _clearLabel;
        // 手番が移るたびに「〔キャラ名〕の番」を一瞬見せるアナウンス帯とその文言ラベル。
        private VisualElement _turnBanner;
        private Label _turnBannerLabel;
        // 帯（手番アナウンス・待機表示）の購読を 1 度だけ張るためのフラグと、表示→非表示のトゥイーン用トークン。
        private bool _bannersSetup;
        private CancellationTokenSource _turnBannerCts;
        // 他プレイヤーの操作（買い物・ミニゲーム・陣地選択）を待っている間だけ出す待機表示と、
        // その文言ラベル・末尾の「.」ラベル（「.」だけ別ラベルにしてピルの幅を一定に保つ）。
        private VisualElement _waitingBanner;
        private Label _waitingBannerLabel;
        private Label _waitingBannerDots;
        // 待機表示の「…」を動かすトゥイーン用トークン（表示のたびに張り替える）。
        private CancellationTokenSource _waitingBannerCts;
        // いま待たせている席とその理由（Busy で配られたもの）。None なら「相手の手番のルーレット待ち」だけを見る。
        private int _busySeat = -1;
        private BusyReason _busyReason = BusyReason.None;
        // 表示中の待機文言（同じ内容なら張り替えず「.」のアニメを続ける）。
        private string _waitingMessage;
        // オンラインのミニゲームで、他のプレイヤーの結果値が揃うのを待っているか。
        private bool _waitingMiniGameScores;
        // 勝敗確定後に出す「ホームに戻る」ボタンとその帯（既定は USS で非表示）。
        private VisualElement _gameOverActions;
        private Button _homeReturnButton;
        // ホームへの遷移を二重に起動しないためのガード。
        private bool _returningHome;
        // 取得したアイテムを並べる右下の手札コンテナ。
        private VisualElement _itemHand;
        // ロード済みアイテム絵のキャッシュ（取得マスで抽選するたびに使い回す）。
        private readonly Dictionary<ItemId, Sprite> _itemSprites = new();
        // 手札に並べたカード（同じアイテムはカードを増やさず 1 枚にまとめる）と、その所持枚数。
        private readonly Dictionary<ItemId, VisualElement> _handCards = new();
        private readonly Dictionary<ItemId, int> _handCounts = new();
        // 手札の枚数バッジの USS クラス（追加・消費の両方から更新するため定数化）。
        private const string HandCountClass = "item-hand__count";
        private const string HandCountVisibleClass = "item-hand__count--visible";
        // 手札クリックで開くアイテム詳細モーダル（使用する／閉じる）。BuildCells で生成。
        private ItemModalPresenter _itemModal;
        // ミニゲームアイテム使用時に遊ぶミニゲームを選ばせるモーダル。BuildCells で生成。
        private MiniGameSelectPresenter _miniGameSelect;
        // アイテム取得マス着地時に開く「アイテムショップ」モーダル。BuildCells で生成。
        private ItemShopPresenter _itemShop;
        // マスをタップしたときに開く説明モーダル（見せるだけ）。BuildCells で生成。
        private BoardCellInfoPresenter _cellInfo;
        // アイテムショップに並べる枚数のランダム範囲（カタログ総数でクランプ）。
        private const int ItemShopMinItems = 2;
        private const int ItemShopMaxItems = 4;
        // 陣地獲得アイテムのマス選択ガイドバナー（USS で既定非表示・選択中だけ表示）。
        private VisualElement _territorySelectBanner;
        // 陣地選択の結果を受け渡す完了ソース（選んだ盤面 index／キャンセル・破棄で -1）。選択中だけ非 null。
        private UniTaskCompletionSource<int> _territorySelectionTcs;
        // アイテム効果（選択→演出）の実行中フラグ。多重起動と、実行中の「使用する」再有効化を防ぐ。
        private bool _itemEffectRunning;
        // 直前の一時停止（切断による進行停止）の状態。復帰したときだけスピンを戻すために覚えておく。
        private bool _wasPaused;
        // 陣地選択のハイライトを付けたマスの USS クラス。
        private const string SelectableCellClass = "board-cell--selectable";
        // 選択できるマスに重ねるキラキラのリング要素の USS クラス。
        private const string SelectableGlowClass = "board-cell__glow";
        // アイテム抽選の乱数源（ゲーム内の見た目のランダム性用。抽選ロジック自体は ItemCatalog にある）。
        private readonly System.Random _itemRng = new();
        // 直前の着地で決まった「続けて動くマス数」（進む＝正／戻る＝負・0 なら連鎖しない）。
        // マス数は着地のたびのランダムなので、演出で使った値をそのまま連鎖の移動にも使う
        // （盤面データから引き直すと、決めた値と違うマス数だけ動いてしまう）。
        private int _pendingChainSteps;
        private BoardDefinition _boardDef;
        private BoardLayoutCalculator _layout;
        private BoardZoomController _zoomController;
        private bool _ownsBoardDef;
        private int _cellCount;
        private int _pieceCount;
        private bool _cellsBuilt;
        private bool _cellIconLoadStarted;
        private bool _frameLoadStarted;
        private bool _backgroundLoadStarted;
        private bool _piecesBuilt;
        private bool _headerBuilt;
        private bool _iconLoadStarted;
        private bool _territoriesSetup;
        // Construct（DI 注入）が済んだか。BuildCells は選択マップ（_boardSession）を参照するため、
        // OnEnable と Construct の両方がそろってから実行する（どちらが先でも動くようにするガード）。
        private bool _constructed;
        private CancellationToken _destroyCt;
        private readonly CompositeDisposable _disposables = new();
        private readonly BoardIconLoader _iconLoader = new();
        // 全マス共通の枠オーバーレイ要素（盤面に枠画像が設定されているときだけ生成する）。
        private readonly List<VisualElement> _frames = new();

        [Inject]
        public void Construct(
            BoardModel model,
            TerritoryModel territory,
            SoundStore soundStore,
            SoundPlayer soundPlayer,
            CpuCharacterPicker characterPicker,
            GameParticipants participants,
            MoneyModel money,
            ItemModel items,
            MiniGameLauncher launcher,
            TurnModel turn,
            RouletteModel rouletteModel,
            RoulettePresenter roulette,
            BoardSessionModel boardSession,
            SceneTransitioner sceneTransitioner,
            GameSessionModel gameSession,
            OnlineRosterSessionModel onlineRoster,
            OnlineGameSync sync)
        {
            _model = model;
            _sync = sync;
            _gameSession = gameSession;
            _onlineRoster = onlineRoster;
            _territory = territory;
            _soundStore = soundStore;
            _soundPlayer = soundPlayer;
            _money = money;
            _items = items;
            _launcher = launcher;
            _turn = turn;
            _rouletteModel = rouletteModel;
            _roulette = roulette;
            _boardSession = boardSession;
            _sceneTransitioner = sceneTransitioner;
            _characterPicker = characterPicker;
            // 手札を右下に出すのは自分＝人間プレイヤーだけ（ネームプレートの「（あなた）」表示にも使う）。
            // オンラインはロビーで確定した自分の席、それ以外は参加者リストの最初の Human を採用する。
            _humanPlayer = 0;
            if (gameSession.Mode == GameMode.Online && onlineRoster.HasRoster)
            {
                _humanPlayer = onlineRoster.MySeat;
            }
            else
            {
                for (int i = 0; i < participants.Count; i++)
                {
                    if (participants.KindOf(i) == PlayerKind.Human)
                    {
                        _humanPlayer = i;
                        break;
                    }
                }
            }

            // ネームプレートは「アイコン＋名前」だけを出し、所持金・占領地・所持アイテムはクリックで開く
            // 詳細モーダル（_playerDetail）に出す。モーダルは UI 要素がそろう BuildCells で生成するため、
            // ここでは遅延解決するラムダを渡す（Construct と OnEnable の順序に依存しない）。
            _nameplateView = new PlayerNameplateView(
                participants,
                _characterPicker,
                _iconLoader,
                _humanPlayer,
                destroyCancellationToken,
                player => _playerDetail?.Open(player));

            // アイテム取得を購読し、人間プレイヤーのぶんだけ右下の手札にサムネイルを足す。
            _disposables.Add(_items.Gained.Subscribe(gain =>
            {
                if (gain.Player == _humanPlayer)
                {
                    AppendItemToHand(gain.Item);
                }
            }));

            // アイテム使用（モーダルの「使用する」）を購読し、手札表示から 1 枚減らす。
            _disposables.Add(_items.Used.Subscribe(use =>
            {
                if (use.Player == _humanPlayer)
                {
                    RemoveItemFromHand(use.Item);
                }
            }));

            // コマ位置は Model を source of truth とし、Position を購読して描画へ反映する。
            // 購読と UI 構築（OnEnable / injection）の順序が不定のため、_pieces を null ガードする。
            // DOTween.dll の AddTo 拡張と衝突しないよう CompositeDisposable.Add で管理する。
            for (int i = 0; i < _model.PlayerCount; i++)
            {
                int player = i;
                _disposables.Add(_model.Position(player).Subscribe(position =>
                {
                    if (_pieces != null && player < _pieces.Length && _pieces[player] != null)
                    {
                        _layout?.PlaceAtCell(_pieces[player], position);
                        // 移動でマスの占有状況が変わるので、全コマのずらし表示を組み直す。
                        RefreshPieceOffsets();
                    }
                }));
            }

            // 勝者が確定したら結果メッセージを表示する。
            _disposables.Add(_model.Winner.Subscribe(winner =>
            {
                if (winner < 0 || _clearLabel == null)
                {
                    return;
                }
                _clearLabel.text = WinnerText(winner);
                _soundPlayer.PlaySafe(_soundStore?.DecisionSE);
                // 決着したらもう誰の操作も待たないので待機表示を消す。
                SetBusy(-1, BusyReason.None);
                // 勝敗が決まったら「ホームに戻る」ボタンを出す。
                ShowGameOverActions();
                // 自分（人間プレイヤー）の勝敗でパーティクル Prefab を前面で再生する（合成シェーダーは共通）。
                if (winner == _humanPlayer)
                {
                    // 勝利＝花火。
                    _victoryEffect ??= new ScreenEffectPlayer(_victoryEffectPrefab, _victoryEffectShader);
                    _victoryEffect.PlayAsync(
                        _victoryEffectDistance,
                        _victoryEffectVerticalOffset,
                        _victoryEffectScale,
                        _victoryEffectCount,
                        _victoryEffectStagger,
                        false, // 花火は実再生時間で片付ける。
                        _destroyCt).Forget();
                }
                else
                {
                    // 敗北（CPU の勝利）＝雨。
                    _defeatEffect ??= new ScreenEffectPlayer(_defeatEffectPrefab, _victoryEffectShader);
                    _defeatEffect.PlayAsync(
                        _defeatEffectDistance,
                        _defeatEffectVerticalOffset,
                        _defeatEffectScale,
                        _defeatEffectCount,
                        _defeatEffectStagger,
                        true, // 雨はシーンを出るまで降らせ続ける。
                        _destroyCt).Forget();
                }
            }));

            // 誰かの切断で進行が止まっている間は入力を閉じ、待機表示で理由を出す
            // （復帰すれば自分の手番のスピンだけ戻す。猶予切れなら下の SessionLost が引き継ぐ）。
            _disposables.Add(_sync.Paused.Subscribe(paused =>
            {
                // 購読時にも現在値（false）が流れてくる。まだ一時停止していないのに「復帰」の処理を
                // 走らせるとゲーム開始前にスピンを押せてしまうので、止まったことがある場合だけ戻す。
                bool resumed = !paused && _wasPaused;
                _wasPaused = paused;

                if (paused && _roulette != null)
                {
                    _roulette.SetInteractable(false);
                }
                else if (resumed)
                {
                    RestoreSpinIfMyIdleTurn();
                }
                RefreshWaitingBanner();
            }));

            // オンラインで誰かが退出したら対戦は続行できない。決着時と同じ帯で知らせて Home へ戻れるようにする。
            _disposables.Add(_sync.SessionLost.Subscribe(lost =>
            {
                if (!lost || _returningHome || _model.IsFinished)
                {
                    return;
                }
                if (_clearLabel != null)
                {
                    _clearLabel.text = "相手が退出しました";
                }
                // 退出した相手の操作を待っていた場合、待機表示は用済みなので消す。
                SetBusy(-1, BusyReason.None);
                ShowGameOverActions();
            }));

            _constructed = true;

            // OnEnable が先に走っていれば、この時点でマス・コマ・ヘッダー・陣地を構築できる。
            // BuildCells は選択マップの参照に _boardSession が要るため、注入後のここで（も）呼ぶ。
            BuildCells();
            BuildPiecesIfReady();
            BuildPlayerHeaderIfReady();
            StartLoadingPieceIconsIfReady();
            SetupTerritoriesIfReady();
            SetupBannersIfReady();
        }

        private void Awake()
        {
            _uiDocument = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            // Unity 6 では破棄前に最低 1 回 destroyCancellationToken を参照しないと
            // MissingReferenceException が出るため、ここでキャプチャしておく（patterns.md #2）。
            _destroyCt = destroyCancellationToken;
            BuildCells();
            BuildPiecesIfReady();
            BuildPlayerHeaderIfReady();
            StartLoadingPieceIconsIfReady();
            SetupTerritoriesIfReady();
            SetupBannersIfReady();
        }

        private void OnDestroy()
        {
            _disposables.Dispose();
            _iconLoader.Dispose();
            _zoomController?.Dispose();
            _turnBannerCts?.Cancel();
            _turnBannerCts?.Dispose();
            _waitingBannerCts?.Cancel();
            _waitingBannerCts?.Dispose();

            // フォールバックで生成した盤面データ（アセットではない）は明示的に破棄する。
            if (_ownsBoardDef && _boardDef != null)
            {
                Destroy(_boardDef);
                _boardDef = null;
            }
        }

        /// <summary>
        /// 描画に使う盤面データを解決する。優先順位は
        /// (1) MapSelect で選ばれたマップ（<see cref="_catalog"/> から <see cref="_boardSession"/> の識別子で解決）、
        /// (2) インスペクタ割り当ての <see cref="_definition"/>、
        /// (3) <see cref="_columns"/>/<see cref="_rows"/> から生成する矩形リング（フォールバック）。
        /// オンライン等でマップ未選択のときは (1) を飛ばして従来どおり (2)/(3) になる。
        /// </summary>
        private void ResolveDefinition()
        {
            if (_boardDef != null)
            {
                return;
            }

            // (1) 選択されたマップをカタログから解決する。
            BoardDefinition resolved = null;
            if (_catalog != null && _boardSession != null && _boardSession.HasSelection)
            {
                resolved = _catalog.Find(_boardSession.SelectedId);
            }

            // (2) 解決できなければインスペクタ割り当てのマップにフォールバックする。
            if (resolved == null || resolved.CellCount == 0)
            {
                resolved = _definition;
            }

            if (resolved != null && resolved.CellCount > 0)
            {
                _boardDef = resolved;
                _ownsBoardDef = false;
            }
            else
            {
                // (3) どちらも無ければ矩形リングを生成する。
                _boardDef = BoardDefinition.CreateRectangular(_columns, _rows);
                _ownsBoardDef = true;
            }

            _cellCount = _boardDef.CellCount;
        }

        private void BuildCells()
        {
            if (_cellsBuilt)
            {
                return;
            }

            // 選択マップ（_boardSession）を参照するため、DI 注入（Construct）が済むまで待つ。
            // OnEnable が先でも、後から Construct が BuildCells を呼び直して構築する。
            if (!_constructed)
            {
                return;
            }

            VisualElement root = _uiDocument.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("Board の rootVisualElement が見つかりませんでした。");
                return;
            }

            _boardBackground = root.Q<VisualElement>("BoardBackground");
            _boardArea = root.Q<VisualElement>("BoardArea");
            _playerHeader = root.Q<VisualElement>("PlayerHeader");
            _clearLabel = root.Q<Label>("ClearLabel");
            // 手番アナウンス帯（「〔キャラ名〕の番」）。表示制御は SetupBannersIfReady で購読する。
            _turnBanner = root.Q<VisualElement>("TurnBanner");
            _turnBannerLabel = root.Q<Label>("TurnBannerLabel");
            // 勝敗確定後に出す「ホームに戻る」ボタン。既定は USS で非表示。
            _gameOverActions = root.Q<VisualElement>("GameOverActions");
            _homeReturnButton = root.Q<Button>("HomeReturnButton");
            if (_homeReturnButton != null)
            {
                _homeReturnButton.clicked += OnHomeReturnClicked;
            }
            // BuildCells より先に勝者が確定していた場合に備えて、確定済みなら即座に出す。
            if (_model.IsFinished)
            {
                ShowGameOverActions();
            }
            _itemHand = root.Q<VisualElement>("ItemHand");
            // 手札クリックで開くアイテム詳細モーダル。BuildCells は Construct 後にしか走らないため
            // _items / _humanPlayer は確定済み。アイテム絵はロード済みキャッシュから引く（未ロードは絵なし表示）。
            VisualElement itemModalOverlay = root.Q<VisualElement>("ItemModal");
            if (itemModalOverlay != null)
            {
                _itemModal = new ItemModalPresenter(
                    itemModalOverlay,
                    HandleItemUse,
                    item => _itemSprites.TryGetValue(item, out Sprite sprite) ? sprite : null,
                    _uiDocument,
                    CanUseItem);
            }

            // ミニゲームアイテム使用時に遊ぶミニゲームを選ばせるモーダル。
            VisualElement miniGameSelectOverlay = root.Q<VisualElement>("MiniGameSelectModal");
            if (miniGameSelectOverlay != null)
            {
                _miniGameSelect = new MiniGameSelectPresenter(miniGameSelectOverlay, _uiDocument, _iconLoader, _destroyCt);
            }

            // アイテム取得マス着地時に開くアイテムショップ。絵はショップ側からロードさせて _itemSprites に載せておくと
            // 購入後の手札サムネイルも同じキャッシュから引ける。
            VisualElement itemShopOverlay = root.Q<VisualElement>("ItemShopModal");
            if (itemShopOverlay != null)
            {
                _itemShop = new ItemShopPresenter(
                    itemShopOverlay,
                    _uiDocument,
                    (def, token) => LoadItemSpriteAsync(def, token),
                    _iconLoader,
                    _destroyCt);
            }

            // 上部のネームプレートをクリックしたときに開くプレイヤー詳細モーダル（所持金・占領地・所持アイテム）。
            // アイテム絵はショップ・手札と同じ _itemSprites キャッシュ経由でロードする。
            VisualElement playerDetailOverlay = root.Q<VisualElement>("PlayerDetailModal");
            if (playerDetailOverlay != null)
            {
                _playerDetail = new PlayerDetailPresenter(
                    playerDetailOverlay,
                    _money,
                    _territory,
                    _items,
                    _characterPicker,
                    _iconLoader,
                    _humanPlayer,
                    (def, token) => LoadItemSpriteAsync(def, token),
                    _uiDocument,
                    _destroyCt);
                // 開いたままシーンが破棄されても所持金・占領地の購読が残らないようまとめて落とす。
                _disposables.Add(_playerDetail);
            }

            // マスをタップしたときに開く説明モーダル（見せるだけなので手番も演出も問わない）。
            VisualElement cellInfoOverlay = root.Q<VisualElement>("CellInfoModal");
            if (cellInfoOverlay != null)
            {
                _cellInfo = new BoardCellInfoPresenter(cellInfoOverlay, _uiDocument);
            }

            // 他プレイヤーの操作を待っている間だけ出す待機表示。既定は USS で非表示。
            _waitingBanner = root.Q<VisualElement>("WaitingBanner");
            _waitingBannerLabel = root.Q<Label>("WaitingBannerLabel");
            _waitingBannerDots = root.Q<Label>("WaitingBannerDots");

            // 陣地獲得アイテムのマス選択ガイド（バナー＋キャンセル）。既定は USS で非表示。
            _territorySelectBanner = root.Q<VisualElement>("TerritorySelectBanner");
            Button territoryCancel = root.Q<Button>("TerritorySelectCancel");
            if (territoryCancel != null)
            {
                territoryCancel.clicked += () => _territorySelectionTcs?.TrySetResult(-1);
            }
            _landing = new BoardLandingPresentation(
                root.Q<VisualElement>("CellPopup"),
                root.Q<VisualElement>("FlagPopup"),
                root.Q<Label>("MoneyFloat"));
            if (_boardArea == null || _clearLabel == null)
            {
                Debug.LogError("Board の UI 要素が見つかりませんでした。");
                return;
            }

            ResolveDefinition();

            _cellsBuilt = true;
            _cells = new VisualElement[_cellCount];
            _cellIcons = new Sprite[_cellCount];

            // マス同士をつなぐ接続線。マス・コマより先に追加して背後に描く。
            VisualElement linesElement = new();
            linesElement.AddToClassList("board-lines");
            linesElement.pickingMode = PickingMode.Ignore;
            _layout = new BoardLayoutCalculator(_boardDef, _boardArea, linesElement, _cells, _cellFillRatio, _visibleColumns);
            linesElement.generateVisualContent += _layout.DrawConnectingLines;
            _boardArea.Add(linesElement);

            for (int i = 0; i < _cellCount; i++)
            {
                BoardCellDefinition definition = _boardDef.Cell(i);
                VisualElement cell = new();
                cell.AddToClassList("board-cell");
                cell.pickingMode = PickingMode.Ignore;
                if (i == 0)
                {
                    cell.AddToClassList("board-cell--goal");
                    cell.Add(new Label("S/G") { pickingMode = PickingMode.Ignore });
                }
                ApplyCellAppearance(cell, definition);
                AddFrameOverlay(cell);
                _layout.PlaceAtCell(cell, i);
                _boardArea.Add(cell);
                _cells[i] = cell;
            }

            StartLoadingCellIcons();
            StartLoadingFrameIfReady();
            StartLoadingBackgroundIfReady();

            // リング領域をグリッドのアスペクト比に合わせて中央配置する。画面比が変わっても
            // マスが均等に並ぶよう、レイアウト確定（と以後のリサイズ）のたびに再計算する。
            // レイアウト更新のたびにズーム/パンのクランプ（既定位置寄せ含む）も更新する。
            _boardArea.parent.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                _layout.LayoutBoardArea();
                _zoomController?.OnLayoutChanged();
            });
            _layout.LayoutBoardArea();

            // ズームイン／アウト・ドラッグでのパンを配線する（対象は BoardArea のみ）。
            // 新規追加のシリアライズ配列が空で読まれた場合に備え、既定段階へフォールバックする。
            int[] zoomLevels = _zoomColumnLevels != null && _zoomColumnLevels.Length > 0
                ? _zoomColumnLevels
                : new[] { 2, 3, 4, 6, 8 };
            _zoomController = new BoardZoomController(
                root, _boardArea, _layout, _visibleColumns, _boardDef.GridColumns, _cellFillRatio, zoomLevels);
            _zoomController.LoadMagnifierIconAsync(_destroyCt).Forget();
            // マスをタップしたら説明モーダルを開く（ドラッグはパンのまま。陣地選択中はそちらが優先される）。
            _zoomController.SetCellTapHandler(TryOpenCellInfoAt);
        }

        /// <summary>マスの塗り色・イベント表示を <paramref name="definition"/> に合わせて設定する。</summary>
        private void ApplyCellAppearance(VisualElement cell, BoardCellDefinition definition)
        {
            if (definition.HasCustomColor)
            {
                cell.style.backgroundColor = definition.Color;
            }

            string marker = EventMarker(definition);
            if (marker == null)
            {
                return;
            }

            Label eventLabel = new(marker) { pickingMode = PickingMode.Ignore };
            eventLabel.AddToClassList("board-cell__event");
            cell.Add(eventLabel);
        }

        /// <summary>イベントをマス上に表示する短い記号。<see cref="BoardCellEvent.None"/> なら null。</summary>
        private static string EventMarker(BoardCellDefinition definition)
        {
            switch (definition.Event)
            {
                // 動くマス数は着地のたびのランダムなので、記号に数字は載せない（止まるまで分からない）。
                case BoardCellEvent.Forward:
                    return "▲";
                case BoardCellEvent.Back:
                    return "▼";
                case BoardCellEvent.MiniGame:
                    return "MG";
                case BoardCellEvent.MoneyUp:
                    return "$+";
                case BoardCellEvent.MoneyDown:
                    return "$-";
                case BoardCellEvent.Territory:
                    return "陣";
                case BoardCellEvent.Item:
                    return "ア";
                default:
                    return null;
            }
        }

        /// <summary>アイコンアドレスを持つマスの画像を Addressables から読み込んで貼り付ける（1 度だけ）。</summary>
        private void StartLoadingCellIcons()
        {
            if (_cellIconLoadStarted || _boardDef == null)
            {
                return;
            }
            _cellIconLoadStarted = true;
            _iconLoader.LoadCellIconsAsync(_boardDef, (index, sprite) =>
            {
                if (_cells == null || index >= _cells.Length || _cells[index] == null)
                {
                    return;
                }
                if (_cellIcons != null && index < _cellIcons.Length)
                {
                    _cellIcons[index] = sprite; // 着地演出（BoardLandingPresentation のポップアップ）で流用する
                }
                _cells[index].style.backgroundImage = new StyleBackground(sprite);
                _cells[index].AddToClassList("board-cell--icon");
            }, _destroyCt).Forget();
        }

        /// <summary>盤面に枠画像が設定されていれば、マス画像の上に重ねる枠オーバーレイ要素を追加する。</summary>
        private void AddFrameOverlay(VisualElement cell)
        {
            if (_boardDef == null || !_boardDef.HasFrame)
            {
                return;
            }
            VisualElement frame = new() { pickingMode = PickingMode.Ignore };
            frame.AddToClassList("board-cell__frame");
            cell.Add(frame);
            _frames.Add(frame);
        }

        /// <summary>全マス共通の枠画像を読み込んで各マスの枠オーバーレイに貼る（1 度だけ）。未配置なら枠なしのまま。</summary>
        private void StartLoadingFrameIfReady()
        {
            if (_frameLoadStarted || _boardDef == null || !_boardDef.HasFrame || _frames.Count == 0)
            {
                return;
            }
            _frameLoadStarted = true;
            LoadFrameAsync(_destroyCt).Forget();
        }

        private async UniTaskVoid LoadFrameAsync(CancellationToken ct)
        {
            Sprite frame = await _iconLoader.LoadSpriteAsync(_boardDef.FrameAddress, "盤面枠画像", ct);
            if (frame == null)
            {
                return;
            }
            foreach (VisualElement frameElement in _frames)
            {
                frameElement.style.backgroundImage = new StyleBackground(frame);
            }
        }

        /// <summary>
        /// 盤面の背後に画面全体で敷く背景画像（<see cref="BoardDefinition.BackgroundAddress"/>）を
        /// 読み込んで貼る（1 度だけ）。未設定・未配置なら背景なしのまま。
        /// </summary>
        private void StartLoadingBackgroundIfReady()
        {
            if (_backgroundLoadStarted || _boardDef == null || !_boardDef.HasBackground || _boardBackground == null)
            {
                return;
            }
            _backgroundLoadStarted = true;
            LoadBackgroundAsync(_destroyCt).Forget();
        }

        private async UniTaskVoid LoadBackgroundAsync(CancellationToken ct)
        {
            Sprite background = await _iconLoader.LoadSpriteAsync(_boardDef.BackgroundAddress, "盤面背景画像", ct);
            if (background == null || _boardBackground == null)
            {
                return;
            }
            _boardBackground.style.backgroundImage = new StyleBackground(background);
        }

        /// <summary>
        /// 参加者ぶんのコマを構築する。マス（BuildCells）と Model（injection）の両方が
        /// そろって初めて構築できるため、OnEnable / Construct の後に来た側が呼び出す。
        /// </summary>
        private void BuildPiecesIfReady()
        {
            if (_piecesBuilt || _model == null || _boardArea == null)
            {
                return;
            }

            _piecesBuilt = true;
            _pieceCount = _model.PlayerCount;
            _pieces = new VisualElement[_pieceCount];

            for (int player = 0; player < _pieceCount; player++)
            {
                VisualElement piece = new();
                piece.AddToClassList("board-piece");
                piece.AddToClassList($"board-piece--p{PlayerColors.IndexOf(player)}");
                piece.pickingMode = PickingMode.Ignore;

                Label tag = new(PieceLabel(player)) { pickingMode = PickingMode.Ignore };
                tag.AddToClassList("board-piece__label");
                piece.Add(tag);

                _layout?.PlaceAtCell(piece, _model.Position(player).CurrentValue);
                _boardArea.Add(piece);
                _pieces[player] = piece;

                // アイコンのロードが先に終わっていれば、この時点で貼り付ける。
                ApplyPieceIcon(player);
            }

            // 同マスに乗ったコマが重ならないよう、全コマの中心オフセットを占有状況から決める。
            RefreshPieceOffsets();
        }

        /// <summary>
        /// 上部ヘッダーに全プレイヤーのネームプレート（横 1 行・アイコンとキャラ名だけ。クリックで詳細モーダル）を表示する。
        /// 構築の本体は <see cref="PlayerNameplateView"/> が担う。
        /// マス（BuildCells）と injection（Construct）の両方がそろってから 1 度だけ構築する。
        /// </summary>
        private void BuildPlayerHeaderIfReady()
        {
            if (_headerBuilt || _playerHeader == null || _nameplateView == null)
            {
                return;
            }

            _headerBuilt = true;
            _nameplateView.Build(_playerHeader);
        }

        /// <summary>
        /// 陣地マスを <see cref="TerritoryModel"/> に初期化し、各陣地マスの所有者を購読して
        /// 占拠プレイヤーの色にマスを塗り替える。マス（BuildCells）と injection（Construct）の
        /// 両方がそろってから 1 度だけ実行する。
        /// </summary>
        private void SetupTerritoriesIfReady()
        {
            if (_territoriesSetup || !_cellsBuilt || _territory == null || _boardDef == null)
            {
                return;
            }

            _territoriesSetup = true;

            List<int> territoryCells = new();
            for (int i = 0; i < _cellCount; i++)
            {
                if (_boardDef.Cell(i).Event == BoardCellEvent.Territory)
                {
                    territoryCells.Add(i);
                }
            }
            _territory.Initialize(territoryCells);

            foreach (int index in territoryCells)
            {
                if (_cells == null || index >= _cells.Length || _cells[index] == null)
                {
                    continue;
                }
                int cellIndex = index;
                VisualElement cell = _cells[index];
                cell.AddToClassList("board-cell--territory");
                _disposables.Add(_territory.Owner(index).Subscribe(owner => ApplyTerritoryOwner(cell, cellIndex, owner)));
            }
        }

        /// <summary>
        /// 手番の変化を購読し、手番が移るたびに「〔キャラ名〕の番」のアナウンス帯を出す。
        /// あわせて、他プレイヤーの操作を待っている間の待機表示も手番・ルーレット状態の変化で更新する。
        /// 購読は 1 度だけ張り、以降は <see cref="TurnModel.CurrentPlayer"/> の変化で自動表示する
        /// （購読時に現在の手番でも即発火するので、初手番のアナウンスも出る）。
        /// </summary>
        private void SetupBannersIfReady()
        {
            if (_bannersSetup || !_cellsBuilt || _turn == null || _rouletteModel == null || _turnBanner == null)
            {
                return;
            }
            _bannersSetup = true;
            _disposables.Add(_turn.CurrentPlayer.Subscribe(player =>
            {
                ShowTurnBanner(player);
                // 手番が移ったら「誰を待っているか」も変わる（自分の手番なら消える）。
                RefreshWaitingBanner();
            }));
            // 相手がルーレットを回し始めたら円盤が動いて待っているのが分かるので、待機表示は消す。
            _disposables.Add(_rouletteModel.State.Subscribe(_ => RefreshWaitingBanner()));
        }

        /// <summary>手番プレイヤーのキャラ名でアナウンス帯を出し、少し見せてから隠す。</summary>
        private void ShowTurnBanner(int player)
        {
            if (_turnBanner == null || _turnBannerLabel == null)
            {
                return;
            }

            // 自分（人間プレイヤー）の手番は「あなたの番」、それ以外はキャラ名で「〔キャラ名〕の番」。
            string who = player == _humanPlayer ? "あなた" : CharacterNameOf(player);
            _soundPlayer.PlaySafe(_soundStore?.Enter1SE);
            ShowBannerText($"{who}の番");
        }

        /// <summary>
        /// アナウンス帯に <paramref name="text"/> を出して少し見せてから隠す。
        /// 手番の告知のほか、ミニゲームの勝者発表のような一時的な知らせにも使う。
        /// </summary>
        private void ShowBannerText(string text)
        {
            if (_turnBanner == null || _turnBannerLabel == null)
            {
                return;
            }

            _turnBannerLabel.text = text;
            // 続けて出し直すときは前回のトゥイーンを打ち切る。
            _turnBannerCts?.Cancel();
            _turnBannerCts?.Dispose();
            _turnBannerCts = CancellationTokenSource.CreateLinkedTokenSource(_destroyCt);
            AnimateTurnBannerAsync(_turnBannerCts.Token).Forget();
        }

        // アナウンス帯を --visible で出し、一定時間見せてから隠す。次の手番で打ち切られたら途中で抜ける。
        private async UniTaskVoid AnimateTurnBannerAsync(CancellationToken ct)
        {
            try
            {
                _turnBanner.AddToClassList("turn-banner--visible");
                await UniTask.Delay(TimeSpan.FromSeconds(1.4), cancellationToken: ct);
                _turnBanner.RemoveFromClassList("turn-banner--visible");
            }
            catch (OperationCanceledException)
            {
                // 次の手番アナウンスに差し替えられた（=打ち切り）。--visible はそのまま新しい表示へ引き継ぐ。
            }
        }

        /// <summary>席 <paramref name="player"/> に割り当てられたキャラの表示名（手番アナウンス・待機表示で使う）。</summary>
        private string CharacterNameOf(int player)
        {
            return CharacterCatalog.Find(_characterPicker.ResolveCharacter(player)).DisplayName;
        }

        /// <summary>待機表示の USS クラス（表示中だけ付ける）。</summary>
        private const string WaitingBannerVisibleClass = "waiting-banner--visible";

        /// <summary>待機表示の末尾に付ける「.」の最大数（0〜この数を繰り返して待っている感を出す）。</summary>
        private const int WaitingDotMax = 3;

        /// <summary>待機表示の「.」を 1 つ増やす間隔（秒）。</summary>
        private const float WaitingDotIntervalSeconds = 0.45f;

        /// <summary>
        /// 他プレイヤーの操作（買い物・ミニゲーム・陣地選択）の開始／終了を受け取る。
        /// その席の決定を自分が行う場合（＝自分の操作。一人用モードは全席が該当）は待たされる側ではないので無視する。
        /// </summary>
        private void SetBusy(int seat, BusyReason reason)
        {
            bool waited = reason != BusyReason.None && !_sync.IsLocalDecider(seat);
            _busySeat = waited ? seat : -1;
            _busyReason = waited ? reason : BusyReason.None;
            RefreshWaitingBanner();
        }

        /// <summary>
        /// 待機表示をいまの状態に合わせ直す。優先度は
        /// (1) 他プレイヤーが時間のかかる操作中（<see cref="SetBusy"/> で受けたもの）、
        /// (2) 他プレイヤーの手番でまだルーレットが回っていない（＝こちらは待つだけ）、
        /// (3) どちらでもなければ非表示。
        /// </summary>
        private void RefreshWaitingBanner()
        {
            if (_waitingBanner == null || _waitingBannerLabel == null || _waitingBannerDots == null)
            {
                return;
            }

            string message = ResolveWaitingMessage();
            if (message == null)
            {
                HideWaitingBanner();
                return;
            }
            if (message == _waitingMessage)
            {
                return; // 同じ内容なら張り替えず「.」のアニメを続ける。
            }

            _waitingMessage = message;
            _waitingBannerLabel.text = message;
            _waitingBanner.AddToClassList(WaitingBannerVisibleClass);
            _waitingBannerCts?.Cancel();
            _waitingBannerCts?.Dispose();
            _waitingBannerCts = CancellationTokenSource.CreateLinkedTokenSource(_destroyCt);
            AnimateWaitingDotsAsync(_waitingBannerCts.Token).Forget();
        }

        /// <summary>いま出すべき待機文言（待つ必要がなければ null）。</summary>
        private string ResolveWaitingMessage()
        {
            // 決着後・打ち切り後はもう誰も待たない（決着後に誰かがホームへ戻っても待機表示は出さない）。
            if (_model.IsFinished || _sync.SessionLost.CurrentValue)
            {
                return null;
            }

            // 切断による一時停止は何より優先して知らせる（盤面が動かない理由がこれなので）。
            if (_sync.Paused.CurrentValue)
            {
                int grace = _sync.PauseGraceSeconds;
                if (_sync.PausedSeat == _sync.MySeat)
                {
                    return $"接続が切れました。再接続しています（最大{grace}秒）";
                }
                string who = _sync.PausedSeat >= 0 ? CharacterNameOf(_sync.PausedSeat) : "他のプレイヤー";
                return $"{who}が切断しました。復帰を待っています（最大{grace}秒）";
            }

            // オンラインのミニゲームは全員が同時に遊ぶので、待つ相手は 1 人に定まらない。
            if (_waitingMiniGameScores)
            {
                return "他のプレイヤーの結果を待っています";
            }

            if (_busyReason != BusyReason.None)
            {
                string what = _busyReason switch
                {
                    BusyReason.ItemShop => "買い物中",
                    BusyReason.MiniGame => "ミニゲーム中",
                    BusyReason.TerritorySelect => "陣地を選んでいます",
                    _ => "考え中",
                };
                return $"{CharacterNameOf(_busySeat)}が{what}";
            }

            // 円盤は回している間しか出ないので、相手が回し始めるまでは画面が動かず待たされていることが伝わらない。
            // アイテム効果の演出中は画面が動いているので出さない（決着・打ち切りは冒頭で弾いている）。
            if (_turn == null || _rouletteModel == null || _itemEffectRunning)
            {
                return null;
            }
            int current = _turn.CurrentPlayer.CurrentValue;
            if (_sync.IsLocalDecider(current) || _rouletteModel.State.CurrentValue != RouletteState.Idle)
            {
                return null;
            }
            return $"{CharacterNameOf(current)}のルーレット待ち";
        }

        /// <summary>待機表示を消す（待っていた操作の結果が届いた・待つ必要がなくなった）。</summary>
        private void HideWaitingBanner()
        {
            _waitingMessage = null;
            _waitingBannerCts?.Cancel();
            _waitingBannerCts?.Dispose();
            _waitingBannerCts = null;
            _waitingBanner?.RemoveFromClassList(WaitingBannerVisibleClass);
        }

        // 待機文言の末尾の「.」を増やし続けて、止まっているのではなく待っていることを見せる。
        private async UniTaskVoid AnimateWaitingDotsAsync(CancellationToken ct)
        {
            try
            {
                int dots = 0;
                while (true)
                {
                    _waitingBannerDots.text = new string('.', dots);
                    dots = (dots + 1) % (WaitingDotMax + 1);
                    await UniTask.Delay(TimeSpan.FromSeconds(WaitingDotIntervalSeconds), cancellationToken: ct);
                }
            }
            catch (OperationCanceledException)
            {
                // 待機表示を消した・別の待機に差し替えた（=打ち切り）。
            }
        }

        /// <summary>
        /// 陣地マスの表示を所有者（-1=未占拠 / 0=YOU / 1=CPU）に合わせて切り替える。
        /// 占拠されたマスは所有者の旗画像で塗り替え（未ロードなら色クラスのみ）、
        /// 未占拠に戻ったときは territory 画像へ戻す。所有者色は枠線クラスで残す。
        /// </summary>
        private void ApplyTerritoryOwner(VisualElement cell, int index, int owner)
        {
            for (int i = 0; i < PlayerColors.Count; i++)
            {
                cell.RemoveFromClassList($"board-cell--owned-p{i}");
            }

            if (owner < 0)
            {
                // 未占拠：territory 画像に戻す（ロード済みのとき）。
                if (_cellIcons != null && index < _cellIcons.Length && _cellIcons[index] != null)
                {
                    cell.style.backgroundImage = new StyleBackground(_cellIcons[index]);
                }
                return;
            }

            cell.AddToClassList($"board-cell--owned-p{PlayerColors.IndexOf(owner)}");

            // 占拠者の旗画像でマスを塗る。占拠後はこのマスは旗画像のまま（territory 画像には戻さない）。
            Sprite flag = _flagIcons != null && owner < _flagIcons.Length ? _flagIcons[owner] : null;
            if (flag != null)
            {
                cell.style.backgroundImage = new StyleBackground(flag);
                cell.AddToClassList("board-cell--flag");
            }
        }

        /// <summary>
        /// 各プレイヤーのコマに使うキャラアイコン（バッジ）を Addressables から読み込む。
        /// コマ構築（BuildPiecesIfReady）と injection（Construct）の両方がそろってから 1 度だけ起動する。
        /// </summary>
        private void StartLoadingPieceIconsIfReady()
        {
            if (_iconLoadStarted || _model == null || _characterPicker == null)
            {
                return;
            }

            _iconLoadStarted = true;
            _pieceIcons = new Sprite[_model.PlayerCount];
            _iconLoader.LoadPieceIconsAsync(
                _pieceIcons.Length,
                player => CharacterCatalog.Find(_characterPicker.ResolveCharacter(player)).PieceIconAddress,
                (player, sprite) =>
                {
                    _pieceIcons[player] = sprite;
                    ApplyPieceIcon(player);
                },
                destroyCancellationToken).Forget();

            // 陣地マス占拠の旗演出・占拠マスの塗りに使う各プレイヤーの旗画像を先読みする。
            _flagIcons = new Sprite[_model.PlayerCount];
            _iconLoader.LoadPieceIconsAsync(
                _flagIcons.Length,
                player => CharacterCatalog.Find(_characterPicker.ResolveCharacter(player)).FlagAddress,
                (player, sprite) => _flagIcons[player] = sprite,
                destroyCancellationToken).Forget();
        }

        /// <summary>ロード済みのアイコンをコマへ貼り付ける。コマ・アイコンのどちらか未準備なら何もしない。</summary>
        private void ApplyPieceIcon(int player)
        {
            if (_pieces == null || player < 0 || player >= _pieces.Length || _pieces[player] == null)
            {
                return;
            }
            if (_pieceIcons == null || player >= _pieceIcons.Length || _pieceIcons[player] == null)
            {
                return;
            }

            VisualElement piece = _pieces[player];
            piece.style.backgroundImage = new StyleBackground(_pieceIcons[player]);
            // 色背景を透過にして YOU/CPU ラベルを隠す（バッジ自体で見分ける）。プレイヤー色は枠線で残る。
            piece.AddToClassList("board-piece--icon");
        }

        private string PieceLabel(int player)
        {
            if (_pieceCount <= 1)
            {
                return "YOU";
            }
            // 自分の席は index 0 とは限らない（オンラインは自分の席＝ロビーで確定した席）。
            return player == _humanPlayer ? "YOU" : "CPU";
        }

        private string WinnerText(int winner)
        {
            // 一人用・オンラインとも参加者は最低 2 人（単独プレイ廃止）なので、勝者のキャラ名を出す。
            CharacterId id = _characterPicker.ResolveCharacter(winner);
            string characterName = CharacterCatalog.Find(id).DisplayName;
            return $"{characterName}の勝ち！";
        }

        // 勝敗確定後に「ホームに戻る」ボタンの帯を表示する。
        private void ShowGameOverActions()
        {
            _gameOverActions?.AddToClassList("board-gameover-actions--visible");
        }

        // 「ホームに戻る」を押したら Home シーンへ遷移する（連打・多重遷移をガード）。
        private void OnHomeReturnClicked()
        {
            if (_returningHome)
            {
                return;
            }
            _returningHome = true;
            _soundPlayer.PlaySafe(_soundStore?.Enter1SE);
            ReturnHomeAsync().Forget();
        }

        /// <summary>
        /// オンラインセッションを離脱してから Home シーンへ戻る。離脱しないとルームに残ったままになり、
        /// 残ったプレイヤーからは在席して見えるうえ、次のマッチングにも引きずる。
        /// NGO の停止は <c>ISession.LeaveAsync</c> が一緒に行う（こちらで <c>Shutdown</c> は呼ばない）。
        /// 一人用モードはセッションを持たないので離脱は即座に返る。
        /// </summary>
        private async UniTaskVoid ReturnHomeAsync()
        {
            _onlineRoster.Clear();
            await _gameSession.LeaveCurrentSessionAsync();
            await _sceneTransitioner.Transit(Scenes.Home);
        }

        /// <summary>
        /// 全コマの中心オフセットを、いま各マスに乗っているコマの数で決め直す。
        /// 同じマスに複数乗っているときは円状にずらして全員見えるようにし、単独なら中央に置く。
        /// コマ移動でマスの占有状況が変わるたびに呼ぶ。
        /// </summary>
        private void RefreshPieceOffsets()
        {
            if (_pieces == null)
            {
                return;
            }

            // マス index → そのマスに乗っているプレイヤー（表示順＝プレイヤー index 昇順）。
            Dictionary<int, List<int>> byCell = new();
            for (int player = 0; player < _pieces.Length; player++)
            {
                if (_pieces[player] == null)
                {
                    continue;
                }
                int cell = _model.Position(player).CurrentValue;
                if (!byCell.TryGetValue(cell, out List<int> group))
                {
                    group = new List<int>();
                    byCell[cell] = group;
                }
                group.Add(player);
            }

            foreach (List<int> group in byCell.Values)
            {
                for (int order = 0; order < group.Count; order++)
                {
                    (float dx, float dy) = OffsetInGroup(order, group.Count);
                    _pieces[group[order]].style.translate =
                        new Translate(Length.Percent(-50f + dx), Length.Percent(-50f + dy));
                }
            }
        }

        /// <summary>
        /// 同じマスに <paramref name="count"/> 個乗っているうちの <paramref name="order"/> 番目のコマの、
        /// 中心（-50%,-50%）からのずらし量（％・コマ自身のサイズ基準）。単独なら 0。複数なら円状に配る。
        /// </summary>
        private static (float Dx, float Dy) OffsetInGroup(int order, int count)
        {
            if (count <= 1)
            {
                return (0f, 0f);
            }

            // 2 個は近め、3 個以上は大きめの円に均等配置（上から時計回り）。
            float radius = count == 2 ? 34f : 46f;
            double angle = (2.0 * Math.PI * order / count) - (Math.PI / 2.0);
            return (radius * (float)Math.Cos(angle), radius * (float)Math.Sin(angle));
        }

        /// <summary>
        /// 1 手番で連鎖できる「進む／戻る」マスの上限。進む→進む…と繋がる盤面でも必ず止まるようにする
        /// （全クライアントで同じ定数なので、上限で打ち切っても結果はずれない）。
        /// </summary>
        private const int MaxChainedMoves = 8;

        /// <summary>
        /// プレイヤー <paramref name="player"/> のコマを <paramref name="steps"/> マス進める。
        /// ルーレットの出目とミニゲームのボーナスの両方から呼ばれる共通の移動演出。
        /// 移動中・ゲーム終了後や 0 以下の歩数は無視する。
        /// <paramref name="externalCt"/> は呼び出し元のキャンセル（Destroy 等）を連結するためのもの。
        ///
        /// **止まったマスが「進む／戻る」なら、そこから続けて動く**（<see cref="MaxChainedMoves"/> 回まで）。
        /// 連鎖先のマスでも着地イベントは通常どおり発動するので、進んだ先が陣地マスなら占拠まで走る。
        /// 動くマス数は着地のたびのランダム（<see cref="MoveCellRule"/>）で、着地演出が決めて配った値を
        /// <see cref="TryGetChainedSteps"/> で受け取る＝オンラインでも全員が同じ連鎖をたどる。
        /// </summary>
        public async UniTask AdvanceAsync(int player, int steps, CancellationToken externalCt = default)
        {
            if (_model.IsMoving.CurrentValue || _model.IsFinished)
            {
                return;
            }

            if (steps <= 0 || _pieces == null || player < 0 || player >= _pieces.Length || _pieces[player] == null)
            {
                return;
            }

            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(_destroyCt, externalCt);
            CancellationToken ct = linked.Token;

            try
            {
                int next = steps;
                for (int hop = 0; ; hop++)
                {
                    if (!await MoveAndLandAsync(player, next, ct))
                    {
                        return; // 破棄された（連鎖も打ち切る）
                    }
                    // 決着したらそこで終わり。上限に達したら連鎖を断つ（進む→進むの循環対策）。
                    if (_model.IsFinished || hop >= MaxChainedMoves
                        || !TryGetChainedSteps(out next))
                    {
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _soundPlayer.StopLoopSafe();
            }
        }

        /// <summary>
        /// 1 区間ぶんの移動（<paramref name="steps"/> が負なら戻る）と、止まったマスの着地演出。
        /// 破棄されたら false を返して呼び出し元の連鎖を止める。
        /// </summary>
        private async UniTask<bool> MoveAndLandAsync(int player, int steps, CancellationToken ct)
        {
            _model.BeginMove();

            // 移動を始めたらズームを既定へ戻し、動かすコマを画面中央に据える（以後ステップごとに追従）。
            FocusCameraOnPlayer(player, resetZoom: true);

            // 移動を始めたら走行 SE をループで流す。コマが止まった時点で止める（着地演出中は鳴らさない）。
            // キャンセル時は呼び出し元の finally で確実に止める。
            _soundPlayer.PlayLoopSafe(_soundStore?.RunSE);

            // 周回勝利は廃止したので、出目ぶんそのまま進む（スタート＝ゴールを通過してループし続ける）。
            // 戻るマスも同じループを逆向きにたどるだけ（スタートを跨いだら盤面の末尾へ回り込む）。
            int direction = steps >= 0 ? 1 : -1;
            int count = Mathf.Abs(steps);
            for (int i = 0; i < count; i++)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(_stepInterval), cancellationToken: ct);
                if (this == null)
                {
                    return false;
                }

                int next = BoardMath.Advance(_model.Position(player).CurrentValue, direction, _cellCount);
                _model.SetPosition(player, next); // Position 購読がコマの描画を更新する

                // コマが新しいマスに着いたのを見せてから、少し間を置いてカメラをそのマスへパン追従させる
                // （移動とパンを同フレームで行うとコマが中央に貼りついて "動いてから追う" 感じにならないため）。
                if (_panFollowDelay > 0f)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(_panFollowDelay), cancellationToken: ct);
                }
                FocusCameraOnPlayer(player, resetZoom: false);
            }

            _model.EndMove();
            // コマが止まった時点で走行 SE を止める（着地演出＝お金の浮遊テキスト等の間は鳴らさない）。
            _soundPlayer.StopLoopSafe();
            // 止まったマスの画像表示＋着地イベント（お金の浮遊テキスト等）の演出。
            await PlayLandingSequenceAsync(player, ct);
            return true;
        }

        /// <summary>
        /// 直前の着地で決まった「続けて動くマス数」（戻るは負）を <paramref name="steps"/> に入れて true を返す。
        /// 進む／戻る以外のマスに止まっていれば false。
        ///
        /// 値は着地演出（<see cref="ApplyLandingEventAsync"/>）が <see cref="MoveCellRule"/> で決めて配り、
        /// 全クライアントが受信したものを覚えている。盤面データから引き直さないのは、マス数が着地のたびの
        /// ランダムで、演出で見せた数字と実際に動くマス数をずらせないため。1 度取り出したら消費する
        /// （同じ値で二重に連鎖しないように）。
        /// </summary>
        private bool TryGetChainedSteps(out int steps)
        {
            steps = _pendingChainSteps;
            _pendingChainSteps = 0;
            return steps != 0;
        }

        /// <summary>
        /// プレイヤー <paramref name="player"/> のコマがいるマスが画面中央に来るようカメラ（ズーム領域）を寄せる。
        /// <paramref name="resetZoom"/> が true ならズーム倍率を既定へ戻してから寄せる（移動開始時）。
        /// </summary>
        private void FocusCameraOnPlayer(int player, bool resetZoom)
        {
            if (_zoomController == null || _boardDef == null)
            {
                return;
            }
            int index = _model.Position(player).CurrentValue;
            if (index < 0 || index >= _boardDef.CellCount)
            {
                return;
            }
            Vector2Int grid = _boardDef.Cell(index).Grid;
            int columns = _boardDef.GridColumns;
            int rows = _boardDef.GridRows;
            Vector2 normalized = new(
                columns > 1 ? grid.x / (float)(columns - 1) : 0.5f,
                rows > 1 ? grid.y / (float)(rows - 1) : 0.5f);
            _zoomController.CenterOn(normalized, resetZoom);
        }

        /// <summary>
        /// 着地演出を統括する。止まったマスの画像を中央に拡大表示し、着地イベントを反映する。
        /// お金マスでは増減額の浮遊テキストと画像を同じタイミングで消し、それ以外は画像を少し見せてから消す。
        /// </summary>
        private async UniTask PlayLandingSequenceAsync(int player, CancellationToken ct)
        {
            // 画像を出してから浮遊テキストを出すまでの間（0.5 秒）と、浮遊テキストが浮かび上がる時間（1.5 秒）。
            // お金マスは画像を浮遊テキストと同時に消すので、画像の合計表示は 0.5 + 1.5 = 2.0 秒になる。
            const float PreHoldSeconds = 0.5f;
            const float FloatSeconds = 1.5f;
            // お金・陣地以外のマス（スタート等）は画像を計 1.0 秒表示してから 0.2 秒でフェードアウトさせる。
            const float CellPopupHoldSeconds = 1.0f;

            // 着地演出はアイテムショップなど別のモーダルを開くことがあるので、見せるだけのマス説明は先に閉じる
            // （重なって見えないだけでなく、SortingOrder の退避が二重になって戻らなくなるのを防ぐ）。
            _cellInfo?.Close();

            // 連鎖は「この着地で決まったマス数」だけで起きる。前の着地の値が残っていると、進む／戻る以外の
            // マスに止まったのに動いてしまうので、演出に入る前に必ず落とす。
            _pendingChainSteps = 0;

            int position = _model.Position(player).CurrentValue;

            // 陣地マスは専用の旗演出（中央に旗を表示→縮小しながらマスへ重ねて占拠）に置き換える。
            // 旗がマスに重なった瞬間の占拠確定（ロジック）はコールバックでここから渡す。
            if (_boardDef != null && position >= 0 && position < _boardDef.CellCount
                && _boardDef.Cell(position).Event == BoardCellEvent.Territory)
            {
                // すでに自分が占拠している陣地マスなら、占拠状態は変わらないので旗演出をスキップする。
                ReadOnlyReactiveProperty<int> currentOwner = _territory?.Owner(position);
                if (currentOwner != null && currentOwner.CurrentValue == player)
                {
                    return;
                }
                Sprite flag = _flagIcons != null && player >= 0 && player < _flagIcons.Length ? _flagIcons[player] : null;
                VisualElement targetCell = _cells != null && position < _cells.Length ? _cells[position] : null;
                await _landing.PlayTerritoryFlagSequenceAsync(flag, targetCell, () => ApplyTerritoryLanding(player, position), ct);
                return;
            }

            // アイテム取得マスは、ランダムなラインナップのアイテムショップを開いてお金で購入する。
            if (_boardDef != null && position >= 0 && position < _boardDef.CellCount
                && _boardDef.Cell(position).Event == BoardCellEvent.Item)
            {
                await PlayItemShopSequenceAsync(player, ct);
                return;
            }

            // ミニゲームマスは、そのマスに設定されたミニゲームを遊んで勝てば所持金報酬をもらう。
            if (_boardDef != null && position >= 0 && position < _boardDef.CellCount
                && _boardDef.Cell(position).Event == BoardCellEvent.MiniGame)
            {
                await PlayMiniGameCellSequenceAsync(player, _boardDef.Cell(position).MiniGame, ct);
                return;
            }

            // 止まったマスの画像を中央に出す（消さずに保持）。
            Sprite cellIcon = _cellIcons != null && position >= 0 && position < _cellIcons.Length
                ? _cellIcons[position]
                : null;
            bool popupShown = await _landing.ShowCellPopupAsync(cellIcon, ct);
            if (popupShown)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(PreHoldSeconds), cancellationToken: ct);
            }

            // 着地イベント反映。お金マスでは浮遊テキストと同じタイミングで画像を消すため popupShown を渡す。
            // 浮遊テキストは FloatSeconds かけて浮かび上がり、画像と同時に消す。
            bool hidPopup = await ApplyLandingEventAsync(player, popupShown, FloatSeconds, ct);

            // お金以外（＝画像がまだ出たまま）は、計 CellPopupHoldSeconds 秒見せてから画像を消す
            // （PreHoldSeconds ぶんは経過済みなので残りだけ待つ）。
            if (popupShown && !hidPopup)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(Mathf.Max(0f, CellPopupHoldSeconds - PreHoldSeconds)), cancellationToken: ct);
                await _landing.HideCellPopupAsync(ct);
            }
        }

        /// <summary>
        /// ミニゲームマスの演出。そのマスに設定されたミニゲーム（<paramref name="game"/>＝盤面エディタで選ぶ）を
        /// 着地した本人のクライアントだけが遊び（<see cref="DecideMiniGameCellAsync"/>）、勝敗から決まる報酬額を発行する。
        /// 適用（所持金への加算と浮遊テキスト）は全クライアントが受信して行うので、オンラインでも所持金が食い違わない。
        /// ミニゲームアイテムと違って**遊ぶゲームは選べない**（マスごとに決まっている）。
        /// </summary>
        private async UniTask PlayMiniGameCellSequenceAsync(int player, MiniGameId game, CancellationToken ct)
        {
            // ゲームの内容（被っちゃやーよのカード構成など）を全員でそろえるため、着地した人が種を配る。
            // 遊ぶゲームの種類はマスのデータから全員が導けるので配らない。
            if (_sync.IsLocalDecider(player))
            {
                _sync.Publish(GameAction.MiniGameLanding(player, NextMiniGameSeed()));
            }

            GameAction start = await WaitForActionAsync(GameActionType.MiniGameLanding, ct);
            await RunMiniGameAsync(start.Seat, game, start.MiniGameSeed, ct);
        }

        /// <summary>
        /// ミニゲームを遊んで報酬を配る。オンラインと一人用で遊び方が違う。
        ///
        /// **オンライン**: 参加者全員が同じ内容（<paramref name="seed"/>）のミニゲームを同時に遊ぶ。
        /// 相手はこのクライアントでシミュレートせず、各自が自分の結果値を <see cref="GameActionType.MiniGameScore"/> で
        /// 配る。全員ぶんが揃ったら <see cref="MiniGameRanking.Resolve"/> で勝者を決めるので、
        /// 誰かが判定役にならなくても全クライアントが同じ結論に至る。
        ///
        /// **一人用**: <paramref name="starter"/> が誰であっても自分（人間プレイヤー）が CPU 相手に遊ぶ
        /// （<see cref="RunLocalMiniGameAsync"/>）。相手はゲーム内の CPU が務める。
        /// </summary>
        private async UniTask RunMiniGameAsync(int starter, MiniGameId game, int seed, CancellationToken ct)
        {
            if (_money == null)
            {
                return;
            }

            if (!_sync.IsOnline)
            {
                await RunLocalMiniGameAsync(starter, game, ct);
                return;
            }

            // 参加者の並びは「自分が先頭」。ミニゲーム側は index 0 を自分として扱うので、
            // キャラも結果値もこの並びに合わせて渡す。
            int[] order = MiniGameSeatOrder();
            CharacterId[] characters = MiniGameCharacters(order);

            // プレイ中の途中経過（連打数など）を配り合う経路。受け取った値は席からミニゲーム内の
            // 参加者 index へ直して書き込む（ミニゲーム側は index 0 を自分として描くため）。
            MiniGameProgressChannel progress = new(
                order.Length, value => _sync.PublishProgress(_humanPlayer, value));
            void OnProgress(int seat, int value) => progress.Apply(ParticipantOf(order, seat), value);
            _sync.ProgressReceived += OnProgress;

            // ランチャーが無い（注入に失敗した）ときも「最悪の結果」を必ず配る。ここで黙って抜けると
            // 全員の結果値を待っている他のクライアントが進めなくなる。
            int myValue = MiniGameRanking.WorstValue(game);
            try
            {
                if (_launcher != null)
                {
                    MiniGameResult result = await _launcher.PlayAsync(
                        game, ct, order.Length, characters, simulateOpponents: false, seed, progress);
                    myValue = result.Value;
                }
            }
            finally
            {
                _sync.ProgressReceived -= OnProgress;
            }
            _sync.Publish(GameAction.MiniGameScore(_humanPlayer, myValue));

            int[] valuesBySeat = await CollectMiniGameScoresAsync(ct);
            int[] values = new int[order.Length];
            for (int i = 0; i < order.Length; i++)
            {
                values[i] = valuesBySeat[order[i]];
            }

            await ApplyMiniGameWinnersAsync(game, order, values, ct);
        }

        /// <summary>
        /// 一人用モードのミニゲーム。**着地したのが自分でも CPU でも自分が CPU 相手に遊ぶ**
        /// （オンラインで全員が同時に遊ぶのと同じ体験にするため）。
        ///
        /// 報酬は次の 1 人が受け取る:
        /// <list type="bullet">
        /// <item>自分が勝った → 自分（CPU が着地したミニゲームなら報酬を横取りする）</item>
        /// <item>自分が負けた → 着地した CPU（<paramref name="starter"/>）</item>
        /// <item>自分が着地して負けた → 勝者なし（誰も受け取らない）</item>
        /// </list>
        /// </summary>
        private async UniTask RunLocalMiniGameAsync(int starter, MiniGameId game, CancellationToken ct)
        {
            bool iWon;
            if (_launcher != null)
            {
                // 相手はゲーム内の CPU が務めるので、参加者ぶんの盤面キャラをそのまま渡して
                // 勝敗はゲームの判定（DetermineMiniGameWin）に任せる。
                int[] order = MiniGameSeatOrder();
                MiniGameResult result = await _launcher.PlayAsync(
                    game, ct, order.Length, MiniGameCharacters(order));
                iWon = DetermineMiniGameWin(result);
            }
            else
            {
                // ランチャーが無い（注入に失敗した）ときは遊ばせるものが無いので勝敗を抽選する。
                iWon = _itemRng.Next(2) == 0;
            }

            // 負けたときの勝者は着地した CPU。自分が着地して負けたときだけ勝者なし（誰も報酬を得ない）。
            int winner = iWon ? _humanPlayer : starter;
            bool noWinner = !iWon && starter == _humanPlayer;
            await AwardMiniGameAsync(noWinner ? Array.Empty<int>() : new[] { winner }, ct);
        }

        /// <summary>
        /// ミニゲームの参加者の並び（先頭が自分＝人間プレイヤー、以降は席順）。
        /// ミニゲーム側は index 0 を「自分」として描くので、この並びでキャラ・結果値を渡す。
        /// </summary>
        private int[] MiniGameSeatOrder()
        {
            int[] order = new int[_pieceCount];
            order[0] = _humanPlayer;
            int next = 1;
            for (int seat = 0; seat < _pieceCount; seat++)
            {
                if (seat != _humanPlayer)
                {
                    order[next++] = seat;
                }
            }
            return order;
        }

        /// <summary>参加者の並び <paramref name="order"/> に対応する盤面キャラ（走者・カードの表示に使う）。</summary>
        private CharacterId[] MiniGameCharacters(IReadOnlyList<int> order)
        {
            CharacterId[] characters = new CharacterId[order.Count];
            for (int i = 0; i < order.Count; i++)
            {
                characters[i] = _characterPicker.ResolveCharacter(order[i]);
            }
            return characters;
        }

        /// <summary>
        /// 席 <paramref name="seat"/> が <paramref name="order"/> の何番目の参加者かを返す（見つからなければ -1）。
        /// ミニゲーム側は index 0 を自分として描くので、受信した席をこの並びへ直してから使う。
        /// </summary>
        private static int ParticipantOf(IReadOnlyList<int> order, int seat)
        {
            for (int i = 0; i < order.Count; i++)
            {
                if (order[i] == seat)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// 全参加者の結果値が揃うまで <see cref="GameActionType.MiniGameScore"/> を集める（戻り値の index＝席）。
        /// 同じ席から二重に届いた場合は先着を採る。集めている間は「他のプレイヤーの結果を待っています」を出す。
        /// </summary>
        private async UniTask<int[]> CollectMiniGameScoresAsync(CancellationToken ct)
        {
            int[] bySeat = new int[_pieceCount];
            bool[] received = new bool[_pieceCount];
            int remaining = _pieceCount;

            _waitingMiniGameScores = true;
            RefreshWaitingBanner();
            try
            {
                while (remaining > 0)
                {
                    GameAction action = await WaitForActionAsync(GameActionType.MiniGameScore, ct);
                    int seat = action.Seat;
                    if (seat < 0 || seat >= _pieceCount || received[seat])
                    {
                        continue;
                    }
                    received[seat] = true;
                    bySeat[seat] = action.MiniGameValue;
                    remaining--;
                }
            }
            finally
            {
                _waitingMiniGameScores = false;
                RefreshWaitingBanner();
            }
            return bySeat;
        }

        /// <summary>
        /// 集めた結果値から勝者を決めて報酬を配り、誰が勝ったかを帯で知らせる。
        /// 判定は純粋関数（<see cref="MiniGameRanking.Resolve"/>）なので全クライアントで同じ結果になる。
        /// </summary>
        private UniTask ApplyMiniGameWinnersAsync(
            MiniGameId game, IReadOnlyList<int> order, IReadOnlyList<int> values, CancellationToken ct)
        {
            bool[] wins = MiniGameRanking.Resolve(game, values);
            List<int> winners = new();
            for (int i = 0; i < wins.Length; i++)
            {
                if (wins[i])
                {
                    winners.Add(order[i]);
                }
            }
            return AwardMiniGameAsync(winners, ct);
        }

        /// <summary>
        /// ミニゲームの勝者 <paramref name="winnerSeats"/> に報酬を配り、誰が勝ったかを帯で知らせる
        /// （オンライン・一人用の共通処理）。自分が勝ったときだけ増額の浮遊テキストも出す。
        /// </summary>
        private async UniTask AwardMiniGameAsync(IReadOnlyList<int> winnerSeats, CancellationToken ct)
        {
            List<string> winnerNames = new();
            bool iWon = false;

            for (int i = 0; i < winnerSeats.Count; i++)
            {
                int seat = winnerSeats[i];
                _money.Add(seat, MiniGameRewardMoney);
                winnerNames.Add(CharacterNameOf(seat));
                iWon |= seat == _humanPlayer;
            }

            ShowBannerText(winnerNames.Count == 0
                ? "ミニゲーム 勝者なし"
                : $"ミニゲーム {string.Join("・", winnerNames)} の勝ち！");

            if (winnerNames.Count > 0)
            {
                _soundPlayer.PlaySafe(_soundStore?.MoneySE);
            }
            if (iWon)
            {
                await ShowItemMoneyFloatAsync(MiniGameRewardMoney, ct);
            }
        }

        /// <summary>ミニゲームの内容を組み立てる種を引く（0 は「種なし」の意味なので避ける）。</summary>
        private int NextMiniGameSeed()
        {
            int seed = _itemRng.Next(1, int.MaxValue);
            return seed;
        }

        /// <summary>
        /// アイテム取得マスの演出。着地した本人のクライアントだけが、ランダムな枚数・重複なしのラインナップ
        /// （<see cref="ItemCatalog.RandomLineup"/>）を抽選して購入を決め（<see cref="DecidePurchaseAsync"/>）、
        /// 結果を発行する。適用（代金の支払いと手札への追加）は全クライアントが受信して行う
        /// （<see cref="ApplyShopResultAsync"/>）ので、オンラインでも所持金と手札が食い違わない。
        /// </summary>
        private async UniTask PlayItemShopSequenceAsync(int player, CancellationToken ct)
        {
            if (_items == null)
            {
                return;
            }

            // ラインナップの抽選も購入の選択も、着地した本人のクライアントだけが行って結果を発行する。
            // 他のクライアントはショップを開かず、結果が届くのを待つ（オンラインで買い物を一致させる）。
            if (_sync.IsLocalDecider(player))
            {
                ItemId? purchased = await DecidePurchaseAsync(player, ct);
                _sync.Publish(GameAction.ShopResult(player, purchased.HasValue ? (int)purchased.Value : -1));
            }
            else
            {
                // 買い物の間こちらの画面は何も動かないので、誰を待っているのかを出しておく。
                // 「誰がどのマスに着地したか」は全クライアントで一致しているので、Busy を配らなくても導ける。
                SetBusy(player, BusyReason.ItemShop);
            }

            GameAction result = await WaitForActionAsync(GameActionType.ShopResult, ct);
            SetBusy(player, BusyReason.None);
            await ApplyShopResultAsync(result.Seat, result.ShopItemId, ct);
        }

        /// <summary>
        /// アイテムショップで何を買うかを決める（買わないなら null）。人間プレイヤーはショップモーダルで
        /// 商品情報を見て選び（「買わずに閉じる」なら買わない・暗幕クリックでは閉じない。モーダルは全画面暗幕
        /// （sortingOrder 100）でスピンボタン等を覆うため別途の無効化は要らない）、CPU は買える範囲でランダムに選ぶ。
        /// </summary>
        private async UniTask<ItemId?> DecidePurchaseAsync(int player, CancellationToken ct)
        {
            IReadOnlyList<ItemDefinition> lineup = ItemCatalog.RandomLineup(_itemRng, ItemShopMinItems, ItemShopMaxItems);
            if (lineup == null || lineup.Count == 0)
            {
                return null;
            }

            // 所持金の範囲でしか買えない。CPU も人間も同じ予算で判定する（マイナスにはしない）。
            int budget = _money != null ? _money.Money(player).CurrentValue : int.MaxValue;

            if (player == _humanPlayer && _itemShop != null)
            {
                return await _itemShop.SelectAsync(lineup, budget, ct);
            }
            return PickCpuPurchase(lineup, budget);
        }

        /// <summary>
        /// 購入結果を適用する（全クライアントで実行）。代金を支払い、買ったアイテムを手札へ加える
        /// （自分の席のぶんだけ <see cref="ItemModel.Gained"/> 購読が右下の手札に並べる）。
        /// 支払った代金は、お金マスと同じ浮遊テキスト（−価格）で見せる。
        /// 買ったのが自分以外（CPU・他プレイヤー）のときは、こちらの画面にはショップが出ていない＝
        /// 何を買ったのか分からないので、アイテム絵を中央にポップし帯でも知らせる。
        /// <paramref name="itemId"/> が負なら買わなかったので何もしない。
        /// </summary>
        private async UniTask ApplyShopResultAsync(int player, int itemId, CancellationToken ct)
        {
            if (itemId < 0 || _items == null)
            {
                return;
            }

            ItemDefinition item = ItemCatalog.Find((ItemId)itemId);
            if (item == null)
            {
                return;
            }

            _money?.Add(player, -item.Price);
            _items.Add(player, item.Id);
            // 購入（お金を払う）なので取得 SE ではなくお金 SE を鳴らす。
            _soundPlayer.PlaySafe(_soundStore?.MoneySE);

            // 自分の買い物はショップモーダルで見えているので出さない。CPU・他プレイヤーのぶんだけ
            // 「誰が何を買ったか」をアイテム絵のポップと帯で知らせる。
            bool popupShown = false;
            if (player != _humanPlayer)
            {
                Sprite sprite = await LoadItemSpriteAsync(item, ct);
                popupShown = await _landing.ShowCellPopupAsync(sprite, ct);
                ShowBannerText($"{CharacterNameOf(player)}が「{item.DisplayName}」を購入！");
            }

            // 支払いで所持金が減ったことを、お金マス・アイテム効果と同じ中央の浮遊テキストで見せる
            // （アイテム絵を出したときは、お金マスと同じく浮遊テキストと同時に消す）。
            await _landing.ShowMoneyFloatAsync(-item.Price, popupShown, ItemMoneyFloatSeconds, ct);
        }

        /// <summary>
        /// CPU がショップのラインナップから購入するアイテムを選ぶ。買える（価格 &le; 所持金）ものだけを候補に
        /// ランダムで 1 つ返す。買えるものが無ければ null（買わない）。
        /// </summary>
        private ItemId? PickCpuPurchase(IReadOnlyList<ItemDefinition> lineup, int budget)
        {
            List<ItemDefinition> affordable = new();
            for (int i = 0; i < lineup.Count; i++)
            {
                if (lineup[i].Price <= budget)
                {
                    affordable.Add(lineup[i]);
                }
            }
            if (affordable.Count == 0)
            {
                return null;
            }
            return affordable[_itemRng.Next(affordable.Count)].Id;
        }

        /// <summary>アイテム絵をロードしてキャッシュから返す。未配置なら null（手札は文字プレースホルダになる）。</summary>
        private async UniTask<Sprite> LoadItemSpriteAsync(ItemDefinition item, CancellationToken ct)
        {
            if (item == null)
            {
                return null;
            }
            if (_itemSprites.TryGetValue(item.Id, out Sprite cached))
            {
                return cached;
            }
            Sprite sprite = await _iconLoader.LoadSpriteAsync(item.ImageAddress, "アイテム画像", ct);
            if (sprite != null)
            {
                _itemSprites[item.Id] = sprite;
            }
            return sprite;
        }

        /// <summary>
        /// 取得したアイテムのサムネイルを右下の手札に足す。同じアイテムを重ねて取ったときは
        /// カードを増やさず、既存カード右下の枚数バッジを「x2」のように更新する。
        /// カードはクリックで詳細モーダル（使用する／閉じる）を開く。
        /// 絵が未ロードならアイテム名の文字で代替する。
        /// </summary>
        private void AppendItemToHand(ItemId item)
        {
            if (_itemHand == null)
            {
                return;
            }

            int count = _handCounts.TryGetValue(item, out int current) ? current + 1 : 1;
            _handCounts[item] = count;

            if (_handCards.TryGetValue(item, out VisualElement existing))
            {
                Label countLabel = existing.Q<Label>(className: HandCountClass);
                if (countLabel != null)
                {
                    countLabel.text = $"x{count}";
                    countLabel.AddToClassList(HandCountVisibleClass);
                }
                return;
            }

            VisualElement el = new();
            el.AddToClassList("item-hand__card");
            el.RegisterCallback<ClickEvent>(_ => _itemModal?.Open(item));

            if (_itemSprites.TryGetValue(item, out Sprite sprite) && sprite != null)
            {
                el.style.backgroundImage = new StyleBackground(sprite);
            }
            else
            {
                ItemDefinition def = ItemCatalog.Find(item);
                Label label = new(def?.DisplayName ?? "?") { pickingMode = PickingMode.Ignore };
                label.AddToClassList("item-hand__label");
                el.Add(label);
            }

            // 枚数バッジ。1 枚目は USS 側で非表示のまま、2 枚目からクラス付与で表示する。
            Label badge = new() { pickingMode = PickingMode.Ignore };
            badge.AddToClassList(HandCountClass);
            el.Add(badge);

            _handCards[item] = el;
            _itemHand.Add(el);
        }

        /// <summary>
        /// 使用（消費）されたアイテムを手札表示へ反映する。枚数を 1 減らしてバッジを更新し、
        /// 最後の 1 枚だったらカードごと取り除く。
        /// </summary>
        private void RemoveItemFromHand(ItemId item)
        {
            if (!_handCounts.TryGetValue(item, out int current) || current <= 0)
            {
                return;
            }

            int count = current - 1;
            if (count <= 0)
            {
                _handCounts.Remove(item);
                if (_handCards.TryGetValue(item, out VisualElement card))
                {
                    card.RemoveFromHierarchy();
                    _handCards.Remove(item);
                }
                return;
            }

            _handCounts[item] = count;
            if (_handCards.TryGetValue(item, out VisualElement existing))
            {
                Label countLabel = existing.Q<Label>(className: HandCountClass);
                if (countLabel != null)
                {
                    countLabel.text = $"x{count}";
                    if (count < 2)
                    {
                        // 1 枚に戻ったらバッジを隠す（取得時と同じ「1 枚はバッジなし」表示に揃える）。
                        countLabel.RemoveFromClassList(HandCountVisibleClass);
                    }
                }
            }
        }

        /// <summary>
        /// コマが止まったマスのイベントを発動する。お金イベント（増減）と陣地マス（占拠）、
        /// 進む／戻るマス（動くマス数の浮遊テキスト）を扱い、ミニゲームは従来どおり表示のみで未発動。
        /// 進む／戻るの再移動そのものは <see cref="AdvanceAsync"/> の連鎖が担う。
        /// お金の変化量判定は <see cref="CellEventResolver"/>・加算は <see cref="MoneyModel"/>、
        /// 陣地の占拠・勝利判定（総数÷プレイヤー数の切り上げ）は <see cref="TerritoryModel"/> が担う。
        /// お金マスで画像ポップアップ（<paramref name="popupShown"/>）を浮遊テキストと同時に消した場合は true を返す。
        /// </summary>
        private async UniTask<bool> ApplyLandingEventAsync(int player, bool popupShown, float floatSeconds, CancellationToken ct)
        {
            if (_boardDef == null)
            {
                return false;
            }

            int position = _model.Position(player).CurrentValue;
            if (position < 0 || position >= _boardDef.CellCount)
            {
                return false;
            }

            BoardCellDefinition cell = _boardDef.Cell(position);

            // 進む／戻るマスは、続けて動くマス数（+n / -n）をお金と同じ浮遊テキストで見せてから連鎖へ入る。
            // マス数はマスごとの固定値ではなく着地のたびのランダム（MoveCellRule）。着地した本人だけが決めて発行し、
            // 全員が受信した値を連鎖に使う（オンラインで移動先を一致させる）。お金マスと同じ規約。
            if (CellEventResolver.IsMoveEvent(cell.Event))
            {
                if (_sync.IsLocalDecider(player))
                {
                    if (CellEventResolver.TryGetMoveSteps(cell.Event, MoveCellRule.Steps(_itemRng), out int decided))
                    {
                        _sync.Publish(GameAction.MoveLanding(player, decided));
                    }
                }

                GameAction move = await WaitForActionAsync(GameActionType.MoveLanding, ct);
                // 連鎖の移動そのものは AdvanceAsync が TryGetChainedSteps 経由でこの値を拾って行う。
                _pendingChainSteps = move.MoveSteps;
                await _landing.ShowMoveFloatAsync(_pendingChainSteps, popupShown, floatSeconds, ct);
                return popupShown;
            }

            // 陣地マスは PlayLandingSequenceAsync の旗演出側で占拠を確定するため、ここには来ない。
            // お金マスかどうかはイベント種別だけで決まるので、全クライアントの判定が一致する。
            if (_money == null || !CellEventResolver.IsMoneyEvent(cell.Event))
            {
                return false;
            }

            // 増減額はマスごとの固定値ではなく着地のたびに n×100 のランダム。着地した本人だけが決めて
            // 発行し、全員が受信した額を適用する（オンラインで所持金を一致させる）。
            if (_sync.IsLocalDecider(player))
            {
                if (CellEventResolver.TryGetMoneyDelta(cell.Event, MoneyCellRule.Amount(_itemRng), out int decided))
                {
                    _sync.Publish(GameAction.MoneyLanding(player, decided));
                }
            }

            GameAction landing = await WaitForActionAsync(GameActionType.MoneyLanding, ct);
            int delta = landing.MoneyDelta;

            _money.Add(landing.Seat, delta);
            _soundPlayer.PlaySafe(_soundStore?.MoneySE);
            // 増減額（+n / -n）をポップ画像の底から上へ浮かび上がらせる。画像も浮遊テキストと同時に消す。
            await _landing.ShowMoneyFloatAsync(delta, popupShown, floatSeconds, ct);
            return popupShown;
        }

        /// <summary>
        /// 期待する種別のアクションが届くまで待つ。先にアイテム使用・待機表示が割り込んできた場合は
        /// 取りこぼさずに適用し（効果や表示が消えてしまわないように）、
        /// それ以外の想定外の種別は進行の組み立て違いなので警告して読み飛ばす
        /// （待ち続けてハングするより、ログを残して先へ進めるほうが原因を追いやすい）。
        /// </summary>
        private async UniTask<GameAction> WaitForActionAsync(GameActionType expected, CancellationToken ct)
        {
            while (true)
            {
                GameAction action = await _sync.NextAsync(ct);
                if (action.Type == expected)
                {
                    return action;
                }
                if (action.Type == GameActionType.ItemUse || action.Type == GameActionType.Busy)
                {
                    await ApplyActionAsync(action, ct);
                    continue;
                }
                Debug.LogWarning($"想定外のアクションを受信しました（期待: {expected} / 実際: {action.Type}）。読み飛ばします。");
            }
        }

        /// <summary>
        /// 陣地マスに着地したプレイヤーがそのマスを占拠する（相手の陣地でも上書き）。
        /// 勝利に必要な数を占拠したら勝者を確定する（表示は Winner 購読が行う）。
        /// </summary>
        private void ApplyTerritoryLanding(int player, int position)
        {
            if (_territory == null)
            {
                return;
            }

            _territory.Claim(player, position); // マスの色替えは Owner 購読が行う
            _soundPlayer.PlaySafe(_soundStore?.Enter3SE);

            if (_territory.HasReachedGoal(player))
            {
                _model.SetWinner(player);
            }
        }

        /// <summary>
        /// モーダルの「使用する」を有効にしてよいか。自分の手番で、まだルーレットを回していない（Idle）ときだけ。
        /// 回した後（Spinning/Stopped）・コマ移動中・別のアイテム効果の実行中・
        /// 誰かの切断で進行が止まっている間は無効にする。
        /// </summary>
        private bool CanUseItem()
        {
            return !_itemEffectRunning
                   && !_sync.Paused.CurrentValue // 切断による一時停止中は誰も盤面を進めない
                   && _turn.CurrentPlayer.CurrentValue == _humanPlayer
                   && _rouletteModel.State.CurrentValue == RouletteState.Idle;
        }

        /// <summary>
        /// アイテム「使用する」の効果ハンドラ。ここでは効果の**パラメータを決めて発行するだけ**で、
        /// 消費（<see cref="ItemModel.Use"/>）も効果の適用も行わない（適用は <see cref="ApplyActionAsync"/>）。
        /// 決定と適用を分けることで、オンラインでも全クライアントが同じ効果を同じ順に反映できる。
        ///
        /// 陣地獲得（<see cref="ItemId.StealTerritory"/>）は奪うマスを選ばせ、
        /// ミニゲーム（<see cref="ItemId.MiniGame"/>）は遊んで所持金報酬を確定し、
        /// お金よこどり（<see cref="ItemId.StealMoney"/>）は席ごとの奪取額を抽選する。
        /// いずれもキャンセル・対象なしのときは発行しない＝消費しない。
        /// 効果はターンを消費しない（使用後もルーレットを回せる）。
        /// </summary>
        private void HandleItemUse(ItemId item)
        {
            if (_itemEffectRunning)
            {
                return;
            }

            switch (item)
            {
                case ItemId.StealTerritory:
                    DecideTerritoryStealAsync(_destroyCt).Forget();
                    return;
                case ItemId.MiniGame:
                    DecideMiniGameAsync(_destroyCt).Forget();
                    return;
                case ItemId.StealMoney:
                    DecideMoneySteal();
                    return;
                default:
                    // 勝利（InstantWin）のように決めることが無いアイテムは、そのまま発行して適用へ回す。
                    // 発行から適用（受信）までの間に続けて使われないよう、ここでも効果中にしておく。
                    BeginItemEffect();
                    _sync.Publish(GameAction.ItemUse(_humanPlayer, (int)item));
                    return;
            }
        }

        /// <summary>
        /// 受信したアクションを適用する（全クライアントで実行）。アイテム使用と待機表示を扱う
        /// （スピン・着地は <see cref="Turn.GameFlowController"/> と着地演出側が扱う）。
        /// アイテムの消費はここで行うので、キャンセルされた使用（＝発行されなかったもの）は消費されない。
        ///
        /// 待機表示（<see cref="GameActionType.Busy"/>）は盤面を進めないお知らせなので、表示を切り替えて即座に返る。
        /// それ以外のアクションが届いたときは「待っていた操作が済んだ」ということなので待機表示を消す
        /// （キャンセルされて結果が発行されない場合だけ、決めた人が <see cref="BusyReason.None"/> を配って消す）。
        /// </summary>
        public async UniTask ApplyActionAsync(GameAction action, CancellationToken ct)
        {
            if (action.Type == GameActionType.Busy)
            {
                SetBusy(action.Seat, (BusyReason)action.BusyReasonId);
                return;
            }

            // Busy 以外が届いた＝待たせていた操作が済んだ。この後の BeginItemEffect までは同期処理なので
            // （効果の演出中は待機表示を出さない）、ここで出し直しても描画は挟まらずちらつかない。
            SetBusy(action.Seat, BusyReason.None);

            if (action.Type != GameActionType.ItemUse || _items == null)
            {
                return;
            }

            int itemId = action.UsedItemId;
            ItemDefinition definition = itemId < 0 ? null : ItemCatalog.Find((ItemId)itemId);
            if (definition == null)
            {
                return;
            }

            int seat = action.Seat;
            ItemId item = (ItemId)itemId;

            BeginItemEffect();
            try
            {
                _items.Use(seat, item); // 手札からの減算は Used 購読側（表示するのは自分の席のぶんだけ）

                // 効果ごとの演出（旗・浮遊テキスト・ミニゲーム）へ入る前に、全アイテム共通の「使った」演出を挟む。
                await PlayItemUsePresentationAsync(seat, definition, ct);

                switch (item)
                {
                    case ItemId.StealTerritory:
                        await ApplyTerritoryStealAsync(seat, action.EffectArgAt(0, -1), ct);
                        break;
                    case ItemId.StealMoney:
                        await ApplyMoneyStealAsync(seat, action, ct);
                        break;
                    case ItemId.MiniGame:
                        // 効果パラメータは「遊ぶゲーム」と「内容を組み立てる種」。オンラインは全員が
                        // 同じ内容を同時に遊び、一人用は使用者だけが CPU 相手に遊ぶ。
                        await RunMiniGameAsync(
                            seat, (MiniGameId)action.EffectArgAt(0), action.EffectArgAt(1), ct);
                        break;
                    case ItemId.InstantWin:
                        // 即座に使用者の勝ちを確定する。Winner 購読が勝者表示・「ホームに戻る」・
                        // 決着エフェクトまで自動で走らせる。SetWinner は確定済みなら上書きしない。
                        _model.SetWinner(seat);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                // シーン破棄によるキャンセルは正常終了として扱う。
            }
            finally
            {
                EndItemEffect();
            }
        }

        /// <summary>アイテム効果の開始。実行中はスピンを無効化する（効果はターン非消費なので後で戻す）。</summary>
        private void BeginItemEffect()
        {
            _itemEffectRunning = true;
            // 効果の演出中は画面が動いているので待機表示は要らない。
            RefreshWaitingBanner();
            if (_roulette != null)
            {
                _roulette.SetInteractable(false);
            }
        }

        /// <summary>
        /// アイテム効果の終了。自分の手番かつルーレット未回転（Idle）のままならスピンを再び押せるように戻す
        /// （他プレイヤーの効果を見ていただけのクライアントでは条件を満たさないので何も起きない）。
        /// </summary>
        private void EndItemEffect()
        {
            _itemEffectRunning = false;
            // 効果が終わってもまだ相手の手番なら、待機表示（ルーレット待ち）へ戻す。
            RefreshWaitingBanner();
            RestoreSpinIfMyIdleTurn();
        }

        /// <summary>
        /// 一時的に閉じていたスピンを戻す。戻すのは「自分の手番・ルーレット未回転（Idle）・決着前で、
        /// アイテム効果も切断による一時停止も走っていない」ときだけ（それ以外は閉じたままでよい）。
        /// アイテム効果の終了（<see cref="EndItemEffect"/>）と切断からの復帰の両方から呼ぶ。
        /// </summary>
        private void RestoreSpinIfMyIdleTurn()
        {
            if (_roulette != null && !_model.IsFinished
                && !_itemEffectRunning && !_sync.Paused.CurrentValue && !_sync.SessionLost.CurrentValue
                && _turn.CurrentPlayer.CurrentValue == _humanPlayer
                && _rouletteModel.State.CurrentValue == RouletteState.Idle)
            {
                _roulette.SetInteractable(true);
            }
        }

        /// <summary>
        /// ミニゲームアイテムで勝ったときに得る所持金報酬。
        /// </summary>
        private const int MiniGameRewardMoney = 500;

        /// <summary>
        /// アイテム効果による所持金の増減を見せる浮遊テキストの表示時間（秒）。
        /// </summary>
        private const float ItemMoneyFloatSeconds = 1.5f;

        /// <summary>
        /// アイテム使用時に絵を中央へ出しておく時間（秒）。この後フェードアウトして効果の演出へ入るので、
        /// 「使った」ことが伝わる範囲でなるべく短くする。
        /// </summary>
        private const float ItemUsePopupSeconds = 0.7f;

        /// <summary>
        /// アイテムを使ったことを見せる共通演出（効果の種別に依らず、どのアイテムでも同じように出す）。
        /// アイテム絵を中央にポップし、「〔キャラ名〕が「〔アイテム名〕」を使用！」の帯と SE を添えてから
        /// 絵を消す。適用側（全クライアント）から呼ぶので、相手が何を使ったのかも分かる。
        /// アイテム絵が未配置のときはポップを飛ばして帯と SE だけになる。
        /// </summary>
        private async UniTask PlayItemUsePresentationAsync(int seat, ItemDefinition item, CancellationToken ct)
        {
            if (item == null)
            {
                return;
            }

            ShowBannerText($"{CharacterNameOf(seat)}が「{item.DisplayName}」を使用！");
            _soundPlayer.PlaySafe(_soundStore?.ItemGetSE);

            Sprite sprite = await LoadItemSpriteAsync(item, ct);
            bool popupShown = await _landing.ShowCellPopupAsync(sprite, ct);
            if (!popupShown)
            {
                return;
            }

            await UniTask.Delay(TimeSpan.FromSeconds(ItemUsePopupSeconds), cancellationToken: ct);
            // 効果側の演出（旗・ミニゲーム・勝者表示）を覆わないよう、絵は必ず消してから戻る。
            await _landing.HideCellPopupAsync(ct);
        }

        /// <summary>
        /// ミニゲームアイテムの決定。遊ぶミニゲームを選ばせて起動し、勝敗から所持金報酬を確定して発行する
        /// （キャンセルなら発行しない＝消費しない）。ミニゲーム自体はローカル完結で、他プレイヤーへは
        /// 結果（報酬額）だけを配る。選択・プレイの間はスピンボタンを無効化する。
        /// </summary>
        private async UniTaskVoid DecideMiniGameAsync(CancellationToken ct)
        {
            if (_miniGameSelect == null || _launcher == null)
            {
                return;
            }

            BeginItemEffect();
            // 選択とプレイの間、他のクライアントの画面は何も動かないので待機表示を出してもらう
            // （キャンセルされたら下の finally で解除を配る。成功したら ItemUse の受信が表示を消す）。
            _sync.Publish(GameAction.Busy(_humanPlayer, BusyReason.MiniGame));
            bool published = false;
            try
            {
                // 「使用する」を押したアイテム詳細モーダルが Close で sortingOrder を元へ戻すのは
                // このメソッド呼び出しの直後（同フレーム）。それを待たずに選択モーダルを開くと、
                // 持ち上げ済みの sortingOrder を base として取り込んでしまい閉じても戻らなくなるため、
                // 1 フレーム待って詳細モーダルの Close を先に完了させてから開く。
                await UniTask.Yield(PlayerLoopTiming.Update, ct);

                MiniGameId? chosen = await _miniGameSelect.SelectAsync(ct);
                if (chosen == null)
                {
                    return; // キャンセル・破棄：消費しない
                }

                // 決めるのは「どのゲームを」「どの内容で」だけ。プレイと報酬の反映は受信側
                // （ApplyActionAsync → RunMiniGameAsync）が行うので、オンラインでは全員が同時に遊べる。
                _sync.Publish(GameAction.ItemUse(
                    _humanPlayer, (int)ItemId.MiniGame, (int)chosen.Value, NextMiniGameSeed()));
                published = true;
            }
            catch (OperationCanceledException)
            {
                // シーン破棄によるキャンセルは正常終了として扱う。
            }
            finally
            {
                // 発行できたなら、続けて走る適用側（ApplyActionAsync）が効果の終了まで面倒を見る。
                if (!published)
                {
                    _sync.Publish(GameAction.Busy(_humanPlayer, BusyReason.None));
                    EndItemEffect();
                }
            }
        }

        /// <summary>
        /// ミニゲームの結果 <paramref name="result"/> から人間プレイヤーの勝ち（1 位）かを判定する。
        /// いずれのゲームもスコア 1=勝ち／0=負けで報告する（2D レースは先着、タップ連打は連打数 1 位、
        /// 被っちゃやーよは獲得。CPU の連打はゲーム側でシミュレートするのでここでの想定値比較は不要）。
        /// </summary>
        private bool DetermineMiniGameWin(MiniGameResult result)
        {
            return result.Score == 1;
        }

        /// <summary>
        /// お金よこどりの決定。自分以外の参加者それぞれから奪う額を <see cref="MoneyStealRule"/> で抽選し、
        /// 席ごとの奪取額を効果パラメータとして発行する（席 index がそのままパラメータの並び順）。
        /// 奪える額が無い（相手がいない・全員の所持金が 0 以下）ときは発行しない＝消費しない。
        /// </summary>
        private void DecideMoneySteal()
        {
            if (_money == null)
            {
                return;
            }

            int[] amounts = new int[_money.PlayerCount];
            int total = 0;
            for (int player = 0; player < amounts.Length; player++)
            {
                if (player == _humanPlayer)
                {
                    continue;
                }
                int amount = MoneyStealRule.Amount(_money.Money(player).CurrentValue, _itemRng);
                amounts[player] = amount;
                total += amount;
            }

            if (total <= 0)
            {
                // 奪える相手がいない（全員 0 以下 or 相手なし）。消費しない。
                return;
            }

            // 適用（送金と演出）は受信側が行う。発行できたので、効果の終了は適用側に任せる。
            BeginItemEffect();
            _sync.Publish(GameAction.ItemUse(_humanPlayer, (int)ItemId.StealMoney, amounts));
        }

        /// <summary>
        /// 陣地獲得の決定。自分以外が持つ陣地マス（未占拠＋相手占拠）から 1 つを選ばせ、選んだマスを発行する。
        /// 対象が無い・キャンセル・シーン破棄のときは発行しない＝消費しない。
        /// 選択の間はスピンボタンを無効化する（使用後は自分の手番のまま通常のルーレットを回せる）。
        /// </summary>
        private async UniTaskVoid DecideTerritoryStealAsync(CancellationToken ct)
        {
            if (_territory == null || _cells == null)
            {
                return;
            }

            IReadOnlyList<int> eligible = _territory.CellsNotOwnedBy(_humanPlayer);
            if (eligible.Count == 0)
            {
                // 奪える・占領できる陣地マスが無い（すべて自分の占拠 or 陣地マス自体が無い）。消費しない。
                return;
            }

            BeginItemEffect();
            // マスを選んでいる間、他のクライアントの画面は何も動かないので待機表示を出してもらう
            // （キャンセルされたら下の finally で解除を配る。成功したら ItemUse の受信が表示を消す）。
            _sync.Publish(GameAction.Busy(_humanPlayer, BusyReason.TerritorySelect));
            bool published = false;
            try
            {
                int chosen = await SelectTerritoryCellAsync(eligible, ct);
                if (chosen < 0)
                {
                    return; // キャンセル・破棄：消費しない
                }
                _sync.Publish(GameAction.ItemUse(_humanPlayer, (int)ItemId.StealTerritory, chosen));
                published = true;
            }
            catch (OperationCanceledException)
            {
                // シーン破棄によるキャンセルは正常終了として扱う。
            }
            finally
            {
                if (!published)
                {
                    _sync.Publish(GameAction.Busy(_humanPlayer, BusyReason.None));
                    EndItemEffect();
                }
            }
        }

        /// <summary>
        /// 陣地獲得の適用。着地時と同じ旗演出 → 占拠確定（上書きで奪う）→ 必要数なら勝者。
        /// </summary>
        private async UniTask ApplyTerritoryStealAsync(int player, int cellIndex, CancellationToken ct)
        {
            if (_territory == null || _cells == null || cellIndex < 0 || cellIndex >= _cells.Length)
            {
                return;
            }

            Sprite flag = _flagIcons != null && player >= 0 && player < _flagIcons.Length ? _flagIcons[player] : null;
            await _landing.PlayTerritoryFlagSequenceAsync(
                flag, _cells[cellIndex], () => ApplyTerritoryLanding(player, cellIndex), ct);
        }

        /// <summary>
        /// お金よこどりの適用。席ごとの奪取額（<paramref name="action"/> の効果パラメータ）を相手から引き、
        /// 合計を使用者に足す（合計は保存される）。
        /// 浮遊テキストは**この画面の持ち主から見た増減**を見せる（適用は全クライアントで走るので、
        /// 一律に合計の増額を出すと奪われた側の画面にも「+」が出てしまう）＝使った本人は奪った合計をプラスで、
        /// 奪われた側は自分が失った額をマイナスで（誰にやられたかの帯つきで）見る。
        /// どちらでもない席（所持金 0 以下で奪われなかった人）には出さない。
        /// </summary>
        private async UniTask ApplyMoneyStealAsync(int player, GameAction action, CancellationToken ct)
        {
            if (_money == null)
            {
                return;
            }

            int total = 0;
            // 自分の席が奪われた額（自分が使用者、または奪われなかったときは 0）。
            int myLoss = 0;
            int seats = Mathf.Min(_money.PlayerCount, action.EffectArgCount);
            for (int seat = 0; seat < seats; seat++)
            {
                int amount = action.EffectArgAt(seat);
                if (seat == player || amount <= 0)
                {
                    continue;
                }
                _money.Add(seat, -amount);
                total += amount;
                if (seat == _humanPlayer)
                {
                    myLoss = amount;
                }
            }

            if (total <= 0)
            {
                return;
            }

            _money.Add(player, total);

            int delta = player == _humanPlayer ? total : -myLoss;
            if (delta == 0)
            {
                return; // 使った本人でも奪われた側でもない席（一人用の CPU 相手ぶんもここで止まる）
            }

            // 奪われた側の画面にはアイテムを使う操作が出ていない＝マイナスの理由が分からないので、
            // 購入の知らせ（ApplyShopResultAsync）と同じように誰にやられたかを帯で添える。
            if (delta < 0)
            {
                ShowBannerText($"{CharacterNameOf(player)}にお金を奪われた！");
            }

            _soundPlayer.PlaySafe(_soundStore?.MoneySE);
            await ShowItemMoneyFloatAsync(delta, ct);
        }

        /// <summary>アイテム効果による所持金の増減を中央の浮遊テキストで見せる（マス画像は出さない）。</summary>
        private UniTask ShowItemMoneyFloatAsync(int delta, CancellationToken ct)
        {
            return _landing.ShowMoneyFloatAsync(delta, false, ItemMoneyFloatSeconds, ct);
        }

        /// <summary>
        /// 対象の陣地マス <paramref name="eligible"/> を金枠で強調し、ガイドバナーを出して、
        /// 盤面タップ（<see cref="BoardZoomController.BeginCellSelection"/> 経由・パンは有効のまま）または
        /// キャンセルを待つ。選んだ盤面 index を返す（キャンセル・破棄は -1）。
        /// </summary>
        private async UniTask<int> SelectTerritoryCellAsync(IReadOnlyList<int> eligible, CancellationToken ct)
        {
            // 選択できるマスを金枠で強調し、上にキラキラのリング要素を重ねる（パルスは下のループが動かす）。
            List<VisualElement> glows = new();
            for (int i = 0; i < eligible.Count; i++)
            {
                int index = eligible[i];
                if (index >= 0 && index < _cells.Length && _cells[index] != null)
                {
                    _cells[index].AddToClassList(SelectableCellClass);
                    VisualElement glow = new() { pickingMode = PickingMode.Ignore };
                    glow.AddToClassList(SelectableGlowClass);
                    _cells[index].Add(glow);
                    glows.Add(glow);
                }
            }
            ShowTerritoryBanner(true);

            UniTaskCompletionSource<int> tcs = new();
            _territorySelectionTcs = tcs;
            _zoomController?.BeginCellSelection(screenPos => TryPickCell(eligible, screenPos));

            // 選択が終わる（確定・キャンセル・破棄）までキラキラを回し、finally で止める。
            using CancellationTokenSource pulseCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            AnimateSelectableGlowAsync(glows, pulseCts.Token).Forget();

            try
            {
                using (ct.Register(() => tcs.TrySetResult(-1)))
                {
                    return await tcs.Task;
                }
            }
            finally
            {
                pulseCts.Cancel();
                _zoomController?.EndCellSelection();
                ShowTerritoryBanner(false);
                for (int i = 0; i < eligible.Count; i++)
                {
                    int index = eligible[i];
                    if (index >= 0 && index < _cells.Length && _cells[index] != null)
                    {
                        _cells[index].RemoveFromClassList(SelectableCellClass);
                    }
                }
                for (int i = 0; i < glows.Count; i++)
                {
                    glows[i].RemoveFromHierarchy();
                }
                _territorySelectionTcs = null;
            }
        }

        /// <summary>
        /// 選択できる陣地マスのキラキラ演出。各マスに重ねたリング（<paramref name="glows"/>）を、
        /// マスの外へ広がりながら消える「パルス（ping）」として毎フレーム動かす。マスごとに位相をずらして
        /// 時間差でキラッとさせる。<paramref name="ct"/> のキャンセル（選択終了・破棄）で静かに止まる。
        /// </summary>
        private async UniTaskVoid AnimateSelectableGlowAsync(List<VisualElement> glows, CancellationToken ct)
        {
            // 1 秒あたりのパルス回数と、マスごとの位相ずらし量。
            const float Speed = 0.9f;
            const float PhaseStep = 0.35f;
            try
            {
                float elapsed = 0f;
                while (!ct.IsCancellationRequested)
                {
                    elapsed += Time.deltaTime;
                    for (int i = 0; i < glows.Count; i++)
                    {
                        // 0→1 を繰り返す位相。小さいうちは明るく、広がるにつれて消える（＝ping）。
                        float cycle = Mathf.Repeat(elapsed * Speed + i * PhaseStep, 1f);
                        glows[i].style.opacity = (1f - cycle) * 0.85f;
                        float scale = Mathf.Lerp(0.95f, 1.55f, cycle);
                        glows[i].style.scale = new Scale(new Vector2(scale, scale));
                    }
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        /// <summary>
        /// タップ位置 <paramref name="screenPos"/>（パネル座標）が対象マスの上なら、そのマスを選択して true を返す。
        /// どの対象マスにも当たらなければ false（選択は継続）。
        /// </summary>
        private bool TryPickCell(IReadOnlyList<int> eligible, Vector2 screenPos)
        {
            for (int i = 0; i < eligible.Count; i++)
            {
                int index = eligible[i];
                if (index < 0 || index >= _cells.Length)
                {
                    continue;
                }
                VisualElement cell = _cells[index];
                if (cell != null && cell.worldBound.Contains(screenPos))
                {
                    _territorySelectionTcs?.TrySetResult(index);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// タップ位置 <paramref name="screenPos"/>（パネル座標）にマスがあれば、そのマスの説明モーダルを開いて true を返す。
        /// どのマスにも当たらなければ false。見せるだけで盤面には触らないので、誰の手番でも・演出中でも開ける
        /// （<see cref="BoardZoomController.SetCellTapHandler"/> から呼ばれる）。
        /// </summary>
        private bool TryOpenCellInfoAt(Vector2 screenPos)
        {
            if (_cellInfo == null || _cells == null || _boardDef == null)
            {
                return false;
            }

            for (int index = 0; index < _cells.Length; index++)
            {
                VisualElement cell = _cells[index];
                if (cell == null || !cell.worldBound.Contains(screenPos))
                {
                    continue;
                }
                OpenCellInfo(index);
                return true;
            }
            return false;
        }

        /// <summary>
        /// マス <paramref name="index"/> の説明モーダルを開く。絵はロード済みのマス画像キャッシュ
        /// （<c>_cellIcons</c>）から引くので、占拠して旗に差し替わった陣地マスでも元のマスの絵を見せる。
        /// </summary>
        private void OpenCellInfo(int index)
        {
            BoardCellDefinition definition = _boardDef.Cell(index);
            bool isStart = index == 0;
            string title = isStart ? BoardEventDescription.StartLabel : BoardEventLabel.Of(definition.Event);
            string description = isStart
                ? BoardEventDescription.StartDescription
                : BoardEventDescription.Of(definition.Event, definition.MiniGame, MiniGameRewardMoney);
            Sprite sprite = _cellIcons != null && index < _cellIcons.Length ? _cellIcons[index] : null;

            _cellInfo.Open(title, description, TerritoryStatusOf(index, definition), sprite);
        }

        /// <summary>
        /// 陣地マスの占拠状況の行（占拠者と勝利に必要な数）。陣地マス以外・陣地を持たない盤面では空文字を返し、
        /// モーダル側で行ごと隠される。
        /// </summary>
        private string TerritoryStatusOf(int index, BoardCellDefinition definition)
        {
            if (definition.Event != BoardCellEvent.Territory || _territory == null)
            {
                return string.Empty;
            }
            ReadOnlyReactiveProperty<int> owner = _territory.Owner(index);
            if (owner == null)
            {
                return string.Empty;
            }

            int player = owner.CurrentValue;
            string ownerText = player < 0
                ? "まだ誰も占拠していない"
                : $"占拠中：{(player == _humanPlayer ? "あなた" : CharacterNameOf(player))}";
            return $"{ownerText}／勝利に必要な陣地：{_territory.RequiredToWin} マス";
        }

        /// <summary>陣地選択ガイドバナーの表示/非表示を切り替える。</summary>
        private void ShowTerritoryBanner(bool visible)
        {
            if (_territorySelectBanner != null)
            {
                _territorySelectBanner.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }
}
