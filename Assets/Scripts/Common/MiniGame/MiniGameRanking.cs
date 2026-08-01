using System.Collections.Generic;

namespace Common.MiniGame
{
    /// <summary>
    /// 全参加者の「結果値」から勝者を決める純粋関数。オンライン対戦では各クライアントが自分の値だけを
    /// 出し合い、集まった値をこの関数に通して勝敗を決めるので、**全員が同じ結論に至る**（誰かが判定役に
    /// なる必要が無い）。結果値の意味はゲームごとに違う（<see cref="Resolve"/> のコメント参照）。
    /// </summary>
    public static class MiniGameRanking
    {
        /// <summary>2D レースでゴールできなかった走者の結果値（タイムなので「最悪」を表す最大値）。</summary>
        public const int NotFinished = int.MaxValue;

        /// <summary>被っちゃやーよで制限時間内に選べなかった（無効票）ときの結果値。</summary>
        public const int NoChoice = -1;

        /// <summary>
        /// <paramref name="game"/> で「最悪」を意味する結果値。ミニゲームを起動できなかったときでも
        /// この値を配れば、他の参加者を待たせたまま止めずに済む（自分は必ず負けになる）。
        /// </summary>
        public static int WorstValue(MiniGameId game)
        {
            return game switch
            {
                MiniGameId.Race => NotFinished,   // タイムなので最大＝最悪
                MiniGameId.Overlap => NoChoice,   // 無効票＝獲得できない
                _ => 0,                            // 連打数なので 0
            };
        }

        /// <summary>
        /// 参加者ごとの結果値 <paramref name="values"/>（index＝参加者）から、勝ったかどうかを参加者ごとに返す。
        /// 結果値の意味とルールはゲームごとに決まる。
        /// - タップ連打: 連打数。最多が勝ち（同数は全員勝ち）
        /// - 2D レース: ゴールまでのミリ秒。最短が勝ち（同着は全員勝ち・全員未ゴールなら勝者なし）
        /// - 被っちゃやーよ: 選んだカードの index。他の誰とも被らなければ勝ち（複数人が同時に勝ちうる・無効票は負け）
        /// </summary>
        public static bool[] Resolve(MiniGameId game, IReadOnlyList<int> values)
        {
            int count = values?.Count ?? 0;
            bool[] wins = new bool[count];
            if (count == 0)
            {
                return wins;
            }

            if (game == MiniGameId.Overlap)
            {
                ResolveUnique(values, wins);
                return wins;
            }

            // レースはタイムなので小さいほど良い。それ以外（連打数）は大きいほど良い。
            ResolveBest(values, wins, lowerIsBetter: game == MiniGameId.Race);
            return wins;
        }

        // 他の誰とも同じ値でなければ勝ち（無効票＝負値は勝てない）。
        private static void ResolveUnique(IReadOnlyList<int> values, bool[] wins)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] == NoChoice)
                {
                    continue;
                }

                bool unique = true;
                for (int j = 0; j < values.Count; j++)
                {
                    if (j != i && values[j] == values[i])
                    {
                        unique = false;
                        break;
                    }
                }
                wins[i] = unique;
            }
        }

        // 最良値（最大 or 最小）を取った参加者が勝ち。同値は全員勝ち。
        private static void ResolveBest(IReadOnlyList<int> values, bool[] wins, bool lowerIsBetter)
        {
            int best = values[0];
            for (int i = 1; i < values.Count; i++)
            {
                if (lowerIsBetter ? values[i] < best : values[i] > best)
                {
                    best = values[i];
                }
            }

            // 全員がゴールできなかったレースは勝者なし（最良値が「未ゴール」のまま）。
            if (lowerIsBetter && best == NotFinished)
            {
                return;
            }

            for (int i = 0; i < values.Count; i++)
            {
                wins[i] = values[i] == best;
            }
        }
    }
}
