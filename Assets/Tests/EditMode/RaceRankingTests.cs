using System.Collections.Generic;
using Common.MiniGame;
using MiniGame.RaceGame;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class RaceRankingTests
    {
        private static RaceEntry Finished(int runner, int millis)
        {
            return new RaceEntry(runner, true, millis, 1f);
        }

        private static RaceEntry Running(int runner, float progress)
        {
            return new RaceEntry(runner, false, MiniGameRanking.NotFinished, progress);
        }

        [Test]
        public void ゴール済みはタイムの短い順に並ぶ()
        {
            IReadOnlyList<RaceStanding> standings = RaceRanking.Order(new List<RaceEntry>
            {
                Finished(0, 9100),
                Finished(1, 8400),
                Finished(2, 11700),
            });

            Assert.AreEqual(new[] { 1, 0, 2 }, Runners(standings));
            Assert.AreEqual(new[] { 1, 2, 3 }, Ranks(standings));
        }

        [Test]
        public void 同着は同じ順位になり次の順位は人数ぶん飛ぶ()
        {
            IReadOnlyList<RaceStanding> standings = RaceRanking.Order(new List<RaceEntry>
            {
                Finished(0, 8400),
                Finished(1, 8400),
                Finished(2, 9000),
            });

            Assert.AreEqual(new[] { 1, 1, 3 }, Ranks(standings));
        }

        [Test]
        public void 走行中はゴール済みより後ろで進んでいる順に並ぶ()
        {
            // オンラインで自分だけゴールした直後の暫定順位を想定。
            IReadOnlyList<RaceStanding> standings = RaceRanking.Order(new List<RaceEntry>
            {
                Finished(0, 9100),
                Running(1, 0.3f),
                Running(2, 0.8f),
            });

            Assert.AreEqual(new[] { 0, 2, 1 }, Runners(standings));
            Assert.AreEqual(new[] { 1, 2, 3 }, Ranks(standings));
        }

        [Test]
        public void タイムが分からないゴール済みはタイムが分かる走者より後ろになる()
        {
            // 一人用モードの CPU（先着で決着するのでタイムを持っていない）を想定。
            IReadOnlyList<RaceStanding> standings = RaceRanking.Order(new List<RaceEntry>
            {
                new RaceEntry(0, true, MiniGameRanking.NotFinished, 1f),
                Finished(1, 9100),
            });

            Assert.AreEqual(new[] { 1, 0 }, Runners(standings));
        }

        [Test]
        public void 空の入力では空の順位表になる()
        {
            Assert.IsEmpty(RaceRanking.Order(null));
            Assert.IsEmpty(RaceRanking.Order(new List<RaceEntry>()));
        }

        private static int[] Runners(IReadOnlyList<RaceStanding> standings)
        {
            int[] runners = new int[standings.Count];
            for (int i = 0; i < standings.Count; i++)
            {
                runners[i] = standings[i].Entry.Runner;
            }
            return runners;
        }

        private static int[] Ranks(IReadOnlyList<RaceStanding> standings)
        {
            int[] ranks = new int[standings.Count];
            for (int i = 0; i < standings.Count; i++)
            {
                ranks[i] = standings[i].Rank;
            }
            return ranks;
        }
    }
}
