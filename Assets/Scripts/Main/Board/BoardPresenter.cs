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
        [SerializeField] private int _columns = 6;
        [SerializeField] private int _rows = 5;
        [SerializeField] private float _stepInterval = 0.18f;

        private BoardModel _model;
        private SoundStore _soundStore;
        private SoundPlayer _soundPlayer;
        private CharacterSessionModel _characterSession;
        private GameParticipants _participants;

        private UIDocument _uiDocument;
        private VisualElement _boardArea;
        private VisualElement[] _pieces;
        private Sprite[] _pieceIcons;
        private Label _clearLabel;
        private int _cellCount;
        private int _pieceCount;
        private bool _cellsBuilt;
        private bool _piecesBuilt;
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
                PlaySe(_soundStore?.ResultSE);
            }));

            // OnEnable が先に走っていれば、この時点でコマを構築できる。
            BuildPiecesIfReady();
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
            _clearLabel = root.Q<Label>("ClearLabel");
            if (_boardArea == null || _clearLabel == null)
            {
                Debug.LogError("Board の UI 要素が見つかりませんでした。");
                return;
            }

            _cellsBuilt = true;
            _cellCount = BoardMath.PerimeterCellCount(_columns, _rows);

            for (int i = 0; i < _cellCount; i++)
            {
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
                PlaceAtCell(cell, i);
                _boardArea.Add(cell);
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
                PlaceAtCell(piece, _model.Position(player).CurrentValue);
                _boardArea.Add(piece);
                _pieces[player] = piece;

                // アイコンのロードが先に終わっていれば、この時点で貼り付ける。
                ApplyPieceIcon(player);
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
                    Sprite sprite = await TryLoadPieceIconAsync(address, ct);
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

        private async UniTask<Sprite> TryLoadPieceIconAsync(string address, CancellationToken ct)
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
                Debug.LogWarning($"コマ画像 '{address}' のロードに失敗。色コマ表示にします: {e.Message}");
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
            (int column, int row) = BoardMath.CellGridPosition(index, _columns, _rows);
            float left = _columns > 1 ? column / (float)(_columns - 1) * 100f : 50f;
            float top = _rows > 1 ? row / (float)(_rows - 1) * 100f : 50f;
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
