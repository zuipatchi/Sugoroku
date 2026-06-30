using System;
using System.Threading;
using Common.SoundManagement;
using Common.Store;
using Cysharp.Threading.Tasks;
using Main.Roulette;
using R3;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Main.Board
{
    /// <summary>
    /// すごろく盤（ループ）の UI。外周にマスを並べてコマを描画し、
    /// ルーレットの出目に応じてコマを 1 マスずつ移動させる。
    /// 位置・状態は <see cref="BoardModel"/>、出目は <see cref="RouletteModel"/> が持つ。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class BoardPresenter : MonoBehaviour
    {
        [SerializeField] private int _columns = 6;
        [SerializeField] private int _rows = 5;
        [SerializeField] private float _stepInterval = 0.18f;

        private BoardModel _model;
        private RouletteModel _roulette;
        private SoundStore _soundStore;
        private SoundPlayer _soundPlayer;

        private UIDocument _uiDocument;
        private VisualElement _boardArea;
        private VisualElement _piece;
        private Label _clearLabel;
        private int _cellCount;
        private bool _built;
        private CancellationToken _destroyCt;
        private readonly CompositeDisposable _disposables = new();

        [Inject]
        public void Construct(BoardModel model, RouletteModel roulette, SoundStore soundStore, SoundPlayer soundPlayer)
        {
            _model = model;
            _roulette = roulette;
            _soundStore = soundStore;
            _soundPlayer = soundPlayer;

            // ルーレットが停止して出目が確定したらコマを動かす。
            // DOTween.dll の AddTo 拡張と衝突しないよう CompositeDisposable.Add で管理する。
            _disposables.Add(_roulette.State.Subscribe(state =>
            {
                if (state == RouletteState.Stopped)
                {
                    AdvanceAsync(_roulette.Result.CurrentValue).Forget();
                }
            }));

            // コマ位置は Model を source of truth とし、Position を購読して描画へ反映する
            // （BuildBoard は OnEnable=injection 前に走るため、ここで購読する）。
            _disposables.Add(_model.Position.Subscribe(position =>
            {
                if (_piece != null)
                {
                    PlaceAtCell(_piece, position);
                }
            }));
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
            BuildBoard();
        }

        private void OnDestroy()
        {
            _disposables.Dispose();
        }

        private void BuildBoard()
        {
            if (_built)
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

            _built = true;
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

            _piece = new VisualElement();
            _piece.AddToClassList("board-piece");
            _piece.pickingMode = PickingMode.Ignore;
            // 初期位置はスタート（0）。以降は Position 購読（Construct）で更新する。
            PlaceAtCell(_piece, 0);
            _boardArea.Add(_piece);
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
        /// コマを <paramref name="steps"/> マス進める。ルーレットの出目とミニゲームのボーナスの
        /// 両方から呼ばれる共通の移動演出。移動中・クリア後や 0 以下の歩数は無視する。
        /// <paramref name="externalCt"/> は呼び出し元のキャンセル（Destroy 等）を連結するためのもの。
        /// </summary>
        public async UniTask AdvanceAsync(int steps, CancellationToken externalCt = default)
        {
            if (_model.IsMoving.CurrentValue || _model.IsCleared.CurrentValue)
            {
                return;
            }

            if (steps <= 0 || _piece == null)
            {
                return;
            }

            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(_destroyCt, externalCt);
            CancellationToken ct = linked.Token;
            _model.BeginMove();

            int start = _model.Position.CurrentValue;
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

                    int next = BoardMath.Advance(_model.Position.CurrentValue, 1, _cellCount);
                    _model.SetPosition(next); // Position 購読がコマの描画を更新する
                    PlaySe(_soundStore?.Enter2SE);
                }

                _model.CompleteMove(clears);
                if (clears)
                {
                    _clearLabel.text = "ゴール！";
                    PlaySe(_soundStore?.ResultSE);
                }
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
