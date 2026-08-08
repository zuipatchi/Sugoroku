using System.Collections.Generic;
using MiniGame;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class ScoreRankingTests
    {
        [Test]
        public void スコアの大きい順に並ぶ()
        {
            IReadOnlyList<ScoreStanding> standings = ScoreRanking.Order(new List<int> { 12, 31, 7 });

            Assert.AreEqual(new[] { 1, 0, 2 }, Participants(standings));
            Assert.AreEqual(new[] { 1, 2, 3 }, Ranks(standings));
        }

        [Test]
        public void 同点は同じ順位になり次の順位は人数ぶん飛ぶ()
        {
            IReadOnlyList<ScoreStanding> standings = ScoreRanking.Order(new List<int> { 20, 20, 5 });

            Assert.AreEqual(new[] { 1, 1, 3 }, Ranks(standings));
        }

        [Test]
        public void 同点は参加者indexの小さい順に並ぶ()
        {
            IReadOnlyList<ScoreStanding> standings = ScoreRanking.Order(new List<int> { 5, 20, 20, 5 });

            Assert.AreEqual(new[] { 1, 2, 0, 3 }, Participants(standings));
        }

        [Test]
        public void 全員同点なら全員1位()
        {
            IReadOnlyList<ScoreStanding> standings = ScoreRanking.Order(new List<int> { 0, 0, 0 });

            Assert.AreEqual(new[] { 1, 1, 1 }, Ranks(standings));
        }

        [Test]
        public void 空やnullは空の順位表を返す()
        {
            Assert.IsEmpty(ScoreRanking.Order(new List<int>()));
            Assert.IsEmpty(ScoreRanking.Order(null));
        }

        private static int[] Participants(IReadOnlyList<ScoreStanding> standings)
        {
            int[] participants = new int[standings.Count];
            for (int i = 0; i < standings.Count; i++)
            {
                participants[i] = standings[i].Participant;
            }
            return participants;
        }

        private static int[] Ranks(IReadOnlyList<ScoreStanding> standings)
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
