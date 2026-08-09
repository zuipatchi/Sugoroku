using Common.MiniGame;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class MiniGameRankingTests
    {
        [Test]
        public void タップ連打は連打数が最多の人が勝つ()
        {
            bool[] wins = MiniGameRanking.Resolve(MiniGameId.Tap, new[] { 30, 51, 12 });

            Assert.IsFalse(wins[0]);
            Assert.IsTrue(wins[1]);
            Assert.IsFalse(wins[2]);
        }

        [Test]
        public void タップ連打の同数は全員勝ちになる()
        {
            // 席順で優劣を付けると先手が有利になるので、同点は並び立たせる。
            bool[] wins = MiniGameRanking.Resolve(MiniGameId.Tap, new[] { 40, 40, 12 });

            Assert.IsTrue(wins[0]);
            Assert.IsTrue(wins[1]);
            Assert.IsFalse(wins[2]);
        }

        [Test]
        public void レースはゴールタイムが最短の人が勝つ()
        {
            bool[] wins = MiniGameRanking.Resolve(
                MiniGameId.Race, new[] { 8200, 7100, MiniGameRanking.NotFinished });

            Assert.IsFalse(wins[0]);
            Assert.IsTrue(wins[1]);
            Assert.IsFalse(wins[2]);
        }

        [Test]
        public void レースは全員未ゴールなら勝者なし()
        {
            bool[] wins = MiniGameRanking.Resolve(
                MiniGameId.Race, new[] { MiniGameRanking.NotFinished, MiniGameRanking.NotFinished });

            Assert.IsFalse(wins[0]);
            Assert.IsFalse(wins[1]);
        }

        [Test]
        public void 被っちゃやーよは誰とも被らなかった人だけが勝つ()
        {
            // 0 番と 2 番が同じカードを選んで共倒れ、1 番だけが単独。
            bool[] wins = MiniGameRanking.Resolve(MiniGameId.Overlap, new[] { 1, 0, 1 });

            Assert.IsFalse(wins[0]);
            Assert.IsTrue(wins[1]);
            Assert.IsFalse(wins[2]);
        }

        [Test]
        public void 被っちゃやーよは複数人が同時に勝てる()
        {
            bool[] wins = MiniGameRanking.Resolve(MiniGameId.Overlap, new[] { 0, 1, 2 });

            Assert.IsTrue(wins[0]);
            Assert.IsTrue(wins[1]);
            Assert.IsTrue(wins[2]);
        }

        [Test]
        public void 被っちゃやーよの無効票は勝てない()
        {
            // 制限時間内に選べなかった人（NoChoice）は、被っていなくても獲得できない。
            bool[] wins = MiniGameRanking.Resolve(
                MiniGameId.Overlap, new[] { MiniGameRanking.NoChoice, MiniGameRanking.NoChoice, 2 });

            Assert.IsFalse(wins[0]);
            Assert.IsFalse(wins[1]);
            Assert.IsTrue(wins[2]);
        }

        [Test]
        public void 参加者が居なければ勝者も居ない()
        {
            Assert.AreEqual(0, MiniGameRanking.Resolve(MiniGameId.Tap, new int[0]).Length);
            Assert.AreEqual(0, MiniGameRanking.Resolve(MiniGameId.Tap, null).Length);
        }
        [Test]
        public void 連打の順位は連打数の多い順に付く()
        {
            int[] ranks = MiniGameRanking.RanksOf(MiniGameId.Tap, new[] { 30, 50, 10 });

            Assert.AreEqual(2, ranks[0]);
            Assert.AreEqual(1, ranks[1]);
            Assert.AreEqual(3, ranks[2]);
        }

        [Test]
        public void レースの順位はタイムの短い順に付く()
        {
            int[] ranks = MiniGameRanking.RanksOf(MiniGameId.Race, new[] { 9000, 7000, 8000 });

            Assert.AreEqual(3, ranks[0]);
            Assert.AreEqual(1, ranks[1]);
            Assert.AreEqual(2, ranks[2]);
        }

        [Test]
        public void 同着は同じ順位になり次の順位は人数ぶん飛ぶ()
        {
            // 賞金は順位で決まるので、同着に別々の額を配ってしまわないこと（1, 1, 3 位）。
            int[] ranks = MiniGameRanking.RanksOf(MiniGameId.Tap, new[] { 50, 50, 10 });

            Assert.AreEqual(1, ranks[0]);
            Assert.AreEqual(1, ranks[1]);
            Assert.AreEqual(3, ranks[2]);
        }

        [Test]
        public void ゴールできなかった走者は順位が付かない()
        {
            // タイムが無いので順位を付けようがない＝0（圏外）。賞金も 0 になる。
            int[] ranks = MiniGameRanking.RanksOf(
                MiniGameId.Race, new[] { MiniGameRanking.NotFinished, 7000 });

            Assert.AreEqual(0, ranks[0]);
            Assert.AreEqual(1, ranks[1]);
        }

        [Test]
        public void 順位が定義できないゲームは全員圏外になる()
        {
            // 被っちゃやーよは順位を使わない（呼び出し側が HasRanking で振り分ける）。
            int[] ranks = MiniGameRanking.RanksOf(MiniGameId.Overlap, new[] { 0, 1, 2 });

            Assert.AreEqual(new[] { 0, 0, 0 }, ranks);
        }

        [Test]
        public void 参加者が居なければ順位も無い()
        {
            Assert.AreEqual(0, MiniGameRanking.RanksOf(MiniGameId.Tap, new int[0]).Length);
            Assert.AreEqual(0, MiniGameRanking.RanksOf(MiniGameId.Tap, null).Length);
        }
    }
}