using Main.Board;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class BoardMathTests
    {
        private const int Columns = 6;
        private const int Rows = 5;
        private const int CellCount = 18; // 2 * 6 + 2 * 5 - 4

        [Test]
        public void PerimeterCellCountは外周のマス数を返す()
        {
            Assert.AreEqual(CellCount, BoardMath.PerimeterCellCount(Columns, Rows));
        }

        [Test]
        public void Advanceはループせず素直に前進する()
        {
            Assert.AreEqual(5, BoardMath.Advance(2, 3, CellCount));
        }

        [Test]
        public void Advanceはスタートを越えるとループして巻き戻る()
        {
            // 16 + 5 = 21 → 21 % 18 = 3
            Assert.AreEqual(3, BoardMath.Advance(16, 5, CellCount));
        }

        [Test]
        public void Advanceはちょうど一周するとスタートに戻る()
        {
            Assert.AreEqual(0, BoardMath.Advance(13, 5, CellCount));
        }

        [TestCase(2, 3, false)] // 5 < 18
        [TestCase(16, 5, true)] // 21 >= 18（通過）
        [TestCase(13, 5, true)] // 18 >= 18（ちょうど到達）
        [TestCase(13, 4, false)] // 17 < 18
        public void CompletesLapは周回到達_通過の境界を判定する(int current, int steps, bool expected)
        {
            Assert.AreEqual(expected, BoardMath.CompletesLap(current, steps, CellCount));
        }

        [Test]
        public void CellGridPositionの四隅が正しい()
        {
            Assert.AreEqual((0, 0), BoardMath.CellGridPosition(0, Columns, Rows), "左上(スタート)");
            Assert.AreEqual((Columns - 1, 0), BoardMath.CellGridPosition(Columns - 1, Columns, Rows), "右上");
            Assert.AreEqual((Columns - 1, Rows - 1), BoardMath.CellGridPosition(Columns + Rows - 2, Columns, Rows), "右下");
            Assert.AreEqual((0, Rows - 1), BoardMath.CellGridPosition(2 * Columns + Rows - 3, Columns, Rows), "左下");
        }

        [Test]
        public void CellGridPositionは時計回りに連続して隣接する()
        {
            for (int i = 0; i < CellCount; i++)
            {
                (int Column, int Row) a = BoardMath.CellGridPosition(i, Columns, Rows);
                (int Column, int Row) b = BoardMath.CellGridPosition(BoardMath.Advance(i, 1, CellCount), Columns, Rows);
                int manhattan = System.Math.Abs(a.Column - b.Column) + System.Math.Abs(a.Row - b.Row);
                Assert.AreEqual(1, manhattan, $"index {i} と次のマスはちょうど 1 マス隣接していること");
            }
        }

        [Test]
        public void CellGridPositionは外周マスを重複なく埋める()
        {
            bool[,] visited = new bool[Columns, Rows];
            for (int i = 0; i < CellCount; i++)
            {
                (int Column, int Row) = BoardMath.CellGridPosition(i, Columns, Rows);
                Assert.IsFalse(visited[Column, Row], $"index {i} のマス ({Column}, {Row}) が重複している");
                visited[Column, Row] = true;
            }
        }
    }
}
