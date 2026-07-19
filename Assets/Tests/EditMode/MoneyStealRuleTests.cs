using System;
using Main.Money;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class MoneyStealRuleTests
    {
        [Test]
        public void 所持金が0以下なら奪える額は0()
        {
            Random rng = new(1);
            Assert.AreEqual(0, MoneyStealRule.Amount(0, rng));
            Assert.AreEqual(0, MoneyStealRule.Amount(-500, rng));
        }

        [Test]
        public void 奪う額は下限割合以上上限割合以下に収まる()
        {
            const int opponent = 1000;
            Random rng = new(12345);
            for (int i = 0; i < 200; i++)
            {
                int amount = MoneyStealRule.Amount(opponent, rng);
                Assert.GreaterOrEqual(amount, (int)(opponent * MoneyStealRule.MinFraction));
                Assert.LessOrEqual(amount, (int)(opponent * MoneyStealRule.MaxFraction));
            }
        }

        [Test]
        public void 奪う額は最低1で全額は超えない()
        {
            Random rng = new(7);
            // 所持金 1 でも割合切り捨てで 0 にならず最低 1 を奪える。
            Assert.AreEqual(1, MoneyStealRule.Amount(1, rng));
            // 相手の所持金を超えて奪わない。
            for (int i = 0; i < 100; i++)
            {
                Assert.LessOrEqual(MoneyStealRule.Amount(3, rng), 3);
            }
        }

        [Test]
        public void 同じseedなら決定的に同じ額になる()
        {
            int first = MoneyStealRule.Amount(1000, new Random(999));
            int second = MoneyStealRule.Amount(1000, new Random(999));
            Assert.AreEqual(first, second);
        }

        [Test]
        public void 乱数源がnullなら下限割合で計算する()
        {
            Assert.AreEqual((int)(1000 * MoneyStealRule.MinFraction), MoneyStealRule.Amount(1000, null));
        }
    }
}
