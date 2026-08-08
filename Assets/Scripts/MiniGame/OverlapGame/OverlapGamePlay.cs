using System;
using System.Collections.Generic;
using System.Threading;
using Common.Character;
using Common.MiniGame;
using Common.SoundManagement;
using Common.Store;
using Cysharp.Threading.Tasks;
using Main.Item;
using UnityEngine;
using UnityEngine.UIElements;

namespace MiniGame.OverlapGame
{
    /// <summary>
    /// 「被っちゃやーよ」の UI 構築と進行。<see cref="MiniGameHostPresenter"/> からクローン済みの UXML ルートを
    /// 受け取り、カウントダウン → アイテム選択 → 全員オープン → 結果まで駆動する。
    /// 提示アイテム・被り判定・勝敗は <see cref="OverlapGameModel"/> が持ち、ここは表示・入力・時間駆動だけを担う。
    /// 選択カードは提示アイテム数（＝参加者数）に応じて動的生成する。
    ///
    /// オンライン対戦では相手の選択を <see cref="MiniGameProgressChannel"/> で配り合ってから開示するので、
    /// 一人用モード（CPU の選択を <see cref="OverlapGameModel"/> が持つ）と同じく
    /// 「誰がどのカードを選んだか」がカード上のキャラ名バッジで分かる。
    /// </summary>
    public sealed class OverlapGamePlay : IDisposable
    {
        private const int RevealPauseMs = 900;  // オープン演出（バッジ表示）を見せてから結果を出すまでの間
        // 画面の背景に敷く店内の絵。アイテムを選ぶゲームなのでアイテムショップと同じ絵を使う。
        private const string BackgroundAddress = "Image/Shop";

        // オンラインで相手の選択を待つ上限。制限時間（ChoiceSeconds）いっぱい粘る相手がいても届くよう、
        // ゲーム開始タイミングのずれぶんの余裕を足してある。全員ぶん揃えば待たずに進む。
        private const float OpponentWaitSeconds = 8f;
        // 自分の選択を配り直す間隔。途中経過の経路はポーリングで取りこぼしに強い代わりに、
        // 相手がまだ受信を始めていない時期に配ったぶんは届かないので、揃うまで繰り返し送る。
        private const float PublishIntervalSeconds = 0.2f;
        // 選択がまだ届いていない参加者を表す値（未選択＝無効票は MiniGameRanking.NoChoice＝-1 と区別する）。
        private const int UnknownChoice = -2;
        // 途中経過は整数 1 つしか運べず 0 が「未送信」を意味するので、選択 index に下駄を履かせて送る
        // （未送信=0 / 無効票(-1)=1 / index 0=2 …）。
        private const int ChoiceValueOffset = 2;
        // 結果一覧の並び順にもなる結果の区分（獲得 → 未確定 → かぶり → 時間切れ）。
        private const int OutcomeWon = 0;
        private const int OutcomeUnknown = 1;
        private const int OutcomeOverlapped = 2;
        private const int OutcomeInvalid = 3;

        private readonly OverlapGameModel _model;
        private readonly MiniGameSessionModel _session;
        private readonly SoundStore _soundStore;
        private readonly SoundPlayer _soundPlayer;

        private readonly AddressableSpriteLoader _spriteLoader = new();
        private readonly List<CardView> _cards = new();
        // 参加者ごとの選択カード index（index 0＝自分／-1＝無効票／UnknownChoice＝未着）。
        // 一人用は Model の CPU 選択から、オンラインは途中経過の経路から埋める。
        private readonly List<int> _choices = new();

        private bool _allChoicesKnown;

        private Label _titleLabel;
        private Label _hintLabel;
        private VisualElement _choiceRow;
        private Label _countdownLabel;
        private Label _timerLabel;
        private VisualElement _resultPanel;
        private Label _resultLabel;
        private VisualElement _resultList;
        private Button _closeButton;

        // 結果パネルに出す全参加者ぶんの一覧（行の生成・並べ替えは共通ビューが担う）。
        private MiniGameStandingsView _standings;

        private UniTaskCompletionSource _closeSource;

        public OverlapGamePlay(
            OverlapGameModel model,
            MiniGameSessionModel session,
            SoundStore soundStore,
            SoundPlayer soundPlayer)
        {
            _model = model;
            _session = session;
            _soundStore = soundStore;
            _soundPlayer = soundPlayer;
        }

        /// <summary>
        /// UI を構築する（フェードイン前に await される）。提示アイテムのカードとその画像ロードまで済ませ、
        /// 画面を見せられる状態にする。進行と入力受付は <see cref="RunAsync"/> 側。
        /// </summary>
        public async UniTask BuildAsync(VisualElement root, CancellationToken ct)
        {
            _titleLabel = root.Q<Label>("TitleLabel");
            _hintLabel = root.Q<Label>("HintLabel");
            _choiceRow = root.Q<VisualElement>("ChoiceRow");
            _countdownLabel = root.Q<Label>("CountdownLabel");
            _timerLabel = root.Q<Label>("TimerLabel");
            _resultPanel = root.Q<VisualElement>("ResultPanel");
            _resultLabel = root.Q<Label>("ResultLabel");
            _resultList = root.Q<VisualElement>("ResultList");
            _closeButton = root.Q<Button>("CloseButton");
            if (_titleLabel == null || _hintLabel == null || _choiceRow == null || _countdownLabel == null
                || _timerLabel == null || _resultPanel == null || _resultLabel == null || _resultList == null
                || _closeButton == null)
            {
                Debug.LogError("OverlapGame の UI 要素が見つかりませんでした。");
                return;
            }

            _standings = new MiniGameStandingsView(_resultList, "overlap-standing");

            // 参加者数はセッション（起動側が指定）から取る。未設定（0 以下）のときだけ既定へフォールバック。
            int playerCount = _session.PlayerCount > 0 ? _session.PlayerCount : OverlapGameConfig.DefaultPlayerCount;
            // オンライン対戦では相手が実プレイヤーなので CPU の選択は作らない。提示カードを全員で揃えるため、
            // 種は起動側が配った共有の種（無ければその場の乱数）を使う。
            _model.Setup(playerCount, _session.ResolveSeed(), _session.SimulateOpponents);

            _choices.Clear();
            _standings.Clear();
            for (int i = 0; i < _model.PlayerCount; i++)
            {
                _choices.Add(UnknownChoice);
                // 結果パネルの一覧も参加者と同じ並びで作っておく（中身は RefreshStandings で埋める）。
                _standings.AddParticipant(LabelForParticipant(i), i == 0);
            }
            _allChoicesKnown = false;

            _closeSource = new UniTaskCompletionSource();
            _closeButton.clicked += OnCloseClicked;

            // カード絵と背景は互いに独立なので並列にロードする（表示前に両方そろえる）。
            await UniTask.WhenAll(BuildCardsAsync(ct), ApplyBackgroundAsync(root, ct));

            _titleLabel.text = "被っちゃやーよ";
            _hintLabel.text = "ほかの人とかぶらないアイテムを選ぼう！";
            SetCardsEnabled(false);
            SetDisplay(_countdownLabel, false);
            SetDisplay(_timerLabel, false);
            SetDisplay(_resultPanel, false);
        }

        /// <summary>
        /// カウントダウン → 選択待ち → オープン → 結果表示を駆動し、「進む」クリックで
        /// スコア（獲得＝被らなかった=1／被った=0）を返す。フェードイン後に呼ばれる想定で Forget して走らせる。
        /// </summary>
        public async UniTask<(int Score, int Value)> RunAsync(CancellationToken ct)
        {
            if (_closeSource == null)
            {
                return (0, MiniGameRanking.NoChoice);
            }

            SetDisplay(_countdownLabel, true);
            _model.BeginCountdown();
            await MiniGameCountdown.RunAsync(_countdownLabel, _soundStore, _soundPlayer, ct);
            SetDisplay(_countdownLabel, false);

            _model.BeginChoosing();
            _hintLabel.text = "どれを選ぶ？";
            SetCardsEnabled(true);
            SetDisplay(_timerLabel, true);

            await WaitForChoiceOrTimeoutAsync(ct);

            SetCardsEnabled(false);
            SetDisplay(_timerLabel, false);

            await CollectChoicesAsync(ct);

            _hintLabel.text = "オープン！";
            RevealChoices();
            _soundPlayer.PlaySafe(_soundStore?.Enter3SE);

            await UniTask.Delay(RevealPauseMs, cancellationToken: ct);

            RevealResult();

            await _closeSource.Task.AttachExternalCancellation(ct);
            _model.Finish();
            // 結果値は選んだカードの index（未選択は NoChoice）。オンラインでは誰とも被らなければ勝ち。
            return (IsLocalWin ? 1 : 0, _model.PlayerChoiceIndex);
        }

        // 制限時間 ChoiceSeconds の間だけプレイヤーの選択を待つ。クリックで Model が Revealed へ進むとループを抜ける。
        // 時間内に選べなければ TimeOut で無効票にする。
        private async UniTask WaitForChoiceOrTimeoutAsync(CancellationToken ct)
        {
            float remaining = OverlapGameConfig.ChoiceSeconds;
            while (_model.Phase.CurrentValue == OverlapGamePhase.Choosing && remaining > 0f)
            {
                _timerLabel.text = $"のこり {Mathf.Max(0, Mathf.CeilToInt(remaining))}";
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                remaining -= Time.deltaTime;
            }

            if (_model.Phase.CurrentValue == OverlapGamePhase.Choosing)
            {
                _model.TimeOut();
            }
        }

        /// <summary>
        /// 開示に使う「誰がどのカードを選んだか」を全参加者ぶん集める。
        /// 一人用モードは CPU の選択を <see cref="OverlapGameModel"/> が確定済みなので即座に埋まる。
        /// オンライン対戦は相手が実プレイヤーなので、自分の選択を配りつつ相手のぶんが届くのを待つ
        /// （<see cref="OpponentWaitSeconds"/> で打ち切る。取りこぼしても正式な勝敗は盤面側が
        /// 全員の結果値を突き合わせて決めるので、ここは見た目だけが欠ける）。
        /// </summary>
        private async UniTask CollectChoicesAsync(CancellationToken ct)
        {
            if (_choices.Count == 0)
            {
                return;
            }
            _choices[0] = _model.PlayerChoiceIndex;

            if (OpponentsSimulated)
            {
                IReadOnlyList<int> cpuChoices = _model.CpuChoiceIndices;
                for (int i = 0; i < cpuChoices.Count && i + 1 < _choices.Count; i++)
                {
                    _choices[i + 1] = cpuChoices[i];
                }
                _allChoicesKnown = true;
                return;
            }

            MiniGameProgressChannel progress = _session?.Progress;
            if (progress == null)
            {
                return;
            }

            _hintLabel.text = "ほかのプレイヤーを待っています...";

            float waited = 0f;
            float sincePublish = PublishIntervalSeconds;  // 初回は待たずに配る
            while (true)
            {
                if (sincePublish >= PublishIntervalSeconds)
                {
                    sincePublish = 0f;
                    progress.Publish(EncodeChoice(_choices[0]));
                }
                ApplyReceivedChoices(progress);
                if (_allChoicesKnown || waited >= OpponentWaitSeconds)
                {
                    return;
                }

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                float dt = Time.deltaTime;
                waited += dt;
                sincePublish += dt;
            }
        }

        // 届いている相手の選択を取り込む（一度埋まった参加者は上書きしない）。
        private void ApplyReceivedChoices(MiniGameProgressChannel progress)
        {
            // 参加者数と経路の配列長は起動側が揃えているが、食い違っても落ちないよう短い方に合わせる。
            int count = Mathf.Min(_choices.Count, progress.Values.Length);
            bool all = true;
            for (int participant = 1; participant < _choices.Count; participant++)
            {
                if (_choices[participant] == UnknownChoice && participant < count)
                {
                    _choices[participant] = DecodeChoice(progress.Values[participant]);
                }
                all &= _choices[participant] != UnknownChoice;
            }
            _allChoicesKnown = all;
        }

        private static int EncodeChoice(int choice)
        {
            return choice + ChoiceValueOffset;
        }

        private static int DecodeChoice(int value)
        {
            return value <= 0 ? UnknownChoice : value - ChoiceValueOffset;
        }

        /// <summary>相手をこのクライアントでシミュレートしているか（＝一人用モード・MiniGameTest）。</summary>
        private bool OpponentsSimulated => _session == null || _session.SimulateOpponents;

        /// <summary>
        /// 集まった選択から見た自分の勝ち（＝選べていて、全員ぶん揃ったうえで誰とも被っていない）。
        /// 一人用モードは必ず揃うので <see cref="OverlapGameModel.IsPlayerWin"/> と同じ。オンライン対戦で
        /// 相手の選択が届かなかったときは false に倒す（正式な勝敗は結果値を突き合わせる盤面側が決める）。
        /// </summary>
        private bool IsLocalWin =>
            !_model.IsPlayerVoteInvalid && !IsPlayerOverlapped() && _allChoicesKnown;

        /// <summary>自分の選んだカードが他の誰かと被っているか（未着の参加者は判定に含めない）。</summary>
        private bool IsPlayerOverlapped()
        {
            return IsOverlapped(0);
        }

        /// <summary>
        /// <paramref name="participant"/> の選んだカードが他の誰かと被っているか。
        /// 選べていない（無効票・未着）参加者は被りようがないので false。
        /// </summary>
        private bool IsOverlapped(int participant)
        {
            if (participant >= _choices.Count || _choices[participant] < 0)
            {
                return false;
            }
            for (int other = 0; other < _choices.Count; other++)
            {
                if (other != participant && _choices[other] == _choices[participant])
                {
                    return true;
                }
            }
            return false;
        }

        public void Dispose()
        {
            if (_closeButton != null)
            {
                _closeButton.clicked -= OnCloseClicked;
            }
            _spriteLoader.Dispose();
        }

        /// <summary>
        /// 画面の背景に店内の絵を貼る。拡大縮小は USS（<c>.overlap-root</c> の <c>background-size: cover</c>）に任せる。
        /// 未配置のときは何もしない＝USS の地色がそのまま見える（2Dレースのコース背景と同じ扱い）。
        /// 貼り先は渡された <paramref name="root"/>（＝<c>UIDocument.rootVisualElement</c>）ではなく、
        /// その子の <c>OverlapRoot</c>。親へ貼っても不透明な地色を持つ子に隠れて見えない。
        /// </summary>
        private async UniTask ApplyBackgroundAsync(VisualElement root, CancellationToken ct)
        {
            VisualElement target = root?.Q<VisualElement>("OverlapRoot");
            if (target == null)
            {
                return;
            }
            Sprite sprite = await _spriteLoader.TryLoadAsync(BackgroundAddress, "ショップ背景", ct);
            if (sprite != null)
            {
                target.style.backgroundImage = new StyleBackground(sprite);
            }
        }

        // 提示アイテムぶんのカードを生成し、画像を並列ロードして貼る。
        private async UniTask BuildCardsAsync(CancellationToken ct)
        {
            _choiceRow.Clear();
            _cards.Clear();

            IReadOnlyList<ItemId> offered = _model.OfferedItems;
            List<UniTask> loads = new(offered.Count);
            for (int i = 0; i < offered.Count; i++)
            {
                int index = i;
                CardView card = CreateCard(offered[i]);
                card.Button.clicked += () => OnCardClicked(index);
                _choiceRow.Add(card.Button);
                _cards.Add(card);
                loads.Add(ApplyItemImageAsync(card, offered[i], ct));
            }

            await UniTask.WhenAll(loads);
        }

        private CardView CreateCard(ItemId id)
        {
            Button button = new();
            button.AddToClassList("overlap-card");

            VisualElement image = new() { pickingMode = PickingMode.Ignore };
            image.AddToClassList("overlap-card__image");
            button.Add(image);

            Label nameLabel = new(ItemCatalog.Find(id)?.DisplayName ?? id.ToString())
            {
                pickingMode = PickingMode.Ignore
            };
            nameLabel.AddToClassList("overlap-card__name");
            button.Add(nameLabel);

            // 選んだ参加者ぶんの名前バッジを開示時に積む入れ物（中身は RevealChoices で生成）。
            VisualElement badges = new() { pickingMode = PickingMode.Ignore };
            badges.AddToClassList("overlap-card__badges");
            button.Add(badges);

            return new CardView(button, image, badges);
        }

        private async UniTask ApplyItemImageAsync(CardView card, ItemId id, CancellationToken ct)
        {
            ItemDefinition definition = ItemCatalog.Find(id);
            string address = definition != null ? definition.ImageAddress : string.Empty;
            Sprite sprite = string.IsNullOrEmpty(address)
                ? null
                : await _spriteLoader.TryLoadAsync(address, "アイテム画像", ct);
            if (sprite != null)
            {
                card.Image.style.backgroundImage = new StyleBackground(sprite);
            }
            else
            {
                // 未配置は色面プレースホルダ（名前ラベルは常に出しているので識別できる）。
                card.Image.style.backgroundImage = StyleKeyword.None;
                card.Image.style.backgroundColor = new StyleColor(new Color(0.32f, 0.36f, 0.5f));
            }
        }

        private void OnCardClicked(int index)
        {
            // Choose は Choosing 中のみ成功する（制限時間後・確定後のクリックは無視される）。
            if (!_model.Choose(index))
            {
                return;
            }
            _soundPlayer.PlaySafe(_soundStore?.Enter2SE);
        }

        // 全員の選択をカードのバッジで開示する。そのカードを選んだ参加者ぶんのキャラ名バッジを積む
        // （自分＝青・それ以外＝赤）。自分のカードが他の誰かと被れば overlapped、
        // 全員ぶんの選択が揃ったうえで被っていなければ safe（揃っていなければ枠は付けない）。
        private void RevealChoices()
        {
            bool overlapped = IsPlayerOverlapped();
            int playerChoice = _choices.Count > 0 ? _choices[0] : -1;
            for (int i = 0; i < _cards.Count; i++)
            {
                CardView card = _cards[i];
                card.Badges.Clear();

                for (int participant = 0; participant < _choices.Count; participant++)
                {
                    if (_choices[participant] == i)
                    {
                        card.Badges.Add(CreateChooserBadge(participant));
                    }
                }

                bool isPlayerCard = playerChoice >= 0 && i == playerChoice;
                card.Button.EnableInClassList("overlap-card--overlapped", isPlayerCard && overlapped);
                card.Button.EnableInClassList(
                    "overlap-card--safe", isPlayerCard && !overlapped && _allChoicesKnown);
            }
        }

        // 参加者（index 0＝自分）の名前バッジ。セッションにキャラがあればキャラ名、無ければ YOU/CPU。
        private Label CreateChooserBadge(int participant)
        {
            Label badge = new(LabelForParticipant(participant)) { pickingMode = PickingMode.Ignore };
            badge.AddToClassList("overlap-card__badge");
            badge.AddToClassList(participant == 0 ? "overlap-card__badge--you" : "overlap-card__badge--cpu");
            return badge;
        }

        private string LabelForParticipant(int participant)
        {
            IReadOnlyList<CharacterId> characters = _session?.Characters;
            if (characters != null && characters.Count > participant)
            {
                return CharacterCatalog.Find(characters[participant]).DisplayName;
            }
            return participant == 0 ? "YOU" : "CPU";
        }

        private void RevealResult()
        {
            bool invalid = _model.IsPlayerVoteInvalid;
            bool overlapped = IsPlayerOverlapped();
            bool win = IsLocalWin;

            _titleLabel.text = "結果";
            _hintLabel.text = string.Empty;
            // 誰かと被っていれば揃うのを待たずに負けが確定する。逆に「被らなかった」は全員ぶん揃って
            // 初めて言えるので、届かなかった相手がいるオンライン対戦だけは断定せず盤面の発表へ委ねる。
            _resultLabel.text = invalid
                ? "時間切れ！むこうひょう…"
                : overlapped
                    ? "かぶっちゃった…"
                    : _allChoicesKnown
                        ? "かぶらなかった！ゲット！"
                        : "結果はこのあと発表！";
            _resultLabel.EnableInClassList("overlap-result__label--win", win);
            _resultLabel.EnableInClassList("overlap-result__label--lose", invalid || overlapped);
            RefreshStandings();
            SetDisplay(_resultPanel, true);

            // 勝敗が確定していないときは結果音を鳴らさない（オープンの Enter3SE だけで受ける）。
            if (win)
            {
                _soundPlayer.PlaySafe(_soundStore?.ItemGetSE);
            }
            else if (invalid || overlapped)
            {
                _soundPlayer.PlaySafe(_soundStore?.Cancel1SE);
            }
        }

        /// <summary>
        /// 全参加者の結果（獲得／かぶり／時間切れ＋選んだアイテム）を一覧へ並べる。順位ではなく区分で
        /// 並べ、同じ区分の中は参加者 index 順。オンライン対戦で選択が届かなかった相手は「？」のままにする
        /// （正式な勝敗は結果値を突き合わせる盤面側が決める）。
        /// </summary>
        private void RefreshStandings()
        {
            List<StandingLine> lines = new(_choices.Count);
            for (int outcome = OutcomeWon; outcome <= OutcomeInvalid; outcome++)
            {
                for (int participant = 0; participant < _choices.Count; participant++)
                {
                    if (OutcomeOf(participant) != outcome)
                    {
                        continue;
                    }
                    lines.Add(new StandingLine(participant, RankTextOf(outcome), ValueTextOf(participant)));
                }
            }
            _standings.Refresh(lines);
        }

        private int OutcomeOf(int participant)
        {
            int choice = _choices[participant];
            if (choice == UnknownChoice)
            {
                return OutcomeUnknown;
            }
            if (choice < 0)
            {
                return OutcomeInvalid;
            }
            if (IsOverlapped(participant))
            {
                return OutcomeOverlapped;
            }
            // 「誰とも被らなかった」は全員ぶん揃って初めて言えるので、揃うまでは断定しない。
            return _allChoicesKnown ? OutcomeWon : OutcomeUnknown;
        }

        private static string RankTextOf(int outcome)
        {
            return outcome switch
            {
                OutcomeWon => "獲得！",
                OutcomeOverlapped => "かぶり",
                OutcomeInvalid => "時間切れ",
                _ => "？",
            };
        }

        // 選んだアイテム名。無効票は「えらべず」、まだ届いていない相手は「—」。
        private string ValueTextOf(int participant)
        {
            int choice = _choices[participant];
            if (choice == UnknownChoice)
            {
                return "—";
            }
            if (choice < 0)
            {
                return "えらべず";
            }

            IReadOnlyList<ItemId> offered = _model.OfferedItems;
            if (choice >= offered.Count)
            {
                return "—";
            }
            return ItemCatalog.Find(offered[choice])?.DisplayName ?? offered[choice].ToString();
        }

        private void SetCardsEnabled(bool enabled)
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                _cards[i].Button.SetEnabled(enabled);
            }
        }

        private void OnCloseClicked()
        {
            _closeSource?.TrySetResult();
        }

        private static void SetDisplay(VisualElement element, bool visible)
        {
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // 1 枚の選択カードと、開示時にキャラ名バッジを積むコンテナをまとめて持つ。
        private readonly struct CardView
        {
            public CardView(Button button, VisualElement image, VisualElement badges)
            {
                Button = button;
                Image = image;
                Badges = badges;
            }

            public Button Button { get; }
            public VisualElement Image { get; }
            public VisualElement Badges { get; }
        }
    }
}
