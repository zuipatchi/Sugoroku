using System;
using System.Collections.Generic;
using System.Threading;
using Common.Character;
using Common.SoundManagement;
using Common.Store;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Main.Board;
using R3;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UIElements;
using VContainer;

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
        // キャラアイコンの下地（コイン風）。白い座面をゴールドのリングで縁取り、虹色セクター上でアバターを浮き立たせる。
        private static readonly Color _coinBaseColor = new(250f / 255f, 250f / 255f, 252f / 255f);
        private static readonly Color _coinRingColor = new(235f / 255f, 200f / 255f, 90f / 255f);

        // 針が低速で境界を通過するときだけティック SE を鳴らすための速度しきい値（度/フレーム）。
        private const float TickSeSpeedThreshold = 9f;

        // セクター中心線上でのアバター配置半径（円盤半径に対する比）。数字はアバターの子バッジなので独立配置は不要。
        private const float IconRadiusFactor = 0.62f;
        // 隣のコインと重ならないよう、コイン直径を隣接中心間距離（弦長）のこの割合までに収める。
        private const float CoinChordFillRatio = 0.88f;
        // セクター数が少ないときにコインが大きくなりすぎないよう、直径を円盤半径のこの割合で頭打ちにする。
        private const float CoinDiameterCapRatio = 0.62f;
        // 白座面の外に出すゴールドリングの太さ（px）。
        private const float CoinRingWidth = 3f;
        // アバター画像はコイン白座面のさらに内側に収める割合。
        private const float AvatarInsetRatio = 0.84f;

        [SerializeField] private int _sectorCount = 8;
        [Tooltip("押し始めの初速（度/秒）。一瞬のタップでも最低これだけ回る。")]
        [SerializeField] private float _minSpinSpeed = 360f;
        [Tooltip("押し続けたときの最高速（度/秒）。")]
        [SerializeField] private float _maxSpinSpeed = 1200f;
        [Tooltip("押下中の加速度（度/秒^2）。")]
        [SerializeField] private float _spinAcceleration = 2400f;
        [Tooltip("離した後の減速度（度/秒^2）。大きいほど早く止まる。")]
        [SerializeField] private float _spinDeceleration = 720f;
        [Tooltip("一瞬のタップでも最低この秒数は回す（下限）。実際の回転時間は下限〜上限でランダムに決まる。")]
        [SerializeField] private float _minSpinDuration = 1.5f;
        [Tooltip("最低回転時間の上限（秒）。")]
        [SerializeField] private float _maxSpinDuration = 2.5f;

        private RouletteModel _model;
        private BoardModel _board;
        private SoundStore _soundStore;
        private SoundPlayer _soundPlayer;

        private UIDocument _uiDocument;
        private VisualElement _wheel;
        private Label _pointer;
        private Button _spinButton;
        private Label _resultLabel;
        // 各セクターに貼るキャラアイコン（コイン）。円盤の子として一緒に周回し、逆回転で正立を保つ。数字は各アイコンの子バッジ。
        private readonly List<VisualElement> _characterIcons = new();
        private readonly List<AsyncOperationHandle<Sprite>> _iconHandles = new();
        private readonly CompositeDisposable _disposables = new();

        private float _currentRotation;
        private float _lastAngle;
        private float _angularVelocity;
        private bool _isHolding;
        private float _spinElapsed;
        private float _targetSpinDuration;
        private float _coastUntilElapsed = float.PositiveInfinity;
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
            // コマ移動中・クリア後は回せないため、その 2 状態を購読してボタンを更新する。
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
            foreach (AsyncOperationHandle<Sprite> handle in _iconHandles)
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
            }
            _iconHandles.Clear();
        }

        private void Update()
        {
            // 回転中（押下中＋減速中）のみ角度を更新する。
            if (_model == null || _wheel == null || _model.State.CurrentValue != RouletteState.Spinning)
            {
                return;
            }

            float dt = Time.deltaTime;
            _spinElapsed += dt;

            if (_isHolding)
            {
                // 押下中は加速。
                _angularVelocity = Mathf.MoveTowards(_angularVelocity, _maxSpinSpeed, _spinAcceleration * dt);
            }
            else if (_spinElapsed >= _coastUntilElapsed)
            {
                // 最低回転時間ぶんのコーストを終えたら減速して止める。
                _angularVelocity = Mathf.MoveTowards(_angularVelocity, 0f, _spinDeceleration * dt);
            }
            // それ以外（離した直後〜減速開始まで）は等速でコーストし、すぐには止めない。

            if (_angularVelocity > 0f)
            {
                _currentRotation += _angularVelocity * dt;
                ApplyAngle(_currentRotation);
            }

            // 減速フェーズに入って速度が尽きたら停止して出目を確定する。
            if (!_isHolding && _spinElapsed >= _coastUntilElapsed && Mathf.Approximately(_angularVelocity, 0f))
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

            _characterIcons.Clear();
            for (int i = 0; i < _sectorCount; i++)
            {
                // セクターごとのキャラアイコン（コイン）。画像ロード前はプレースホルダ色。
                VisualElement icon = new() { pickingMode = PickingMode.Ignore };
                icon.AddToClassList("roulette-character");
                icon.style.backgroundColor = PlaceholderColor(i, _sectorCount);
                _wheel.Add(icon);
                _characterIcons.Add(icon);

                // 出目の数字はアイコンの子にして、アイコンの正立・周回にそのまま追従させる。
                // USS で下部中央のバッジとして配置する（コード側の位置・逆回転は不要）。
                Label label = new() { text = (i + 1).ToString() };
                label.AddToClassList("roulette-number");
                label.pickingMode = PickingMode.Ignore;
                icon.Add(label);
            }

            // レイアウト確定後にアイコン（コイン）を配置する。数字は子バッジなので一緒に付いてくる。
            _wheel.RegisterCallback<GeometryChangedEvent>(_ => PositionSectorContents());
            // キャラ画像は非同期ロード。破棄・遷移で自然に止まるよう destroyCancellationToken を渡す。
            LoadCharacterIconsAsync(destroyCancellationToken).Forget();
        }

        // 各セクターのアイコンにキャラ画像を貼る。未配置・失敗はプレースホルダ色のまま残す。
        private async UniTaskVoid LoadCharacterIconsAsync(CancellationToken ct)
        {
            for (int i = 0; i < _characterIcons.Count; i++)
            {
                CharacterId id = RouletteMath.CharacterForSector(i);
                CharacterDefinition definition = CharacterCatalog.Find(id);
                Sprite icon = await TryLoadIconAsync(definition.IconAddress, ct);
                if (icon != null)
                {
                    _characterIcons[i].style.backgroundImage = new StyleBackground(icon);
                    // 画像を貼れたらプレースホルダ色は消す（透過部分に色が透けないように）。
                    _characterIcons[i].style.backgroundColor = new StyleColor(Color.clear);
                }
            }
        }

        private async UniTask<Sprite> TryLoadIconAsync(string address, CancellationToken ct)
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
                Debug.LogWarning($"ルーレットのキャラ画像 '{address}' のロードに失敗。プレースホルダ表示にします: {e.Message}");
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
                return null;
            }
        }

        private static Color PlaceholderColor(int index, int count)
        {
            float hue = (count <= 0) ? 0f : (float)index / count;
            return Color.HSVToRGB(hue, 0.45f, 0.65f);
        }

        // 隣り合うコインが重ならないコイン直径（px）を、円盤半径とセクター数から求める。
        // 隣接するアイコン中心間の弦長 = 2r·sin(π/n)。その一定割合をコイン直径とし、少数セクターでは上限で頭打ちにする。
        private float CoinDiameter(float wheelRadius)
        {
            float iconRadius = wheelRadius * IconRadiusFactor;
            float chord = 2f * iconRadius * Mathf.Sin(Mathf.PI / _sectorCount);
            return Mathf.Min(chord * CoinChordFillRatio, wheelRadius * CoinDiameterCapRatio);
        }

        // キャラアイコン（コイン）をセクター中心線上に配置し、セクター数に応じたサイズに整える。
        // 数字はアイコンの子バッジなので一緒に付いてくる。
        private void PositionSectorContents()
        {
            float width = _wheel.resolvedStyle.width;
            float height = _wheel.resolvedStyle.height;
            if (width <= 0f || height <= 0f)
            {
                return;
            }

            Vector2 center = new(width * 0.5f, height * 0.5f);
            float radius = Mathf.Min(width, height) * 0.5f;
            float iconRadius = radius * IconRadiusFactor;
            float sector = RouletteMath.SectorAngle(_sectorCount);
            // アバター画像はコイン白座面の内側に収める。コインの縁取り（ゴールド＋白）は Painter2D 側で描く。
            float avatarSize = CoinDiameter(radius) * AvatarInsetRatio;

            for (int i = 0; i < _characterIcons.Count; i++)
            {
                float angleFromTop = (i + 0.5f) * sector * Mathf.Deg2Rad;
                // 上方向を基準に時計回り（画面は y 軸下向き）。
                Vector2 dir = new(Mathf.Sin(angleFromTop), -Mathf.Cos(angleFromTop));
                Vector2 iconPos = center + dir * iconRadius;

                VisualElement icon = _characterIcons[i];
                icon.style.width = avatarSize;
                icon.style.height = avatarSize;
                SetCircleRadius(icon, avatarSize * 0.5f);
                // 要素自身のサイズに依存せず中心に合わせる（USS の translate: -50% -50% と併用）。
                icon.style.left = iconPos.x;
                icon.style.top = iconPos.y;
            }

            // 停止中も現在の回転ぶん逆回転させて、アイコンの顔（と子の数字）を正立させておく。
            CounterRotateCharacterIcons(_currentRotation);
        }

        // 4 隅の border-radius をまとめて設定して円形にする。
        private static void SetCircleRadius(VisualElement element, float r)
        {
            element.style.borderTopLeftRadius = r;
            element.style.borderTopRightRadius = r;
            element.style.borderBottomLeftRadius = r;
            element.style.borderBottomRightRadius = r;
        }

        // 円盤の回転 v を打ち消す逆回転をアイコンに与え、周回しても傾かない（常に正立する）ようにする。
        // 数字はアイコンの子なので、この逆回転にそのまま追従する（別途回す必要はない）。
        private void CounterRotateCharacterIcons(float v)
        {
            Rotate counter = new(new Angle(-v, AngleUnit.Degree));
            for (int i = 0; i < _characterIcons.Count; i++)
            {
                _characterIcons[i].style.rotate = counter;
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

            // 5) キャラアイコンの下地コイン（ゴールドのリング → 白い座面）。アイコン要素はこの上（子要素）に描画される。
            //    円形なので円盤が回転しても見た目は変わらない（周回はするが傾かない）。数字バッジはアイコン側（USS）で描く。
            float iconRadius = radius * IconRadiusFactor;
            float coinRingRadius = CoinDiameter(radius) * 0.5f;
            float coinBaseRadius = coinRingRadius - CoinRingWidth;
            for (int i = 0; i < _sectorCount; i++)
            {
                float angleFromTop = (i + 0.5f) * sector * Mathf.Deg2Rad;
                Vector2 dir = new(Mathf.Sin(angleFromTop), -Mathf.Cos(angleFromTop));
                Vector2 iconCenter = center + dir * iconRadius;
                painter.fillColor = _coinRingColor;
                FillCircle(painter, iconCenter, coinRingRadius);
                painter.fillColor = _coinBaseColor;
                FillCircle(painter, iconCenter, coinBaseRadius);
            }

            // 6) 中心ハブ（軸キャップ）。暗→明→ゴールドの三重円で立体感を出す。
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

            // 前回の当たり強調・祝い演出を消してから回し始める（パルス途中で再回転されても残らないように）。
            ClearHighlight();
            _wheelPulseTween?.Kill();
            _wheel.style.scale = new Scale(Vector3.one);

            _isHolding = true;
            _angularVelocity = _minSpinSpeed;
            _lastAngle = _currentRotation;
            _spinElapsed = 0f;
            _targetSpinDuration = UnityEngine.Random.Range(_minSpinDuration, _maxSpinDuration);
            _coastUntilElapsed = float.PositiveInfinity;
            _model.BeginSpin();
            PlaySe(_soundStore?.Enter1SE);
            // ポインタ捕捉は Button の Clickable が行うため、ボタン外でリリースしても PointerUp は届く。
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
        /// 押下解除。すぐ離しても <see cref="_targetSpinDuration"/> 秒（1.5〜2.5 秒のランダム）回ってから止まるよう、
        /// 目標時間から通常減速にかかる時間を引いた時点まで等速でコーストし、その後に減速を始める。
        /// 既に目標時間を過ぎている（長押しで十分回した）場合はすぐ減速へ移る。
        /// </summary>
        private void ReleaseHold()
        {
            if (!_isHolding)
            {
                return;
            }
            _isHolding = false;

            // 現在の角速度から通常減速で止まり切るのにかかる時間。
            float stopDuration = _angularVelocity / _spinDeceleration;
            // 目標時間ちょうどで止まるよう、減速開始の経過時間を逆算する。
            _coastUntilElapsed = Mathf.Max(_spinElapsed, _targetSpinDuration - stopDuration);
        }

        private bool CanStartSpin()
        {
            return _model != null
                   && _board != null
                   && _model.State.CurrentValue != RouletteState.Spinning
                   && !_board.IsMoving.CurrentValue
                   && !_board.IsCleared.CurrentValue;
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
            // アイコン（と子の数字）は円盤の子なので一緒に周回するが、逆回転させて常に正立させる。
            CounterRotateCharacterIcons(v);

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
            if (_spinButton == null || _board == null)
            {
                return;
            }

            // 回転中（Spinning）はボタンを押下したまま離す操作を受け取る必要があるため無効化しない。
            // 再回転のガードは OnPointerDown 側の状態チェックで行う。
            bool canPress = !_board.IsMoving.CurrentValue && !_board.IsCleared.CurrentValue;
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
