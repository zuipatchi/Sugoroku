using System;
using Common.GameSession;
using Main.Board;
using Main.Turn;
using NUnit.Framework;
using R3;

namespace Tests.EditMode
{
    public class TerritoryModelTests
    {
        // 参加者 <paramref name="playerCount"/> 人ぶんの一人用セッションで TerritoryModel を作る
        // （RequiredToWin の分母＝プレイヤー数を検証するため人数を変えられるようにする）。
        private static TerritoryModel NewTerritory(int playerCount = 2)
        {
            GameSessionModel session = new();
            session.SetSinglePlayer();
            PlayerCountSessionModel count = new();
            count.Select(playerCount);
            return new TerritoryModel(new GameParticipants(session, count));
        }

        [Test]
        public void Initialize直後は全マス未占拠()
        {
            using TerritoryModel territory = NewTerritory();
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
            using TerritoryModel territory = NewTerritory();
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
        public void ChangedはInitializeと有効なClaimで発火する()
        {
            using TerritoryModel territory = NewTerritory();
            int count = 0;
            using IDisposable sub = territory.Changed.Subscribe(_ => count++);

            territory.Initialize(new[] { 2, 5 });
            Assert.AreEqual(1, count, "Initialize で1回発火");

            territory.Claim(0, 2);
            Assert.AreEqual(2, count, "有効な Claim で1回発火");

            territory.Claim(0, 7); // 陣地マスでない → 発火しない
            Assert.AreEqual(2, count, "陣地マス以外の Claim では発火しない");
        }

        [Test]
        public void 陣地マス以外へのClaimは無視される()
        {
            using TerritoryModel territory = NewTerritory();
            territory.Initialize(new[] { 2 });

            territory.Claim(0, 7); // 陣地マスでない
            Assert.AreEqual(0, territory.CountOwnedBy(0));
            Assert.IsNull(territory.Owner(7));
        }

        // RequiredToWin ＝ 総数をプレイヤー数で割った端数切り上げ。
        [TestCase(2, 8, 4)] // 2人・総数8 → ceil(8/2)=4
        [TestCase(4, 8, 2)] // 4人・総数8 → ceil(8/4)=2
        [TestCase(3, 8, 3)] // 3人・総数8 → ceil(8/3)=3
        [TestCase(4, 7, 2)] // 4人・総数7 → ceil(7/4)=2
        [TestCase(2, 1, 1)] // 2人・総数1 → ceil(1/2)=1
        public void RequiredToWinは総数をプレイヤー数で割った切り上げ(int players, int total, int expected)
        {
            using TerritoryModel territory = NewTerritory(players);
            int[] cells = new int[total];
            for (int i = 0; i < total; i++)
            {
                cells[i] = i;
            }
            territory.Initialize(cells);

            Assert.AreEqual(expected, territory.RequiredToWin);
        }

        [Test]
        public void HasReachedGoalは必要数の占拠で真になる()
        {
            using TerritoryModel territory = NewTerritory(4); // 4人・総数4 → 必要数 ceil(4/4)=1
            territory.Initialize(new[] { 0, 1, 2, 3 });

            Assert.IsFalse(territory.HasReachedGoal(0), "0個ではまだ到達していない");

            territory.Claim(0, 0);
            Assert.IsTrue(territory.HasReachedGoal(0), "必要数=1（ceil(4/4)）に到達");
        }

        [Test]
        public void HasReachedGoalは2人なら半分の切り上げ()
        {
            using TerritoryModel territory = NewTerritory(2); // 2人・総数5 → 必要数 ceil(5/2)=3
            territory.Initialize(new[] { 0, 1, 2, 3, 4 });

            territory.Claim(0, 0);
            territory.Claim(0, 1);
            Assert.IsFalse(territory.HasReachedGoal(0), "2個ではまだ到達していない");

            territory.Claim(0, 2);
            Assert.IsTrue(territory.HasReachedGoal(0), "3個（ceil(5/2)）で到達");
        }

        [Test]
        public void 陣地マスが無ければ勝利不能()
        {
            using TerritoryModel territory = NewTerritory();
            territory.Initialize(new int[0]);

            Assert.AreEqual(0, territory.Total);
            Assert.AreEqual(int.MaxValue, territory.RequiredToWin);
            Assert.IsFalse(territory.HasReachedGoal(0));
        }

        [Test]
        public void CellsNotOwnedByは未占拠と相手占拠を返し自分の占拠を除く()
        {
            using TerritoryModel territory = NewTerritory();
            territory.Initialize(new[] { 2, 5, 8 });

            territory.Claim(0, 2); // 自分（p0）が占拠
            territory.Claim(1, 5); // 相手（p1）が占拠
            // 8 は未占拠のまま

            // 陣地獲得で p0 が選べるのは「自分以外」＝相手占拠(5)＋未占拠(8)。
            Assert.That(territory.CellsNotOwnedBy(0), Is.EquivalentTo(new[] { 5, 8 }));
            // p1 から見れば自分(5)を除いた 2, 8。
            Assert.That(territory.CellsNotOwnedBy(1), Is.EquivalentTo(new[] { 2, 8 }));
        }

        [Test]
        public void CellsNotOwnedByは全マス自分の占拠なら空()
        {
            using TerritoryModel territory = NewTerritory();
            territory.Initialize(new[] { 2, 5 });

            territory.Claim(0, 2);
            territory.Claim(0, 5);

            Assert.IsEmpty(territory.CellsNotOwnedBy(0));
            // 陣地マスが無い盤面でも空。
            using TerritoryModel empty = NewTerritory();
            empty.Initialize(new int[0]);
            Assert.IsEmpty(empty.CellsNotOwnedBy(0));
        }
    }
}
