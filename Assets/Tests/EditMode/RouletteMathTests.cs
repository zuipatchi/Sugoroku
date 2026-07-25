using Main.Roulette;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class RouletteMathTests
    {
        private const int Count = 8;
        private const float SectorDeg = 360f / Count;

        [Test]
        public void SectorAngleは360を分割数で割った値()
        {
            Assert.AreEqual(SectorDeg, RouletteMath.SectorAngle(Count), 0.0001f);
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
            // 上部ローカル角 = -rotation。rotation = -(k*sectorDeg) で境界 k*sectorDeg ちょうどになる。
            for (int k = 0; k < Count; k++)
            {
                float rotation = -(k * SectorDeg);
                Assert.AreEqual(k, RouletteMath.ResultFromRotation(rotation, Count), $"boundary k {k}");
            }
        }

        [Test]
        public void SectorCountは参加者数掛けるKで最低1に丸める()
        {
            Assert.AreEqual(8, RouletteMath.SectorCount(2, 4));
            Assert.AreEqual(9, RouletteMath.SectorCount(3, 3));
            Assert.AreEqual(6, RouletteMath.SectorCount(2, 3));
            // 参加者数・K が 0 以下でも最低 1 に丸めて 1 以上を返す。
            Assert.AreEqual(1, RouletteMath.SectorCount(0, 0));
            Assert.AreEqual(3, RouletteMath.SectorCount(1, 3));
        }

        [Test]
        public void ParticipantForSectorは参加者を巡回で均等に割り振る()
        {
            // 2 人・K=4（8 セクター）：交互に並び、各参加者 4 枚ずつ。
            int[] expected2 = { 0, 1, 0, 1, 0, 1, 0, 1 };
            int p0 = 0, p1 = 0;
            for (int i = 0; i < expected2.Length; i++)
            {
                Assert.AreEqual(expected2[i], RouletteMath.ParticipantForSector(i, 2), $"sector {i}");
                if (RouletteMath.ParticipantForSector(i, 2) == 0) { p0++; } else { p1++; }
            }
            Assert.AreEqual(4, p0);
            Assert.AreEqual(4, p1);
        }

        [Test]
        public void ParticipantForSectorは3人でもできる限り均等に割り振る()
        {
            // 3 人・K=3（9 セクター）：各参加者 3 枚ずつで完全に均等。
            int[] counts = new int[3];
            for (int i = 0; i < 9; i++)
            {
                counts[RouletteMath.ParticipantForSector(i, 3)]++;
            }
            Assert.AreEqual(new[] { 3, 3, 3 }, counts);
        }

        [Test]
        public void StepsForSectorは各参加者に同じ数字セットを1枚ずつ与える()
        {
            // 2 人・K=4（8 セクター）：各参加者が 1,2,3,4 を 1 枚ずつ持つ。
            // セクター i → 参加者 i%2・数字 (i/2)+1。
            Assert.AreEqual(1, RouletteMath.StepsForSector(0, 2)); // p0
            Assert.AreEqual(1, RouletteMath.StepsForSector(1, 2)); // p1
            Assert.AreEqual(2, RouletteMath.StepsForSector(2, 2)); // p0
            Assert.AreEqual(2, RouletteMath.StepsForSector(3, 2)); // p1
            Assert.AreEqual(4, RouletteMath.StepsForSector(6, 2)); // p0
            Assert.AreEqual(4, RouletteMath.StepsForSector(7, 2)); // p1

            // 参加者ごとに集めた数字セットが完全に一致する（数字も全キャラ同じ）。
            System.Collections.Generic.List<int> p0Numbers = new();
            System.Collections.Generic.List<int> p1Numbers = new();
            for (int i = 0; i < 8; i++)
            {
                if (RouletteMath.ParticipantForSector(i, 2) == 0)
                {
                    p0Numbers.Add(RouletteMath.StepsForSector(i, 2));
                }
                else
                {
                    p1Numbers.Add(RouletteMath.StepsForSector(i, 2));
                }
            }
            Assert.AreEqual(new[] { 1, 2, 3, 4 }, p0Numbers);
            Assert.AreEqual(new[] { 1, 2, 3, 4 }, p1Numbers);
        }
    }
}
