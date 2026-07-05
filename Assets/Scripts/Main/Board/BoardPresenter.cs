using System;
using System.Collections.Generic;
using System.Threading;
using Common.Character;
using Common.SoundManagement;
using Common.Store;
using Cysharp.Threading.Tasks;
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
        // 盤面データ（形・経路・イベント・見た目）。未割り当てなら下の _columns/_rows から矩形リングを生成する。
        [SerializeField] private BoardDefinition _definition;
        // _definition 未割り当て時のフォールバック用。縦画面向けに幅より高さの大きい縦長リング（列 < 行）。周回マス数は 2*列+2*行-4。
        [SerializeField] private int _columns = 5;
        [SerializeField] private int _rows = 7;
        [SerializeField] private float _stepInterval = 0.18f;
        // マスの一辺をマス中心間隔の何割にするか。1 未満にすると隣接マスの間に隙間が空き、そこを接続線でつなぐ。
        [SerializeField, Range(0.3f, 1f)] private float _cellFillRatio = 0.62f;

        private BoardModel _model;
        private SoundStore _soundStore;
        private SoundPlayer _soundPlayer;
        private MoneyModel _money;
        private CpuCharacterPicker _characterPicker;
        private PlayerNameplateView _nameplateView;

        private UIDocument _uiDocument;
        private VisualElement _boardArea;
        private VisualElement _playerHeader;
        private VisualElement[] _cells;
        private VisualElement[] _pieces;
        private Sprite[] _pieceIcons;
        private Label _clearLabel;
        private BoardDefinition _boardDef;
        private BoardLayoutCalculator _layout;
        private bool _ownsBoardDef;
        private int _cellCount;
        private int _pieceCount;
        private bool _cellsBuilt;
        private bool _cellIconLoadStarted;
        private bool _frameLoadStarted;
        private bool _piecesBuilt;
        private bool _headerBuilt;
        private bool _iconLoadStarted;
        private CancellationToken _destroyCt;
        private readonly CompositeDisposable _disposables = new();
        private readonly BoardIconLoader _iconLoader = new();
        // 全マス共通の枠オーバーレイ要素（盤面に枠画像が設定されているときだけ生成する）。
        private readonly List<VisualElement> _frames = new();

        [Inject]
        public void Construct(
            BoardModel model,
            SoundStore soundStore,
            SoundPlayer soundPlayer,
            CharacterSessionModel characterSession,
            GameParticipants participants,
            MoneyModel money)
        {
            _model = model;
            _soundStore = soundStore;
            _soundPlayer = soundPlayer;
            _money = money;
            _characterPicker = new CpuCharacterPicker(participants, characterSession);
            _nameplateView = new PlayerNameplateView(participants, money, _characterPicker, _disposables);

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

            // OnEnable が先に走っていれば、この時点でコマ・ヘッダーを構築できる。
            BuildPiecesIfReady();
            BuildPlayerHeaderIfReady();
            StartLoadingPieceIconsIfReady();
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
        }

        private void OnDestroy()
        {
            _disposables.Dispose();
            _iconLoader.Dispose();

            // フォールバックで生成した盤面データ（アセットではない）は明示的に破棄する。
            if (_ownsBoardDef && _boardDef != null)
            {
                Destroy(_boardDef);
                _boardDef = null;
            }
        }

        /// <summary>
        /// 描画に使う盤面データを解決する。アセット（<see cref="_definition"/>）が割り当てられていればそれを、
        /// 無ければ <see cref="_columns"/>/<see cref="_rows"/> から従来の矩形リングを生成して使う。
        /// </summary>
        private void ResolveDefinition()
        {
            if (_boardDef != null)
            {
                return;
            }

            if (_definition != null && _definition.CellCount > 0)
            {
                _boardDef = _definition;
                _ownsBoardDef = false;
            }
            else
            {
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

            VisualElement root = _uiDocument.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("Board の rootVisualElement が見つかりませんでした。");
                return;
            }

            _boardArea = root.Q<VisualElement>("BoardArea");
            _playerHeader = root.Q<VisualElement>("PlayerHeader");
            _clearLabel = root.Q<Label>("ClearLabel");
            if (_boardArea == null || _clearLabel == null)
            {
                Debug.LogError("Board の UI 要素が見つかりませんでした。");
                return;
            }

            ResolveDefinition();

            _cellsBuilt = true;
            _cells = new VisualElement[_cellCount];

            // マス同士をつなぐ接続線。マス・コマより先に追加して背後に描く。
            VisualElement linesElement = new();
            linesElement.AddToClassList("board-lines");
            linesElement.pickingMode = PickingMode.Ignore;
            _layout = new BoardLayoutCalculator(_boardDef, _boardArea, linesElement, _cells, _cellFillRatio);
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
            _boardArea.parent.RegisterCallback<GeometryChangedEvent>(_ => _layout.LayoutBoardArea());
            _layout.LayoutBoardArea();
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
            Sprite frame = await _iconLoader.LoadFrameAsync(_boardDef.FrameAddress, ct);
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

            int start = _model.Position(player).CurrentValue;
            bool clears = BoardMath.CompletesLap(start, steps, _cellCount);
            // 周回するときはゴール（0）でちょうど止まるよう、必要なマス数だけ進める。
            int hops = clears ? _cellCount - start : steps;

            try
            {
                for (int i = 0; i < hops; i++)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(_stepInterval), cancellationToken: ct);
                    if (this == null)
                    {
                        return;
                    }

                    int next = BoardMath.Advance(_model.Position(player).CurrentValue, 1, _cellCount);
                    _model.SetPosition(player, next); // Position 購読がコマの描画を更新する
                    _soundPlayer.PlaySafe(_soundStore?.Enter2SE);
                }

                // 勝者表示は Winner 購読が行う。
                _model.CompleteMove(player, clears);

                // ゴールで終了した手番は発動しない。止まったマスのお金イベントを反映する。
                if (!clears)
                {
                    ApplyLandingEvent(player);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        /// <summary>
        /// コマが止まったマスのイベントを発動する。現状はお金イベント（増減）のみ扱い、
        /// 進む／戻る／休み／ミニゲームは従来どおり表示のみで未発動。
        /// 変化量の判定は <see cref="CellEventResolver"/>、加算は <see cref="MoneyModel"/> が担う。
        /// </summary>
        private void ApplyLandingEvent(int player)
        {
            if (_boardDef == null || _money == null)
            {
                return;
            }

            int position = _model.Position(player).CurrentValue;
            if (position < 0 || position >= _boardDef.CellCount)
            {
                return;
            }

            BoardCellDefinition cell = _boardDef.Cell(position);
            if (!CellEventResolver.TryGetMoneyDelta(cell.Event, cell.Amount, out int delta))
            {
                return;
            }

            _money.Add(player, delta);
            _soundPlayer.PlaySafe(cell.Event == BoardCellEvent.MoneyUp ? _soundStore?.Enter3SE : _soundStore?.Cancel1SE);
        }
    }
}
