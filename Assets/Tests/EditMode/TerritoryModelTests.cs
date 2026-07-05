using Main.Board;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class TerritoryModelTests
    {
        [Test]
        public void Initialize直後は全マス未占拠()
        {
            using TerritoryModel territory = new();
            territory.Initialize(new[] { 2, 5, 8 });

            Assert.AreEqual(3, territory.Total);
            Assert.AreEqual(-1, territory.Owner(2).CurrentValue);
            Assert.AreEqual(0, territory.CountOwnedBy(0));
            Assert.IsTrue(territory.IsTerritory(5));
            Assert.IsFalse(territory.IsTerritory(3));
        }

        [Test]
        public void Claimは所有者を上書きして奪える()
        {
            using TerritoryModel territory = new();
            territory.Initialize(new[] { 2, 5 });

            territory.Claim(0, 2);
            Assert.AreEqual(0, territory.Owner(2).CurrentValue);
            Assert.AreEqual(1, territory.CountOwnedBy(0));

            // 相手が同じマスに止まると上書きで奪える。
            territory.Claim(1, 2);
            Assert.AreEqual(1, territory.Owner(2).CurrentValue);
            Assert.AreEqual(0, territory.CountOwnedBy(0));
            Assert.AreEqual(1, territory.CountOwnedBy(1));
        }

        [Test]
        public void 陣地マス以外へのClaimは無視される()
        {
            using TerritoryModel territory = new();
            territory.Initialize(new[] { 2 });

            territory.Claim(0, 7); // 陣地マスでない
            Assert.AreEqual(0, territory.CountOwnedBy(0));
            Assert.IsNull(territory.Owner(7));
        }

        [TestCase(4, 3)] // 総数4 → 過半数3
        [TestCase(3, 2)] // 総数3 → 過半数2
        [TestCase(1, 1)] // 総数1 → 過半数1
        public void RequiredToWinは過半数(int total, int expected)
        {
            using TerritoryModel territory = new();
            int[] cells = new int[total];
            for (int i = 0; i < total; i++)
            {
                cells[i] = i;
            }
            territory.Initialize(cells);

            Assert.AreEqual(expected, territory.RequiredToWin);
        }

        [Test]
        public void HasMajorityは過半数占拠で真になる()
        {
            using TerritoryModel territory = new();
            territory.Initialize(new[] { 0, 1, 2, 3 }); // 過半数 = 3

            territory.Claim(0, 0);
            territory.Claim(0, 1);
            Assert.IsFalse(territory.HasMajority(0), "2個ではまだ過半数でない");

            territory.Claim(0, 2);
            Assert.IsTrue(territory.HasMajority(0), "3個で過半数");
        }

        [Test]
        public void 陣地マスが無ければ勝利不能()
        {
            using TerritoryModel territory = new();
            territory.Initialize(new int[0]);

            Assert.AreEqual(0, territory.Total);
            Assert.AreEqual(int.MaxValue, territory.RequiredToWin);
            Assert.IsFalse(territory.HasMajority(0));
        }
    }
}
