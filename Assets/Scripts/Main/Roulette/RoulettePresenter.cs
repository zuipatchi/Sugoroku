using System;
using System.Collections.Generic;
using System.Threading;
using Common.Character;
using Common.SoundManagement;
using Common.Store;
using Cysharp.Threading.Tasks;
using Main.Board;
using Main.Turn;
using R3;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;
using Random = UnityEngine.Random;

namespace Main.Roulette
{
    /// <summary>
    /// 円盤ルーレットの UI。手番連携 API（<see cref="SetInteractable"/>／<see cref="WaitForManualSpinAsync"/>／
    /// <see cref="AutoSpinAsync"/>／<see cref="WaitForHideAsync"/>）と R3 購読・UI 要素の組み立てを担い、
    /// 円盤描画は <see cref="RouletteWheelRenderer"/>、回転の角速度シミュレーションは <see cref="RouletteSpinPhysics"/>、
    /// キャラアイコンのロードは <see cref="RouletteIconLoader"/>、DOTween 演出は <see cref="RouletteEffects"/> に委譲する。
    /// 状態遷移は <see cref="RouletteModel"/> が担い、出目の算出（停止角度 → セクター）はここで行う。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class RoulettePresenter : MonoBehaviour
    {
        // スピンボタンに貼る画像の Addressable アドレス。未配置なら文字（「長押しで回す」）のまま。
        private const string SpinButtonImageAddress = "Image/Roulette";

        [Tooltip("1 キャラあたりの数字枚数（K）。各参加者が数字 1〜K を 1 枚ずつ持つ。セクター総数 = 参加者数 × K。")]
        [SerializeField] private int _numbersPerCharacter = 3;
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
        [Tooltip("止まってから円盤を隠すまでの待ち時間（秒）。出目を見せてから背後の盤面へ視線を戻す。")]
        [SerializeField] private float _hideAfterStopSeconds = 1.2f;

        private RouletteModel _model;
        private BoardModel _board;
        private CpuCharacterPicker _characterPicker;
        private SoundStore _soundStore;
        private SoundPlayer _soundPlayer;
        // 参加者数とセクター総数（＝参加者数 × K）。注入後に確定する。均等配置・出目の割り当てに使う。
        private int _participantCount;
        private int _sectorCount;

        private UIDocument _uiDocument;
        private VisualElement _wheel;
        // 円盤・タイトル・出目ラベルをまとめて表示/非表示する対象。回しているときだけ見せ、
        // それ以外は隠して背後の盤面を見せる。スピンボタンは手番トリガーとして常に残す。
        private VisualElement _wheelArea;
        private Label _pointer;
        private Button _spinButton;
        private Label _resultLabel;
        // 停止後に「少し待ってから隠す」遅延タスクのキャンセル用。再回転・手番リセットで打ち切る。
        private CancellationTokenSource _hideCts;
        // 円盤の表示状態。スピン後に「消えてからコマを動かす」ため、GameFlowController が非表示化を待てるようにする。
        private readonly ReactiveProperty<bool> _visible = new(false);
        // 各セクターに貼るキャラアイコン（コイン）。円盤の子として一緒に周回し、逆回転で正立を保つ。数字は各アイコンの子バッジ。
        private readonly List<VisualElement> _characterIcons = new();
        private readonly RouletteIconLoader _iconLoader = new();
        private readonly CompositeDisposable _disposables = new();

        // 分割した協調クラス。physics は Awake で、renderer は参加者数が確定してセクター総数が決まる TryBuildWheel で、
        // effects は UI 要素取得後（OnEnable）で生成する。
        private RouletteWheelRenderer _wheelRenderer;
        private RouletteSpinPhysics _spinPhysics;
        private RouletteEffects _effects;

        // ティック演出（セクター境界の通過検出）用に、前フレームで表示へ反映した角度を覚えておく。
        private float _lastAngle;
        private bool _wheelBuilt;
        // UI 要素の取得（OnEnable）が済んだか。Construct（参加者数）と両方そろってから円盤を組む。
        private bool _uiReady;
        // 手番制御。ゲーム進行（GameFlowController）が現在の手番プレイヤーに応じて切り替える。
        // false の間は手動スピン不可（CPU の番・自分の番でないときなど）。
        private bool _turnInteractable;
        // 押し続けを離してスピンが確定したか。true の間（惰性回転中）はボタンを無効にして
        // 再押下を防ぐ。次の手番開始（SetInteractable(true)）でクリアして再び押せるようにする。
        private bool _spinReleased;

        [Inject]
        public void Construct(
            RouletteModel model,
            BoardModel board,
            GameParticipants participants,
            CpuCharacterPicker characterPicker,
            SoundStore soundStore,
            SoundPlayer soundPlayer)
        {
            _model = model;
            _board = board;
            _characterPicker = characterPicker;
            _soundStore = soundStore;
            _soundPlayer = soundPlayer;

            // 参加者数からセクター総数を確定する（参加者を均等に並べ、各キャラが数字 1〜K を 1 枚ずつ持つ）。
            _participantCount = participants.Count;
            _sectorCount = RouletteMath.SectorCount(_participantCount, _numbersPerCharacter);
            // UI 構築（OnEnable）が済んでいれば円盤を組む。まだなら OnEnable 側から組む（順不同ガード）。
            TryBuildWheel();

            // OnEnable で UI 構築済みのため、ここで購読してよい（injection は OnEnable の後）。
            // DOTween.dll の AddTo 拡張と衝突するため、ここでは CompositeDisposable.Add で購読を管理する。
            // コマ移動中・クリア後は回せないため、その 2 状態を購読してボタンを更新する。
            _disposables.Add(_board.IsMoving.Subscribe(_ => UpdateSpinEnabled()));
            _disposables.Add(_board.Winner.Subscribe(_ => UpdateSpinEnabled()));
            // 回している間だけ円盤を見せる。回転開始（Spinning）で表示、停止（Stopped）で少し待って隠し、
            // 手番リセット（Idle）で即座に隠す。人間・CPU どちらも BeginSpin を通るので同じ経路で表示される。
            _disposables.Add(_model.State.Subscribe(OnRouletteStateChanged));
            _disposables.Add(_visible);
        }

        private void Awake()
        {
            _uiDocument = GetComponent<UIDocument>();
            // 円盤描画（renderer）はセクター総数が確定する Construct 後に作る（TryBuildWheel）。
            // 回転物理はセクター数に依らないためここで作ってよい。
            _spinPhysics = new RouletteSpinPhysics(_minSpinSpeed, _maxSpinSpeed, _spinAcceleration);
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
            _wheelArea = root.Q<VisualElement>("WheelArea");
            _pointer = root.Q<Label>("Pointer");
            _spinButton = root.Q<Button>("SpinButton");
            _resultLabel = root.Q<Label>("ResultLabel");
            if (_wheel == null || _wheelArea == null
                || _pointer == null || _spinButton == null || _resultLabel == null)
            {
                Debug.LogError("Roulette の UI 要素が見つかりませんでした。");
                return;
            }

            // 再有効化で UI 要素を取り直すため、演出も現在の要素で作り直す（前回の Tween は OnDisable で Kill 済み）。
            _effects = new RouletteEffects(_wheel, _pointer, _resultLabel);

            // UI 要素がそろった。セクター総数が確定していれば（Construct 済み）円盤を組む（順不同ガード）。
            _uiReady = true;
            TryBuildWheel();
            // 初期は隠しておく（回し始めるまで盤面を見せる）。State 購読（Construct）でも同期される。
            SetRouletteVisible(false);
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
            _spinPhysics?.Halt();
            CancelPendingHide();
            _effects?.KillAll();
            if (_spinButton != null)
            {
                _spinButton.UnregisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
                _spinButton.UnregisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
                _spinButton.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            }
        }

        private void OnDestroy()
        {
            CancelPendingHide();
            _disposables.Dispose();
            _iconLoader.ReleaseAll();
        }

        private void Update()
        {
            // 回転中（押下中＋減速中）のみ角度を更新する。
            if (_model == null || _wheel == null || _model.State.CurrentValue != RouletteState.Spinning)
            {
                return;
            }

            if (_spinPhysics.Tick(Time.deltaTime))
            {
                ApplyAngle(_spinPhysics.CurrentRotation);
            }

            // 減速し切って速度が尽きたら停止して出目を確定する。
            if (_spinPhysics.HasStopped)
            {
                FinalizeSpin();
            }
        }

        /// <summary>
        /// UI 要素の取得（OnEnable）と参加者数の確定（Construct）が両方そろったら円盤を組む。
        /// どちらが先でも動くよう両方から呼ぶ（一度だけ実行）。
        /// </summary>
        private void TryBuildWheel()
        {
            // 再有効化・順不同の呼び出しでの二重構築を防ぐ。UI と参加者数が両方そろってから組む。
            if (_wheelBuilt || !_uiReady || _sectorCount <= 0)
            {
                return;
            }
            _wheelBuilt = true;

            // セクター総数はここで確定する（参加者数 × K）。描画（renderer）もここで作る。
            _wheelRenderer = new RouletteWheelRenderer(_sectorCount);
            _wheel.generateVisualContent += _wheelRenderer.Draw;

            // セクターごとに割り当てる参加者のキャラ（画像ロードに渡す）。
            List<CharacterId> sectorCharacters = new(_sectorCount);
            _characterIcons.Clear();
            for (int i = 0; i < _sectorCount; i++)
            {
                int participant = RouletteMath.ParticipantForSector(i, _participantCount);
                sectorCharacters.Add(_characterPicker.ResolveCharacter(participant));

                // セクターごとのキャラアイコン（コイン）。画像ロード前はプレースホルダ色。
                VisualElement icon = new() { pickingMode = PickingMode.Ignore };
                icon.AddToClassList("roulette-character");
                icon.style.backgroundColor = CharacterPalette.PlaceholderColor(i, _sectorCount);
                _wheel.Add(icon);
                _characterIcons.Add(icon);

                // 出目の数字はアイコンの子にして、アイコンの正立・周回にそのまま追従させる。
                // 各参加者が数字 1〜K を 1 枚ずつ持つよう StepsForSector で番号を振る。
                // USS で下部中央のバッジとして配置する（コード側の位置・逆回転は不要）。
                Label label = new() { text = RouletteMath.StepsForSector(i, _participantCount).ToString() };
                label.AddToClassList("roulette-number");
                label.pickingMode = PickingMode.Ignore;
                icon.Add(label);
            }

            // レイアウト確定後にアイコン（コイン）を配置する。数字は子バッジなので一緒に付いてくる。
            _wheel.RegisterCallback<GeometryChangedEvent>(_ => PositionSectorContents());
            // キャラ画像は非同期ロード。破棄・遷移で自然に止まるよう destroyCancellationToken を渡す。
            _iconLoader.LoadCharacterIconsAsync(_characterIcons, sectorCharacters, destroyCancellationToken).Forget();
            // スピンボタンの文字を Roulette.png に差し替える（未配置なら文字のまま）。
            _iconLoader.LoadSpinButtonAsync(_spinButton, SpinButtonImageAddress, destroyCancellationToken).Forget();
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
            float iconRadius = _wheelRenderer.IconRadius(radius);
            float sector = RouletteMath.SectorAngle(_sectorCount);
            // アバター画像はコイン白座面の内側に収める。コインの縁取り（ゴールド＋白）は Painter2D 側で描く。
            float avatarSize = _wheelRenderer.AvatarSize(radius);

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
            CounterRotateCharacterIcons(_spinPhysics.CurrentRotation);
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

        private void OnPointerDown(PointerDownEvent evt)
        {
            // 画像ボタンは円形の見た目なので、四隅（円の外）の押下は弾いて見た目と当たり判定を揃える。
            // TrickleDown で Button 内部の Clickable より先に受けるため、ここで止めれば押下扱いにならない。
            if (IsOutsideCircularButton(evt.localPosition))
            {
                evt.StopImmediatePropagation();
                return;
            }

            if (!CanStartSpin())
            {
                return;
            }

            BeginSpinInternal();
            // ポインタ捕捉は Button の Clickable が行うため、ボタン外でリリースしても PointerUp は届く。
        }

        /// <summary>
        /// スピンボタンが円形の画像表示のとき、ボタンローカル座標 <paramref name="localPosition"/> が
        /// 内接円の外にあるか。文字フォールバック（矩形ボタン）のときは常に false。
        /// </summary>
        private bool IsOutsideCircularButton(Vector3 localPosition)
        {
            if (_spinButton == null || !_spinButton.ClassListContains(RouletteIconLoader.SpinButtonImageClass))
            {
                return false;
            }

            float halfWidth = _spinButton.resolvedStyle.width * 0.5f;
            float halfHeight = _spinButton.resolvedStyle.height * 0.5f;
            float radius = Mathf.Min(halfWidth, halfHeight);
            float dx = localPosition.x - halfWidth;
            float dy = localPosition.y - halfHeight;
            return dx * dx + dy * dy > radius * radius;
        }

        /// <summary>回転を開始する内部処理。手動（PointerDown）と CPU 自動（<see cref="AutoSpinAsync"/>）で共有する。</summary>
        private void BeginSpinInternal()
        {
            // 前回の当たり強調・祝い演出を消してから回し始める（パルス途中で再回転されても残らないように）。
            ClearHighlight();
            _effects.ResetWheelPulse();

            _spinPhysics.BeginHold();
            _lastAngle = _spinPhysics.CurrentRotation;
            _model.BeginSpin();
            _soundPlayer.PlaySafe(_soundStore?.Enter1SE);
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
        /// 押下解除。離した瞬間の速度に関わらず <see cref="_minStopDuration"/>〜<see cref="_maxStopDuration"/> の
        /// ランダムな時間をかけて ease-out で減速して止める（詳細は <see cref="RouletteSpinPhysics.Release"/>）。
        /// </summary>
        private void ReleaseHold()
        {
            if (!_spinPhysics.IsHolding)
            {
                return;
            }
            _spinPhysics.Release(Random.Range(_minStopDuration, _maxStopDuration));
            // 離した瞬間にボタンを無効化する（惰性回転中の再押下を防ぐ）。捕捉中の
            // PointerUp はこの後 Clickable が処理して解放するため、同期無効化でも問題ない。
            _spinReleased = true;
            UpdateSpinEnabled();
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
            _spinPhysics.Halt();
            // 止まったセクターから「進む参加者」と「出目（マス数）」を確定する（止まったキャラが進む方式）。
            int sectorIndex = RouletteMath.ResultFromRotation(_spinPhysics.CurrentRotation, _sectorCount);
            int steps = RouletteMath.StepsForSector(sectorIndex, _participantCount);
            int player = RouletteMath.ParticipantForSector(sectorIndex, _participantCount);
            _model.CompleteSpin(steps, player);
            _soundPlayer.PlaySafe(_soundStore?.DecisionSE);
            ShowWinHighlight(sectorIndex);
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
                _effects.BouncePointer();
                // 長押し中の高速回転も含め、セクター境界を通過するたびにティック SE を鳴らす
                // （高速時は 1 フレームに複数境界を跨ぐが、鳴るのはフレームあたり 1 回）。
                _soundPlayer.PlaySafe(_soundStore?.RouletteSE);
            }
            _lastAngle = v;
        }

        private void ShowWinHighlight(int index)
        {
            _wheelRenderer.HighlightIndex = index;
            _wheel.MarkDirtyRepaint();

            // 当たりの瞬間に円盤をひと突き拡大して祝う。
            _effects.PulseWheelOnWin();
        }

        private void ClearHighlight()
        {
            if (_wheelRenderer.HighlightIndex < 0)
            {
                return;
            }
            _wheelRenderer.HighlightIndex = -1;
            _wheel.MarkDirtyRepaint();
        }

        /// <summary>
        /// 状態に応じて円盤の表示/非表示を切り替える。回している間だけ見せ、それ以外は隠して盤面を見せる。
        /// </summary>
        private void OnRouletteStateChanged(RouletteState state)
        {
            switch (state)
            {
                case RouletteState.Spinning:
                    // 回転開始。再回転なら停止後の隠し予約を取り消してから表示する。
                    // 前回の出目が回転中に残って見えないよう、出目ラベルを空にする（停止時に CompleteSpin で入る）。
                    CancelPendingHide();
                    if (_resultLabel != null)
                    {
                        _resultLabel.text = string.Empty;
                    }
                    SetRouletteVisible(true);
                    break;
                case RouletteState.Stopped:
                    // 出目を中央に表示する。人間・CPU どちらの停止もここを通るので両方で表示される。
                    // Result の値が前回と同じでも確実に出せるよう、購読ではなくここで現在値から反映する。
                    int value = _model.Result.CurrentValue;
                    if (_resultLabel != null && value > 0)
                    {
                        _resultLabel.text = value.ToString();
                        _effects?.PlayResultLabelPop();
                    }
                    // 出た目を少し見せてから隠す。
                    ScheduleHideAfterStop();
                    break;
                default:
                    // Idle（手番リセット）。予約中の隠しを取り消して即座に隠す。
                    CancelPendingHide();
                    SetRouletteVisible(false);
                    break;
            }
        }

        /// <summary>
        /// 円盤・出目ラベルをまとめて表示/非表示する。スピンボタンは対象外（手番トリガーとして常に残す）。
        /// レイアウトを保って（＝ボタンが動かないよう）透明にする <c>visibility</c> で切り替え、
        /// 隠している間は背後の盤面が透けて見える。
        /// </summary>
        private void SetRouletteVisible(bool visible)
        {
            Visibility visibility = visible ? Visibility.Visible : Visibility.Hidden;
            if (_wheelArea != null)
            {
                _wheelArea.style.visibility = visibility;
            }
            if (_resultLabel != null)
            {
                _resultLabel.style.visibility = visibility;
            }
            _visible.Value = visible;
        }

        /// <summary>
        /// 円盤が隠れるまで待つ。<see cref="Turn.GameFlowController"/> が「ルーレットが消えてからコマを動かす」
        /// ために、スピン停止後にこれを待ってから前進させる。すでに隠れていれば即座に返る。
        /// </summary>
        public async UniTask WaitForHideAsync(CancellationToken ct)
        {
            await _visible.Where(visible => !visible).FirstAsync(ct);
        }

        /// <summary><see cref="_hideAfterStopSeconds"/> 秒後に円盤を隠す予約をする。前の予約は打ち切る。</summary>
        private void ScheduleHideAfterStop()
        {
            CancelPendingHide();
            _hideCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            HideAfterStopAsync(_hideCts.Token).Forget();
        }

        private async UniTaskVoid HideAfterStopAsync(CancellationToken ct)
        {
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(_hideAfterStopSeconds), cancellationToken: ct);
                SetRouletteVisible(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void CancelPendingHide()
        {
            if (_hideCts == null)
            {
                return;
            }
            _hideCts.Cancel();
            _hideCts.Dispose();
            _hideCts = null;
        }

        /// <summary>
        /// 手番制御。<paramref name="interactable"/> が false の間は手動スピンできない（CPU の番など）。
        /// <see cref="Turn.GameFlowController"/> が手番プレイヤーに応じて切り替える。
        /// </summary>
        public void SetInteractable(bool interactable)
        {
            _turnInteractable = interactable;
            // 新しい手番で手動スピンを許可するときは、前回の「離した」状態をクリアして再び押せるようにする。
            if (interactable)
            {
                _spinReleased = false;
            }
            UpdateSpinEnabled();
        }

        /// <summary>
        /// 手動スピンが停止するまで待ち、結果（進む参加者＋マス数）を返す（人間の手番用）。
        /// 呼び出し前に <see cref="RouletteModel.Reset"/> 済みであることを前提に、次の Stopped を待つ。
        /// </summary>
        public async UniTask<RouletteOutcome> WaitForManualSpinAsync(CancellationToken ct)
        {
            await _model.State.Where(state => state == RouletteState.Stopped).FirstAsync(ct);
            return CurrentOutcome();
        }

        /// <summary>
        /// CPU の手番。円盤を自動で回して自然に停止させ、結果（進む参加者＋マス数）を返す。
        /// 手動と同じ回転物理を使うため、少しの間ホールドしてから離す。
        /// </summary>
        public async UniTask<RouletteOutcome> AutoSpinAsync(CancellationToken ct)
        {
            BeginSpinInternal();
            float hold = Random.Range(_minStopDuration * 0.25f, _minStopDuration * 0.5f);
            await UniTask.Delay(TimeSpan.FromSeconds(hold), cancellationToken: ct);
            ReleaseHold();
            await _model.State.Where(state => state == RouletteState.Stopped).FirstAsync(ct);
            return CurrentOutcome();
        }

        /// <summary>確定済みの Model 値から現在のスピン結果を組み立てる。</summary>
        private RouletteOutcome CurrentOutcome()
        {
            return new RouletteOutcome(_model.AdvancingPlayer.CurrentValue, _model.Result.CurrentValue);
        }

        private void UpdateSpinEnabled()
        {
            if (_spinButton == null || _board == null)
            {
                return;
            }

            // 押し続けて回している間（Spinning かつ未リリース）はボタンを押下したまま離す操作を
            // 受け取る必要があるため無効化しない。離した後（_spinReleased）の惰性回転中は無効化して
            // 再押下を防ぐ。自分の手番でない（_turnInteractable == false）ときは常に無効化する。
            bool canPress = _turnInteractable && !_spinReleased && !_board.IsMoving.CurrentValue && !_board.IsFinished;
            _spinButton.SetEnabled(canPress);
        }

    }
}
