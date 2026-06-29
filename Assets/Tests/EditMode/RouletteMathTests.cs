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

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(5)]
        public void StopRotationの逆変換で同じ出目に戻る(int turns)
        {
            for (int i = 0; i < Count; i++)
            {
                float rotation = RouletteMath.StopRotation(i, Count, turns);
                Assert.AreEqual(i, RouletteMath.ResultFromRotation(rotation, Count), $"index {i}, turns {turns}");
            }
        }

        [TestCase(0f)]
        [TestCase(123.4f)]
        [TestCase(-50f)]
        [TestCase(720f)]
        public void NextRotationの逆変換で狙った出目に止まる(float current)
        {
            for (int i = 0; i < Count; i++)
            {
                float rotation = RouletteMath.NextRotation(current, i, Count, 5);
                Assert.AreEqual(i, RouletteMath.ResultFromRotation(rotation, Count), $"current {current}, index {i}");
            }
        }

        [TestCase(0f)]
        [TestCase(123.4f)]
        [TestCase(-50f)]
        public void NextRotationは常に現在角度より進み指定回転数以上回る(float current)
        {
            for (int i = 0; i < Count; i++)
            {
                float rotation = RouletteMath.NextRotation(current, i, Count, 5);
                Assert.Greater(rotation, current);
                Assert.GreaterOrEqual(rotation - current, 5 * 360f);
            }
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
