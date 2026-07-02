using System.Collections.Generic;
using System.Linq;
using Common.GameSession;

namespace Main.Turn
{
    /// <summary>
    /// このゲームの参加者リスト。<see cref="GameMode"/> に応じて構成する。
    /// 一人用モードは [Human, Cpu] の 2 人（CPU と 1 対 1）、
    /// オンラインは [Human] の 1 人（従来の単独プレイ挙動）。
    /// </summary>
    public sealed class GameParticipants
    {
        private readonly IReadOnlyList<PlayerKind> _players;

        public GameParticipants(GameSessionModel gameSession)
        {
            _players = Build(gameSession.Mode);
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

        private static IReadOnlyList<PlayerKind> Build(GameMode mode)
        {
            if (mode == GameMode.SinglePlayer)
            {
                return new[] { PlayerKind.Human, PlayerKind.Cpu };
            }

            return new[] { PlayerKind.Human };
        }
    }
}
