using Common.MiniGame;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class MiniGamePrizeTests
    {
        [Test]
        public void 順位別の賞金は上位ほど高い()
        {
            Assert.AreEqual(500, MiniGamePrize.ForRank(1));
            Assert.AreEqual(300, MiniGamePrize.ForRank(2));
            Assert.AreEqual(100, MiniGamePrize.ForRank(3));
        }

        [Test]
        public void 賞金の出ない順位は0になる()
        {
            // 4 位以下と、順位が付かなかった参加者（0＝圏外）・負値は 0。
            Assert.AreEqual(0, MiniGamePrize.ForRank(MiniGamePrize.PaidRanks + 1));
            Assert.AreEqual(0, MiniGamePrize.ForRank(0));
            Assert.AreEqual(0, MiniGamePrize.ForRank(-1));
        }

        [Test]
        public void 被っちゃやーよだけは順位が付かない()
        {
            // 「誰とも被らなければ勝ち」は複数人が同時に勝ちうるので順位を定義できない。
            Assert.IsFalse(MiniGamePrize.HasRanking(MiniGameId.Overlap));
            Assert.IsTrue(MiniGamePrize.HasRanking(MiniGameId.Tap));
            Assert.IsTrue(MiniGamePrize.HasRanking(MiniGameId.Race));
        }

        [Test]
        public void 一律の賞金は1位と同額()
        {
            Assert.AreEqual(MiniGamePrize.ForRank(1), MiniGamePrize.Win);
        }
    }
}
