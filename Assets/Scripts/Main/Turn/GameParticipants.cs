using System.Collections.Generic;
using System.Linq;
using Common.GameSession;

namespace Main.Turn
{
    /// <summary>
    /// このゲームの参加者リスト。<see cref="GameMode"/> に応じて構成する。
    /// 一人用モードは [Human, Cpu, ...] の <see cref="PlayerCountSessionModel.Count"/> 人
    /// （自分 1 人＋残りが CPU。MapSelect で人数を選ぶ）、
    /// オンラインは接続した実プレイヤーぶんの [Human, Human, ...]（最低 2 人。単独プレイは廃止）。
    /// オンラインのルームは 2 人固定（<c>MatchingService</c> の MaxPlayers=2・2 人揃うまで待つ）なので
    /// 参加者数は <see cref="OnlinePlayerCount"/> 人。将来ルーム人数を可変にする場合はここで実接続数を反映する。
    /// </summary>
    public sealed class GameParticipants
    {
        // オンラインの参加者数。マッチングの 2 人固定ルームに合わせる（単独プレイ廃止＝最低 2 人）。
        private const int OnlinePlayerCount = 2;

        private readonly IReadOnlyList<PlayerKind> _players;

        public GameParticipants(GameSessionModel gameSession, PlayerCountSessionModel playerCount)
        {
            _players = Build(gameSession.Mode, playerCount.Count);
        }

        /// <summary>参加者の総数。</summary>
        public int Count => _players.Count;

        /// <summary>CPU が参加しているか（一人用モードでの CPU 対戦かどうか）。</summary>
        public bool HasCpu => _players.Contains(PlayerKind.Cpu);

        /// <summary>プレイヤー <paramref name="player"/> の種類（Human / Cpu）。</summary>
        public PlayerKind KindOf(int player)
        {
            return _players[player];
        }

        private static IReadOnlyList<PlayerKind> Build(GameMode mode, int playerCount)
        {
            if (mode == GameMode.SinglePlayer)
            {
                // 自分（先攻＝index 0）＋残りは CPU。人数は PlayerCountSessionModel でクランプ済みだが念のため下限 2。
                int count = playerCount < 2 ? 2 : playerCount;
                List<PlayerKind> players = new(count) { PlayerKind.Human };
                for (int i = 1; i < count; i++)
                {
                    players.Add(PlayerKind.Cpu);
                }
                return players;
            }

            // オンラインは接続した実プレイヤーぶん（最低 2 人）を全員 Human で構成する。
            List<PlayerKind> online = new(OnlinePlayerCount);
            for (int i = 0; i < OnlinePlayerCount; i++)
            {
                online.Add(PlayerKind.Human);
            }
            return online;
        }
    }
}
