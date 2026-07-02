using System;
using System.Threading;
using Common.MiniGame;
using Common.SoundManagement;
using Common.Store;
using Cysharp.Threading.Tasks;
using Main.Board;
using Main.Turn;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Main
{
    /// <summary>
    /// 動作確認用のミニゲーム起動ボタン。<see cref="MiniGameLauncher"/> でミニゲームを遊び、
    /// スコアが <see cref="_winThreshold"/> 以上なら盤面のコマを <see cref="_bonusSteps"/> マス進める。
    /// 盤面の特殊マスやターン連携が入るまでの暫定トリガー。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MiniGameTriggerPresenter : MonoBehaviour
    {
        [SerializeField] private int _winThreshold = 25;
        [SerializeField] private int _bonusSteps = 3;

        private MiniGameLauncher _launcher;
        private BoardPresenter _board;
        private SoundStore _soundStore;
        private SoundPlayer _soundPlayer;

        private UIDocument _uiDocument;
        private Button _button;
        private CancellationToken _destroyCt;
        private bool _busy;
        // CPU 対戦（一人用モード）では、ターン進行と干渉しないようテスト用トリガーを無効化する。
        private bool _disabledForCpu;

        [Inject]
        public void Construct(
            MiniGameLauncher launcher,
            BoardPresenter board,
            GameParticipants participants,
            SoundStore soundStore,
            SoundPlayer soundPlayer)
        {
            _launcher = launcher;
            _board = board;
            _disabledForCpu = participants.HasCpu;
            _soundStore = soundStore;
            _soundPlayer = soundPlayer;
        }

        private void Awake()
        {
            _uiDocument = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            _destroyCt = destroyCancellationToken;

            VisualElement root = _uiDocument.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("MiniGameTrigger の rootVisualElement が見つかりませんでした。");
                return;
            }

            _button = root.Q<Button>("MiniGameButton");
            if (_button == null)
            {
                Debug.LogError("MiniGameButton が見つかりませんでした。");
                return;
            }

            // CPU 対戦ではテスト用ボタンを隠し、クリックも受け付けない。
            if (_disabledForCpu)
            {
                _button.style.display = DisplayStyle.None;
                _button = null;
                return;
            }

            _button.clicked += OnClicked;
        }

        private void OnDisable()
        {
            if (_button != null)
            {
                _button.clicked -= OnClicked;
                _button = null;
            }
        }

        private void OnClicked()
        {
            PlayAsync().Forget();
        }

        private async UniTaskVoid PlayAsync()
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            try
            {
                PlaySe(_soundStore?.Enter1SE);

                MiniGameResult result = await _launcher.PlayAsync(MiniGameId.Tap, _destroyCt);

                // ローカル完結のため「勝者」はしきい値で判定する（本来の順位判定は同期フェーズで導入）。
                if (result.Score >= _winThreshold)
                {
                    // CPU 戦では無効化済みのため、ここに来るのは単独プレイ（プレイヤー 0）のみ。
                    await _board.AdvanceAsync(0, _bonusSteps, _destroyCt);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _busy = false;
            }
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
