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
    /// </summary>
    public sealed class OverlapGamePlay : IDisposable
    {
        private const int RevealPauseMs = 900;  // オープン演出（バッジ表示）を見せてから結果を出すまでの間

        private readonly OverlapGameModel _model;
        private readonly MiniGameSessionModel _session;
        private readonly SoundStore _soundStore;
        private readonly SoundPlayer _soundPlayer;

        private readonly AddressableSpriteLoader _spriteLoader = new();
        private readonly List<CardView> _cards = new();

        private Label _titleLabel;
        private Label _hintLabel;
        private VisualElement _choiceRow;
        private Label _countdownLabel;
        private Label _timerLabel;
        private VisualElement _resultPanel;
        private Label _resultLabel;
        private Button _closeButton;

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
            _closeButton = root.Q<Button>("CloseButton");
            if (_titleLabel == null || _hintLabel == null || _choiceRow == null || _countdownLabel == null
                || _timerLabel == null || _resultPanel == null || _resultLabel == null || _closeButton == null)
            {
                Debug.LogError("OverlapGame の UI 要素が見つかりませんでした。");
                return;
            }

            // 参加者数はセッション（起動側が指定）から取る。未設定（0 以下）のときだけ既定へフォールバック。
            int playerCount = _session.PlayerCount > 0 ? _session.PlayerCount : OverlapGameConfig.DefaultPlayerCount;
            // オンライン対戦では相手が実プレイヤーなので CPU の選択は作らない。提示カードを全員で揃えるため、
            // 種は起動側が配った共有の種（無ければその場の乱数）を使う。
            _model.Setup(playerCount, _session.ResolveSeed(), _session.SimulateOpponents);

            _closeSource = new UniTaskCompletionSource();
            _closeButton.clicked += OnCloseClicked;

            await BuildCardsAsync(ct);

            _titleLabel.text = "被っちゃやーよ";
            _hintLabel.text = "ほかの人とかぶらないアイテムを選ぼう！";
            SetCardsEnabled(false);
            SetDisplay(_countdownLabel, false);
            SetDisplay(_timerLabel, false);
            SetDisplay(_resultPanel, false);
        }

        /// <summary>
        /// カウントダウン → 選択待ち → オープン → 結果表示を駆動し、「結果を反映」クリックで
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
            RevealChoices();
            _soundPlayer.PlaySafe(_soundStore?.Enter3SE);

            await UniTask.Delay(RevealPauseMs, cancellationToken: ct);

            RevealResult();

            await _closeSource.Task.AttachExternalCancellation(ct);
            _model.Finish();
            // 結果値は選んだカードの index（未選択は NoChoice）。オンラインでは誰とも被らなければ勝ち。
            return (_model.IsPlayerWin ? 1 : 0, _model.PlayerChoiceIndex);
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

        public void Dispose()
        {
            if (_closeButton != null)
            {
                _closeButton.clicked -= OnCloseClicked;
            }
            _spriteLoader.Dispose();
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

        // 全員の選択をカードのバッジで開示する。カードを選んだ参加者ぶんのキャラ名バッジを積む
        // （プレイヤー＝青・CPU＝赤）。プレイヤーのカードが他の誰かと被れば overlapped、被らなければ safe。
        private void RevealChoices()
        {
            IReadOnlyList<int> cpuChoices = _model.CpuChoiceIndices;
            for (int i = 0; i < _cards.Count; i++)
            {
                CardView card = _cards[i];
                card.Badges.Clear();

                bool isPlayer = i == _model.PlayerChoiceIndex;
                bool isCpu = _model.IsChosenByCpu(i);

                if (isPlayer)
                {
                    card.Badges.Add(CreateChooserBadge(0));
                }
                for (int j = 0; j < cpuChoices.Count; j++)
                {
                    if (cpuChoices[j] == i)
                    {
                        card.Badges.Add(CreateChooserBadge(j + 1));
                    }
                }

                card.Button.EnableInClassList("overlap-card--overlapped", isPlayer && isCpu);
                card.Button.EnableInClassList("overlap-card--safe", isPlayer && !isCpu);
            }
        }

        // 参加者（index 0＝プレイヤー）の名前バッジ。セッションにキャラがあればキャラ名、無ければ YOU/CPU。
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
            bool win = _model.IsPlayerWin;
            _titleLabel.text = "結果";
            _hintLabel.text = string.Empty;
            // オンライン対戦では他プレイヤーの選択がまだ届いていないので被りを断定しない
            // （正式な勝敗は全員の選択が揃ってから盤面側が発表する）。
            _resultLabel.text = !_session.SimulateOpponents
                ? _model.IsPlayerVoteInvalid ? "時間切れ！むこうひょう…" : "結果はこのあと発表！"
                : win
                    ? "かぶらなかった！ゲット！"
                    : _model.IsPlayerVoteInvalid
                        ? "時間切れ！むこうひょう…"
                        : "かぶっちゃった…";
            _resultLabel.EnableInClassList("overlap-result__label--win", win);
            _resultLabel.EnableInClassList("overlap-result__label--lose", !win);
            SetDisplay(_resultPanel, true);
            _soundPlayer.PlaySafe(win ? _soundStore?.ItemGetSE : _soundStore?.Cancel1SE);
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

        private static int NextSeed()
        {
            return UnityEngine.Random.Range(int.MinValue, int.MaxValue);
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
