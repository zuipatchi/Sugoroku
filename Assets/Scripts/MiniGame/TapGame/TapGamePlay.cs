using System;
using System.Collections.Generic;
using System.Threading;
using Common.Character;
using Common.MiniGame;
using Common.SoundManagement;
using Common.Store;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using R3;
using UnityEngine;
using UnityEngine.UIElements;

namespace MiniGame.TapGame
{
    /// <summary>
    /// タップ連打ミニゲームの UI 構築と進行。<see cref="MiniGameHostPresenter"/> から
    /// クローン済みの UXML ルートを受け取り、カウントダウン → 計測 → 結果まで駆動する。
    /// タップ数・残り時間・フェーズは <see cref="TapGameModel"/> が持ち、ここは表示・入力・時間駆動だけを担う。
    /// 選択中キャラのカード絵を中央に表示し、タップのたびにカードを「がたがた」振動＋「パンチ」拡大で弾ませる。
    /// </summary>
    public sealed class TapGamePlay : IDisposable
    {
        private const float PlayDurationSeconds = 5f;
        private const int RevealLeadInMs = 500;
        // オンライン対戦で自分の連打数を配る間隔（秒）。見た目だけの情報なので毎フレームは送らない。
        private const float ProgressIntervalSeconds = 0.2f;
        // 叩く場所（タップボタン）に貼るサンドバッグの絵。未配置なら TapGame.uss の地色のままにする。
        private const string TapTargetAddress = "Image/MiniGame/SandBag";
        // 画面全体に敷くジムの絵（サンドバッグを叩く場所の背景）。未配置なら TapGame.uss の地色のまま。
        private const string BackgroundAddress = "Image/GymBackground";
        // 「タップ」案内の点滅（明るい ↔ 暗いを往復する片道の秒数と、暗いときの不透明度）。
        private const float HintBlinkSeconds = 0.4f;
        private const float HintBlinkMinOpacity = 0.15f;

        private readonly TapGameModel _model;
        private readonly MiniGameSessionModel _session;
        private readonly SoundStore _soundStore;
        private readonly SoundPlayer _soundPlayer;
        private readonly CharacterSessionModel _characterSession;
        private readonly CompositeDisposable _disposables = new();

        private Label _timerLabel;
        private Label _countLabel;
        private Label _centerLabel;
        private Label _tapHintLabel;
        private Button _tapButton;
        private VisualElement _characterCard;
        private VisualElement _scoreboard;
        private VisualElement _resultPanel;
        private Label _resultLabel;
        private Button _closeButton;

        // スコアボードの各参加者チップの連打数ラベル（index＝参加者）。毎フレーム更新する。
        private readonly List<Label> _scoreCountLabels = new();

        private readonly AddressableSpriteLoader _spriteLoader = new();
        private Tween _shakeTween;
        private float _shakePhase;
        private Tween _hintTween;
        private float _hintOpacity = 1f;

        private UniTaskCompletionSource _closeSource;

        public TapGamePlay(
            TapGameModel model,
            MiniGameSessionModel session,
            SoundStore soundStore,
            SoundPlayer soundPlayer,
            CharacterSessionModel characterSession)
        {
            _model = model;
            _session = session;
            _soundStore = soundStore;
            _soundPlayer = soundPlayer;
            _characterSession = characterSession;
        }

        /// <summary>
        /// タップ連打 UI を構築する（フェードイン前に await される）。キャラカード絵のロードと
        /// Model → UI の購読まで済ませ、画面を見せられる状態にする。進行と入力受付は <see cref="RunAsync"/> 側。
        /// </summary>
        public async UniTask BuildAsync(VisualElement root, CancellationToken ct)
        {
            _timerLabel = root.Q<Label>("TimerLabel");
            _countLabel = root.Q<Label>("CountLabel");
            _centerLabel = root.Q<Label>("CenterLabel");
            _tapHintLabel = root.Q<Label>("TapHintLabel");
            _tapButton = root.Q<Button>("TapButton");
            _characterCard = root.Q<VisualElement>("CharacterCard");
            _scoreboard = root.Q<VisualElement>("Scoreboard");
            _resultPanel = root.Q<VisualElement>("ResultPanel");
            _resultLabel = root.Q<Label>("ResultLabel");
            _closeButton = root.Q<Button>("CloseButton");
            if (_timerLabel == null || _countLabel == null || _centerLabel == null || _tapButton == null
                || _scoreboard == null || _resultPanel == null || _resultLabel == null || _closeButton == null)
            {
                Debug.LogError("TapGame の UI 要素が見つかりませんでした。");
                return;
            }

            // 参加者数はセッション（起動側が指定）から取る。未設定（0 以下）のときだけソロ（1 人）へ。
            int playerCount = _session != null && _session.PlayerCount > 0 ? _session.PlayerCount : 1;
            // オンライン対戦では相手は実プレイヤーなので CPU の自動連打を止める（結果は持ち寄って決める）。
            _model.Setup(
                TapGameConfig.Default,
                playerCount,
                _session != null ? _session.ResolveSeed() : NextSeed(),
                OpponentsSimulated);

            _tapButton.clicked += OnTapClicked;
            _closeButton.clicked += OnCloseClicked;

            // Model を source of truth として UI へ反映する。
            _disposables.Add(_model.TapCount.Subscribe(count =>
            {
                if (_countLabel != null)
                {
                    _countLabel.text = count.ToString();
                }
            }));
            _disposables.Add(_model.RemainingSeconds.Subscribe(secs =>
            {
                if (_timerLabel != null)
                {
                    _timerLabel.text = $"残り時間：{secs:0.0}";
                }
            }));
            _disposables.Add(_model.Phase.Subscribe(ApplyPhase));

            _closeSource = new UniTaskCompletionSource();

            // 背景・サンドバッグ・キャラカードはいずれも Addressables なので並列でロードする。
            await UniTask.WhenAll(ApplyBackgroundAsync(root, ct), ApplyTapTargetAsync(ct), ApplyCharacterCardAsync(ct));
            await BuildScoreboardAsync(ct);
        }

        // 参加者数ぶんのスコアボードチップ（キャラアイコン＋連打数）を生成し、アイコンを並列ロードして貼る。
        // 連打数ラベルは _scoreCountLabels に控え、進行中は毎フレーム更新する。
        private async UniTask BuildScoreboardAsync(CancellationToken ct)
        {
            _scoreboard.Clear();
            _scoreCountLabels.Clear();

            int count = _model.ParticipantCount;
            IReadOnlyList<CharacterId> characters = _session?.Characters;

            List<UniTask> loads = new(count);
            for (int p = 0; p < count; p++)
            {
                bool isPlayer = p == 0;
                CharacterId id = characters != null && characters.Count > p
                    ? characters[p]
                    : (isPlayer ? _characterSession.Selected : CharacterCatalog.All[p % CharacterCatalog.All.Count].Id);

                VisualElement chip = new() { pickingMode = PickingMode.Ignore };
                chip.AddToClassList("tap-score-chip");
                if (isPlayer)
                {
                    chip.AddToClassList("tap-score-chip--you");
                }

                VisualElement icon = new() { pickingMode = PickingMode.Ignore };
                icon.AddToClassList("tap-score-chip__icon");
                chip.Add(icon);

                Label countLabel = new("0") { pickingMode = PickingMode.Ignore };
                countLabel.AddToClassList("tap-score-chip__count");
                chip.Add(countLabel);

                _scoreboard.Add(chip);
                _scoreCountLabels.Add(countLabel);
                loads.Add(ApplyChipIconAsync(icon, id, ct));
            }

            await UniTask.WhenAll(loads);
        }

        private async UniTask ApplyChipIconAsync(VisualElement icon, CharacterId id, CancellationToken ct)
        {
            CharacterDefinition definition = CharacterCatalog.Find(id);
            Sprite sprite = await _spriteLoader.TryLoadAsync(definition.PieceIconAddress, "コマアイコン", ct);
            if (sprite != null)
            {
                icon.style.backgroundImage = new StyleBackground(sprite);
            }
            else
            {
                icon.style.backgroundImage = StyleKeyword.None;
                icon.style.backgroundColor = CharacterPalette.PlaceholderColorFor(id);
            }
        }

        /// <summary>相手をこのクライアントでシミュレートしているか（＝一人用モード）。</summary>
        private bool OpponentsSimulated => _session == null || _session.SimulateOpponents;

        /// <summary>
        /// 自分の連打数を配り、届いている相手の連打数をスコアボードへ反映する（オンラインのみ）。
        /// 一人用モードでは相手をゲーム内の CPU がシミュレートしているので何もしない。
        /// </summary>
        private void PublishAndApplyProgress()
        {
            MiniGameProgressChannel progress = _session?.Progress;
            if (progress == null)
            {
                return;
            }

            progress.Publish(_model.TapCount.CurrentValue);
            for (int p = 1; p < _model.ParticipantCount; p++)
            {
                _model.SetTapCount(p, progress.Values[p]);
            }
        }

        // スコアボードの各連打数ラベルを現在値へ更新する（進行中に毎フレーム呼ぶ）。
        private void UpdateScoreboard()
        {
            for (int p = 0; p < _scoreCountLabels.Count; p++)
            {
                _scoreCountLabels[p].text = _model.TapCountOf(p).ToString();
            }
        }

        /// <summary>
        /// カウントダウン → 計測 → 結果表示を駆動し、「進む」クリックでスコア（1 位=1／それ以外=0）を返す。
        /// 計測中はスコアボードを毎フレーム更新する（一人用は各 CPU を <see cref="TapGameModel.Tick"/> で
        /// 自動連打させ、オンラインは互いの連打数を配り合って表示する）。
        /// フェードイン後に呼ばれる想定で、Forget して走らせる。
        /// </summary>
        public async UniTask<(int Score, int Value)> RunAsync(CancellationToken ct)
        {
            if (_closeSource == null)
            {
                // UI 構築に失敗している。従来どおり結果は報告せず、キャンセル（シーンアンロード）まで待機する。
                return await UniTask.Never<(int Score, int Value)>(ct);
            }

            _centerLabel.text = "準備…";
            await UniTask.Delay(RevealLeadInMs, cancellationToken: ct);

            _model.BeginCountdown();
            await MiniGameCountdown.RunAsync(_centerLabel, _soundStore, _soundPlayer, ct);

            _model.StartPlaying(PlayDurationSeconds);
            UpdateScoreboard();

            float elapsed = 0f;
            float sinceProgress = 0f;
            while (elapsed < PlayDurationSeconds)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                float dt = Time.deltaTime;
                elapsed += dt;
                _model.Tick(dt);
                _model.UpdateRemaining(PlayDurationSeconds - elapsed);

                // オンライン対戦では相手をシミュレートしない代わりに、互いの連打数を配り合って
                // スコアボードに出す（見た目だけの情報なので、間引いて送り取りこぼしは気にしない）。
                sinceProgress += dt;
                if (sinceProgress >= ProgressIntervalSeconds)
                {
                    sinceProgress = 0f;
                    PublishAndApplyProgress();
                }

                UpdateScoreboard();
            }

            // 最後の値を送り切ってから締める（間引きの都合で数回ぶん遅れていることがある）。
            PublishAndApplyProgress();

            _model.Finish();
            UpdateScoreboard();
            _soundPlayer.PlaySafe(_soundStore?.DecisionSE);

            int playerTaps = _model.TapCount.CurrentValue;
            bool win = _model.IsPlayerWin;
            // オンライン対戦では最後の数回ぶんが届いていないことがあるので順位を断定しない
            // （正式な勝敗は全員の結果値が揃ってから盤面側が発表する）。
            _resultLabel.text = OpponentsSimulated
                ? win
                    ? $"1位！　タップ数 {playerTaps} 回"
                    : $"{PlayerRank()}位　タップ数 {playerTaps} 回"
                : $"タップ数 {playerTaps} 回　結果はこのあと発表！";

            await _closeSource.Task.AttachExternalCancellation(ct);
            // 結果値はタップ数。オンラインでは全員ぶんを持ち寄って最多の人が勝ちになる。
            return (win ? 1 : 0, playerTaps);
        }

        // プレイヤーの順位（1 位＝自分より多い参加者が 0 人）。同数は同順で自分を上に見なす。
        private int PlayerRank()
        {
            int playerTaps = _model.TapCountOf(0);
            int rank = 1;
            for (int p = 1; p < _model.ParticipantCount; p++)
            {
                if (_model.TapCountOf(p) > playerTaps)
                {
                    rank++;
                }
            }
            return rank;
        }

        private static int NextSeed()
        {
            return UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        }

        public void Dispose()
        {
            _shakeTween?.Kill();
            _shakeTween = null;
            _hintTween?.Kill();
            _hintTween = null;
            if (_tapButton != null)
            {
                _tapButton.clicked -= OnTapClicked;
            }
            if (_closeButton != null)
            {
                _closeButton.clicked -= OnCloseClicked;
            }
            _spriteLoader.Dispose();
            _disposables.Dispose();
        }

        private void ApplyPhase(TapGamePhase phase)
        {
            if (_tapButton == null)
            {
                return;
            }
            _tapButton.SetEnabled(phase == TapGamePhase.Playing);
            if (_countLabel != null)
            {
                // カウントダウン中（3.2.1）はタップ数の 0 を出さず、計測が始まってから表示する。
                _countLabel.style.display = phase == TapGamePhase.Playing ? DisplayStyle.Flex : DisplayStyle.None;
            }
            _centerLabel.style.display =
                (phase == TapGamePhase.Ready || phase == TapGamePhase.Countdown)
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            if (_tapHintLabel != null)
            {
                // 叩く場所の案内。「スタート！」が消えて計測が始まってから出し、点滅させて目を引く。
                bool showHint = phase == TapGamePhase.Playing;
                _tapHintLabel.style.display = showHint ? DisplayStyle.Flex : DisplayStyle.None;
                if (showHint)
                {
                    StartHintBlink();
                }
                else
                {
                    StopHintBlink();
                }
            }
            _resultPanel.style.display = phase == TapGamePhase.Finished ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // 「タップ」案内をチカチカ点滅させる（明滅を往復ループ）。計測中だけ回し、止めたら不透明へ戻す。
        private void StartHintBlink()
        {
            _hintTween?.Kill();
            _hintOpacity = 1f;
            _tapHintLabel.style.opacity = 1f;

            _hintTween = DOTween.To(
                    () => _hintOpacity,
                    v =>
                    {
                        _hintOpacity = v;
                        if (_tapHintLabel != null)
                        {
                            _tapHintLabel.style.opacity = v;
                        }
                    },
                    HintBlinkMinOpacity,
                    HintBlinkSeconds)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo);
        }

        private void StopHintBlink()
        {
            _hintTween?.Kill();
            _hintTween = null;
            if (_tapHintLabel != null)
            {
                _tapHintLabel.style.opacity = 1f;
            }
        }

        // 画面の背景にジムの絵を貼る。貼り先は BuildAsync に渡る root（＝UXML ルートの親）ではなく
        // 子の TapRoot（親へ貼っても不透明な地色を持つ子に隠れて見えない＝patterns.md 8 の注意）。
        // 拡大縮小は USS（.tap-root の background-size: cover）に任せ、未配置なら何もしない＝地色のまま。
        private async UniTask ApplyBackgroundAsync(VisualElement root, CancellationToken ct)
        {
            VisualElement target = root?.Q<VisualElement>("TapRoot");
            if (target == null)
            {
                return;
            }
            Sprite sprite = await _spriteLoader.TryLoadAsync(BackgroundAddress, "ジム背景", ct);
            if (sprite != null)
            {
                target.style.backgroundImage = new StyleBackground(sprite);
            }
        }

        // 叩く場所（タップボタン）にサンドバッグの絵を貼る。拡大縮小は USS
        // （.tap-button の background-size: cover）に任せる。未配置なら何もしない＝地色のまま。
        private async UniTask ApplyTapTargetAsync(CancellationToken ct)
        {
            Sprite target = await _spriteLoader.TryLoadAsync(TapTargetAddress, "サンドバッグ", ct);
            if (target != null)
            {
                _tapButton.style.backgroundImage = new StyleBackground(target);
            }
        }

        // 選択中キャラのカード絵を読み込んで中央に表示する。未配置ならプレースホルダ（色面）にフォールバックする。
        private async UniTask ApplyCharacterCardAsync(CancellationToken ct)
        {
            if (_characterCard == null)
            {
                return;
            }

            // プレイヤー（index 0）のキャラはセッション指定を優先し、無ければ選択キャラへフォールバック。
            CharacterId id = _session != null && _session.Characters.Count > 0
                ? _session.Characters[0]
                : _characterSession.Selected;
            CharacterDefinition definition = CharacterCatalog.Find(id);

            Sprite card = await _spriteLoader.TryLoadAsync(definition.CardAddress, "キャラカード", ct);
            if (card != null)
            {
                _characterCard.style.backgroundImage = new StyleBackground(card);
            }
            else
            {
                _characterCard.style.backgroundImage = StyleKeyword.None;
                _characterCard.style.backgroundColor = CharacterPalette.PlaceholderColorFor(id);
            }
        }

        // タップのたびにカードを弾ませる。「がたがた」（減衰する小刻みな振動）と「パンチ」（ぷにっと拡大→戻る）を合わせた演出。
        // 位相 1→0 を 1 本の Tween で流し、その位相から毎フレーム位置と拡大を計算する。
        private void ShakeCard()
        {
            if (_characterCard == null)
            {
                return;
            }

            _shakeTween?.Kill();

            float amplitude = UnityEngine.Random.Range(9f, 13f);
            float sign = (UnityEngine.Random.value < 0.5f) ? -1f : 1f;

            _shakePhase = 1f;
            ApplyShake(1f, amplitude, sign);

            _shakeTween = DOTween.To(
                    () => _shakePhase,
                    p =>
                    {
                        _shakePhase = p;
                        ApplyShake(p, amplitude, sign);
                    },
                    0f,
                    0.4f)
                .SetEase(Ease.Linear);
        }

        // 位相 phase（1→0）から、減衰する小刻み振動（がたがた）と減衰する拡大（パンチ）を適用する。
        private void ApplyShake(float phase, float amplitude, float sign)
        {
            if (_characterCard == null)
            {
                return;
            }

            // phase を減衰係数に使い、揺れ幅・拡大量ともに 0 へ収束させる。
            float offsetX = sign * amplitude * phase * Mathf.Sin(phase * 42f);
            float offsetY = amplitude * 0.6f * phase * Mathf.Cos(phase * 38f);
            _characterCard.style.translate = new Translate(
                new Length(offsetX, LengthUnit.Pixel),
                new Length(offsetY, LengthUnit.Pixel));

            float punch = 1f + 0.16f * phase;
            _characterCard.style.scale = new Scale(new Vector3(punch, punch, 1f));
        }

        private void OnTapClicked()
        {
            _model.Tap();
            ShakeCard();
            _soundPlayer.PlaySafe(_soundStore?.RandomPunchSE);
        }

        private void OnCloseClicked()
        {
            // 結果を起動側（Main）へ返すため、RunAsync の待機を解く。
            _closeSource?.TrySetResult();
        }
    }
}
