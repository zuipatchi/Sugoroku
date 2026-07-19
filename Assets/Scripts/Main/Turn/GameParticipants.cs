using System.Collections.Generic;
using System.Linq;
using Common.GameSession;

namespace Main.Turn
{
    /// <summary>
    /// このゲームの参加者リスト。<see cref="GameMode"/> に応じて構成する。
    /// 一人用モードは [Human, Cpu, ...] の <see cref="PlayerCountSessionModel.Count"/> 人
    /// （自分 1 人＋残りが CPU。MapSelect で人数を選ぶ）、
    /// オンラインは [Human] の 1 人（従来の単独プレイ挙動）。
    /// </summary>
    public sealed class GameParticipants
    {
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

            return new[] { PlayerKind.Human };
        }
    }
}
