using System;
using Main.Board;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class MoveCellRuleTests
    {
        [Test]
        public void マス数は下限と上限の範囲に収まる()
        {
            Random rng = new(1234);
            for (int i = 0; i < 200; i++)
            {
                int steps = MoveCellRule.Steps(rng);
                Assert.GreaterOrEqual(steps, MoveCellRule.MinSteps);
                Assert.LessOrEqual(steps, MoveCellRule.MaxSteps);
            }
        }

        [Test]
        public void マス数は常に正で0を返さない()
        {
            // 0 マスだと「進むマスに止まったのに動かない」＝連鎖が途切れて演出も無意味になる。
            Random rng = new(7);
            for (int i = 0; i < 200; i++)
            {
                Assert.Greater(MoveCellRule.Steps(rng), 0);
            }
        }

        [Test]
        public void 同じseedなら同じマス数を返す()
        {
            // オンラインでは着地した本人が決めた値を配るので同期自体は seed に依存しないが、
            // テストで固定できることが MoneyCellRule / MoneyStealRule と揃った規約になっている。
            Assert.AreEqual(MoveCellRule.Steps(new Random(999)), MoveCellRule.Steps(new Random(999)));
        }

        [Test]
        public void 乱数源がnullなら下限を返す()
        {
            Assert.AreEqual(MoveCellRule.MinSteps, MoveCellRule.Steps(null));
        }

        [Test]
        public void 十分な試行で下限と上限の両方が出る()
        {
            // 範囲の端が出ない実装ミス（rng.Next の上限を +1 し忘れる等）を検出する。
            Random rng = new(42);
            bool sawMin = false;
            bool sawMax = false;
            for (int i = 0; i < 500; i++)
            {
                int steps = MoveCellRule.Steps(rng);
                sawMin |= steps == MoveCellRule.MinSteps;
                sawMax |= steps == MoveCellRule.MaxSteps;
            }
            Assert.IsTrue(sawMin, $"下限 {MoveCellRule.MinSteps} が一度も出ませんでした。");
            Assert.IsTrue(sawMax, $"上限 {MoveCellRule.MaxSteps} が一度も出ませんでした。");
        }
    }
}
