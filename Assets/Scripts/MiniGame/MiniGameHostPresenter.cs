using System;
using System.Threading;
using Common.MiniGame;
using Common.SceneManagement;
using Common.SoundManagement;
using Common.Store;
using Cysharp.Threading.Tasks;
using MiniGame.TapGame;
using R3;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UIElements;
using VContainer;

namespace MiniGame
{
    /// <summary>
    /// ミニゲームシーンのホスト。<see cref="MiniGameSessionModel.CurrentGame"/> に応じた UI を
    /// Addressables でロードして表示し、ゲームを進行させて結果を <see cref="MiniGameSessionModel.Report"/> で返す。
    /// 現状はタップ連打のみ実装。新しいゲームはここに分岐を足し、対応する UXML を Addressables に登録する。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MiniGameHostPresenter : MonoBehaviour, ISceneReady
    {
        private const float PlayDurationSeconds = 5f;
        private const int RevealLeadInMs = 500;
        private const int CountdownStepMs = 700;
        private const int StartFlashMs = 500;

        private MiniGameSessionModel _session;
        private TapGameModel _tap;
        private SoundStore _soundStore;
        private SoundPlayer _soundPlayer;

        private UIDocument _uiDocument;
        private Label _timerLabel;
        private Label _countLabel;
        private Label _centerLabel;
        private Button _tapButton;
        private VisualElement _resultPanel;
        private Label _resultLabel;
        private Button _closeButton;

        private CancellationToken _destroyCt;
        private bool _uiReady;
        private readonly CompositeDisposable _disposables = new();

        [Inject]
        public void Construct(
            MiniGameSessionModel session,
            TapGameModel tap,
            SoundStore soundStore,
            SoundPlayer soundPlayer)
        {
            _session = session;
            _tap = tap;
            _soundStore = soundStore;
            _soundPlayer = soundPlayer;
        }

        private void Awake()
        {
            _uiDocument = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            // 破棄前に最低 1 回参照しておく（patterns.md #2）。
            _destroyCt = destroyCancellationToken;
        }

        private void OnDestroy()
        {
            _disposables.Dispose();
        }

        // SceneTransitioner / MiniGameLauncher がフェードイン前に待つ。
        // UI 構築（Addressables ロード）が終わってから画面を見せる。
        public async UniTask ReadyAsync(CancellationToken ct)
        {
            await BuildUiAsync(ct);
            if (!_uiReady)
            {
                return;
            }
            // フェードイン後に演出が始まるよう、ゲーム進行はリードインを挟んで別タスクで走らせる。
            GameLoopAsync(_destroyCt).Forget();
        }

        private async UniTask BuildUiAsync(CancellationToken ct)
        {
            VisualElement root = _uiDocument.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("MiniGame の rootVisualElement が見つかりませんでした。");
                return;
            }

            string address = AddressFor(_session.CurrentGame);
            VisualTreeAsset tree = await Addressables.LoadAssetAsync<VisualTreeAsset>(address)
                .ToUniTask(cancellationToken: ct);

            root.Clear();
            tree.CloneTree(root);

            _timerLabel = root.Q<Label>("TimerLabel");
            _countLabel = root.Q<Label>("CountLabel");
            _centerLabel = root.Q<Label>("CenterLabel");
            _tapButton = root.Q<Button>("TapButton");
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
            _disposables.Add(_tap.TapCount.Subscribe(count =>
            {
                if (_countLabel != null)
                {
                    _countLabel.text = count.ToString();
                }
            }));
            _disposables.Add(_tap.RemainingSeconds.Subscribe(secs =>
            {
                if (_timerLabel != null)
                {
                    _timerLabel.text = secs.ToString("0.0");
                }
            }));
            _disposables.Add(_tap.Phase.Subscribe(ApplyPhase));

            _uiReady = true;
        }

        private void ApplyPhase(TapGamePhase phase)
        {
            if (_tapButton == null)
            {
                return;
            }
            _tapButton.SetEnabled(phase == TapGamePhase.Playing);
            _centerLabel.style.display =
                (phase == TapGamePhase.Ready || phase == TapGamePhase.Countdown)
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            _resultPanel.style.display = phase == TapGamePhase.Finished ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private async UniTaskVoid GameLoopAsync(CancellationToken ct)
        {
            try
            {
                _centerLabel.text = "準備…";
                await UniTask.Delay(RevealLeadInMs, cancellationToken: ct);

                _tap.BeginCountdown();
                for (int n = 3; n >= 1; n--)
                {
                    _centerLabel.text = n.ToString();
                    PlaySe(_soundStore?.Enter1SE);
                    await UniTask.Delay(CountdownStepMs, cancellationToken: ct);
                }

                _centerLabel.text = "スタート！";
                PlaySe(_soundStore?.Enter2SE);
                await UniTask.Delay(StartFlashMs, cancellationToken: ct);

                _tap.StartPlaying(PlayDurationSeconds);

                float elapsed = 0f;
                while (elapsed < PlayDurationSeconds)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    elapsed += Time.deltaTime;
                    _tap.UpdateRemaining(PlayDurationSeconds - elapsed);
                }

                _tap.Finish();
                PlaySe(_soundStore?.ResultSE);

                int score = _tap.TapCount.CurrentValue;
                _resultLabel.text = $"タップ数 {score} 回！";
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void OnTapClicked()
        {
            _tap.Tap();
            PlaySe(_soundStore?.Enter2SE);
        }

        private void OnCloseClicked()
        {
            // 結果を起動側（Main）へ返す。これを受けて MiniGameLauncher がシーンをアンロードする。
            _session.Report(_tap.TapCount.CurrentValue);
        }

        private void PlaySe(AudioClip clip)
        {
            if (_soundPlayer != null && clip != null)
            {
                _soundPlayer.PlaySE(clip);
            }
        }

        private static string AddressFor(MiniGameId game)
        {
            return game switch
            {
                MiniGameId.Tap => "MiniGame/TapGame",
                _ => "MiniGame/TapGame"
            };
        }
    }
}
