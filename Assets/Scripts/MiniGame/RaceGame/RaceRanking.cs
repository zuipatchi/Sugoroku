using System.Collections.Generic;
using Common.MiniGame;

namespace MiniGame.RaceGame
{
    /// <summary>順位表に並べる走者 1 人ぶんの結果。</summary>
    public readonly struct RaceEntry
    {
        public RaceEntry(int runner, bool finished, int millis, float progress)
        {
            Runner = runner;
            Finished = finished;
            Millis = millis;
            Progress = progress;
        }

        /// <summary>走者 index（0＝自分）。</summary>
        public int Runner { get; }

        /// <summary>ゴール済みか。</summary>
        public bool Finished { get; }

        /// <summary>
        /// ゴールタイム（ミリ秒）。分からないときは <see cref="MiniGameRanking.NotFinished"/>
        /// （まだ走っている・一人用モードで CPU のタイムを持っていない、のどちらでも起こる）。
        /// </summary>
        public int Millis { get; }

        /// <summary>いまの進捗（0＝スタート／1＝ゴール）。まだ走っている走者を並べるのに使う。</summary>
        public float Progress { get; }
    }

    /// <summary>順位表の 1 行（順位＋その走者の結果）。</summary>
    public readonly struct RaceStanding
    {
        public RaceStanding(int rank, RaceEntry entry)
        {
            Rank = rank;
            Entry = entry;
        }

        /// <summary>1 始まりの順位。同着は同じ順位になり、その次の順位は人数ぶん飛ぶ（1, 2, 2, 4）。</summary>
        public int Rank { get; }

        public RaceEntry Entry { get; }
    }

    /// <summary>
    /// 走者の結果を順位順に並べる純粋関数。オンライン対戦では相手がまだ走っている途中でも呼ばれるので、
    /// **ゴール済みとまだ走っている走者が混ざった状態**を扱えるようにしてある（暫定順位の表示に使う）。
    /// 全員がゴールしたときの並びは、勝者を決める <see cref="MiniGameRanking.Resolve"/>
    /// （レースはタイムが短いほど良い）と同じ結論になる。
    /// </summary>
    public static class RaceRanking
    {
        /// <summary>
        /// <paramref name="entries"/> を順位順に並べて返す。並び順は
        /// 「ゴール済み → タイムの短い順（タイム不明は後ろ）」「まだ走っている者同士は進んでいる順」。
        /// </summary>
        public static IReadOnlyList<RaceStanding> Order(IReadOnlyList<RaceEntry> entries)
        {
            List<RaceStanding> standings = new(entries?.Count ?? 0);
            if (entries == null || entries.Count == 0)
            {
                return standings;
            }

            List<RaceEntry> sorted = new(entries);
            sorted.Sort(Compare);

            int rank = 1;
            for (int i = 0; i < sorted.Count; i++)
            {
                // 前の行と優劣が付かない（＝同着）ならその順位を引き継ぎ、付くなら「i+1 位」に飛ぶ。
                if (i > 0 && CompareRank(sorted[i - 1], sorted[i]) != 0)
                {
                    rank = i + 1;
                }
                standings.Add(new RaceStanding(rank, sorted[i]));
            }
            return standings;
        }

        // 優劣が付かないときは走者 index で並びを安定させる（List.Sort は安定ソートではないため）。
        private static int Compare(RaceEntry a, RaceEntry b)
        {
            int order = CompareRank(a, b);
            return order != 0 ? order : a.Runner.CompareTo(b.Runner);
        }

        // 順位の優劣だけを比べる（0＝同着）。並びの安定化はここに混ぜない。
        private static int CompareRank(RaceEntry a, RaceEntry b)
        {
            if (a.Finished != b.Finished)
            {
                return a.Finished ? -1 : 1;
            }

            if (!a.Finished)
            {
                return b.Progress.CompareTo(a.Progress); // まだ走っている者は進んでいるほど上
            }

            bool aKnown = a.Millis != MiniGameRanking.NotFinished;
            bool bKnown = b.Millis != MiniGameRanking.NotFinished;
            if (aKnown != bKnown)
            {
                return aKnown ? -1 : 1; // タイムが分かっている方を上に置く
            }
            return aKnown ? a.Millis.CompareTo(b.Millis) : 0;
        }
    }
}
