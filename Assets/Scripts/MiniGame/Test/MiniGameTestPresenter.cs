using System;
using System.Threading;
using Common.GameSession;
using Common.MiniGame;
using Common.SoundManagement;
using Common.Store;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace MiniGame
{
    /// <summary>
    /// ミニゲーム単体の動作確認用シーン（<c>MiniGameTest</c>）のホスト。
    /// <see cref="MiniGameCatalog"/> のミニゲームをボタンで一覧し、押すと <see cref="MiniGameLauncher"/> で
    /// 起動して結果スコアを表示する。本番フロー（Title→Home→…）には出さず、エディタでこのシーンを
    /// 直接開いて Play する前提。新しいミニゲームは MiniGameCatalog に 1 行足せば自動でボタンが並ぶ。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MiniGameTestPresenter : MonoBehaviour
    {
        private MiniGameLauncher _launcher;
        private SoundStore _soundStore;
        private SoundPlayer _soundPlayer;

        private UIDocument _uiDocument;
        private VisualElement _list;
        private Label _resultLabel;
        private Button _playerCountMinus;
        private Button _playerCountPlus;
        private Label _playerCountValue;
        // 起動するミニゲームの参加者数（人間＋CPU）。−／＋ で 2〜8 を増減する。
        private int _playerCount = PlayerCountSessionModel.Min;
        private CancellationToken _destroyCt;
        private bool _busy;

        [Inject]
        public void Construct(
            MiniGameLauncher launcher,
            SoundStore soundStore,
            SoundPlayer soundPlayer)
        {
            _launcher = launcher;
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

            VisualElement root = _uiDocument.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("MiniGameTest の rootVisualElement が見つかりませんでした。");
                return;
            }

            _list = root.Q<VisualElement>("GameList");
            _resultLabel = root.Q<Label>("ResultLabel");
            _playerCountMinus = root.Q<Button>("PlayerCountMinus");
            _playerCountPlus = root.Q<Button>("PlayerCountPlus");
            _playerCountValue = root.Q<Label>("PlayerCountValue");
            if (_list == null || _resultLabel == null
                || _playerCountMinus == null || _playerCountPlus == null || _playerCountValue == null)
            {
                Debug.LogError("MiniGameTest の UI 要素が見つかりませんでした。");
                return;
            }

            // OnEnable が複数回走っても二重購読しないよう、都度外してから張り直す。
            _playerCountMinus.clicked -= OnPlayerCountMinusClicked;
            _playerCountMinus.clicked += OnPlayerCountMinusClicked;
            _playerCountPlus.clicked -= OnPlayerCountPlusClicked;
            _playerCountPlus.clicked += OnPlayerCountPlusClicked;
            UpdatePlayerCount();

            BuildButtons();
        }

        private void OnPlayerCountMinusClicked()
        {
            ChangePlayerCount(-1);
        }

        private void OnPlayerCountPlusClicked()
        {
            ChangePlayerCount(1);
        }

        // 人数を増減して表示・ボタンの有効状態を更新する（SE つき）。範囲端では何もしない。
        private void ChangePlayerCount(int delta)
        {
            int next = Mathf.Clamp(_playerCount + delta, PlayerCountSessionModel.Min, PlayerCountSessionModel.Max);
            if (next == _playerCount)
            {
                return;
            }
            _playerCount = next;
            _soundPlayer.PlaySafe(_soundStore?.Enter2SE);
            UpdatePlayerCount();
        }

        // 現在の人数を数値ラベルに反映し、下限/上限で −／＋ を無効化する。
        private void UpdatePlayerCount()
        {
            _playerCountValue.text = _playerCount.ToString();
            _playerCountMinus.SetEnabled(_playerCount > PlayerCountSessionModel.Min);
            _playerCountPlus.SetEnabled(_playerCount < PlayerCountSessionModel.Max);
        }

        // カタログの各ミニゲームを 1 ボタンずつ生成する。増えたら自動で並ぶ。
        private void BuildButtons()
        {
            _list.Clear();
            foreach (MiniGameDefinition definition in MiniGameCatalog.All)
            {
                MiniGameId id = definition.Id;
                Button button = new() { text = definition.DisplayName };
                button.AddToClassList("test-game-button");
                button.clicked += () => PlayAsync(id).Forget();
                _list.Add(button);
            }
        }

        private async UniTaskVoid PlayAsync(MiniGameId id)
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            try
            {
                _soundPlayer.PlaySafe(_soundStore?.Enter1SE);

                MiniGameResult result = await _launcher.PlayAsync(id, _destroyCt, _playerCount);

                if (_resultLabel != null)
                {
                    _resultLabel.text = $"{MiniGameCatalog.Find(id).DisplayName}：スコア {result.Score}";
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

    }
}
