using System;
using System.Threading;
using Common.Character;
using Common.SoundManagement;
using Common.Store;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace MiniGame.RaceGame
{
    /// <summary>
    /// タイミングメーター式 2D レースの UI 構築と進行。<see cref="MiniGameHostPresenter"/> から
    /// クローン済みの UXML ルートを受け取り、カウントダウン → レース → 結果まで駆動する。
    /// レース状態は <see cref="RaceGameModel"/> が持ち、ここは表示・入力・時間駆動だけを担う。
    /// メーターのアニメーションと入力は Presenter 側、判定・前進・勝敗は Model 側。
    /// </summary>
    public sealed class RaceGamePlay : IDisposable
    {
        private const float MeterSweepSpeed = 1.6f;   // メーターの往復速度（1/秒）
        private const float TapPauseSeconds = 0.35f;  // タップ後にメーターを止めておく時間
        private const float StartPercent = 82f;       // 進捗 0（スタート）のときの走者の left%
        private const float GoalPercent = 2f;         // 進捗 1（ゴール）のときの走者の left%

        private readonly RaceGameModel _model;
        private readonly SoundStore _soundStore;
        private readonly SoundPlayer _soundPlayer;
        private readonly CharacterSessionModel _characterSession;

        private readonly AddressableSpriteLoader _spriteLoader = new();
        private readonly RaceMeter _meter = new(MeterSweepSpeed);

        private Label _titleLabel;
        private VisualElement _playerRunner;
        private VisualElement _cpuRunner;
        private Label _countdownLabel;
        private Label _judgeLabel;
        private VisualElement _meterPanel;
        private VisualElement _meterMarker;
        private VisualElement _meterGood;
        private VisualElement _meterGreat;
        private Button _tapButton;
        private VisualElement _resultPanel;
        private Label _resultLabel;
        private Button _closeButton;

        private UniTaskCompletionSource _closeSource;

        private float _pauseRemaining;

        public RaceGamePlay(
            RaceGameModel model,
            SoundStore soundStore,
            SoundPlayer soundPlayer,
            CharacterSessionModel characterSession)
        {
            _model = model;
            _soundStore = soundStore;
            _soundPlayer = soundPlayer;
            _characterSession = characterSession;
        }

        /// <summary>
        /// レース UI を構築する（フェードイン前に await される）。走者スプライトのロードと初期配置まで済ませ、
        /// 盤面を見せられる状態にする。進行と入力受付は <see cref="RunAsync"/> 側。
        /// </summary>
        public async UniTask BuildAsync(VisualElement root, CancellationToken ct)
        {
            _titleLabel = root.Q<Label>("TitleLabel");
            _playerRunner = root.Q<VisualElement>("PlayerRunner");
            _cpuRunner = root.Q<VisualElement>("CpuRunner");
            _countdownLabel = root.Q<Label>("CountdownLabel");
            _judgeLabel = root.Q<Label>("JudgeLabel");
            _meterPanel = root.Q<VisualElement>("MeterPanel");
            _meterMarker = root.Q<VisualElement>("MeterMarker");
            _meterGood = root.Q<VisualElement>("MeterGood");
            _meterGreat = root.Q<VisualElement>("MeterGreat");
            _tapButton = root.Q<Button>("TapButton");
            _resultPanel = root.Q<VisualElement>("ResultPanel");
            _resultLabel = root.Q<Label>("ResultLabel");
            _closeButton = root.Q<Button>("CloseButton");
            if (_titleLabel == null || _playerRunner == null || _cpuRunner == null || _countdownLabel == null
                || _judgeLabel == null || _meterPanel == null || _meterMarker == null || _meterGood == null
                || _meterGreat == null || _tapButton == null || _resultPanel == null || _resultLabel == null
                || _closeButton == null)
            {
                Debug.LogError("RaceGame の UI 要素が見つかりませんでした。");
                return;
            }

            RaceGameConfig config = RaceGameConfig.Default;
            _model.Setup(config, NextSeed());

            CharacterId playerId = _characterSession.Selected;
            CharacterId cpuId = RaceOpponentPicker.Pick(
                playerId,
                CharacterCatalog.All,
                count => UnityEngine.Random.Range(0, count));

            await ApplyRunnerSpriteAsync(_playerRunner, playerId, ct);
            await ApplyRunnerSpriteAsync(_cpuRunner, cpuId, ct);

            LayoutMeterZones(config);
            _closeSource = new UniTaskCompletionSource();

            _tapButton.clicked += OnTapClicked;
            _closeButton.clicked += OnCloseClicked;

            _meter.Reset();
            _pauseRemaining = 0f;

            PlaceRunner(_playerRunner, 0f);
            PlaceRunner(_cpuRunner, 0f);
            UpdateMeterMarker();

            _titleLabel.text = "2Dレース";
            _tapButton.SetEnabled(false);
            SetDisplay(_meterPanel, false);
            SetDisplay(_countdownLabel, false);
            SetDisplay(_judgeLabel, false);
            SetDisplay(_resultPanel, false);
        }

        /// <summary>
        /// カウントダウン → レース進行 → 結果表示を駆動し、「結果を反映」クリックでスコア（勝ち=1／負け=0）を返す。
        /// フェードイン後に呼ばれる想定で、Forget して走らせる。
        /// </summary>
        public async UniTask<int> RunAsync(CancellationToken ct)
        {
            if (_closeSource == null)
            {
                return 0;
            }

            SetDisplay(_countdownLabel, true);
            _model.BeginCountdown();
            await MiniGameCountdown.RunAsync(_countdownLabel, _soundStore, _soundPlayer, ct);
            SetDisplay(_countdownLabel, false);

            _model.StartRacing();
            SetDisplay(_meterPanel, true);
            _tapButton.SetEnabled(true);

            while (_model.Phase.CurrentValue == RaceGamePhase.Racing)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                float dt = Time.deltaTime;

                if (_pauseRemaining > 0f)
                {
                    _pauseRemaining -= dt;
                    if (_pauseRemaining <= 0f)
                    {
                        SetDisplay(_judgeLabel, false);
                    }
                }
                else
                {
                    _meter.Advance(dt);
                }

                _model.Tick(dt);

                UpdateMeterMarker();
                PlaceRunner(_playerRunner, _model.PlayerProgress);
                PlaceRunner(_cpuRunner, _model.CpuProgress);
            }

            _tapButton.SetEnabled(false);
            SetDisplay(_tapButton, false);
            SetDisplay(_meterPanel, false);
            SetDisplay(_judgeLabel, false);

            RevealResult();
            _soundPlayer.PlaySafe(_soundStore?.DecisionSE);

            await _closeSource.Task.AttachExternalCancellation(ct);
            return _model.IsPlayerWin ? 1 : 0;
        }

        public void Dispose()
        {
            if (_tapButton != null)
            {
                _tapButton.clicked -= OnTapClicked;
            }
            if (_closeButton != null)
            {
                _closeButton.clicked -= OnCloseClicked;
            }
            _spriteLoader.Dispose();
        }

        private void OnTapClicked()
        {
            if (_model.Phase.CurrentValue != RaceGamePhase.Racing || _pauseRemaining > 0f)
            {
                return;
            }

            MeterJudgement judgement = _model.ApplyTap(_meter.Value);
            ShowJudge(judgement);
            _pauseRemaining = TapPauseSeconds;
        }

        private void ShowJudge(MeterJudgement judgement)
        {
            switch (judgement)
            {
                case MeterJudgement.Great:
                    _judgeLabel.text = "GREAT!";
                    _judgeLabel.style.color = new StyleColor(new Color(1f, 0.84f, 0.36f));
                    _soundPlayer.PlaySafe(_soundStore?.DecisionSE);
                    break;
                case MeterJudgement.Good:
                    _judgeLabel.text = "GOOD";
                    _judgeLabel.style.color = new StyleColor(new Color(0.55f, 0.85f, 1f));
                    _soundPlayer.PlaySafe(_soundStore?.Enter2SE);
                    break;
                default:
                    _judgeLabel.text = "MISS";
                    _judgeLabel.style.color = new StyleColor(new Color(0.75f, 0.75f, 0.8f));
                    _soundPlayer.PlaySafe(_soundStore?.Enter1SE);
                    break;
            }
            SetDisplay(_judgeLabel, true);
        }

        private void RevealResult()
        {
            _titleLabel.text = "結果";
            _resultLabel.text = _model.IsPlayerWin ? "YOU WIN!" : "YOU LOSE…";
            SetDisplay(_resultPanel, true);
        }

        // Good/Great の帯を config の幅どおりにメーターバー上へ配置する（見た目と判定を一致させる）。
        private void LayoutMeterZones(RaceGameConfig config)
        {
            SetBand(_meterGood, config.GoodHalfWidth);
            SetBand(_meterGreat, config.GreatHalfWidth);
        }

        private static void SetBand(VisualElement band, float halfWidth)
        {
            band.style.left = Length.Percent((0.5f - halfWidth) * 100f);
            band.style.width = Length.Percent(halfWidth * 2f * 100f);
        }

        private void UpdateMeterMarker()
        {
            _meterMarker.style.left = Length.Percent(_meter.Value * 100f);
        }

        // 進捗 0（右端）→ 1（左端）を left% にマップして走者を置く。
        private static void PlaceRunner(VisualElement runner, float progress)
        {
            float percent = StartPercent - Mathf.Clamp01(progress) * (StartPercent - GoalPercent);
            runner.style.left = Length.Percent(percent);
        }

        private async UniTask ApplyRunnerSpriteAsync(VisualElement runner, CharacterId id, CancellationToken ct)
        {
            CharacterDefinition definition = CharacterCatalog.Find(id);
            Sprite sprite = await _spriteLoader.TryLoadAsync(definition.RunAddress, "走行スプライト", ct);
            if (sprite != null)
            {
                runner.style.backgroundImage = new StyleBackground(sprite);
            }
            else
            {
                // 走行絵は明るめのプレースホルダにする（走者シルエットとして目立たせる）。
                runner.style.backgroundImage = StyleKeyword.None;
                runner.style.backgroundColor = CharacterPalette.PlaceholderColor(
                    CharacterCatalog.IndexOf(id), CharacterCatalog.All.Count, 0.55f, 0.85f);
            }
        }

        private static void SetDisplay(VisualElement element, bool visible)
        {
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static int NextSeed()
        {
            return UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        }

        private void OnCloseClicked()
        {
            _closeSource?.TrySetResult();
        }
    }
}
