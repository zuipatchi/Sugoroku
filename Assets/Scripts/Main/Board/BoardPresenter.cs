using System;
using System.Collections.Generic;
using System.Threading;
using Common.Board;
using Common.Character;
using Common.MiniGame;
using Common.SceneManagement;
using Common.SoundManagement;
using Common.Store;
using Cysharp.Threading.Tasks;
using Main.Item;
using Main.Money;
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
        // 手札を右下に出す人間プレイヤーの index（参加者リストから解決）。
        private int _humanPlayer;

        private UIDocument _uiDocument;
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
        // 陣地獲得アイテムのマス選択ガイドバナー（USS で既定非表示・選択中だけ表示）。
        private VisualElement _territorySelectBanner;
        // 陣地選択の結果を受け渡す完了ソース（選んだ盤面 index／キャンセル・破棄で -1）。選択中だけ非 null。
        private UniTaskCompletionSource<int> _territorySelectionTcs;
        // アイテム効果（選択→演出）の実行中フラグ。多重起動と、実行中の「使用する」再有効化を防ぐ。
        private bool _itemEffectRunning;
        // 陣地選択のハイライトを付けたマスの USS クラス。
        private const string SelectableCellClass = "board-cell--selectable";
        // 選択できるマスに重ねるキラキラのリング要素の USS クラス。
        private const string SelectableGlowClass = "board-cell__glow";
        // アイテム抽選の乱数源（ゲーム内の見た目のランダム性用。抽選ロジック自体は ItemCatalog にある）。
        private readonly System.Random _itemRng = new();
        private BoardDefinition _boardDef;
        private BoardLayoutCalculator _layout;
        private BoardZoomController _zoomController;
        private bool _ownsBoardDef;
        private int _cellCount;
        private int _pieceCount;
        private bool _cellsBuilt;
        private bool _cellIconLoadStarted;
        private bool _frameLoadStarted;
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
            CharacterSessionModel characterSession,
            GameParticipants participants,
            MoneyModel money,
            ItemModel items,
            MiniGameLauncher launcher,
            TurnModel turn,
            RouletteModel rouletteModel,
            RoulettePresenter roulette,
            BoardSessionModel boardSession,
            SceneTransitioner sceneTransitioner)
        {
            _model = model;
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
            _characterPicker = new CpuCharacterPicker(participants, characterSession);
            _nameplateView = new PlayerNameplateView(participants, money, territory, _characterPicker, _iconLoader, destroyCancellationToken, _disposables);

            // 手札を右下に出すのは人間プレイヤーだけ。参加者リストから最初の Human を採用する。
            _humanPlayer = 0;
            for (int i = 0; i < participants.Count; i++)
            {
                if (participants.KindOf(i) == PlayerKind.Human)
                {
                    _humanPlayer = i;
                    break;
                }
            }

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
                // 勝敗が決まったら「ホームに戻る」ボタンを出す。
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
        }

        private void OnDestroy()
        {
            _disposables.Dispose();
            _iconLoader.Dispose();
            _zoomController?.Dispose();

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

            _boardArea = root.Q<VisualElement>("BoardArea");
            _playerHeader = root.Q<VisualElement>("PlayerHeader");
            _clearLabel = root.Q<Label>("ClearLabel");
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
                else
                {
                    cell.Add(new Label(i.ToString()) { pickingMode = PickingMode.Ignore });
                }
                ApplyCellAppearance(cell, definition);
                AddFrameOverlay(cell);
                _layout.PlaceAtCell(cell, i);
                _boardArea.Add(cell);
                _cells[i] = cell;
            }

            StartLoadingCellIcons();
            StartLoadingFrameIfReady();

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
                case BoardCellEvent.Forward:
                    return $"▲{definition.Amount}";
                case BoardCellEvent.Back:
                    return $"▼{definition.Amount}";
                case BoardCellEvent.Rest:
                    return "休";
                case BoardCellEvent.MiniGame:
                    return "MG";
                case BoardCellEvent.MoneyUp:
                    return $"$+{definition.Amount}";
                case BoardCellEvent.MoneyDown:
                    return $"$-{definition.Amount}";
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
        /// 上部ヘッダーに全プレイヤーのネームプレート（横 1 行・最大 2 人／ページ・三角ボタンでページ送り）を表示する。
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
            return player == 0 ? "YOU" : "CPU";
        }

        private string WinnerText(int winner)
        {
            // 単独プレイ（オンライン参加者 1 人）は従来通りゴール表示。CPU 戦は勝敗を表示する。
            if (_model.PlayerCount <= 1)
            {
                return "ゴール！";
            }
            return winner == 0 ? "あなたの勝ち！" : "CPUの勝ち！";
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
            _sceneTransitioner.Transit(Scenes.Home).Forget();
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
        /// プレイヤー <paramref name="player"/> のコマを <paramref name="steps"/> マス進める。
        /// ルーレットの出目とミニゲームのボーナスの両方から呼ばれる共通の移動演出。
        /// 移動中・ゲーム終了後や 0 以下の歩数は無視する。
        /// <paramref name="externalCt"/> は呼び出し元のキャンセル（Destroy 等）を連結するためのもの。
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
            _model.BeginMove();

            // 移動を始めたらズームを既定へ戻し、動かすコマを画面中央に据える（以後ステップごとに追従）。
            FocusCameraOnPlayer(player, resetZoom: true);

            // 移動を始めたら走行 SE をループで流す。コマが止まった時点で止める（着地演出中は鳴らさない）。
            // キャンセル時は finally で確実に止める。
            _soundPlayer.PlayLoopSafe(_soundStore?.RunSE);

            // 周回勝利は廃止したので、出目ぶんそのまま進む（スタート＝ゴールを通過してループし続ける）。
            try
            {
                for (int i = 0; i < steps; i++)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(_stepInterval), cancellationToken: ct);
                    if (this == null)
                    {
                        return;
                    }

                    int next = BoardMath.Advance(_model.Position(player).CurrentValue, 1, _cellCount);
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

            int position = _model.Position(player).CurrentValue;

            // 陣地マスは専用の旗演出（中央に旗を表示→縮小しながらマスへ重ねて占拠）に置き換える。
            // 旗がマスに重なった瞬間の占拠確定（ロジック）はコールバックでここから渡す。
            if (_boardDef != null && position >= 0 && position < _boardDef.CellCount
                && _boardDef.Cell(position).Event == BoardCellEvent.Territory)
            {
                Sprite flag = _flagIcons != null && player >= 0 && player < _flagIcons.Length ? _flagIcons[player] : null;
                VisualElement targetCell = _cells != null && position < _cells.Length ? _cells[position] : null;
                await _landing.PlayTerritoryFlagSequenceAsync(flag, targetCell, () => ApplyTerritoryLanding(player, position), ct);
                return;
            }

            // アイテム取得マスは、抽選したアイテム絵を中央に見せてから手札（右下）へ加える。
            if (_boardDef != null && position >= 0 && position < _boardDef.CellCount
                && _boardDef.Cell(position).Event == BoardCellEvent.Item)
            {
                await PlayItemSequenceAsync(player, CellPopupHoldSeconds, ct);
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
        /// アイテム取得マスの演出。カタログからランダムに 1 つ選び、そのアイテム絵を画面中央に
        /// <paramref name="holdSeconds"/> 秒ほど見せてから <see cref="ItemModel"/> へ加える
        /// （人間プレイヤーなら購読で右下の手札へ足される）。アイテム絵が未配置なら演出をスキップして取得だけ行う。
        /// </summary>
        private async UniTask PlayItemSequenceAsync(int player, float holdSeconds, CancellationToken ct)
        {
            if (_items == null)
            {
                return;
            }

            ItemDefinition item = ItemCatalog.RandomItem(_itemRng);
            if (item == null)
            {
                return;
            }

            // 取得を通知する前に絵をロードしておく（手札サムネイルがキャッシュから引けるようにする）。
            Sprite sprite = await LoadItemSpriteAsync(item, ct);

            bool shown = await _landing.ShowCellPopupAsync(sprite, ct);
            if (shown)
            {
                // 抽選したアイテム絵が見えた瞬間に取得 SE を鳴らす（手札へ加わるのはホールド後）。
                _soundPlayer.PlaySafe(_soundStore?.ItemGetSE);
                await UniTask.Delay(TimeSpan.FromSeconds(holdSeconds), cancellationToken: ct);
            }

            _items.Add(player, item.Id);
            if (!shown)
            {
                // 絵が未配置でポップアップを出せなかったときも、取得したことは SE で伝える。
                _soundPlayer.PlaySafe(_soundStore?.ItemGetSE);
            }

            if (shown)
            {
                await _landing.HideCellPopupAsync(ct);
            }
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
        /// コマが止まったマスのイベントを発動する。お金イベント（増減）と陣地マス（占拠）を扱い、
        /// 進む／戻る／休み／ミニゲームは従来どおり表示のみで未発動。
        /// お金の変化量判定は <see cref="CellEventResolver"/>・加算は <see cref="MoneyModel"/>、
        /// 陣地の占拠・過半数判定は <see cref="TerritoryModel"/> が担う。
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

            // 陣地マスは PlayLandingSequenceAsync の旗演出側で占拠を確定するため、ここには来ない。
            if (_money != null && CellEventResolver.TryGetMoneyDelta(cell.Event, cell.Amount, out int delta))
            {
                _money.Add(player, delta);
                _soundPlayer.PlaySafe(_soundStore?.MoneySE);
                // 増減額（+n / -n）をポップ画像の底から上へ浮かび上がらせる。画像も浮遊テキストと同時に消す。
                await _landing.ShowMoneyFloatAsync(delta, popupShown, floatSeconds, ct);
                return popupShown;
            }

            return false;
        }

        /// <summary>
        /// 陣地マスに着地したプレイヤーがそのマスを占拠する（相手の陣地でも上書き）。
        /// 過半数を占拠したら勝者を確定する（表示は Winner 購読が行う）。
        /// </summary>
        private void ApplyTerritoryLanding(int player, int position)
        {
            if (_territory == null)
            {
                return;
            }

            _territory.Claim(player, position); // マスの色替えは Owner 購読が行う
            _soundPlayer.PlaySafe(_soundStore?.Enter3SE);

            if (_territory.HasMajority(player))
            {
                _model.SetWinner(player);
            }
        }

        /// <summary>
        /// モーダルの「使用する」を有効にしてよいか。自分の手番で、まだルーレットを回していない（Idle）ときだけ。
        /// 回した後（Spinning/Stopped）・コマ移動中・別のアイテム効果の実行中は無効にする。
        /// </summary>
        private bool CanUseItem()
        {
            return !_itemEffectRunning
                   && _turn.CurrentPlayer.CurrentValue == _humanPlayer
                   && _rouletteModel.State.CurrentValue == RouletteState.Idle;
        }

        /// <summary>
        /// アイテム「使用する」の効果ハンドラ。アイテム種別で分岐する。
        /// 陣地獲得（<see cref="ItemId.StealTerritory"/>）はマス選択→占拠の演出を起こし、確定時に消費する。
        /// ミニゲーム（<see cref="ItemId.MiniGame"/>）は遊ぶミニゲームを選ばせて起動し、勝てば所持金報酬を与える。
        /// お金よこどり（<see cref="ItemId.StealMoney"/>）は相手の所持金の一部を奪って自分に足す。
        /// 効果ハンドラを持たないアイテムは従来どおり即消費する。効果はターンを消費しない（使用後もルーレットを回せる）。
        /// </summary>
        private void HandleItemUse(ItemId item)
        {
            if (_itemEffectRunning)
            {
                return;
            }

            if (item == ItemId.StealTerritory)
            {
                RunTerritoryStealAsync(_destroyCt).Forget();
                return;
            }

            if (item == ItemId.MiniGame)
            {
                RunMiniGameAsync(_destroyCt).Forget();
                return;
            }

            if (item == ItemId.StealMoney)
            {
                RunMoneyStealAsync(_destroyCt).Forget();
                return;
            }

            // ここに来るのは効果ハンドラを持たないアイテム（現状なし）。将来の未実装アイテムは消費のみ。
            _items.Use(_humanPlayer, item);
        }

        /// <summary>
        /// ミニゲームアイテムで勝ったときに得る所持金報酬。
        /// </summary>
        private const int MiniGameRewardMoney = 500;
        // タップ連打の勝敗に使う CPU の想定タップ数レンジ（5 秒間・両端含む）。
        // 人間のタップ数がこの抽選値以上なら 1 位＝勝ちとする。
        private const int MiniGameCpuTapMin = 25;
        private const int MiniGameCpuTapMax = 40;

        /// <summary>
        /// ミニゲームアイテムの効果。遊ぶミニゲームを選ばせ（キャンセルなら消費せず終了）、
        /// 選んだミニゲームを <see cref="MiniGameLauncher"/> で起動する。勝てば所持金報酬を与える。
        /// 選択・プレイの間はスピンボタンを無効化する（使用後は自分の手番のまま通常のルーレットを回せる）。
        /// </summary>
        private async UniTaskVoid RunMiniGameAsync(CancellationToken ct)
        {
            if (_miniGameSelect == null || _launcher == null)
            {
                return;
            }

            _itemEffectRunning = true;
            if (_roulette != null)
            {
                _roulette.SetInteractable(false);
            }
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

                _items.Use(_humanPlayer, ItemId.MiniGame); // 手札からの減算は Used 購読側

                MiniGameResult result = await _launcher.PlayAsync(chosen.Value, ct);
                if (this == null)
                {
                    return;
                }

                // 勝てば所持金報酬。増額を中央の浮遊テキストで見せる（負けは報酬なし）。
                if (DetermineMiniGameWin(result))
                {
                    _money.Add(_humanPlayer, MiniGameRewardMoney);
                    _soundPlayer.PlaySafe(_soundStore?.MoneySE);
                    await _landing.ShowMoneyFloatAsync(MiniGameRewardMoney, false, 1.5f, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // シーン破棄によるキャンセルは正常終了として扱う。
            }
            finally
            {
                _itemEffectRunning = false;
                // プレイを終えても自分の手番かつ Idle のままなので、スピンを再び押せるように戻す。
                if (_roulette != null && !_model.IsFinished
                    && _turn.CurrentPlayer.CurrentValue == _humanPlayer
                    && _rouletteModel.State.CurrentValue == RouletteState.Idle)
                {
                    _roulette.SetInteractable(true);
                }
            }
        }

        /// <summary>
        /// ミニゲームの結果 <paramref name="result"/> から人間プレイヤーの勝ち（1 位）かを判定する。
        /// 2D レースは先着（スコア 1=勝ち）、タップ連打はスコア＝タップ数を CPU の想定タップ数と比べて
        /// 同数以上なら勝ち（順位づけの CPU 側は <see cref="_itemRng"/> で抽選する）。
        /// </summary>
        private bool DetermineMiniGameWin(MiniGameResult result)
        {
            switch (result.Game)
            {
                case MiniGameId.Race:
                    return result.Score == 1;
                case MiniGameId.Tap:
                default:
                    int cpuTaps = _itemRng.Next(MiniGameCpuTapMin, MiniGameCpuTapMax + 1);
                    return result.Score >= cpuTaps;
            }
        }

        /// <summary>
        /// お金よこどりの効果。自分以外の参加者（1 対 1 では CPU）それぞれの所持金の一部を
        /// <see cref="MoneyStealRule"/> でランダムに奪い、その合計を自分に足す。奪える額が無い
        /// （相手がいない・全員の所持金が 0 以下）ときは消費せず何もしない。増額は中央の浮遊テキストで見せる。
        /// 相手の所持金は UI 非表示なので相手側の演出は無い。効果はターン非消費で、演出の間はスピンを無効化する。
        /// </summary>
        private async UniTaskVoid RunMoneyStealAsync(CancellationToken ct)
        {
            if (_money == null)
            {
                return;
            }

            // 奪える相手（自分以外で所持金が正）ごとの奪取額を先に集計する。合計 0 なら消費しない。
            List<(int Player, int Amount)> steals = new();
            int total = 0;
            for (int player = 0; player < _money.PlayerCount; player++)
            {
                if (player == _humanPlayer)
                {
                    continue;
                }
                int amount = MoneyStealRule.Amount(_money.Money(player).CurrentValue, _itemRng);
                if (amount > 0)
                {
                    steals.Add((player, amount));
                    total += amount;
                }
            }

            if (total <= 0)
            {
                // 奪える相手がいない（全員 0 以下 or 相手なし）。消費しない。
                return;
            }

            _itemEffectRunning = true;
            if (_roulette != null)
            {
                _roulette.SetInteractable(false);
            }
            try
            {
                _items.Use(_humanPlayer, ItemId.StealMoney); // 手札からの減算は Used 購読側

                // 相手から引いて自分に足す（合計は保存される）。
                foreach ((int player, int amount) in steals)
                {
                    _money.Add(player, -amount);
                }
                _money.Add(_humanPlayer, total);

                _soundPlayer.PlaySafe(_soundStore?.MoneySE);
                await _landing.ShowMoneyFloatAsync(total, false, 1.5f, ct);
            }
            catch (OperationCanceledException)
            {
                // シーン破棄によるキャンセルは正常終了として扱う。
            }
            finally
            {
                _itemEffectRunning = false;
                // 演出を終えても自分の手番かつ Idle のままなので、スピンを再び押せるように戻す。
                if (_roulette != null && !_model.IsFinished
                    && _turn.CurrentPlayer.CurrentValue == _humanPlayer
                    && _rouletteModel.State.CurrentValue == RouletteState.Idle)
                {
                    _roulette.SetInteractable(true);
                }
            }
        }

        /// <summary>
        /// 陣地獲得の効果。自分以外が持つ陣地マス（未占拠＋相手占拠）から 1 つをプレイヤーに選ばせ、
        /// 選んだマスを占拠する。対象が無ければ消費せず何もしない。キャンセル・シーン破棄でも消費しない。
        /// 選択・演出の間はスピンボタンを無効化する（使用後は自分の手番のまま通常のルーレットを回せる）。
        /// </summary>
        private async UniTaskVoid RunTerritoryStealAsync(CancellationToken ct)
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

            _itemEffectRunning = true;
            if (_roulette != null)
            {
                _roulette.SetInteractable(false);
            }
            try
            {
                int chosen = await SelectTerritoryCellAsync(eligible, ct);
                if (chosen < 0)
                {
                    return; // キャンセル・破棄：消費しない
                }

                _items.Use(_humanPlayer, ItemId.StealTerritory); // 手札からの減算は Used 購読側

                // 着地時と同じ旗演出→占拠確定（上書きで奪う）→過半数なら勝者。
                Sprite flag = _flagIcons != null && _humanPlayer < _flagIcons.Length ? _flagIcons[_humanPlayer] : null;
                VisualElement targetCell = chosen < _cells.Length ? _cells[chosen] : null;
                await _landing.PlayTerritoryFlagSequenceAsync(flag, targetCell, () => ApplyTerritoryLanding(_humanPlayer, chosen), ct);
            }
            catch (OperationCanceledException)
            {
                // シーン破棄によるキャンセルは正常終了として扱う。
            }
            finally
            {
                _itemEffectRunning = false;
                // 選択・演出を終えても自分の手番かつ Idle のままなので、スピンを再び押せるように戻す。
                if (_roulette != null && !_model.IsFinished
                    && _turn.CurrentPlayer.CurrentValue == _humanPlayer
                    && _rouletteModel.State.CurrentValue == RouletteState.Idle)
                {
                    _roulette.SetInteractable(true);
                }
            }
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
