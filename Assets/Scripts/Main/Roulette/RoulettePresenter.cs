using System.Collections.Generic;
using Common.SoundManagement;
using Common.Store;
using DG.Tweening;
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
        private static readonly Color _sectorColorA = new(70f / 255f, 90f / 255f, 180f / 255f);
        private static readonly Color _sectorColorB = new(40f / 255f, 50f / 255f, 110f / 255f);
        private static readonly Color _ringColor = new(1f, 1f, 1f, 0.85f);

        [SerializeField] private int _sectorCount = 6;
        [SerializeField] private int _spinTurns = 5;
        [SerializeField] private float _spinDuration = 3f;

        private RouletteModel _model;
        private SoundStore _soundStore;
        private SoundPlayer _soundPlayer;

        private UIDocument _uiDocument;
        private VisualElement _wheel;
        private Button _spinButton;
        private Label _resultLabel;
        private readonly List<Label> _numberLabels = new();
        private readonly CompositeDisposable _disposables = new();

        private float _currentRotation;
        private Tween _spinTween;
        private bool _wheelBuilt;

        [Inject]
        public void Construct(RouletteModel model, SoundStore soundStore, SoundPlayer soundPlayer)
        {
            _model = model;
            _soundStore = soundStore;
            _soundPlayer = soundPlayer;

            // OnEnable で UI 構築済みのため、ここで購読してよい（injection は OnEnable の後）。
            // DOTween.dll の AddTo 拡張と衝突するため、ここでは CompositeDisposable.Add で購読を管理する。
            _disposables.Add(_model.State.Subscribe(ApplyState));
            _disposables.Add(_model.Result.Subscribe(value =>
            {
                if (_resultLabel != null && value > 0)
                {
                    _resultLabel.text = $"出た目: {value}";
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
            _spinButton = root.Q<Button>("SpinButton");
            _resultLabel = root.Q<Label>("ResultLabel");
            if (_wheel == null || _spinButton == null || _resultLabel == null)
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

            for (int i = 0; i < _sectorCount; i++)
            {
                float startFromTop = i * sector;
                float endFromTop = (i + 1) * sector;
                int steps = Mathf.Max(2, Mathf.CeilToInt(sector / 5f));

                painter.fillColor = (i % 2 == 0) ? _sectorColorA : _sectorColorB;
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

            // 外周のリング
            painter.strokeColor = _ringColor;
            painter.lineWidth = 4f;
            painter.BeginPath();
            int ringSteps = 72;
            for (int s = 0; s <= ringSteps; s++)
            {
                float a = (s / (float)ringSteps) * 360f * Mathf.Deg2Rad;
                Vector2 p = center + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * (radius - 2f);
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

        private void OnSpinClicked()
        {
            if (_model == null || _model.State.CurrentValue == RouletteState.Spinning)
            {
                return;
            }

            int value = _model.BeginSpin(_sectorCount);
            PlaySe(_soundStore?.Enter1SE);

            float target = RouletteMath.NextRotation(_currentRotation, value - 1, _sectorCount, _spinTurns);

            _spinTween?.Kill();
            float angle = _currentRotation;
            _spinTween = DOTween.To(
                    () => angle,
                    v =>
                    {
                        angle = v;
                        _wheel.style.rotate = new Rotate(new Angle(v, AngleUnit.Degree));
                    },
                    target,
                    _spinDuration)
                .SetEase(Ease.OutCubic)
                .OnComplete(() =>
                {
                    _currentRotation = target;
                    _model.CompleteSpin(value);
                    PlaySe(_soundStore?.ResultSE);
                });
        }

        private void ApplyState(RouletteState state)
        {
            _spinButton?.SetEnabled(state != RouletteState.Spinning);
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
