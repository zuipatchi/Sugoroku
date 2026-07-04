using System;
using System.Collections.Generic;
using System.Threading;
using Common.Character;
using Common.SoundManagement;
using Common.Store;
using Cysharp.Threading.Tasks;
using Main.Turn;
using R3;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UIElements;
using VContainer;

namespace Main.Board
{
    /// <summary>
    /// すごろく盤（ループ）の UI。外周にマスを並べて参加者ぶんのコマを描画し、
    /// 出目に応じてコマを 1 マスずつ移動させる。手番進行は <see cref="Turn.GameFlowController"/> が担い、
    /// 位置・状態は <see cref="BoardModel"/> が持つ。
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
        private CharacterSessionModel _characterSession;
        private GameParticipants _participants;

        private UIDocument _uiDocument;
        private VisualElement _boardArea;
        private VisualElement _playerHeader;
        private VisualElement _linesElement;
        private VisualElement[] _cells;
        private VisualElement[] _pieces;
        private Sprite[] _pieceIcons;
        private Label _clearLabel;
        private BoardDefinition _boardDef;
        private bool _ownsBoardDef;
        private int _gridColumns;
        private int _gridRows;
        private int _cellCount;
        private int _pieceCount;
        private bool _cellsBuilt;
        private bool _cellIconLoadStarted;
        private bool _piecesBuilt;
        private bool _headerBuilt;
        private bool _iconLoadStarted;
        private CharacterId? _cpuCharacter;
        private CancellationToken _destroyCt;
        private readonly CompositeDisposable _disposables = new();
        private readonly List<AsyncOperationHandle<Sprite>> _iconHandles = new();

        [Inject]
        public void Construct(
            BoardModel model,
            SoundStore soundStore,
            SoundPlayer soundPlayer,
            CharacterSessionModel characterSession,
            GameParticipants participants)
        {
            _model = model;
            _soundStore = soundStore;
            _soundPlayer = soundPlayer;
            _characterSession = characterSession;
            _participants = participants;

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
                        PlaceAtCell(_pieces[player], position);
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
                PlaySe(_soundStore?.DecisionSE);
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
            foreach (AsyncOperationHandle<Sprite> handle in _iconHandles)
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
            }
            _iconHandles.Clear();

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

            _gridColumns = _boardDef.GridColumns;
            _gridRows = _boardDef.GridRows;
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
            _linesElement = new VisualElement();
            _linesElement.AddToClassList("board-lines");
            _linesElement.pickingMode = PickingMode.Ignore;
            _linesElement.generateVisualContent += DrawConnectingLines;
            _boardArea.Add(_linesElement);

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
                PlaceAtCell(cell, i);
                _boardArea.Add(cell);
                _cells[i] = cell;
            }

            StartLoadingCellIcons();

            // リング領域をグリッドのアスペクト比に合わせて中央配置する。画面比が変わっても
            // マスが均等に並ぶよう、レイアウト確定（と以後のリサイズ）のたびに再計算する。
            _boardArea.parent.RegisterCallback<GeometryChangedEvent>(_ => LayoutBoardArea());
            LayoutBoardArea();
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
            LoadCellIconsAsync(_destroyCt).Forget();
        }

        private async UniTaskVoid LoadCellIconsAsync(CancellationToken ct)
        {
            try
            {
                for (int i = 0; i < _cellCount; i++)
                {
                    BoardCellDefinition definition = _boardDef.Cell(i);
                    if (!definition.HasIcon)
                    {
                        continue;
                    }
                    Sprite sprite = await TryLoadSpriteAsync(definition.IconAddress, ct);
                    if (sprite == null || _cells == null || i >= _cells.Length || _cells[i] == null)
                    {
                        continue;
                    }
                    _cells[i].style.backgroundImage = new StyleBackground(sprite);
                    _cells[i].AddToClassList("board-cell--icon");
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        /// <summary>
        /// 盤面リング領域を、グリッドの (列-1):(行-1) のアスペクト比を保って利用可能領域に内接させ、
        /// 中央へ配置する。%指定のままだと縦横でマス間隔が不均一になるため、ここでピクセルで整える。
        /// 上マージンを取り、右上のオプションアイコン（Common）を避ける。
        /// </summary>
        private void LayoutBoardArea()
        {
            VisualElement container = _boardArea?.parent;
            if (container == null)
            {
                return;
            }

            float containerWidth = container.resolvedStyle.width;
            float containerHeight = container.resolvedStyle.height;
            if (containerWidth <= 0f || containerHeight <= 0f)
            {
                return;
            }

            // 余白（右上のオプションアイコンは上マージンで避ける）。
            float marginX = containerWidth * 0.08f;
            float marginTop = containerHeight * 0.12f;
            float marginBottom = containerHeight * 0.08f;
            float availableWidth = containerWidth - marginX * 2f;
            float availableHeight = containerHeight - marginTop - marginBottom;

            // マス中心間隔 spacing を、マスの実寸まで含めた盤面が利用可能領域に収まるように決める。
            // 最外周マスは領域端から半マス（= spacing * _cellFillRatio / 2）ずつ外へはみ出すので、
            // 端の 1 マスぶん（両側で _cellFillRatio）を余分に見込む。これで画面端に余白が残る。
            float spanColumns = (_gridColumns - 1) + _cellFillRatio;
            float spanRows = (_gridRows - 1) + _cellFillRatio;
            float spacing = Mathf.Min(availableWidth / spanColumns, availableHeight / spanRows);

            float cellSize = spacing * _cellFillRatio;
            float areaWidth = spacing * (_gridColumns - 1);   // リング領域＝マス中心の矩形
            float areaHeight = spacing * (_gridRows - 1);
            float boundingWidth = areaWidth + cellSize;        // マスの実寸まで含めた見た目の外形
            float boundingHeight = areaHeight + cellSize;

            // 外形を余白内で中央寄せし、リング領域（マス中心の矩形）は半マス内側へ置く。
            _boardArea.style.left = (containerWidth - boundingWidth) * 0.5f + cellSize * 0.5f;
            _boardArea.style.top = marginTop + (availableHeight - boundingHeight) * 0.5f + cellSize * 0.5f;
            _boardArea.style.width = areaWidth;
            _boardArea.style.height = areaHeight;

            ResizeCells(cellSize);

            // 接続線はマス中心を結ぶので、領域サイズが変わったら再描画する。
            _linesElement?.MarkDirtyRepaint();
        }

        /// <summary>各マスの一辺を <paramref name="cellSize"/> に合わせる（マス中心間隔より小さくして隙間を作る）。</summary>
        private void ResizeCells(float cellSize)
        {
            if (_cells == null || cellSize <= 0f)
            {
                return;
            }
            foreach (VisualElement cell in _cells)
            {
                if (cell == null)
                {
                    continue;
                }
                cell.style.width = cellSize;
                cell.style.height = cellSize;
            }
        }

        /// <summary>マス中心を経路順に結ぶ接続線を描く（最後のマスからスタートへ戻ってループを閉じる）。</summary>
        private void DrawConnectingLines(MeshGenerationContext context)
        {
            if (_boardDef == null || _cellCount < 2)
            {
                return;
            }

            Rect content = context.visualElement.contentRect;
            if (content.width <= 0f || content.height <= 0f)
            {
                return;
            }

            float spacing = _gridColumns > 1 ? content.width / (_gridColumns - 1) : content.width;
            Painter2D painter = context.painter2D;
            painter.lineWidth = Mathf.Clamp(spacing * 0.1f, 2f, 8f);
            painter.strokeColor = new Color(1f, 1f, 1f, 0.35f);
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;

            painter.BeginPath();
            painter.MoveTo(CellCenterPixels(0, content));
            for (int i = 1; i < _cellCount; i++)
            {
                painter.LineTo(CellCenterPixels(i, content));
            }
            painter.LineTo(CellCenterPixels(0, content)); // ループを閉じる
            painter.Stroke();
        }

        /// <summary>マス <paramref name="index"/> の中心を、線描画領域 <paramref name="content"/> 内のピクセル座標で返す。</summary>
        private Vector2 CellCenterPixels(int index, Rect content)
        {
            Vector2Int grid = _boardDef.Cell(index).Grid;
            float x = _gridColumns > 1 ? grid.x / (float)(_gridColumns - 1) * content.width : content.width * 0.5f;
            float y = _gridRows > 1 ? grid.y / (float)(_gridRows - 1) * content.height : content.height * 0.5f;
            return new Vector2(x, y);
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
                PlaceAtCell(piece, _model.Position(player).CurrentValue);
                _boardArea.Add(piece);
                _pieces[player] = piece;

                // アイコンのロードが先に終わっていれば、この時点で貼り付ける。
                ApplyPieceIcon(player);
            }
        }

        /// <summary>
        /// 上部ヘッダーに自分（人間プレイヤー）のネームプレートだけを表示する。キャラ名は
        /// 選択中のキャラ（コマと同じ <see cref="ResolveCharacter"/>）の表示名を使う。
        /// マス（BuildCells）と injection（Construct）の両方がそろってから 1 度だけ構築する。
        /// </summary>
        private void BuildPlayerHeaderIfReady()
        {
            if (_headerBuilt || _playerHeader == null || _characterSession == null || _participants == null)
            {
                return;
            }

            _headerBuilt = true;

            for (int player = 0; player < _participants.Count; player++)
            {
                if (_participants.KindOf(player) != PlayerKind.Human)
                {
                    continue; // 相手（CPU 等）は表示しない。自分の情報だけを出す。
                }

                CharacterId id = ResolveCharacter(player);
                string characterName = CharacterCatalog.Find(id).DisplayName;

                VisualElement plate = new() { pickingMode = PickingMode.Ignore };
                plate.AddToClassList("board-nameplate");

                Label role = new("YOU") { pickingMode = PickingMode.Ignore };
                role.AddToClassList("board-nameplate__role");
                plate.Add(role);

                Label nameLabel = new(characterName) { pickingMode = PickingMode.Ignore };
                nameLabel.AddToClassList("board-nameplate__name");
                plate.Add(nameLabel);

                _playerHeader.Add(plate);
            }
        }

        /// <summary>
        /// 各プレイヤーのコマに使うキャラアイコン（バッジ）を Addressables から読み込む。
        /// コマ構築（BuildPiecesIfReady）と injection（Construct）の両方がそろってから 1 度だけ起動する。
        /// </summary>
        private void StartLoadingPieceIconsIfReady()
        {
            if (_iconLoadStarted || _model == null || _characterSession == null || _participants == null)
            {
                return;
            }

            _iconLoadStarted = true;
            _pieceIcons = new Sprite[_model.PlayerCount];
            LoadPieceIconsAsync(destroyCancellationToken).Forget();
        }

        private async UniTaskVoid LoadPieceIconsAsync(CancellationToken ct)
        {
            try
            {
                for (int player = 0; player < _pieceIcons.Length; player++)
                {
                    CharacterId id = ResolveCharacter(player);
                    string address = CharacterCatalog.Find(id).PieceIconAddress;
                    Sprite sprite = await TryLoadSpriteAsync(address, ct);
                    if (sprite == null)
                    {
                        continue; // 未配置のキャラは従来の色コマにフォールバック
                    }
                    _pieceIcons[player] = sprite;
                    ApplyPieceIcon(player);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        /// <summary>プレイヤー <paramref name="player"/> のコマに割り当てるキャラを解決する。</summary>
        private CharacterId ResolveCharacter(int player)
        {
            if (_participants.KindOf(player) == PlayerKind.Human)
            {
                return _characterSession.Selected;
            }

            // CPU は人間と違うキャラをランダムに選ぶ。1 度だけ決めてゲーム中は固定する。
            if (_cpuCharacter == null)
            {
                _cpuCharacter = PickCpuCharacter(_characterSession.Selected);
            }
            return _cpuCharacter.Value;
        }

        /// <summary>人間の選択キャラ <paramref name="human"/> を除いた残りから等確率で 1 体選ぶ。</summary>
        private static CharacterId PickCpuCharacter(CharacterId human)
        {
            IReadOnlyList<CharacterDefinition> all = CharacterCatalog.All;
            int humanIndex = CharacterCatalog.IndexOf(human);
            // オフセット 1..(Count-1) を足すことで、必ず人間と別のキャラ index になる。
            int offset = UnityEngine.Random.Range(1, all.Count);
            return all[(humanIndex + offset) % all.Count].Id;
        }

        private async UniTask<Sprite> TryLoadSpriteAsync(string address, CancellationToken ct)
        {
            AsyncOperationHandle<Sprite> handle = default;
            try
            {
                handle = Addressables.LoadAssetAsync<Sprite>(address);
                Sprite sprite = await handle.ToUniTask(cancellationToken: ct);
                _iconHandles.Add(handle);
                return sprite;
            }
            catch (OperationCanceledException)
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
                throw;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"盤面画像 '{address}' のロードに失敗。色表示にフォールバックします: {e.Message}");
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
                return null;
            }
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

        /// <summary>マス index <paramref name="index"/> の中心（%座標）に要素を配置する。</summary>
        private void PlaceAtCell(VisualElement element, int index)
        {
            if (_boardDef == null || index < 0 || index >= _boardDef.CellCount)
            {
                return;
            }
            Vector2Int grid = _boardDef.Cell(index).Grid;
            float left = _gridColumns > 1 ? grid.x / (float)(_gridColumns - 1) * 100f : 50f;
            float top = _gridRows > 1 ? grid.y / (float)(_gridRows - 1) * 100f : 50f;
            element.style.left = Length.Percent(left);
            element.style.top = Length.Percent(top);
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
                    PlaySe(_soundStore?.Enter2SE);
                }

                // 勝者表示は Winner 購読が行う。
                _model.CompleteMove(player, clears);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void PlaySe(AudioClip clip)
        {
            if (_soundPlayer != null && clip != null)
            {
                _soundPlayer.PlaySE(clip);
            }
        }
    }
}
