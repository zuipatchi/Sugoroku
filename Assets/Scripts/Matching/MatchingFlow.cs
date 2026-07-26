using System;
using System.Collections.Generic;
using System.Threading;
using Common.GameSession;
using Common.SceneManagement;
using Common.SoundManagement;
using Common.Store;
using Cysharp.Threading.Tasks;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace Matching
{
    /// <summary>
    /// マッチングフローのオーケストレーション（認証・自動更新・クイックマッチ・ルーム作成/参加・待機キャンセル・ゲーム開始）。
    /// 状態は MatchingModel.State / Rooms を介して MatchingPresenter へ伝える。
    /// キャンセルトークンは呼び出し元（MatchingPresenter の destroyCancellationToken）から受け取る。
    /// </summary>
    public class MatchingFlow
    {
        public static TimeSpan CreateRoomTimeoutDuration { get; } = TimeSpan.FromSeconds(120);

        private readonly MatchingModel _model;
        private readonly MatchingService _matchingService;
        private readonly SceneTransitioner _sceneTransitioner;
        private readonly GameSessionModel _gameSessionModel;
        private readonly SoundStore _soundStore;
        private readonly SoundPlayer _soundPlayer;

        public MatchingFlow(
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

        public async UniTask InitializeAsync(CancellationToken ct)
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

        public async UniTask AutoRefreshLoopAsync(CancellationToken ct)
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
                    await HandleMatchingErrorAsync("自動更新", e, ct);
                }
            }
        }

        /// <summary>
        /// ルームを作成して定員（<paramref name="maxPlayers"/>）が埋まるまで待つ。
        /// 全員揃えばゲーム開始、タイムアウトなら退室して TimedOut にする。
        /// </summary>
        public async UniTask CreateRoomAsync(int maxPlayers, CancellationToken ct)
        {
            try
            {
                _model.State.Value = MatchingState.CreatingRoom;
                _model.WaitingMax.Value = maxPlayers;
                _model.WaitingCurrent.Value = 0;
                IHostSession session = await _matchingService.CreateRoomAsync("Room", maxPlayers, ct);
                _model.State.Value = MatchingState.WaitingInCreatedRoom;

                bool found = await _matchingService.WaitForPlayerAsync(
                    session, CreateRoomTimeoutDuration, ct, current => _model.WaitingCurrent.Value = current);
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
                await HandleMatchingErrorAsync("ルーム作成", e, ct);
            }
        }

        public async UniTask SelectRoomAsync(string sessionId, CancellationToken ct)
        {
            try
            {
                _model.State.Value = MatchingState.JoiningRoom;
                await _matchingService.JoinRoomAsync(sessionId, ct);
                await StartGameAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                await HandleMatchingErrorAsync("ルーム参加", e, ct);
            }
        }

        public async UniTask CancelWaitAsync(CancellationToken ct)
        {
            try
            {
                await _gameSessionModel.LeaveCurrentSessionAsync();
                await RefreshRoomsAsync(ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Debug.LogError($"キャンセルに失敗: {e}");
                _model.State.Value = MatchingState.Error;
            }
        }

        private async UniTask RefreshRoomsAsync(CancellationToken ct)
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
        }

        private async UniTask StartGameAsync()
        {
            _model.State.Value = MatchingState.Starting;
            _soundPlayer.PlaySE(_soundStore.DecisionSE);
            await _sceneTransitioner.Transit(Scenes.Main);
        }

        private async UniTask HandleMatchingErrorAsync(string context, Exception e, CancellationToken ct)
        {
            Debug.LogError($"{context}に失敗: {e}");
            _model.State.Value = MatchingState.Error;
            await RefreshRoomsAsync(ct);
        }
    }
}
