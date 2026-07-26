using System.Collections.Generic;
using Common.GameSession;
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
        private Button _createButton;
        private Button _playerCountMinus;
        private Button _playerCountPlus;
        private Label _playerCountValue;
        // ホストが作るルームの定員（自分＋相手）。2〜4 でクランプする。
        private int _roomPlayerCount = PlayerCountSessionModel.Min;
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
            _createButton = root.Q<Button>("CreateButton");
            _playerCountMinus = root.Q<Button>("PlayerCountMinus");
            _playerCountPlus = root.Q<Button>("PlayerCountPlus");
            _playerCountValue = root.Q<Label>("PlayerCountValue");
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
            _createButton.clicked += () =>
            {
                _soundPlayer.PlaySE(_soundStore.Enter1SE);
                _flow.CreateRoomAsync(_roomPlayerCount, destroyCancellationToken).Forget();
            };
            _playerCountMinus.clicked += () => ChangeRoomPlayerCount(-1);
            _playerCountPlus.clicked += () => ChangeRoomPlayerCount(1);
            UpdatePlayerCountUI();
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

            // 相手待ち中の参加人数が変わるたびに「◯/◯人」の待機ラベルを更新する。
            _model.WaitingCurrent
                .Subscribe(_ => UpdateWaitingLabel())
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
                UpdateWaitingLabel();
                _cancelWaitButton.style.display = isTimedOut ? DisplayStyle.None : DisplayStyle.Flex;
                _retryButton.style.display = isTimedOut ? DisplayStyle.Flex : DisplayStyle.None;
                _backToTitleButton.style.display = isTimedOut ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        // 相手待ち中の待機ラベルを「◯/◯人」付きで更新する（状態変化・参加人数変化のどちらからも呼ぶ）。
        private void UpdateWaitingLabel()
        {
            switch (_model.State.Value)
            {
                case MatchingState.TimedOut:
                    _waitingLabel.text = "タイムアウトしました";
                    break;
                case MatchingState.WaitingInCreatedRoom:
                    _waitingLabel.text =
                        $"プレイヤーを待っています...（{_model.WaitingCurrent.Value}/{_model.WaitingMax.Value}人）\n"
                        + "全員そろうと自動で開始します";
                    break;
            }
        }

        // 定員ステッパーの増減（2〜4 でクランプ）。端では何もしない。
        private void ChangeRoomPlayerCount(int delta)
        {
            int next = Mathf.Clamp(_roomPlayerCount + delta, PlayerCountSessionModel.Min, PlayerCountSessionModel.Max);
            if (next == _roomPlayerCount)
            {
                return;
            }
            _roomPlayerCount = next;
            _soundPlayer.PlaySE(_soundStore.Enter2SE);
            UpdatePlayerCountUI();
        }

        // 定員の数値ラベルを反映し、下限/上限で −／＋ を無効化する。
        private void UpdatePlayerCountUI()
        {
            _playerCountValue.text = _roomPlayerCount.ToString();
            _playerCountMinus.SetEnabled(_roomPlayerCount > PlayerCountSessionModel.Min);
            _playerCountPlus.SetEnabled(_roomPlayerCount < PlayerCountSessionModel.Max);
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
