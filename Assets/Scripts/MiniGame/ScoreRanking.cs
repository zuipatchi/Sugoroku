using System.Collections.Generic;

namespace MiniGame
{
    /// <summary>順位表の 1 行（参加者 index ＋ 1 始まりの順位）。</summary>
    public readonly struct ScoreStanding
    {
        public ScoreStanding(int participant, int rank)
        {
            Participant = participant;
            Rank = rank;
        }

        /// <summary>参加者 index（0＝自分）。</summary>
        public int Participant { get; }

        /// <summary>1 始まりの順位。同点は同じ順位になり、その次の順位は人数ぶん飛ぶ（1, 2, 2, 4）。</summary>
        public int Rank { get; }
    }

    /// <summary>
    /// 「スコアが大きいほど良い」ミニゲーム（タップ連打の連打数など）の結果を順位順に並べる純粋関数。
    /// 全員ぶんのスコアが揃ったときの並びは、勝者を決める
    /// <see cref="Common.MiniGame.MiniGameRanking.Resolve"/>（最多が勝ち）と同じ結論になる。
    /// 走者のゴール状況まで見る 2D レースは <c>RaceRanking</c> が別に持つ。
    /// </summary>
    public static class ScoreRanking
    {
        /// <summary>
        /// <paramref name="scores"/>（index＝参加者）をスコアの大きい順に並べて返す。
        /// 同点は同じ順位で、並びは参加者 index の小さい方を先にして安定させる。
        /// </summary>
        public static IReadOnlyList<ScoreStanding> Order(IReadOnlyList<int> scores)
        {
            List<ScoreStanding> standings = new(scores?.Count ?? 0);
            if (scores == null || scores.Count == 0)
            {
                return standings;
            }

            List<int> sorted = new(scores.Count);
            for (int participant = 0; participant < scores.Count; participant++)
            {
                sorted.Add(participant);
            }

            // 優劣が付かないときは参加者 index で並びを安定させる（List.Sort は安定ソートではないため）。
            sorted.Sort((a, b) =>
            {
                int order = scores[b].CompareTo(scores[a]);
                return order != 0 ? order : a.CompareTo(b);
            });

            int rank = 1;
            for (int i = 0; i < sorted.Count; i++)
            {
                // 前の行と同点ならその順位を引き継ぎ、下回るなら「i+1 位」に飛ぶ。
                if (i > 0 && scores[sorted[i - 1]] != scores[sorted[i]])
                {
                    rank = i + 1;
                }
                standings.Add(new ScoreStanding(sorted[i], rank));
            }
            return standings;
        }
    }
}
