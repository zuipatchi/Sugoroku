using System;
using System.Collections.Generic;
using System.Threading;
using Common.Board;
using Common.Character;
using Common.SoundManagement;
using Common.Store;
using Cysharp.Threading.Tasks;
using Main.Item;
using Main.Money;
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
    /// お金イベント判定は <see cref="CellEventResolver"/> に分担し、ここでは購読・構築・移動演出を統括する。
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
        private BoardSessionModel _boardSession;
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
        // 各マスに貼った画像。着地演出（ShowCellPopupAsync）で中央に拡大表示するのに保持する。
        private Sprite[] _cellIcons;
        private VisualElement _cellPopup;
        private VisualElement _flagPopup;
        private Label _moneyFloat;
        private Label _clearLabel;
        // 取得したアイテムを並べる右下の手札コンテナ。
        private VisualElement _itemHand;
        // ロード済みアイテム絵のキャッシュ（取得マスで抽選するたびに使い回す）。
        private readonly Dictionary<ItemId, Sprite> _itemSprites = new();
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
            BoardSessionModel boardSession)
        {
            _model = model;
            _territory = territory;
            _soundStore = soundStore;
            _soundPlayer = soundPlayer;
            _money = money;
            _items = items;
            _boardSession = boardSession;
            _characterPicker = new CpuCharacterPicker(participants, characterSession);
            _nameplateView = new PlayerNameplateView(participants, money, _characterPicker, _disposables);

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
            _cellPopup = root.Q<VisualElement>("CellPopup");
            _flagPopup = root.Q<VisualElement>("FlagPopup");
            _moneyFloat = root.Q<Label>("MoneyFloat");
            _clearLabel = root.Q<Label>("ClearLabel");
            _itemHand = root.Q<VisualElement>("ItemHand");
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
                    _cellIcons[index] = sprite; // 着地演出（ShowCellPopupAsync）で流用する
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
                piece.AddToClassList(player == 0 ? "board-piece--p0" : "board-piece--p1");
                piece.pickingMode = PickingMode.Ignore;

                Label tag = new(PieceLabel(player)) { pickingMode = PickingMode.Ignore };
                tag.AddToClassList("board-piece__label");
                piece.Add(tag);

                ApplyPieceOffset(piece, player);
                _layout?.PlaceAtCell(piece, _model.Position(player).CurrentValue);
                _boardArea.Add(piece);
                _pieces[player] = piece;

                // アイコンのロードが先に終わっていれば、この時点で貼り付ける。
                ApplyPieceIcon(player);
            }
        }

        /// <summary>
        /// 上部ヘッダーに自分（人間プレイヤー）のネームプレートだけを表示する。
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
            cell.RemoveFromClassList("board-cell--owned-p0");
            cell.RemoveFromClassList("board-cell--owned-p1");

            if (owner < 0)
            {
                // 未占拠：territory 画像に戻す（ロード済みのとき）。
                if (_cellIcons != null && index < _cellIcons.Length && _cellIcons[index] != null)
                {
                    cell.style.backgroundImage = new StyleBackground(_cellIcons[index]);
                }
                return;
            }

            cell.AddToClassList(owner == 0 ? "board-cell--owned-p0" : "board-cell--owned-p1");

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

        /// <summary>複数コマが同じマスに乗っても重ならないよう、プレイヤーごとに中心をずらす。</summary>
        private void ApplyPieceOffset(VisualElement piece, int player)
        {
            if (_pieceCount <= 1)
            {
                piece.style.translate = new Translate(Length.Percent(-50f), Length.Percent(-50f));
                return;
            }

            float x = player == 0 ? -70f : -30f;
            float y = player == 0 ? -40f : -60f;
            piece.style.translate = new Translate(Length.Percent(x), Length.Percent(y));
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
            if (_boardDef != null && position >= 0 && position < _boardDef.CellCount
                && _boardDef.Cell(position).Event == BoardCellEvent.Territory)
            {
                await PlayTerritoryFlagSequenceAsync(player, position, ct);
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
            bool popupShown = await ShowCellPopupAsync(position, ct);
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
                await HideCellPopupAsync(ct);
            }
        }

        /// <summary>
        /// コマが止まったマス <paramref name="position"/> の画像を画面中央に拡大表示する（消さない）。
        /// 表示できたら true。画像が未配置（未ロード）のマスは false を返し何もしない。
        /// 消すのは呼び出し側（<see cref="HideCellPopupAsync"/> / <see cref="ShowMoneyFloatAsync"/>）。
        /// </summary>
        private async UniTask<bool> ShowCellPopupAsync(int position, CancellationToken ct)
        {
            if (_cellIcons == null || position < 0 || position >= _cellIcons.Length)
            {
                return false;
            }
            return await ShowCellPopupSpriteAsync(_cellIcons[position], ct);
        }

        /// <summary>
        /// 任意の画像 <paramref name="sprite"/> を画面中央のポップアップに拡大表示する（消さない）。
        /// マス画像とアイテム絵で共用する。画像が null なら false を返し何もしない。
        /// </summary>
        private async UniTask<bool> ShowCellPopupSpriteAsync(Sprite sprite, CancellationToken ct)
        {
            if (_cellPopup == null || sprite == null)
            {
                return false;
            }

            _cellPopup.style.backgroundImage = new StyleBackground(sprite);
            _cellPopup.RemoveFromClassList("cell-popup--visible");
            _cellPopup.style.display = DisplayStyle.Flex;

            // 次フレームまで待ってから --visible を付け、縮小→等倍の transition を効かせる。
            await UniTask.NextFrame(ct);
            _cellPopup.AddToClassList("cell-popup--visible");
            return true;
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

            bool shown = await ShowCellPopupSpriteAsync(sprite, ct);
            if (shown)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(holdSeconds), cancellationToken: ct);
            }

            _items.Add(player, item.Id);
            _soundPlayer.PlaySafe(_soundStore?.CheerSE);

            if (shown)
            {
                await HideCellPopupAsync(ct);
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

        /// <summary>取得したアイテムのサムネイルを右下の手札に 1 つ足す。絵が未ロードならアイテム名の文字で代替する。</summary>
        private void AppendItemToHand(ItemId item)
        {
            if (_itemHand == null)
            {
                return;
            }

            VisualElement el = new();
            el.AddToClassList("item-hand__card");
            el.pickingMode = PickingMode.Ignore;

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

            _itemHand.Add(el);
        }

        /// <summary>表示中のマス画像ポップアップをフェードアウトして非表示にする。既に非表示なら何もしない。</summary>
        private async UniTask HideCellPopupAsync(CancellationToken ct)
        {
            if (_cellPopup == null || _cellPopup.style.display == DisplayStyle.None)
            {
                return;
            }
            // 等倍→縮小のフェードアウト（USS transition）ぶん待ってから非表示にする。
            _cellPopup.RemoveFromClassList("cell-popup--visible");
            await UniTask.Delay(TimeSpan.FromSeconds(0.2f), cancellationToken: ct);
            _cellPopup.style.display = DisplayStyle.None;
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
                await ShowMoneyFloatAsync(delta, popupShown, floatSeconds, ct);
                return popupShown;
            }

            return false;
        }

        /// <summary>
        /// お金マスの増減額を画面中央から上へ浮かび上がらせながらフェードアウトさせる演出。
        /// <paramref name="delta"/> が正なら「+ n」を緑、負なら「- n」を赤で表示する。0 なら何もしない。
        /// <paramref name="hidePopup"/> が true なら、表示中のマス画像ポップアップを
        /// 浮遊テキストが消えるのと同じタイミングでフェードアウトさせる。
        /// </summary>
        private async UniTask ShowMoneyFloatAsync(int delta, bool hidePopup, float duration, CancellationToken ct)
        {
            if (_moneyFloat == null || delta == 0)
            {
                // 浮遊テキストが出せない場合でも、保持していた画像は消す。
                if (hidePopup)
                {
                    await HideCellPopupAsync(ct);
                }
                return;
            }

            bool up = delta > 0;
            _moneyFloat.text = up ? $"+ ${delta}" : $"- ${-delta}";
            _moneyFloat.EnableInClassList("money-float--up", up);
            _moneyFloat.EnableInClassList("money-float--down", !up);
            _moneyFloat.style.display = DisplayStyle.Flex;

            // 開始位置はポップ画像の中央やや下（中央 top:50% + 画像高さの一部）。画像が無ければ中央から。
            float startY = hidePopup && _cellPopup != null ? _cellPopup.resolvedStyle.height * 0.1f : 0f;
            const float rise = 170f;
            // 画像ポップアップのフェードアウト（USS transition）ぶん手前で消し始め、テキストと同時に消えるようにする。
            const float PopupFadeLead = 0.2f;
            bool popupHideStarted = false;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                // 前半は不透明のまま読ませ、後半でフェードアウトする。
                _moneyFloat.style.opacity = t < 0.4f ? 1f : 1f - (t - 0.4f) / 0.6f;
                // ポップ画像の底から上へ上昇する。
                _moneyFloat.style.translate = new Translate(0f, startY - rise * t);

                // 浮遊テキストが消えきるのに合わせて画像もフェードアウトを開始する。
                if (hidePopup && !popupHideStarted && elapsed >= duration - PopupFadeLead)
                {
                    popupHideStarted = true;
                    _cellPopup?.RemoveFromClassList("cell-popup--visible");
                }

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            _moneyFloat.style.display = DisplayStyle.None;
            _moneyFloat.style.opacity = 0f;
            _moneyFloat.style.translate = new Translate(0f, 0f);

            // フェードが終わった画像を非表示にする。
            if (hidePopup && _cellPopup != null)
            {
                _cellPopup.style.display = DisplayStyle.None;
            }
        }

        /// <summary>
        /// 陣地マス着地の旗演出。プレイヤーのキャラの旗を画面中央に 1 秒表示してから、
        /// 対象の陣地マスへ縮小移動して重ね、そこで占拠を確定する（占拠後そのマスは旗画像のまま）。
        /// 旗が未ロード（未配置）のときは演出をスキップして占拠だけ行う。
        /// </summary>
        private async UniTask PlayTerritoryFlagSequenceAsync(int player, int position, CancellationToken ct)
        {
            Sprite flag = _flagIcons != null && player >= 0 && player < _flagIcons.Length ? _flagIcons[player] : null;
            VisualElement root = _flagPopup?.parent;
            if (_flagPopup == null || root == null || flag == null)
            {
                ApplyTerritoryLanding(player, position);
                return;
            }

            // 表示・移動の基準になる座標（root ローカル）。
            Vector2 center = new(root.contentRect.width * 0.5f, root.contentRect.height * 0.5f);

            _flagPopup.style.backgroundImage = new StyleBackground(flag);
            SetFlagTransform(center, 0.55f, 0f);
            _flagPopup.style.display = DisplayStyle.Flex;

            // ① 中央にポップイン（拡大＋フェードイン）→ 1.0 秒ホールド。
            await AnimateFlagAsync(center, center, 0.55f, 1f, 0f, 1f, 0.15f, false, ct);
            await UniTask.Delay(TimeSpan.FromSeconds(1f), cancellationToken: ct);

            // ② 対象の陣地マス中心へ移動しつつ、マス幅に合わせて縮小する。
            Vector2 target = center;
            float targetScale = 0.3f;
            VisualElement cell = _cells != null && position >= 0 && position < _cells.Length ? _cells[position] : null;
            if (cell != null)
            {
                target = root.WorldToLocal(cell.worldBound.center);
                float cellWidth = cell.resolvedStyle.width;
                if (cellWidth > 1f)
                {
                    // 旗ポップの基準サイズは USS の 200px。マス幅に収まる倍率へ縮める。
                    targetScale = Mathf.Clamp(cellWidth / 200f, 0.1f, 1f);
                }
            }
            await AnimateFlagAsync(center, target, 1f, targetScale, 1f, 1f, 0.5f, true, ct);

            // ③ マスに重なったところで占拠を確定（マス画像が旗に替わる）→ 旗ポップをフェードアウト。
            ApplyTerritoryLanding(player, position);
            await AnimateFlagAsync(target, target, targetScale, targetScale, 1f, 0f, 0.2f, false, ct);

            _flagPopup.style.display = DisplayStyle.None;
            _flagPopup.style.opacity = 0f;
        }

        /// <summary>旗ポップの中心位置（root ローカル px）・拡大率・不透明度をまとめて設定する。</summary>
        private void SetFlagTransform(Vector2 position, float scale, float opacity)
        {
            _flagPopup.style.left = position.x;
            _flagPopup.style.top = position.y;
            _flagPopup.style.scale = new Scale(new Vector2(scale, scale));
            _flagPopup.style.opacity = opacity;
        }

        /// <summary>
        /// 旗ポップを <paramref name="duration"/> 秒かけて位置・拡大率・不透明度で補間する。
        /// <paramref name="easeInOut"/> が true なら smoothstep、false なら線形。毎フレーム駆動。
        /// </summary>
        private async UniTask AnimateFlagAsync(
            Vector2 from, Vector2 to, float scaleFrom, float scaleTo,
            float opacityFrom, float opacityTo, float duration, bool easeInOut, CancellationToken ct)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float e = easeInOut ? Mathf.SmoothStep(0f, 1f, t) : t;
                Vector2 position = Vector2.LerpUnclamped(from, to, e);
                SetFlagTransform(position, Mathf.LerpUnclamped(scaleFrom, scaleTo, e), Mathf.Lerp(opacityFrom, opacityTo, e));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            SetFlagTransform(to, scaleTo, opacityTo);
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
    }
}
