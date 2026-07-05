using System;
using System.Threading;
using Common.Character;
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

        private readonly TapGameModel _model;
        private readonly SoundStore _soundStore;
        private readonly SoundPlayer _soundPlayer;
        private readonly CharacterSessionModel _characterSession;
        private readonly CompositeDisposable _disposables = new();

        private Label _timerLabel;
        private Label _countLabel;
        private Label _centerLabel;
        private Button _tapButton;
        private VisualElement _characterCard;
        private VisualElement _resultPanel;
        private Label _resultLabel;
        private Button _closeButton;

        private readonly AddressableSpriteLoader _spriteLoader = new();
        private Tween _shakeTween;
        private float _shakePhase;

        private UniTaskCompletionSource _closeSource;

        public TapGamePlay(
            TapGameModel model,
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
        /// タップ連打 UI を構築する（フェードイン前に await される）。キャラカード絵のロードと
        /// Model → UI の購読まで済ませ、画面を見せられる状態にする。進行と入力受付は <see cref="RunAsync"/> 側。
        /// </summary>
        public async UniTask BuildAsync(VisualElement root, CancellationToken ct)
        {
            _timerLabel = root.Q<Label>("TimerLabel");
            _countLabel = root.Q<Label>("CountLabel");
            _centerLabel = root.Q<Label>("CenterLabel");
            _tapButton = root.Q<Button>("TapButton");
            _characterCard = root.Q<VisualElement>("CharacterCard");
            _resultPanel = root.Q<VisualElement>("ResultPanel");
            _resultLabel = root.Q<Label>("ResultLabel");
            _closeButton = root.Q<Button>("CloseButton");
            if (_timerLabel == null || _countLabel == null || _centerLabel == null
                || _tapButton == null || _resultPanel == null || _resultLabel == null || _closeButton == null)
            {
                Debug.LogError("TapGame の UI 要素が見つかりませんでした。");
                return;
            }

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

            await ApplyCharacterCardAsync(ct);
        }

        /// <summary>
        /// カウントダウン → 計測 → 結果表示を駆動し、「結果を反映」クリックでタップ数をスコアとして返す。
        /// フェードイン後に呼ばれる想定で、Forget して走らせる。
        /// </summary>
        public async UniTask<int> RunAsync(CancellationToken ct)
        {
            if (_closeSource == null)
            {
                // UI 構築に失敗している。従来どおり結果は報告せず、キャンセル（シーンアンロード）まで待機する。
                return await UniTask.Never<int>(ct);
            }

            _centerLabel.text = "準備…";
            await UniTask.Delay(RevealLeadInMs, cancellationToken: ct);

            _model.BeginCountdown();
            await MiniGameCountdown.RunAsync(_centerLabel, _soundStore, _soundPlayer, ct);

            _model.StartPlaying(PlayDurationSeconds);

            float elapsed = 0f;
            while (elapsed < PlayDurationSeconds)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                elapsed += Time.deltaTime;
                _model.UpdateRemaining(PlayDurationSeconds - elapsed);
            }

            _model.Finish();
            _soundPlayer.PlaySafe(_soundStore?.DecisionSE);

            int score = _model.TapCount.CurrentValue;
            _resultLabel.text = $"タップ数 {score} 回！";

            await _closeSource.Task.AttachExternalCancellation(ct);
            return _model.TapCount.CurrentValue;
        }

        public void Dispose()
        {
            _shakeTween?.Kill();
            _shakeTween = null;
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
            _resultPanel.style.display = phase == TapGamePhase.Finished ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // 選択中キャラのカード絵を読み込んで中央に表示する。未配置ならプレースホルダ（色面）にフォールバックする。
        private async UniTask ApplyCharacterCardAsync(CancellationToken ct)
        {
            if (_characterCard == null)
            {
                return;
            }

            CharacterId id = _characterSession.Selected;
            CharacterDefinition definition = CharacterCatalog.Find(id);

            Sprite card = await _spriteLoader.TryLoadAsync(definition.CardAddress, "キャラカード", ct);
            if (card != null)
            {
                _characterCard.style.backgroundImage = new StyleBackground(card);
            }
            else
            {
                _characterCard.style.backgroundImage = StyleKeyword.None;
                _characterCard.style.backgroundColor = CharacterPalette.PlaceholderColor(CharacterCatalog.IndexOf(id), CharacterCatalog.All.Count);
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
            _soundPlayer.PlaySafe(_soundStore?.Enter2SE);
        }

        private void OnCloseClicked()
        {
            // 結果を起動側（Main）へ返すため、RunAsync の待機を解く。
            _closeSource?.TrySetResult();
        }
    }
}
