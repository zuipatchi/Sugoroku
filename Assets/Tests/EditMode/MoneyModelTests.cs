using Common.GameSession;
using Main.Money;
using Main.Turn;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class MoneyModelTests
    {
        private static MoneyModel TwoPlayerMoney()
        {
            GameSessionModel session = new();
            session.SetSinglePlayer();
            return new MoneyModel(new GameParticipants(session, new PlayerCountSessionModel()));
        }

        [Test]
        public void 初期状態は全プレイヤーが初期所持金を持つ()
        {
            using MoneyModel money = TwoPlayerMoney();
            Assert.AreEqual(2, money.PlayerCount);
            Assert.AreEqual(MoneyModel.InitialMoney, money.Money(0).CurrentValue);
            Assert.AreEqual(MoneyModel.InitialMoney, money.Money(1).CurrentValue);
        }

        [Test]
        public void Addは正の値で加算する()
        {
            using MoneyModel money = TwoPlayerMoney();
            Assert.AreEqual(300, money.Add(0, 300));
            Assert.AreEqual(MoneyModel.InitialMoney + 300, money.Money(0).CurrentValue);
        }

        [Test]
        public void Addは負の値で減算する()
        {
            using MoneyModel money = TwoPlayerMoney();
            Assert.AreEqual(-200, money.Add(0, -200));
            Assert.AreEqual(MoneyModel.InitialMoney - 200, money.Money(0).CurrentValue);
        }

        [Test]
        public void 所持金はマイナスにならず0で止まる()
        {
            using MoneyModel money = TwoPlayerMoney();
            // 減額が所持金を上回っても、実際に減るのは残高ぶんだけ（返り値もその額）。
            Assert.AreEqual(-MoneyModel.InitialMoney, money.Add(0, -(MoneyModel.InitialMoney + 500)));
            Assert.AreEqual(MoneyModel.MinMoney, money.Money(0).CurrentValue);
        }

        [Test]
        public void 所持金0からの減額は何も動かさない()
        {
            using MoneyModel money = TwoPlayerMoney();
            money.Add(0, -MoneyModel.InitialMoney);
            Assert.AreEqual(0, money.Add(0, -300));
            Assert.AreEqual(MoneyModel.MinMoney, money.Money(0).CurrentValue);
        }

        [Test]
        public void Addはプレイヤーごとに独立している()
        {
            using MoneyModel money = TwoPlayerMoney();
            money.Add(1, 400);
            Assert.AreEqual(MoneyModel.InitialMoney + 400, money.Money(1).CurrentValue);
            Assert.AreEqual(MoneyModel.InitialMoney, money.Money(0).CurrentValue);
        }
    }
}
