using System;
using System.Collections.Generic;
using System.Threading;
using Common.Character;
using Common.MiniGame;
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
        private const float ProgressIntervalSeconds = 0.2f; // 自分の進捗を配る間隔（オンラインのみ）
        private const int ProgressScale = 10000;      // 進捗 0〜1 を整数で配るための倍率
        // 途中経過（整数 1 つ）でゴール済みを表すための下駄。ProgressScale より十分大きく取り、
        // 「この値以上＝ゴール済みで、差がゴールタイム（ミリ秒）」という約束で運ぶ。
        private const int FinishedValueOffset = 1000000;
        private const float RemoteFollowSpeed = 12f;  // 相手の走者を届いた位置へ寄せる速さ（1/秒）
        // コースの背景に敷く路面画像。未配置なら RaceGame.uss の地色のままにする。
        private const string TrackBackgroundAddress = "Image/MiniGame/Track";
        // 画面全体に敷くレース会場の絵（コースの背後）。未配置なら RaceGame.uss の地色のまま。
        private const string BackgroundAddress = "Image/Background/RaceBackground";

        private readonly RaceGameModel _model;
        private readonly MiniGameSessionModel _session;
        private readonly SoundStore _soundStore;
        private readonly SoundPlayer _soundPlayer;
        private readonly CharacterSessionModel _characterSession;

        private readonly AddressableSpriteLoader _spriteLoader = new();
        private readonly RaceMeter _meter = new(MeterSweepSpeed);
        // 走者要素（index 0 = プレイヤー、1〜 = CPU）。RunnerCount ぶんレーンとともに動的生成する。
        private readonly List<VisualElement> _runners = new();
        // 画面に出している進捗（_runners と同じ並び）。オンラインの相手は 200ms ごとにしか届かないので、
        // Model の値へ毎フレーム寄せて滑らかに走らせる。自分と一人用の CPU は Model の値をそのまま出す。
        private readonly List<float> _displayProgress = new();
        // 相手のゴールタイム（ミリ秒・_runners と同じ並び／未ゴールは MiniGameRanking.NotFinished）。
        // オンラインで相手がゴールしたときに途中経過へ載せて届く。
        private readonly List<int> _remoteFinishMillis = new();

        private Label _titleLabel;
        private VisualElement _track;
        private Label _countdownLabel;
        private Label _judgeLabel;
        private VisualElement _meterPanel;
        private VisualElement _meterMarker;
        private VisualElement _meterGood;
        private VisualElement _meterGreat;
        private Button _tapButton;
        private VisualElement _resultPanel;
        private Label _resultLabel;
        private VisualElement _resultList;
        private Label _resultNote;
        private Button _closeButton;

        // 結果パネルの順位表（行の生成・並べ替え・文言）。ここは順位の材料を渡すだけ。
        private RaceStandingsView _standings;

        private UniTaskCompletionSource _closeSource;

        private float _pauseRemaining;
        // 「進む」が押されたか。オンラインでは押されるまで相手の走者を動かし続けるため、
        // _closeSource の完了を待つ代わりにこのフラグでループを回す。
        private bool _closeRequested;
        // 自分（走者 index 0）のゴールタイム（ミリ秒）。オンライン対戦で順位を決める結果値になる。
        private int _playerFinishMillis = MiniGameRanking.NotFinished;

        public RaceGamePlay(
            RaceGameModel model,
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
        /// レース UI を構築する（フェードイン前に await される）。走者スプライトのロードと初期配置まで済ませ、
        /// 盤面を見せられる状態にする。進行と入力受付は <see cref="RunAsync"/> 側。
        /// </summary>
        public async UniTask BuildAsync(VisualElement root, CancellationToken ct)
        {
            _titleLabel = root.Q<Label>("TitleLabel");
            _track = root.Q<VisualElement>("Track");
            _countdownLabel = root.Q<Label>("CountdownLabel");
            _judgeLabel = root.Q<Label>("JudgeLabel");
            _meterPanel = root.Q<VisualElement>("MeterPanel");
            _meterMarker = root.Q<VisualElement>("MeterMarker");
            _meterGood = root.Q<VisualElement>("MeterGood");
            _meterGreat = root.Q<VisualElement>("MeterGreat");
            _tapButton = root.Q<Button>("TapButton");
            _resultPanel = root.Q<VisualElement>("ResultPanel");
            _resultLabel = root.Q<Label>("ResultLabel");
            _resultList = root.Q<VisualElement>("ResultList");
            _resultNote = root.Q<Label>("ResultNote");
            _closeButton = root.Q<Button>("CloseButton");
            if (_titleLabel == null || _track == null || _countdownLabel == null
                || _judgeLabel == null || _meterPanel == null || _meterMarker == null || _meterGood == null
                || _meterGreat == null || _tapButton == null || _resultPanel == null || _resultLabel == null
                || _resultList == null || _resultNote == null || _closeButton == null)
            {
                Debug.LogError("RaceGame の UI 要素が見つかりませんでした。");
                return;
            }

            _standings = new RaceStandingsView(_resultLabel, _resultList, _resultNote);

            RaceGameConfig config = RaceGameConfig.Default;
            // 参加者数はセッション（起動側が指定）から取る。未設定（0 以下）のときだけ 2 人へフォールバック。
            int playerCount = _session != null && _session.PlayerCount > 0 ? _session.PlayerCount : 2;
            // オンライン対戦では相手が実プレイヤーなので CPU は自走させない（順位はゴールタイムで決める）。
            _model.Setup(
                config,
                playerCount,
                _session != null ? _session.ResolveSeed() : NextSeed(),
                _session == null || _session.SimulateOpponents);

            // 画面背景・コース背景・走者のキャラ絵はいずれも Addressables なので並列でロードする。
            await UniTask.WhenAll(
                ApplyBackgroundAsync(root, ct),
                ApplyTrackBackgroundAsync(ct),
                BuildRunnersAsync(_model.RunnerCount, ct));

            LayoutMeterZones(config);
            _closeSource = new UniTaskCompletionSource();
            _closeRequested = false;

            _tapButton.clicked += OnTapClicked;
            _closeButton.clicked += OnCloseClicked;

            _meter.Reset();
            _pauseRemaining = 0f;

            PlaceAllRunners();
            UpdateMeterMarker();

            _titleLabel.text = "2Dレース";
            _tapButton.SetEnabled(false);
            SetDisplay(_meterPanel, false);
            SetDisplay(_countdownLabel, false);
            SetDisplay(_judgeLabel, false);
            SetDisplay(_resultPanel, false);
        }

        // 走者数ぶんのレーン（タグ＋走者要素）を Track へ動的生成し、各走者のキャラ絵を並列ロードして貼る。
        // 結果画面の順位表の行も同じ並びでここで作る（並べ替えは表示時に行う）。
        // 走者 index 0＝プレイヤー（選択キャラ）、1〜＝CPU（プレイヤーとも互いとも被らないキャラ）。
        private async UniTask BuildRunnersAsync(int runnerCount, CancellationToken ct)
        {
            // Track から既存のレーンだけ除き、FinishLine（背面ガイド）は残す。
            _track.Query<VisualElement>(className: "race-lane").ForEach(lane => lane.RemoveFromHierarchy());
            _runners.Clear();
            _displayProgress.Clear();
            _remoteFinishMillis.Clear();
            _standings.Clear();

            // 参加者キャラはセッション（起動側が指定）から取る。全走者ぶん揃っていればそれを使い、
            // 揃っていなければ従来解決＝プレイヤーは選択キャラ・CPU は被らないランダム配布にフォールバックする。
            IReadOnlyList<CharacterId> assigned = _session?.Characters;
            bool useAssigned = assigned != null && assigned.Count >= runnerCount;
            IReadOnlyList<CharacterId> cpuFallback = useAssigned
                ? null
                : RaceOpponentPicker.PickMany(
                    _characterSession.Selected,
                    CharacterCatalog.All,
                    Mathf.Max(0, runnerCount - 1),
                    count => UnityEngine.Random.Range(0, count));

            // 走者が増えるほど 1 レーンが狭くなるので、走者サイズをレーン数で縮める。
            float runnerSize = Mathf.Clamp(320f / runnerCount, 30f, 76f);
            float laneHeightPercent = 100f / runnerCount;

            List<UniTask> loads = new(runnerCount);
            for (int runner = 0; runner < runnerCount; runner++)
            {
                bool isPlayer = runner == 0;
                CharacterId id = useAssigned
                    ? assigned[runner]
                    : (isPlayer ? _characterSession.Selected : cpuFallback[runner - 1]);
                string characterName = CharacterCatalog.Find(id).DisplayName;

                VisualElement lane = new();
                lane.AddToClassList("race-lane");
                lane.style.height = Length.Percent(laneHeightPercent);
                lane.pickingMode = PickingMode.Ignore;

                // ラベルはキャラ名（YOU/CPU の代わり）。色分けクラスでプレイヤー／CPU は残す。
                Label tag = new(characterName) { pickingMode = PickingMode.Ignore };
                tag.AddToClassList("race-lane__tag");
                tag.AddToClassList(isPlayer ? "race-lane__tag--player" : "race-lane__tag--cpu");
                lane.Add(tag);

                VisualElement runnerElement = new() { pickingMode = PickingMode.Ignore };
                runnerElement.AddToClassList("race-runner");
                runnerElement.style.width = runnerSize;
                runnerElement.style.height = runnerSize;
                lane.Add(runnerElement);

                _track.Add(lane);
                _runners.Add(runnerElement);
                _displayProgress.Add(0f);
                _remoteFinishMillis.Add(MiniGameRanking.NotFinished);
                _standings.AddRunner(characterName, isPlayer);
                loads.Add(ApplyRunnerSpriteAsync(runnerElement, id, ct));
            }

            await UniTask.WhenAll(loads);
        }

        /// <summary>
        /// いまの状況を順位表へ渡して組み直す。オンラインで相手がまだ走っているうちは**暫定順位**
        /// （ゴールした人＋走行中の人の位置で並べる）になり、全員がゴールしたら確定順位に変わる。
        /// </summary>
        private void RefreshStandings()
        {
            List<RaceEntry> entries = new(_runners.Count);
            for (int runner = 0; runner < _runners.Count; runner++)
            {
                entries.Add(new RaceEntry(
                    runner, IsFinished(runner), MillisOf(runner), _model.Progress(runner)));
            }

            _standings.Refresh(entries, provisional: !OpponentsSimulated && !AllFinished());
        }

        // 走者がゴールしたか。一人用モードは先着で決着するので、決着した走者だけがゴール済み。
        private bool IsFinished(int runner)
        {
            if (OpponentsSimulated)
            {
                return _model.WinnerIndex == runner;
            }
            return runner == 0
                ? _playerFinishMillis != MiniGameRanking.NotFinished
                : _remoteFinishMillis[runner] != MiniGameRanking.NotFinished;
        }

        // 走者のゴールタイム（分からなければ NotFinished）。自分ぶんは自分で計り、相手ぶんは届いた値。
        private int MillisOf(int runner)
        {
            if (runner == 0)
            {
                return _playerFinishMillis;
            }
            return OpponentsSimulated ? MiniGameRanking.NotFinished : _remoteFinishMillis[runner];
        }

        // 全走者のゴールタイムが揃ったか（＝順位が確定したか）。オンラインでのみ意味を持つ。
        private bool AllFinished()
        {
            for (int runner = 0; runner < _runners.Count; runner++)
            {
                if (!IsFinished(runner))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>全走者を Model の進捗どおりに置き直す（補間を挟まない初期配置用）。</summary>
        private void PlaceAllRunners()
        {
            for (int runner = 0; runner < _runners.Count; runner++)
            {
                float progress = _model.Progress(runner);
                _displayProgress[runner] = progress;
                PlaceRunner(_runners[runner], progress);
            }
        }

        /// <summary>
        /// 走者を進捗どおりに動かす（毎フレーム）。オンラインの相手は <see cref="ProgressIntervalSeconds"/>
        /// 間隔でしか値が届かず、そのまま置くとカクついて見えるので、届いた位置へ滑らかに寄せる。
        /// 自分（index 0）と一人用の CPU は毎フレーム動くので Model の値をそのまま出す。
        /// </summary>
        private void UpdateRunnerPositions(float deltaSeconds)
        {
            bool smoothOpponents = !OpponentsSimulated;
            float follow = 1f - Mathf.Exp(-RemoteFollowSpeed * Mathf.Max(0f, deltaSeconds));
            for (int runner = 0; runner < _runners.Count; runner++)
            {
                float target = _model.Progress(runner);
                float shown = runner > 0 && smoothOpponents
                    ? Mathf.Lerp(_displayProgress[runner], target, follow)
                    : target;
                _displayProgress[runner] = shown;
                PlaceRunner(_runners[runner], shown);
            }
        }

        /// <summary>相手をこのクライアントでシミュレートしているか（＝一人用モード）。</summary>
        private bool OpponentsSimulated => _session == null || _session.SimulateOpponents;

        /// <summary>
        /// 自分の状況を配り、届いている相手の状況を走者へ反映する（オンラインのみ）。
        /// 一人用モードでは相手をゲーム内の CPU が自走させるので何もしない。
        /// 運べるのは整数 1 つなので、走行中は進捗（<see cref="ProgressScale"/> 倍）、ゴール後は
        /// <see cref="FinishedValueOffset"/>＋ゴールタイム（ミリ秒）に載せ替えて送る。
        /// </summary>
        private void PublishAndApplyProgress()
        {
            MiniGameProgressChannel progress = _session?.Progress;
            if (progress == null)
            {
                return;
            }

            progress.Publish(_playerFinishMillis != MiniGameRanking.NotFinished
                ? FinishedValueOffset + _playerFinishMillis
                : Mathf.RoundToInt(Mathf.Clamp01(_model.Progress(0)) * ProgressScale));

            // 走者数（最低 2 にクランプされる）が配列より多いことがあるので、短い方に合わせる。
            int count = Mathf.Min(_model.RunnerCount, progress.Values.Length);
            for (int runner = 1; runner < count; runner++)
            {
                int value = progress.Values[runner];
                if (value >= FinishedValueOffset)
                {
                    _remoteFinishMillis[runner] = value - FinishedValueOffset;
                    _model.SetProgress(runner, 1f);
                    continue;
                }
                _model.SetProgress(runner, value / (float)ProgressScale);
            }
        }

        /// <summary>
        /// カウントダウン → レース進行 → 結果表示を駆動し、「進む」クリックでスコア（勝ち=1／負け=0）を返す。
        /// フェードイン後に呼ばれる想定で、Forget して走らせる。
        /// </summary>
        public async UniTask<(int Score, int Value)> RunAsync(CancellationToken ct)
        {
            if (_closeSource == null)
            {
                return (0, MiniGameRanking.NotFinished);
            }

            SetDisplay(_countdownLabel, true);
            _model.BeginCountdown();
            await MiniGameCountdown.RunAsync(_countdownLabel, _soundStore, _soundPlayer, ct);
            SetDisplay(_countdownLabel, false);

            _model.StartRacing();
            SetDisplay(_meterPanel, true);
            _tapButton.SetEnabled(true);

            // 自分がゴールした時刻（レース開始からの経過ミリ秒）。オンラインの順位付けに使う結果値で、
            // ゴールできずに決着したときは NotFinished のまま残る。
            _playerFinishMillis = MiniGameRanking.NotFinished;
            float raceElapsed = 0f;
            float sinceProgress = 0f;

            while (_model.Phase.CurrentValue == RaceGamePhase.Racing)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                float dt = Time.deltaTime;
                raceElapsed += dt;

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

                // 自分がゴールラインに達した瞬間のタイムを 1 度だけ控える。
                if (_playerFinishMillis == MiniGameRanking.NotFinished && _model.Progress(0) >= 1f)
                {
                    _playerFinishMillis = Mathf.RoundToInt(raceElapsed * 1000f);
                }

                // オンライン対戦では相手を自走させない代わりに、互いの進捗を配り合って走者を動かす
                // （見た目だけの情報なので、間引いて送り取りこぼしは気にしない）。
                sinceProgress += dt;
                if (sinceProgress >= ProgressIntervalSeconds)
                {
                    sinceProgress = 0f;
                    PublishAndApplyProgress();
                }

                UpdateMeterMarker();
                UpdateRunnerPositions(dt);
            }

            // ゴールした自分の位置を送り切ってから締める（間引きの都合で数回ぶん遅れていることがある）。
            PublishAndApplyProgress();

            _tapButton.SetEnabled(false);
            SetDisplay(_tapButton, false);
            SetDisplay(_meterPanel, false);
            SetDisplay(_judgeLabel, false);

            RevealResult();
            _soundPlayer.PlaySafe(_soundStore?.DecisionSE);

            await WaitForCloseAsync(ct);
            // 結果値はゴールまでのミリ秒（未ゴールは NotFinished）。オンラインでは最短の人が勝ち。
            return (_model.IsPlayerWin ? 1 : 0, _playerFinishMillis);
        }

        /// <summary>
        /// 「進む」が押されるまで待つ。オンラインでは自分が先にゴールしても相手はまだ走っているので、
        /// 待っている間も進捗を配り／反映して相手の走者を動かし続け、順位表も更新する
        /// （自分の位置はゴールのまま変わらず、全員がゴールしたところで暫定順位が確定順位に変わる）。
        /// </summary>
        private async UniTask WaitForCloseAsync(CancellationToken ct)
        {
            if (OpponentsSimulated)
            {
                await _closeSource.Task.AttachExternalCancellation(ct);
                return;
            }

            float sinceProgress = 0f;
            while (!_closeRequested)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                float dt = Time.deltaTime;
                sinceProgress += dt;
                if (sinceProgress >= ProgressIntervalSeconds)
                {
                    sinceProgress = 0f;
                    PublishAndApplyProgress();
                    RefreshStandings();
                }
                UpdateRunnerPositions(dt);
            }
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
            RefreshStandings();
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

        /// <summary>
        /// 画面全体にレース会場の絵を貼る。貼り先は <see cref="BuildAsync"/> に渡る <paramref name="root"/>
        /// （＝UXML ルートの親）ではなく子の <c>RaceRoot</c>（親へ貼っても不透明な地色を持つ子に隠れて見えない）。
        /// 拡大縮小は USS（<c>.race-root</c> の <c>background-size: cover</c>）に任せ、未配置なら地色のまま。
        /// </summary>
        private async UniTask ApplyBackgroundAsync(VisualElement root, CancellationToken ct)
        {
            VisualElement target = root?.Q<VisualElement>("RaceRoot");
            if (target == null)
            {
                return;
            }
            Sprite sprite = await _spriteLoader.TryLoadAsync(BackgroundAddress, "レース会場背景", ct);
            if (sprite != null)
            {
                target.style.backgroundImage = new StyleBackground(sprite);
            }
        }

        /// <summary>
        /// コースの背景に路面画像を貼る。拡大縮小は USS（<c>.race-track</c> の <c>background-size: cover</c>）に任せる。
        /// 未配置のときは何もしない＝USS の地色がそのまま見える。
        /// </summary>
        private async UniTask ApplyTrackBackgroundAsync(CancellationToken ct)
        {
            Sprite sprite = await _spriteLoader.TryLoadAsync(TrackBackgroundAddress, "コース背景", ct);
            if (sprite != null)
            {
                _track.style.backgroundImage = new StyleBackground(sprite);
            }
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
                runner.style.backgroundColor = CharacterPalette.PlaceholderColorFor(id, 0.55f, 0.85f);
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
            _closeRequested = true;
            _closeSource?.TrySetResult();
        }
    }
}
