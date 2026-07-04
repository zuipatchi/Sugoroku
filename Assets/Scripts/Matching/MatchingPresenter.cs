using System.Collections.Generic;
using Common.SceneManagement;
using Common.SoundManagement;
using Common.Store;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;
using VContainer.Unity;

namespace Matching
{
    /// <summary>
    /// マッチング画面の UI バインド。UI 要素の取得・MatchingModel の購読→画面反映・
    /// ボタン入力の MatchingFlow への転送のみを担当する。フロー制御は MatchingFlow を参照。
    /// </summary>
    public class MatchingPresenter : MonoBehaviour, IStartable
    {
        private MatchingModel _model;
        private MatchingFlow _flow;
        private SceneTransitioner _sceneTransitioner;
        private SoundStore _soundStore;
        private SoundPlayer _soundPlayer;

        private ScrollView _roomList;
        private Button _backButton;
        private Button _quickMatchButton;
        private Button _createButton;
        private VisualElement _loadingOverlay;
        private Label _loadingLabel;
        private VisualElement _waitingOverlay;
        private Label _waitingLabel;
        private Button _cancelWaitButton;
        private Button _retryButton;
        private Button _backToTitleButton;
        private VisualElement _errorOverlay;
        private Button _closeErrorButton;

        [Inject]
        public void Construct(
            MatchingModel model,
            MatchingFlow flow,
            SceneTransitioner sceneTransitioner,
            SoundStore soundStore,
            SoundPlayer soundPlayer)
        {
            _model = model;
            _flow = flow;
            _sceneTransitioner = sceneTransitioner;
            _soundStore = soundStore;
            _soundPlayer = soundPlayer;
        }

        private void Awake()
        {
            UIDocument uiDocument = GetComponent<UIDocument>();
            VisualElement root = uiDocument.rootVisualElement;

            _roomList = root.Q<ScrollView>("RoomList");
            _backButton = root.Q<Button>("BackButton");
            _quickMatchButton = root.Q<Button>("QuickMatchButton");
            _createButton = root.Q<Button>("CreateButton");
            _loadingOverlay = root.Q<VisualElement>("LoadingOverlay");
            _loadingLabel = root.Q<Label>("LoadingLabel");
            _waitingOverlay = root.Q<VisualElement>("WaitingOverlay");
            _waitingLabel = root.Q<Label>("WaitingLabel");
            _cancelWaitButton = root.Q<Button>("CancelWaitButton");
            _retryButton = root.Q<Button>("RetryButton");
            _backToTitleButton = root.Q<Button>("BackToTitleButton");
            _errorOverlay = root.Q<VisualElement>("ErrorOverlay");
            _closeErrorButton = root.Q<Button>("CloseErrorButton");
        }

        void IStartable.Start()
        {
            _backButton.clicked += () =>
            {
                _soundPlayer.PlaySE(_soundStore.Enter2SE);
                _sceneTransitioner.Transit(Scenes.Title).Forget();
            };
            _quickMatchButton.clicked += () =>
            {
                _soundPlayer.PlaySE(_soundStore.Enter1SE);
                _flow.QuickMatchAsync(destroyCancellationToken).Forget();
            };
            _createButton.clicked += () =>
            {
                _soundPlayer.PlaySE(_soundStore.Enter1SE);
                _flow.CreateRoomAsync(destroyCancellationToken).Forget();
            };
            _cancelWaitButton.clicked += () =>
            {
                _soundPlayer.PlaySE(_soundStore.Cancel1SE);
                _flow.CancelWaitAsync(destroyCancellationToken).Forget();
            };
            _retryButton.clicked += () =>
            {
                _soundPlayer.PlaySE(_soundStore.Enter1SE);
                _flow.InitializeAsync(destroyCancellationToken).Forget();
            };
            _backToTitleButton.clicked += () =>
            {
                _soundPlayer.PlaySE(_soundStore.Enter1SE);
                _sceneTransitioner.Transit(Scenes.Title).Forget();
            };
            _closeErrorButton.clicked += () =>
            {
                _soundPlayer.PlaySE(_soundStore.Enter1SE);
                _flow.InitializeAsync(destroyCancellationToken).Forget();
            };

            _model.State
                .Subscribe(ApplyState)
                .AddTo(destroyCancellationToken);

            // 初期値（空配列）はスキップし、フローがルーム一覧を取得したときだけ再構築する
            // （従来の「取得成功のたびに RebuildRoomList」と同じタイミング）。
            _model.Rooms
                .Skip(1)
                .Subscribe(RebuildRoomList)
                .AddTo(destroyCancellationToken);

            _flow.InitializeAsync(destroyCancellationToken).Forget();
            _flow.AutoRefreshLoopAsync(destroyCancellationToken).Forget();
        }

        private void ApplyState(MatchingState state)
        {
            bool isLoading = state.IsLoading();
            bool isWaiting = state.IsWaiting();
            bool isTimedOut = state == MatchingState.TimedOut;

            _backButton.SetEnabled(state == MatchingState.BrowsingRooms);
            _loadingOverlay.style.display = isLoading ? DisplayStyle.Flex : DisplayStyle.None;
            _waitingOverlay.style.display = isWaiting ? DisplayStyle.Flex : DisplayStyle.None;
            _errorOverlay.style.display = state == MatchingState.Error ? DisplayStyle.Flex : DisplayStyle.None;

            _loadingLabel.text = state switch
            {
                MatchingState.Authenticating => "認証中...",
                MatchingState.CreatingRoom => "ルーム作成中...",
                MatchingState.JoiningRoom => "参加中...",
                MatchingState.Starting => "ゲーム開始...",
                _ => string.Empty
            };

            if (isWaiting)
            {
                _waitingLabel.text = state switch
                {
                    MatchingState.TimedOut => "タイムアウトしました",
                    MatchingState.WaitingForPlayer => $"プレイヤーを待っています...\n{(int)MatchingFlow.QuickMatchTimeoutDuration.TotalSeconds}秒でタイムアウトします",
                    MatchingState.WaitingInCreatedRoom => $"プレイヤーを待っています...\n{(int)MatchingFlow.CreateRoomTimeoutDuration.TotalMinutes}分で自動解散します",
                    _ => "プレイヤーを待っています..."
                };
                _cancelWaitButton.style.display = isTimedOut ? DisplayStyle.None : DisplayStyle.Flex;
                _retryButton.style.display = isTimedOut ? DisplayStyle.Flex : DisplayStyle.None;
                _backToTitleButton.style.display = isTimedOut ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void RebuildRoomList(IReadOnlyList<LobbyInfo> rooms)
        {
            _roomList.Clear();
            if (rooms.Count == 0)
            {
                Label emptyLabel = new Label { text = "ルームがありません" };
                emptyLabel.AddToClassList("empty-state");
                _roomList.Add(emptyLabel);
                return;
            }
            foreach (LobbyInfo room in rooms)
            {
                if (room.Name == MatchingService.QuickMatchRoomName)
                {
                    continue;
                }
                string sessionId = room.LobbyId;
                Button roomButton = new Button(() =>
                {
                    _soundPlayer.PlaySE(_soundStore.Enter1SE);
                    _flow.SelectRoomAsync(sessionId, destroyCancellationToken).Forget();
                })
                {
                    text = $"{room.Name}  {room.PlayerCount}/{room.MaxPlayers}"
                };
                roomButton.AddToClassList("room-item");
                _roomList.Add(roomButton);
            }
        }
    }
}
