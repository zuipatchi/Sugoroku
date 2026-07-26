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
        /// 呼び出し側が「◯/◯人」の進捗表示を更新できるようにする（登録直後と参加検知の各タイミング・メインスレッド）。
        /// </summary>
        public async UniTask<bool> WaitForPlayerAsync(
            ISession session, TimeSpan timeout, CancellationToken ct = default, Action<int> onPlayerCount = null)
        {
            // PlayerJoined は別スレッドで発火し得るため、ポーリング側との可視性を Volatile で保証する。
            bool joined = false;

            void OnPlayerJoined(string playerId)
            {
                session.PlayerJoined -= OnPlayerJoined;
                Volatile.Write(ref joined, true);
            }

            // ハンドラを先に登録してからセッション状態を確認（競合防止）
            // CreateRoomAsync 返却直後に参加された場合、PlayerJoined が登録前に発火する
            session.PlayerJoined += OnPlayerJoined;
            onPlayerCount?.Invoke(session.PlayerCount);

            if (session.AvailableSlots == 0)
            {
                session.PlayerJoined -= OnPlayerJoined;
                return true;
            }

            using CancellationTokenSource timeoutCts = new(timeout);
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, ct);

            try
            {
                // PlayerJoined イベントが主経路。ただしハンドラ登録前に相手が参加したケースに
                // 備え、AvailableSlots も定期ポーリングで監視する。
                while (true)
                {
                    linked.Token.ThrowIfCancellationRequested();
                    onPlayerCount?.Invoke(session.PlayerCount);
                    if (Volatile.Read(ref joined) || session.AvailableSlots == 0)
                    {
                        session.PlayerJoined -= OnPlayerJoined;
                        onPlayerCount?.Invoke(session.PlayerCount);
                        return true;
                    }
                    await UniTask.Delay(TimeSpan.FromMilliseconds(500), cancellationToken: linked.Token);
                }
            }
            catch (OperationCanceledException)
            {
                session.PlayerJoined -= OnPlayerJoined;
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
