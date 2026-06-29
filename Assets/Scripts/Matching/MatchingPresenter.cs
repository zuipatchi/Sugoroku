using System;
using System.Collections.Generic;
using Common.GameSession;
using Common.SceneManagement;
using Common.SoundManagement;
using Common.Store;
using Cysharp.Threading.Tasks;
using R3;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;
using VContainer.Unity;

namespace Matching
{
    public class MatchingPresenter : MonoBehaviour, IStartable
    {
        private static readonly TimeSpan _quickMatchTimeoutDuration = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan _createRoomTimeoutDuration = TimeSpan.FromSeconds(120);

        private MatchingModel _model;
        private MatchingService _matchingService;
        private SceneTransitioner _sceneTransitioner;
        private GameSessionModel _gameSessionModel;
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
            MatchingService matchingService,
            SceneTransitioner sceneTransitioner,
            GameSessionModel gameSessionModel,
            SoundStore soundStore,
            SoundPlayer soundPlayer)
        {
            _model = model;
            _matchingService = matchingService;
            _sceneTransitioner = sceneTransitioner;
            _gameSessionModel = gameSessionModel;
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
                OnQuickMatchButtonClickedAsync().Forget();
            };
            _createButton.clicked += () =>
            {
                _soundPlayer.PlaySE(_soundStore.Enter1SE);
                OnCreateButtonClickedAsync().Forget();
            };
            _cancelWaitButton.clicked += () =>
            {
                _soundPlayer.PlaySE(_soundStore.Cancel1SE);
                CancelWaitAsync().Forget();
            };
            _retryButton.clicked += () =>
            {
                _soundPlayer.PlaySE(_soundStore.Enter1SE);
                InitializeAsync(destroyCancellationToken).Forget();
            };
            _backToTitleButton.clicked += () =>
            {
                _soundPlayer.PlaySE(_soundStore.Enter1SE);
                _sceneTransitioner.Transit(Scenes.Title).Forget();
            };
            _closeErrorButton.clicked += () =>
            {
                _soundPlayer.PlaySE(_soundStore.Enter1SE);
                InitializeAsync(destroyCancellationToken).Forget();
            };

            _model.State
                .Subscribe(ApplyState)
                .AddTo(destroyCancellationToken);

            InitializeAsync(destroyCancellationToken).Forget();
            AutoRefreshLoopAsync(destroyCancellationToken).Forget();
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
                    MatchingState.WaitingForPlayer => $"プレイヤーを待っています...\n{(int)_quickMatchTimeoutDuration.TotalSeconds}秒でタイムアウトします",
                    MatchingState.WaitingInCreatedRoom => $"プレイヤーを待っています...\n{(int)_createRoomTimeoutDuration.TotalMinutes}分で自動解散します",
                    _ => "プレイヤーを待っています..."
                };
                _cancelWaitButton.style.display = isTimedOut ? DisplayStyle.None : DisplayStyle.Flex;
                _retryButton.style.display = isTimedOut ? DisplayStyle.Flex : DisplayStyle.None;
                _backToTitleButton.style.display = isTimedOut ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private async UniTaskVoid InitializeAsync(System.Threading.CancellationToken ct)
        {
            try
            {
                _model.State.Value = MatchingState.Authenticating;
                await _matchingService.AuthenticateAsync(ct);
                await RefreshRoomsAsync(ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Debug.LogError($"初期化に失敗: {e}");
                _model.State.Value = MatchingState.Error;
            }
        }

        private async UniTaskVoid AutoRefreshLoopAsync(System.Threading.CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await UniTask.Delay(2000, cancellationToken: ct);
                    if (_model.State.Value == MatchingState.BrowsingRooms)
                    {
                        await RefreshRoomsAsync(ct);
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception e)
                {
                    await HandleMatchingErrorAsync("自動更新", e);
                }
            }
        }

        private async UniTask RefreshRoomsAsync(System.Threading.CancellationToken ct)
        {
            IReadOnlyList<LobbyInfo> rooms = await _matchingService.GetRoomsAsync(ct);
            // null は「取得できなかった（競合中・SDK 過渡期エラー）」を意味するので表示は据え置く。
            if (rooms == null)
            {
                _model.State.Value = MatchingState.BrowsingRooms;
                return;
            }
            _model.Rooms.Value = rooms;
            _model.State.Value = MatchingState.BrowsingRooms;
            RebuildRoomList(rooms);
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
                if (room.Name == MatchingService.QuickMatchRoomName) continue;
                string sessionId = room.LobbyId;
                Button roomButton = new Button(() =>
                {
                    _soundPlayer.PlaySE(_soundStore.Enter1SE);
                    OnRoomSelectedAsync(sessionId).Forget();
                })
                {
                    text = $"{room.Name}  {room.PlayerCount}/{room.MaxPlayers}"
                };
                roomButton.AddToClassList("room-item");
                _roomList.Add(roomButton);
            }
        }

        private async UniTask StartGameAsync()
        {
            _model.State.Value = MatchingState.Starting;
            _soundPlayer.PlaySE(_soundStore.ResultSE);
            await _sceneTransitioner.Transit(Scenes.Main);
        }

        private async UniTask HandleMatchingErrorAsync(string context, Exception e)
        {
            Debug.LogError($"{context}に失敗: {e}");
            _model.State.Value = MatchingState.Error;
            await RefreshRoomsAsync(destroyCancellationToken);
        }

        private async UniTaskVoid OnQuickMatchButtonClickedAsync()
        {
            try
            {
                _model.State.Value = MatchingState.JoiningRoom;
                LobbyInfo? room = await _matchingService.FindQuickMatchRoomAsync(destroyCancellationToken);

                if (room.HasValue)
                {
                    await _matchingService.JoinRoomAsync(room.Value.LobbyId, destroyCancellationToken);
                    await StartGameAsync();
                }
                else
                {
                    _model.State.Value = MatchingState.CreatingRoom;
                    IHostSession session = await _matchingService.CreateRoomAsync(MatchingService.QuickMatchRoomName, destroyCancellationToken);
                    _model.State.Value = MatchingState.WaitingForPlayer;

                    bool found = await _matchingService.WaitForPlayerAsync(session, _quickMatchTimeoutDuration, destroyCancellationToken);
                    if (found)
                    {
                        await StartGameAsync();
                    }
                    else
                    {
                        await _gameSessionModel.LeaveCurrentSessionAsync();
                        _model.State.Value = MatchingState.TimedOut;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                await HandleMatchingErrorAsync("クイックマッチ", e);
            }
        }

        private async UniTaskVoid OnCreateButtonClickedAsync()
        {
            try
            {
                _model.State.Value = MatchingState.CreatingRoom;
                IHostSession session = await _matchingService.CreateRoomAsync("Room", destroyCancellationToken);
                _model.State.Value = MatchingState.WaitingInCreatedRoom;

                bool found = await _matchingService.WaitForPlayerAsync(session, _createRoomTimeoutDuration, destroyCancellationToken);
                if (found)
                {
                    await StartGameAsync();
                }
                else
                {
                    await _gameSessionModel.LeaveCurrentSessionAsync();
                    _model.State.Value = MatchingState.TimedOut;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                await HandleMatchingErrorAsync("ルーム作成", e);
            }
        }

        private async UniTaskVoid OnRoomSelectedAsync(string sessionId)
        {
            try
            {
                _model.State.Value = MatchingState.JoiningRoom;
                await _matchingService.JoinRoomAsync(sessionId, destroyCancellationToken);
                await StartGameAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                await HandleMatchingErrorAsync("ルーム参加", e);
            }
        }

        private async UniTaskVoid CancelWaitAsync()
        {
            try
            {
                await _gameSessionModel.LeaveCurrentSessionAsync();
                await RefreshRoomsAsync(destroyCancellationToken);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Debug.LogError($"キャンセルに失敗: {e}");
                _model.State.Value = MatchingState.Error;
            }
        }
    }
}
