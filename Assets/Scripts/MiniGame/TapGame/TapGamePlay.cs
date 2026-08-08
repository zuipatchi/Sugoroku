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
    /// **全参加者（自分＋相手）のキャラカードを横一列に並べ、その参加者が叩くたびにそのカードだけを**
    /// 「がたがた」振動＋「パンチ」拡大で弾ませる（<see cref="TapCardShaker"/>）。自分は自分のタップで、
    /// 相手は連打数の増加（一人用は CPU の自動連打／オンラインは届いた値）を見て弾ませる。
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
        // キャラカードの寸法。カード絵は 1:1 なので枠も正方形にし（scale-to-fit で絵の幅が枠幅どおりになる）、
        // 人数が増えたら並べる枠（.tap-character-area）の幅に収まるよう縮める。
        // 実際の寸法は枠の実寸から決めるので（LayoutCards）、ここでは上限・下限だけ持つ。
        private const float CardMaxWidth = 190f;
        private const float CardMinWidth = 70f;
        // 1 枚あたりの左右マージン合計（.tap-character-card の margin-left + margin-right）。
        private const float CardMarginPx = 8f;

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
        private VisualElement _characterArea;
        private VisualElement _scoreboard;
        private VisualElement _resultPanel;
        private Label _resultLabel;
        private VisualElement _resultList;
        private Label _resultNote;
        private Button _closeButton;

        // 結果パネルの順位表（全参加者ぶん。行の生成・並べ替えは共通ビューが担う）。
        private MiniGameStandingsView _standings;

        // スコアボードの各参加者チップの連打数ラベル（index＝参加者）。毎フレーム更新する。
        private readonly List<Label> _scoreCountLabels = new();

        // 参加者ごとのキャラカードの弾み（index＝参加者）。叩いた本人のカードだけを弾ませる。
        private readonly List<TapCardShaker> _cardShakers = new();
        // 参加者ごとのキャラカード本体（index＝参加者）。並べる枠の実寸が決まってから寸法を入れる。
        private readonly List<VisualElement> _cardElements = new();
        // 相手の連打を検知するために覚えておく前フレームの連打数（index＝参加者）。
        private readonly List<int> _lastTapCounts = new();

        private readonly AddressableSpriteLoader _spriteLoader = new();
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
            _characterArea = root.Q<VisualElement>("CharacterArea");
            _scoreboard = root.Q<VisualElement>("Scoreboard");
            _resultPanel = root.Q<VisualElement>("ResultPanel");
            _resultLabel = root.Q<Label>("ResultLabel");
            _resultList = root.Q<VisualElement>("ResultList");
            _resultNote = root.Q<Label>("ResultNote");
            _closeButton = root.Q<Button>("CloseButton");
            if (_timerLabel == null || _countLabel == null || _centerLabel == null || _tapButton == null
                || _scoreboard == null || _resultPanel == null || _resultLabel == null || _resultList == null
                || _resultNote == null || _closeButton == null)
            {
                Debug.LogError("TapGame の UI 要素が見つかりませんでした。");
                return;
            }

            _standings = new MiniGameStandingsView(_resultList, "tap-standing");

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
            // カードの寸法は枠の実寸から決めるので、レイアウトが決まった（変わった）ら入れ直す。
            _characterArea?.RegisterCallback<GeometryChangedEvent>(OnCharacterAreaGeometryChanged);

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
            await UniTask.WhenAll(ApplyBackgroundAsync(root, ct), ApplyTapTargetAsync(ct), BuildCharacterCardsAsync(ct));
            await BuildScoreboardAsync(ct);
        }

        // 参加者数ぶんのスコアボードチップ（キャラアイコン＋連打数）を生成し、アイコンを並列ロードして貼る。
        // 連打数ラベルは _scoreCountLabels に控え、進行中は毎フレーム更新する。
        // 同じ参加者の並びで結果パネルの順位表の行も作っておく（中身は RefreshStandings で埋める）。
        private async UniTask BuildScoreboardAsync(CancellationToken ct)
        {
            _scoreboard.Clear();
            _scoreCountLabels.Clear();
            _standings.Clear();

            int count = _model.ParticipantCount;

            List<UniTask> loads = new(count);
            for (int p = 0; p < count; p++)
            {
                bool isPlayer = p == 0;
                CharacterId id = CharacterFor(p);

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
                _standings.AddParticipant(CharacterCatalog.Find(id).DisplayName, isPlayer);
                loads.Add(ApplyChipIconAsync(icon, id, ct));
            }

            await UniTask.WhenAll(loads);
        }

        /// <summary>
        /// その参加者のキャラ。起動側が渡したセッション指定（index 0＝自分）を優先し、
        /// 未指定のときは自分は選択キャラ・相手はカタログ順にフォールバックする。
        /// </summary>
        private CharacterId CharacterFor(int participant)
        {
            IReadOnlyList<CharacterId> characters = _session?.Characters;
            if (characters != null && characters.Count > participant)
            {
                return characters[participant];
            }
            return participant == 0
                ? _characterSession.Selected
                : CharacterCatalog.All[participant % CharacterCatalog.All.Count].Id;
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

                // 連打数が増えた相手のカードを弾ませる（自分は OnTapClicked で弾ませている）。
                ShakeOpponentsOnTap();
                UpdateScoreboard();
            }

            // 最後の値を送り切ってから締める（間引きの都合で数回ぶん遅れていることがある）。
            PublishAndApplyProgress();
            ShakeOpponentsOnTap();

            _model.Finish();
            UpdateScoreboard();
            _soundPlayer.PlaySafe(_soundStore?.DecisionSE);

            int playerTaps = _model.TapCount.CurrentValue;
            bool win = _model.IsPlayerWin;
            RefreshStandings();

            await _closeSource.Task.AttachExternalCancellation(ct);
            // 結果値はタップ数。オンラインでは全員ぶんを持ち寄って最多の人が勝ちになる。
            return (win ? 1 : 0, playerTaps);
        }

        /// <summary>
        /// 全参加者の連打数を順位表へ渡して組み直す（見出しは自分の順位）。オンライン対戦では最後の
        /// 数回ぶんが届いていないことがあるので**暫定順位**として注記を添える（正式な勝敗は全員の
        /// 結果値が揃ってから盤面側が発表する）。
        /// </summary>
        private void RefreshStandings()
        {
            int count = _model.ParticipantCount;
            List<int> taps = new(count);
            for (int p = 0; p < count; p++)
            {
                taps.Add(_model.TapCountOf(p));
            }

            IReadOnlyList<ScoreStanding> ordered = ScoreRanking.Order(taps);
            List<StandingLine> lines = new(ordered.Count);
            int myRank = 1;
            foreach (ScoreStanding standing in ordered)
            {
                lines.Add(new StandingLine(
                    standing.Participant, $"{standing.Rank}位", $"{taps[standing.Participant]} 回"));
                if (standing.Participant == 0)
                {
                    myRank = standing.Rank;
                }
            }
            _standings.Refresh(lines);

            bool provisional = !OpponentsSimulated;
            _resultLabel.text = !provisional && myRank == 1 ? "1位！" : $"{myRank}位 / {count}人";
            _resultNote.text = provisional ? "ほかのプレイヤーの結果を集計中…（暫定）" : string.Empty;
            _resultNote.style.display = provisional ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static int NextSeed()
        {
            return UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        }

        public void Dispose()
        {
            DisposeCardShakers();
            _characterArea?.UnregisterCallback<GeometryChangedEvent>(OnCharacterAreaGeometryChanged);
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

        /// <summary>
        /// 参加者ぶんのキャラカードを横一列に生成し、カード絵を並列ロードして貼る。
        /// カードごとに弾み（<see cref="TapCardShaker"/>）を持たせ、叩いた本人のカードだけを弾ませる。
        /// 未配置のキャラ絵はプレースホルダ（色面）にフォールバックする。
        /// </summary>
        private async UniTask BuildCharacterCardsAsync(CancellationToken ct)
        {
            if (_characterArea == null)
            {
                return;
            }

            _characterArea.Clear();
            DisposeCardShakers();
            _cardElements.Clear();
            _lastTapCounts.Clear();

            int count = _model.ParticipantCount;

            List<UniTask> loads = new(count);
            for (int p = 0; p < count; p++)
            {
                CharacterId id = CharacterFor(p);

                // カード 1 枚ぶんの枠。自分だけカードの上に「あなた」を載せる。
                // 弾みはこの枠に掛けるので、目印もカードと一緒に震える。
                // 枠は下端をそろえて並ぶので、目印のぶん高くなってもカードの並びは崩れない。
                VisualElement slot = new() { pickingMode = PickingMode.Ignore };
                slot.AddToClassList("tap-character-slot");
                if (p == 0)
                {
                    Label you = new("あなた") { pickingMode = PickingMode.Ignore };
                    you.AddToClassList("tap-character-you");
                    slot.Add(you);
                }

                VisualElement card = new() { pickingMode = PickingMode.Ignore };
                card.AddToClassList("tap-character-card");
                // 寸法は枠の実寸が決まってから入れる（LayoutCards）。それまでは上限の大きさで置く。
                card.style.width = CardMaxWidth;
                card.style.height = CardMaxWidth;

                slot.Add(card);
                _characterArea.Add(slot);
                _cardElements.Add(card);
                _cardShakers.Add(new TapCardShaker(slot));
                _lastTapCounts.Add(0);
                loads.Add(ApplyCardImageAsync(card, id, ct));
            }

            LayoutCards();
            await UniTask.WhenAll(loads);
        }

        // 並べる枠の実寸から 1 枚の寸法を決めて全カードへ入れる（USS 側の幅を変えても追従する）。
        // 枠のレイアウトが決まる前は寸法が読めないので、GeometryChangedEvent でもう一度呼ばれる。
        private void LayoutCards()
        {
            if (_characterArea == null || _cardElements.Count == 0)
            {
                return;
            }

            float areaWidth = _characterArea.resolvedStyle.width;
            if (float.IsNaN(areaWidth) || areaWidth <= 0f)
            {
                // まだ表示されていない（レイアウト前）。次の geometry で拾う。
                return;
            }

            // カード絵は 1:1 なので枠も正方形にする（縦に余白を作らず、絵の大きさが枠幅どおりになる）。
            float size = CardSizeFor(_cardElements.Count, areaWidth);
            foreach (VisualElement card in _cardElements)
            {
                card.style.width = size;
                card.style.height = size;
            }
        }

        // 人数ぶんのカードが枠に収まる 1 枚の寸法（左右マージンぶんを差し引いて等分し、上下限で丸める）。
        private static float CardSizeFor(int count, float areaWidth)
        {
            if (count <= 0)
            {
                return CardMaxWidth;
            }
            float perCard = (areaWidth / count) - CardMarginPx;
            return Mathf.Clamp(perCard, CardMinWidth, CardMaxWidth);
        }

        private async UniTask ApplyCardImageAsync(VisualElement card, CharacterId id, CancellationToken ct)
        {
            CharacterDefinition definition = CharacterCatalog.Find(id);
            Sprite sprite = await _spriteLoader.TryLoadAsync(definition.CardAddress, "キャラカード", ct);
            if (sprite != null)
            {
                card.style.backgroundImage = new StyleBackground(sprite);
            }
            else
            {
                card.style.backgroundImage = StyleKeyword.None;
                card.style.backgroundColor = CharacterPalette.PlaceholderColorFor(id);
            }
        }

        /// <summary>
        /// 相手（index 1〜）の連打を検知して、その参加者のカードだけを弾ませる。
        /// 一人用モードは CPU の自動連打（<see cref="TapGameModel.Tick"/>）、オンラインは
        /// 届いた連打数（<see cref="PublishAndApplyProgress"/>）で数が増えるので、どちらも同じ経路で見える。
        /// オンラインは値が間引かれて届くため、まとめて増えたぶんは 1 回の弾みになる。
        /// </summary>
        private void ShakeOpponentsOnTap()
        {
            for (int p = 1; p < _cardShakers.Count && p < _lastTapCounts.Count; p++)
            {
                int current = _model.TapCountOf(p);
                if (current > _lastTapCounts[p])
                {
                    _cardShakers[p].Shake();
                }
                _lastTapCounts[p] = current;
            }
        }

        private void DisposeCardShakers()
        {
            foreach (TapCardShaker shaker in _cardShakers)
            {
                shaker.Dispose();
            }
            _cardShakers.Clear();
        }

        private void OnCharacterAreaGeometryChanged(GeometryChangedEvent _)
        {
            LayoutCards();
        }

        private void OnTapClicked()
        {
            _model.Tap();
            // 自分（index 0）のカードは押した瞬間に弾ませる（相手は連打数の増加で弾む）。
            if (_cardShakers.Count > 0)
            {
                _cardShakers[0].Shake();
            }
            _soundPlayer.PlaySafe(_soundStore?.RandomPunchSE);
        }

        private void OnCloseClicked()
        {
            // 結果を起動側（Main）へ返すため、RunAsync の待機を解く。
            _closeSource?.TrySetResult();
        }
    }
}
