using System;
using System.Collections.Generic;
using System.Threading;
using Common.GameSession;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace Matching
{
    public class MatchingService
    {
        private readonly GameSessionModel _gameSessionModel;
        private bool _isQuerying;

        public MatchingService(GameSessionModel gameSessionModel)
        {
            _gameSessionModel = gameSessionModel;
        }

        public async UniTask AuthenticateAsync(CancellationToken ct = default)
        {
            await UnityServices.InitializeAsync().AsUniTask().AttachExternalCancellation(ct);
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance
                    .SignInAnonymouslyAsync()
                    .AsUniTask()
                    .AttachExternalCancellation(ct);
            }
        }

        // データを取得できなかった場合（クエリ競合中・SDK の過渡期エラー）は null を返す。
        // 「本当に 0 件」と区別するため、呼び出し側は null なら表示を更新しない。
        public async UniTask<IReadOnlyList<LobbyInfo>> GetRoomsAsync(CancellationToken ct = default)
        {
            if (_isQuerying)
            {
                return null;
            }

            _isQuerying = true;
            try
            {
                QuerySessionsOptions queryOptions = new QuerySessionsOptions();
                QuerySessionsResults results;
                try
                {
                    results = await MultiplayerService.Instance
                        .QuerySessionsAsync(queryOptions)
                        .AsUniTask()
                        .AttachExternalCancellation(ct);
                }
                catch (SessionException)
                {
                    // UGS SDK がセッション離脱直後の過渡期に NullRef を投げるバグの回避。
                    // 次のリフレッシュで再試行される。
                    return null;
                }

                return MapSessionsToRooms(results.Sessions);
            }
            finally
            {
                _isQuerying = false;
            }
        }

        // 満室（空きスロット 0）のセッションを除外し、ISessionInfo を LobbyInfo に変換する。
        // 純ロジックなので EditMode テストの対象（MatchingServiceTests）。
        public static IReadOnlyList<LobbyInfo> MapSessionsToRooms(IList<ISessionInfo> sessions)
        {
            List<LobbyInfo> rooms = new(sessions.Count);
            foreach (ISessionInfo info in sessions)
            {
                if (info.AvailableSlots == 0)
                {
                    continue;
                }
                int playerCount = info.MaxPlayers - info.AvailableSlots;
                rooms.Add(new LobbyInfo(info.Id, info.Name, playerCount, info.MaxPlayers));
            }
            return rooms;
        }

        private static void DisableNgoSceneManagement()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm != null)
            {
                nm.NetworkConfig.EnableSceneManagement = false;
            }
        }

        public async UniTask<IHostSession> CreateRoomAsync(string roomName, int maxPlayers, CancellationToken ct = default)
        {
            await _gameSessionModel.LeaveCurrentSessionAsync();
            DisableNgoSceneManagement();

            SessionOptions options = new SessionOptions
            {
                Name = roomName,
                MaxPlayers = maxPlayers
            };
            IHostSession session = await MultiplayerService.Instance
                .CreateSessionAsync(options)
                .AsUniTask()
                .AttachExternalCancellation(ct);

            _gameSessionModel.SetSession(session);
            return session;
        }

        public async UniTask JoinRoomAsync(string sessionId, CancellationToken ct = default)
        {
            await _gameSessionModel.LeaveCurrentSessionAsync();
            DisableNgoSceneManagement();

            ISession session = await MultiplayerService.Instance
                .JoinSessionByIdAsync(sessionId)
                .AsUniTask()
                .AttachExternalCancellation(ct);

            _gameSessionModel.SetSession(session);
        }

        /// <summary>
        /// ルームが満室（全員参加）になるまで待つ。<paramref name="onPlayerCount"/> に現在の参加人数を通知して、
        /// 呼び出し側が「◯/◯人」の進捗表示を更新できるようにする（開始時とポーリングの各タイミング・メインスレッド）。
        /// </summary>
        public async UniTask<bool> WaitForPlayerAsync(
            ISession session, TimeSpan timeout, CancellationToken ct = default, Action<int> onPlayerCount = null)
        {
            // 完了条件は「定員が埋まる（AvailableSlots==0）」＝全員参加。PlayerJoined は 1 人参加するたびに
            // 発火するため、3〜4 人部屋で 1 人目の参加で開始してしまう。満室のみを完了条件にする。
            onPlayerCount?.Invoke(session.PlayerCount);
            if (session.AvailableSlots == 0)
            {
                return true;
            }

            using CancellationTokenSource timeoutCts = new(timeout);
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, ct);

            try
            {
                // AvailableSlots を 500ms 間隔でポーリングし、満室になったら成立。
                // 併せて現在の参加人数を通知して「◯/◯人」表示を更新する。
                while (true)
                {
                    linked.Token.ThrowIfCancellationRequested();
                    onPlayerCount?.Invoke(session.PlayerCount);
                    if (session.AvailableSlots == 0)
                    {
                        return true;
                    }
                    await UniTask.Delay(TimeSpan.FromMilliseconds(500), cancellationToken: linked.Token);
                }
            }
            catch (OperationCanceledException)
            {
                await UniTask.SwitchToMainThread();
                if (ct.IsCancellationRequested)
                {
                    throw;
                }
                return false;
            }
        }
    }
}
