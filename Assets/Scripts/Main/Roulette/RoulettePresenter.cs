using System;
using System.Collections.Generic;
using System.Threading;
using Common.SoundManagement;
using Common.Store;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Main.Board;
using R3;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;
using Random = UnityEngine.Random;

namespace Main.Roulette
{
    /// <summary>
    /// 円盤ルーレットの UI。Painter2D で円盤を描画し、<see cref="Update"/> で角速度を加減速して回転させる。
    /// ボタンを押し続けている間は加速、離すと減速して自然に停止し、針の真下のセクターが出目になる。
    /// 状態遷移は <see cref="RouletteModel"/> が担い、出目の算出（停止角度 → セクター）はここで行う。
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

        [SerializeField] private int _sectorCount = 6;
        [Tooltip("押し始めの初速（度/秒）。一瞬のタップでも最低これだけ回る。")]
        [SerializeField] private float _minSpinSpeed = 360f;
        [Tooltip("押し続けたときの最高速（度/秒）。")]
        [SerializeField] private float _maxSpinSpeed = 1200f;
        [Tooltip("押下中の加速度（度/秒^2）。")]
        [SerializeField] private float _spinAcceleration = 2400f;
        [Tooltip("離してから止まるまでの時間（秒・下限）。速度に依らずこの時間で ease-out 減速するため、すぐ離しても長押しから離しても止まり方の印象が揃う。")]
        [SerializeField] private float _minStopDuration = 2.5f;
        [Tooltip("離してから止まるまでの時間（秒・上限）。実際の停止時間は下限〜上限でランダムに決まる。")]
        [SerializeField] private float _maxStopDuration = 3.5f;

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
        private float _angularVelocity;
        private bool _isHolding;
        // 離した瞬間の速度と、停止までの経過・目標時間。ease-out 減速に使う。
        private float _decelStartVelocity;
        private float _decelElapsed;
        private float _stopDuration;
        private Tween _pointerBounceTween;
        private Tween _wheelPulseTween;
        private Tween _resultTween;
        private bool _wheelBuilt;
        private int _highlightIndex = -1;
        // 手番制御。ゲーム進行（GameFlowController）が現在の手番プレイヤーに応じて切り替える。
        // false の間は手動スピン不可（CPU の番・自分の番でないときなど）。
        private bool _turnInteractable;

        [Inject]
        public void Construct(RouletteModel model, BoardModel board, SoundStore soundStore, SoundPlayer soundPlayer)
        {
            _model = model;
            _board = board;
            _soundStore = soundStore;
            _soundPlayer = soundPlayer;

            // OnEnable で UI 構築済みのため、ここで購読してよい（injection は OnEnable の後）。
            // DOTween.dll の AddTo 拡張と衝突するため、ここでは CompositeDisposable.Add で購読を管理する。
            // コマ移動中・クリア後は回せないため、その 2 状態を購読してボタンを更新する。
            _disposables.Add(_board.IsMoving.Subscribe(_ => UpdateSpinEnabled()));
            _disposables.Add(_board.Winner.Subscribe(_ => UpdateSpinEnabled()));
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
            // 押し続けで回す方式のため clicked ではなく PointerDown/Up を使う。
            // Button 内部の Clickable は PointerDown を処理後に StopImmediatePropagation するため、
            // バブリング段階に後から登録したハンドラは呼ばれない。Clickable より先に走るよう
            // トリクルダウン（キャプチャ）段階で登録する。ポインタ捕捉は Clickable に任せる。
            _spinButton.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            _spinButton.RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
            _spinButton.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        }

        private void OnDisable()
        {
            _isHolding = false;
            _angularVelocity = 0f;
            _pointerBounceTween?.Kill();
            _pointerBounceTween = null;
            _wheelPulseTween?.Kill();
            _wheelPulseTween = null;
            _resultTween?.Kill();
            _resultTween = null;
            if (_spinButton != null)
            {
                _spinButton.UnregisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
                _spinButton.UnregisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
                _spinButton.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            }
        }

        private void OnDestroy()
        {
            _disposables.Dispose();
        }

        private void Update()
        {
            // 回転中（押下中＋減速中）のみ角度を更新する。
            if (_model == null || _wheel == null || _model.State.CurrentValue != RouletteState.Spinning)
            {
                return;
            }

            float dt = Time.deltaTime;

            if (_isHolding)
            {
                // 押下中は加速。
                _angularVelocity = Mathf.MoveTowards(_angularVelocity, _maxSpinSpeed, _spinAcceleration * dt);
            }
            else
            {
                // 離したら、離した瞬間の速度から _stopDuration 秒かけて ease-out（終盤ほど緩やか）で 0 まで落とす。
                // 停止までの時間は速度に依存しないため、すぐ離しても長押しから離しても止まり方の印象が揃う。
                _decelElapsed += dt;
                float u = _stopDuration > 0f ? Mathf.Clamp01(_decelElapsed / _stopDuration) : 1f;
                _angularVelocity = _decelStartVelocity * (1f - u) * (1f - u);
                if (u >= 1f)
                {
                    _angularVelocity = 0f;
                }
            }

            if (_angularVelocity > 0f)
            {
                _currentRotation += _angularVelocity * dt;
                ApplyAngle(_currentRotation);
            }

            // 減速し切って速度が尽きたら停止して出目を確定する。
            if (!_isHolding && _angularVelocity <= 0f)
            {
                FinalizeSpin();
            }
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

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (!CanStartSpin())
            {
                return;
            }

            BeginSpinInternal();
            // ポインタ捕捉は Button の Clickable が行うため、ボタン外でリリースしても PointerUp は届く。
        }

        /// <summary>回転を開始する内部処理。手動（PointerDown）と CPU 自動（<see cref="AutoSpinAsync"/>）で共有する。</summary>
        private void BeginSpinInternal()
        {
            // 前回の当たり強調・祝い演出を消してから回し始める（パルス途中で再回転されても残らないように）。
            ClearHighlight();
            _wheelPulseTween?.Kill();
            _wheel.style.scale = new Scale(Vector3.one);

            _isHolding = true;
            _angularVelocity = _minSpinSpeed;
            _lastAngle = _currentRotation;
            _model.BeginSpin();
            PlaySe(_soundStore?.Enter1SE);
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            // 押下を解除すると、最低回転時間ぶんコーストしてから減速が始まる。
            ReleaseHold();
        }

        private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            // 何らかの理由で捕捉が外れた場合の保険。押下中ならコースト→減速へ移す。
            ReleaseHold();
        }

        /// <summary>
        /// 押下解除。離した瞬間の速度に関わらず <see cref="_stopDuration"/> 秒
        /// （<see cref="_minStopDuration"/>〜<see cref="_maxStopDuration"/> のランダム）かけて
        /// ease-out で減速して止める。これによりすぐ離しても長押しから離しても止まり方の印象が揃う。
        /// </summary>
        private void ReleaseHold()
        {
            if (!_isHolding)
            {
                return;
            }
            _isHolding = false;

            _decelStartVelocity = _angularVelocity;
            _decelElapsed = 0f;
            _stopDuration = Random.Range(_minStopDuration, _maxStopDuration);
        }

        private bool CanStartSpin()
        {
            return _turnInteractable
                   && _model != null
                   && _board != null
                   && _model.State.CurrentValue != RouletteState.Spinning
                   && !_board.IsMoving.CurrentValue
                   && !_board.IsFinished;
        }

        private void FinalizeSpin()
        {
            _angularVelocity = 0f;
            int value = RouletteMath.ResultFromRotation(_currentRotation, _sectorCount) + 1;
            _model.CompleteSpin(value);
            PlaySe(_soundStore?.ResultSE);
            ShowWinHighlight(value - 1);
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
                // 長押し中の高速回転も含め、セクター境界を通過するたびにティック SE を鳴らす
                // （高速時は 1 フレームに複数境界を跨ぐが、鳴るのはフレームあたり 1 回）。
                PlaySe(_soundStore?.RouletteSE);
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

        /// <summary>
        /// 手番制御。<paramref name="interactable"/> が false の間は手動スピンできない（CPU の番など）。
        /// <see cref="Turn.GameFlowController"/> が手番プレイヤーに応じて切り替える。
        /// </summary>
        public void SetInteractable(bool interactable)
        {
            _turnInteractable = interactable;
            UpdateSpinEnabled();
        }

        /// <summary>
        /// 手動スピンが停止して出目が確定するまで待ち、その出目を返す（人間の手番用）。
        /// 呼び出し前に <see cref="RouletteModel.Reset"/> 済みであることを前提に、次の Stopped を待つ。
        /// </summary>
        public async UniTask<int> WaitForManualSpinAsync(CancellationToken ct)
        {
            await _model.State.Where(state => state == RouletteState.Stopped).FirstAsync(ct);
            return _model.Result.CurrentValue;
        }

        /// <summary>
        /// CPU の手番。円盤を自動で回して自然に停止させ、その出目を返す。
        /// 手動と同じ回転物理を使うため、少しの間ホールドしてから離す。
        /// </summary>
        public async UniTask<int> AutoSpinAsync(CancellationToken ct)
        {
            BeginSpinInternal();
            float hold = Random.Range(_minStopDuration * 0.25f, _minStopDuration * 0.5f);
            await UniTask.Delay(TimeSpan.FromSeconds(hold), cancellationToken: ct);
            ReleaseHold();
            await _model.State.Where(state => state == RouletteState.Stopped).FirstAsync(ct);
            return _model.Result.CurrentValue;
        }

        private void UpdateSpinEnabled()
        {
            if (_spinButton == null || _board == null)
            {
                return;
            }

            // 回転中（Spinning）はボタンを押下したまま離す操作を受け取る必要があるため無効化しない。
            // 再回転のガードは OnPointerDown 側の状態チェックで行う。
            // 自分の手番でない（_turnInteractable == false）ときは常に無効化する。
            bool canPress = _turnInteractable && !_board.IsMoving.CurrentValue && !_board.IsFinished;
            _spinButton.SetEnabled(canPress);
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
