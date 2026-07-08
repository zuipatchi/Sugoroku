using Main.Board;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class BoardLayoutCalculatorTests
    {
        private const float Fill = 0.62f;
        private const float W = 1080f; // 縦画面の想定サイズ
        private const float H = 1920f;

        [Test]
        public void CalculateAreaは横長盤面を表示列数ぶんで割り付けて横へはみ出す()
        {
            BoardLayoutCalculator.AreaLayout a =
                BoardLayoutCalculator.CalculateArea(W, H, 10, 5, Fill, 4);

            // 4 列ぶんが利用可能幅（0.84*W）に収まる間隔になっている。
            float spacing = a.CellSize / Fill;
            float availableWidth = W - W * 0.08f * 2f;
            float spanVisible = (4 - 1) + Fill;
            Assert.AreEqual(availableWidth / spanVisible, spacing, 1e-2f);

            // 10 列ぶんの外形は画面幅を超えてはみ出す（＝ドラッグでパンが必要）。
            float boundingWidth = a.Width + a.CellSize;
            Assert.Greater(boundingWidth, W);

            // 5 行ぶんは画面内に収まる。
            float boundingHeight = a.Height + a.CellSize;
            Assert.Less(boundingHeight, H);

            // はみ出した盤面は左右中央に置かれる（左端は画面外＝負）。
            Assert.Less(a.Left, 0f);
        }

        [Test]
        public void CalculateAreaは表示列数以下の盤面を全体表示する()
        {
            BoardLayoutCalculator.AreaLayout a =
                BoardLayoutCalculator.CalculateArea(W, H, 4, 5, Fill, 4);

            // 盤面全体が画面内に収まる（横にはみ出さない）。
            float boundingWidth = a.Width + a.CellSize;
            float boundingHeight = a.Height + a.CellSize;
            Assert.LessOrEqual(boundingWidth, W);
            Assert.LessOrEqual(boundingHeight, H);
        }
    }
}
