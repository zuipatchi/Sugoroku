using Common.Character;
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
        public void CharacterForSectorはカタログ表示順でキャラを割り当てる()
        {
            // 0 始まりのセクターにカタログ先頭から順に対応する（8 セクターなら先頭 8 体）。
            for (int i = 0; i < Count; i++)
            {
                Assert.AreEqual(CharacterCatalog.All[i].Id, RouletteMath.CharacterForSector(i), $"sector {i}");
            }
        }

        [Test]
        public void CharacterForSectorはカタログ数を超えると巡回する()
        {
            int catalogCount = CharacterCatalog.All.Count;
            // カタログ数ちょうどで先頭へ戻る。
            Assert.AreEqual(CharacterCatalog.All[0].Id, RouletteMath.CharacterForSector(catalogCount));
            // 負のセクターでも範囲内に巡回する（末尾へ回り込む）。
            Assert.AreEqual(CharacterCatalog.All[catalogCount - 1].Id, RouletteMath.CharacterForSector(-1));
        }
    }
}
