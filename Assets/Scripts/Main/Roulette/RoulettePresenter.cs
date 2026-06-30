using System.Collections.Generic;
using Common.SoundManagement;
using Common.Store;
using DG.Tweening;
using Main.Board;
using R3;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Main.Roulette
{
    /// <summary>
    /// 円盤ルーレットの UI。Painter2D で円盤を描画し、DOTween で回転させて出目を表示する。
    /// 出目の決定・状態管理は <see cref="RouletteModel"/> が担い、ここは演出と入出力に専念する。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class RoulettePresenter : MonoBehaviour
    {
        // ポップなボードゲーム調の配色。セクター色は数字ごとに HSV で虹色に振り分ける（_sectorCount に追随）。
        private static readonly Color _dividerColor = new(1f, 1f, 1f, 0.9f);
        private static readonly Color _ringColor = new(1f, 1f, 1f, 0.95f);
        private static readonly Color _winOutlineColor = new(235f / 255f, 200f / 255f, 90f / 255f);
        private static readonly Color _hubOuterColor = new(45f / 255f, 45f / 255f, 70f / 255f);
        private static readonly Color _hubInnerColor = new(245f / 255f, 245f / 255f, 250f / 255f);
        private static readonly Color _hubAccentColor = new(235f / 255f, 200f / 255f, 90f / 255f);

        // 針が低速で境界を通過するときだけティック SE を鳴らすための速度しきい値（度/フレーム）。
        private const float TickSeSpeedThreshold = 9f;

        [SerializeField] private int _sectorCount = 6;
        [SerializeField] private int _spinTurns = 5;
        [SerializeField] private float _spinDuration = 3f;

        private RouletteModel _model;
        private BoardModel _board;
        private SoundStore _soundStore;
        private SoundPlayer _soundPlayer;

        private UIDocument _uiDocument;
        private VisualElement _wheel;
        private Label _pointer;
        private Button _spinButton;
        private Label _resultLabel;
        private readonly List<Label> _numberLabels = new();
        private readonly CompositeDisposable _disposables = new();

        private float _currentRotation;
        private float _lastAngle;
        private Tween _spinTween;
        private Tween _pointerBounceTween;
        private Tween _wheelPulseTween;
        private Tween _resultTween;
        private bool _wheelBuilt;
        private int _highlightIndex = -1;

        [Inject]
        public void Construct(RouletteModel model, BoardModel board, SoundStore soundStore, SoundPlayer soundPlayer)
        {
            _model = model;
            _board = board;
            _soundStore = soundStore;
            _soundPlayer = soundPlayer;

            // OnEnable で UI 構築済みのため、ここで購読してよい（injection は OnEnable の後）。
            // DOTween.dll の AddTo 拡張と衝突するため、ここでは CompositeDisposable.Add で購読を管理する。
            // 回転中・コマ移動中・クリア後はいずれも回せないため、3 つの状態を購読してボタンを更新する。
            _disposables.Add(_model.State.Subscribe(_ => UpdateSpinEnabled()));
            _disposables.Add(_board.IsMoving.Subscribe(_ => UpdateSpinEnabled()));
            _disposables.Add(_board.IsCleared.Subscribe(_ => UpdateSpinEnabled()));
            _disposables.Add(_model.Result.Subscribe(value =>
            {
                if (_resultLabel != null && value > 0)
                {
                    _resultLabel.text = $"出た目: {value}";
                    PlayResultLabelPop();
                }
            }));
        }

        private void Awake()
        {
            _uiDocument = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            VisualElement root = _uiDocument.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("Roulette の rootVisualElement が見つかりませんでした。");
                return;
            }

            _wheel = root.Q<VisualElement>("Wheel");
            _pointer = root.Q<Label>("Pointer");
            _spinButton = root.Q<Button>("SpinButton");
            _resultLabel = root.Q<Label>("ResultLabel");
            if (_wheel == null || _pointer == null || _spinButton == null || _resultLabel == null)
            {
                Debug.LogError("Roulette の UI 要素が見つかりませんでした。");
                return;
            }

            BuildWheel();
            _spinButton.clicked += OnSpinClicked;
        }

        private void OnDisable()
        {
            _spinTween?.Kill();
            _spinTween = null;
            _pointerBounceTween?.Kill();
            _pointerBounceTween = null;
            _wheelPulseTween?.Kill();
            _wheelPulseTween = null;
            _resultTween?.Kill();
            _resultTween = null;
            if (_spinButton != null)
            {
                _spinButton.clicked -= OnSpinClicked;
            }
        }

        private void OnDestroy()
        {
            _disposables.Dispose();
        }

        private void BuildWheel()
        {
            // 再有効化時の二重登録を防ぐため一度だけ構築する。
            if (_wheelBuilt)
            {
                return;
            }
            _wheelBuilt = true;

            _wheel.generateVisualContent += DrawWheel;

            _numberLabels.Clear();
            for (int i = 0; i < _sectorCount; i++)
            {
                Label label = new() { text = (i + 1).ToString() };
                label.AddToClassList("roulette-number");
                label.pickingMode = PickingMode.Ignore;
                _wheel.Add(label);
                _numberLabels.Add(label);
            }

            // レイアウト確定後に数字ラベルを配置する。
            _wheel.RegisterCallback<GeometryChangedEvent>(_ => PositionNumberLabels());
        }

        private void PositionNumberLabels()
        {
            float width = _wheel.resolvedStyle.width;
            float height = _wheel.resolvedStyle.height;
            if (width <= 0f || height <= 0f)
            {
                return;
            }

            Vector2 center = new(width * 0.5f, height * 0.5f);
            float radius = Mathf.Min(width, height) * 0.5f;
            float labelRadius = radius * 0.66f;
            float sector = RouletteMath.SectorAngle(_sectorCount);

            for (int i = 0; i < _numberLabels.Count; i++)
            {
                Label label = _numberLabels[i];
                float angleFromTop = (i + 0.5f) * sector * Mathf.Deg2Rad;
                // 上方向を基準に時計回り（画面は y 軸下向き）。
                Vector2 pos = center + new Vector2(Mathf.Sin(angleFromTop), -Mathf.Cos(angleFromTop)) * labelRadius;
                // ラベル自身のサイズに依存せず中心に合わせる（USS の translate: -50% -50% と併用）。
                label.style.left = pos.x;
                label.style.top = pos.y;
            }
        }

        private void DrawWheel(MeshGenerationContext mgc)
        {
            Rect rect = mgc.visualElement.contentRect;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            Painter2D painter = mgc.painter2D;
            Vector2 center = rect.center;
            float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
            float sector = RouletteMath.SectorAngle(_sectorCount);

            // 1) セクター（数字ごとに虹色。当たりセクターは彩度・明度を上げて強調）。
            for (int i = 0; i < _sectorCount; i++)
            {
                float startFromTop = i * sector;
                float endFromTop = (i + 1) * sector;
                int steps = Mathf.Max(2, Mathf.CeilToInt(sector / 5f));

                painter.fillColor = SectorColor(i, _sectorCount, i == _highlightIndex);
                painter.BeginPath();
                painter.MoveTo(center);
                for (int s = 0; s <= steps; s++)
                {
                    float a = Mathf.Lerp(startFromTop, endFromTop, s / (float)steps) * Mathf.Deg2Rad;
                    Vector2 edge = center + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * radius;
                    painter.LineTo(edge);
                }
                painter.ClosePath();
                painter.Fill();
            }

            // 2) セクター境界の放射状の区切り線。
            painter.strokeColor = _dividerColor;
            painter.lineWidth = 5f;
            for (int i = 0; i < _sectorCount; i++)
            {
                float a = i * sector * Mathf.Deg2Rad;
                Vector2 edge = center + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * radius;
                painter.BeginPath();
                painter.MoveTo(center);
                painter.LineTo(edge);
                painter.Stroke();
            }

            // 3) 外周のリング。
            painter.strokeColor = _ringColor;
            painter.lineWidth = 8f;
            StrokeArc(painter, center, radius - 4f, 0f, 360f);

            // 4) 当たりセクターの強調アウトライン（ゴールドの太い縁取り）。
            if (_highlightIndex >= 0 && _highlightIndex < _sectorCount)
            {
                float start = _highlightIndex * sector;
                float end = (_highlightIndex + 1) * sector;
                painter.strokeColor = _winOutlineColor;
                painter.lineWidth = 6f;
                painter.BeginPath();
                painter.MoveTo(center);
                int steps = Mathf.Max(2, Mathf.CeilToInt(sector / 5f));
                for (int s = 0; s <= steps; s++)
                {
                    float a = Mathf.Lerp(start, end, s / (float)steps) * Mathf.Deg2Rad;
                    Vector2 edge = center + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * (radius - 3f);
                    painter.LineTo(edge);
                }
                painter.ClosePath();
                painter.Stroke();
            }

            // 5) 中心ハブ（軸キャップ）。暗→明→ゴールドの三重円で立体感を出す。
            float hubR = radius * 0.16f;
            painter.fillColor = _hubOuterColor;
            FillCircle(painter, center, hubR);
            painter.fillColor = _hubInnerColor;
            FillCircle(painter, center, hubR * 0.72f);
            painter.fillColor = _hubAccentColor;
            FillCircle(painter, center, hubR * 0.34f);
        }

        private static Color SectorColor(int index, int count, bool highlight)
        {
            float hue = (count <= 0) ? 0f : (float)index / count;
            float saturation = highlight ? 0.85f : 0.6f;
            float value = highlight ? 1f : 0.86f;
            return Color.HSVToRGB(hue, saturation, value);
        }

        private static void StrokeArc(Painter2D painter, Vector2 center, float radius, float startDeg, float endDeg)
        {
            int steps = 72;
            painter.BeginPath();
            for (int s = 0; s <= steps; s++)
            {
                float a = Mathf.Lerp(startDeg, endDeg, s / (float)steps) * Mathf.Deg2Rad;
                Vector2 p = center + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * radius;
                if (s == 0)
                {
                    painter.MoveTo(p);
                }
                else
                {
                    painter.LineTo(p);
                }
            }
            painter.Stroke();
        }

        private static void FillCircle(Painter2D painter, Vector2 center, float radius)
        {
            int steps = 40;
            painter.BeginPath();
            for (int s = 0; s <= steps; s++)
            {
                float a = (s / (float)steps) * 360f * Mathf.Deg2Rad;
                Vector2 p = center + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * radius;
                if (s == 0)
                {
                    painter.MoveTo(p);
                }
                else
                {
                    painter.LineTo(p);
                }
            }
            painter.ClosePath();
            painter.Fill();
        }

        private void OnSpinClicked()
        {
            if (_model == null || _model.State.CurrentValue == RouletteState.Spinning)
            {
                return;
            }
            if (_board != null && (_board.IsMoving.CurrentValue || _board.IsCleared.CurrentValue))
            {
                return;
            }

            // 前回の当たり強調・祝い演出を消してから回し始める（パルス途中で再回転されても残らないように）。
            ClearHighlight();
            _wheelPulseTween?.Kill();
            _wheel.style.scale = new Scale(Vector3.one);

            int value = _model.BeginSpin(_sectorCount);
            PlaySe(_soundStore?.Enter1SE);

            float target = RouletteMath.NextRotation(_currentRotation, value - 1, _sectorCount, _spinTurns);

            _spinTween?.Kill();
            _lastAngle = _currentRotation;
            float angle = _currentRotation;

            // 本回転（強い減速）でぴたりと止める。
            _spinTween = DOTween.To(() => angle, v => { angle = v; ApplyAngle(v); }, target, _spinDuration)
                .SetEase(Ease.OutQuint)
                .OnComplete(() =>
                {
                    _currentRotation = target;
                    _model.CompleteSpin(value);
                    PlaySe(_soundStore?.ResultSE);
                    ShowWinHighlight(value - 1);
                });
        }

        /// <summary>円盤の回転を反映しつつ、針の真下を境界が通過したらティック演出を出す。</summary>
        private void ApplyAngle(float v)
        {
            _wheel.style.rotate = new Rotate(new Angle(v, AngleUnit.Degree));

            float sector = RouletteMath.SectorAngle(_sectorCount);
            int prevBucket = Mathf.FloorToInt(_lastAngle / sector);
            int curBucket = Mathf.FloorToInt(v / sector);
            if (curBucket != prevBucket)
            {
                BouncePointer();
                // 低速時のみティック SE を鳴らす（高速な序盤は連射になるため抑制）。
                if (Mathf.Abs(v - _lastAngle) < TickSeSpeedThreshold)
                {
                    PlaySe(_soundStore?.Enter2SE);
                }
            }
            _lastAngle = v;
        }

        /// <summary>針が「カチッ」と弾む演出（縦方向にスカッシュして戻す）。</summary>
        private void BouncePointer()
        {
            if (_pointer == null)
            {
                return;
            }
            _pointerBounceTween?.Kill();
            float squash = 1.45f;
            _pointer.style.scale = new Scale(new Vector3(1f, squash, 1f));
            _pointerBounceTween = DOTween.To(
                    () => squash,
                    s =>
                    {
                        squash = s;
                        _pointer.style.scale = new Scale(new Vector3(1f, s, 1f));
                    },
                    1f,
                    0.12f)
                .SetEase(Ease.OutBack);
        }

        private void ShowWinHighlight(int index)
        {
            _highlightIndex = index;
            _wheel.MarkDirtyRepaint();

            // 当たりの瞬間に円盤をひと突き拡大して祝う。
            _wheelPulseTween?.Kill();
            float pulse = 1.06f;
            _wheel.style.scale = new Scale(new Vector3(pulse, pulse, 1f));
            _wheelPulseTween = DOTween.To(
                    () => pulse,
                    p =>
                    {
                        pulse = p;
                        _wheel.style.scale = new Scale(new Vector3(p, p, 1f));
                    },
                    1f,
                    0.35f)
                .SetEase(Ease.OutBack);
        }

        private void ClearHighlight()
        {
            if (_highlightIndex < 0)
            {
                return;
            }
            _highlightIndex = -1;
            _wheel.MarkDirtyRepaint();
        }

        private void PlayResultLabelPop()
        {
            if (_resultLabel == null)
            {
                return;
            }
            _resultTween?.Kill();
            float s = 0.5f;
            _resultLabel.style.scale = new Scale(new Vector3(s, s, 1f));
            _resultTween = DOTween.To(
                    () => s,
                    v =>
                    {
                        s = v;
                        _resultLabel.style.scale = new Scale(new Vector3(v, v, 1f));
                    },
                    1f,
                    0.4f)
                .SetEase(Ease.OutBack);
        }

        private void UpdateSpinEnabled()
        {
            if (_spinButton == null || _model == null || _board == null)
            {
                return;
            }

            bool canSpin = _model.State.CurrentValue != RouletteState.Spinning
                           && !_board.IsMoving.CurrentValue
                           && !_board.IsCleared.CurrentValue;
            _spinButton.SetEnabled(canSpin);
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
