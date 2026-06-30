using Main.Roulette;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class RouletteMathTests
    {
        private const int Count = 6;

        [Test]
        public void SectorAngleは360を分割数で割った値()
        {
            Assert.AreEqual(60f, RouletteMath.SectorAngle(Count), 0.0001f);
        }

        [Test]
        public void ResultFromRotationは常に範囲内のセクターを返す()
        {
            float[] rotations = { 0f, 60f, -60f, 359.999f, 360f, -360f, 1234.5f, -1234.5f };
            foreach (float r in rotations)
            {
                int index = RouletteMath.ResultFromRotation(r, Count);
                Assert.GreaterOrEqual(index, 0, $"rotation {r}");
                Assert.Less(index, Count, $"rotation {r}");
            }
        }

        [Test]
        public void セクター境界の角度でも一意にセクターへ割り当てられる()
        {
            // 上部ローカル角 = -rotation。rotation = -(k*60) で境界 k*60 ちょうどになる。
            for (int k = 0; k < Count; k++)
            {
                float rotation = -(k * 60f);
                Assert.AreEqual(k, RouletteMath.ResultFromRotation(rotation, Count), $"boundary k {k}");
            }
        }
    }
}
