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
    /// オンラインの参加者数は、作成/参加したルームの定員（<c>ISession.MaxPlayers</c>＝ホストが 2〜4 で選ぶ）
    /// を <see cref="OnlinePlayerCountFrom"/> で反映する（Session 未設定時は下限 2）。
    /// </summary>
    public sealed class GameParticipants
    {
        private readonly IReadOnlyList<PlayerKind> _players;

        public GameParticipants(GameSessionModel gameSession, PlayerCountSessionModel playerCount)
        {
            _players = Build(gameSession.Mode, playerCount.Count, OnlinePlayerCountFrom(gameSession.SessionMaxPlayers));
        }

        /// <summary>
        /// オンラインの参加者数を、作成/参加したルームの定員（<paramref name="sessionMaxPlayers"/>）から決める。
        /// Session 未設定（null）や 2 未満のときは下限 2 にクランプする。純粋関数（テスト対象）。
        /// </summary>
        public static int OnlinePlayerCountFrom(int? sessionMaxPlayers)
        {
            int count = sessionMaxPlayers ?? 2;
            return count < 2 ? 2 : count;
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

        private static IReadOnlyList<PlayerKind> Build(GameMode mode, int singlePlayerCount, int onlinePlayerCount)
        {
            if (mode == GameMode.SinglePlayer)
            {
                // 自分（先攻＝index 0）＋残りは CPU。人数は PlayerCountSessionModel でクランプ済みだが念のため下限 2。
                int count = singlePlayerCount < 2 ? 2 : singlePlayerCount;
                List<PlayerKind> players = new(count) { PlayerKind.Human };
                for (int i = 1; i < count; i++)
                {
                    players.Add(PlayerKind.Cpu);
                }
                return players;
            }

            // オンラインはルーム定員ぶん（2〜4・最低 2）を全員 Human で構成する。
            List<PlayerKind> online = new(onlinePlayerCount);
            for (int i = 0; i < onlinePlayerCount; i++)
            {
                online.Add(PlayerKind.Human);
            }
            return online;
        }
    }
}
