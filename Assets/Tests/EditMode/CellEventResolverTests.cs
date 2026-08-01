using Main.Board;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class CellEventResolverTests
    {
        [Test]
        public void TryGetMoveStepsは進むマスで正のマス数を返す()
        {
            Assert.IsTrue(CellEventResolver.TryGetMoveSteps(BoardCellEvent.Forward, 3, out int steps));
            Assert.AreEqual(3, steps);
        }

        [Test]
        public void TryGetMoveStepsは戻るマスで負のマス数を返す()
        {
            // 戻るは符号だけを反転させる（実際の巻き戻しは BoardMath.Advance がループで面倒を見る）。
            Assert.IsTrue(CellEventResolver.TryGetMoveSteps(BoardCellEvent.Back, 3, out int steps));
            Assert.AreEqual(-3, steps);
        }

        [TestCase(BoardCellEvent.None)]
        [TestCase(BoardCellEvent.MiniGame)]
        [TestCase(BoardCellEvent.MoneyUp)]
        [TestCase(BoardCellEvent.MoneyDown)]
        [TestCase(BoardCellEvent.Territory)]
        [TestCase(BoardCellEvent.Item)]
        public void TryGetMoveStepsは移動以外のマスでfalseを返す(BoardCellEvent cellEvent)
        {
            Assert.IsFalse(CellEventResolver.TryGetMoveSteps(cellEvent, 3, out int steps));
            Assert.AreEqual(0, steps);
        }

        [Test]
        public void IsMoneyEventはお金マスだけtrueを返す()
        {
            Assert.IsTrue(CellEventResolver.IsMoneyEvent(BoardCellEvent.MoneyUp));
            Assert.IsTrue(CellEventResolver.IsMoneyEvent(BoardCellEvent.MoneyDown));
            Assert.IsFalse(CellEventResolver.IsMoneyEvent(BoardCellEvent.Forward));
            Assert.IsFalse(CellEventResolver.IsMoneyEvent(BoardCellEvent.Territory));
        }

        [Test]
        public void TryGetMoneyDeltaはお金マスの符号を付ける()
        {
            Assert.IsTrue(CellEventResolver.TryGetMoneyDelta(BoardCellEvent.MoneyUp, 300, out int up));
            Assert.AreEqual(300, up);

            Assert.IsTrue(CellEventResolver.TryGetMoneyDelta(BoardCellEvent.MoneyDown, 300, out int down));
            Assert.AreEqual(-300, down);

            Assert.IsFalse(CellEventResolver.TryGetMoneyDelta(BoardCellEvent.Forward, 300, out int none));
            Assert.AreEqual(0, none);
        }
    }
}
