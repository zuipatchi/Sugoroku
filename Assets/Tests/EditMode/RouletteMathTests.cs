using System;
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

        [Test]
        public void セクターと進む人と出目の対応は1対1()
        {
            // 停止セクターの整数 1 つだけをオンラインで配れるのは、この対応が 1 対 1 だから。
            // 重複すると、受信側が「誰が何マス進むか」を一意に復元できなくなる。
            const int numbersPerParticipant = 3;
            for (int participants = 2; participants <= 4; participants++)
            {
                int sectorCount = RouletteMath.SectorCount(participants, numbersPerParticipant);
                bool[,] seen = new bool[participants, numbersPerParticipant];
                for (int sector = 0; sector < sectorCount; sector++)
                {
                    int player = RouletteMath.ParticipantForSector(sector, participants);
                    int steps = RouletteMath.StepsForSector(sector, participants);
                    Assert.IsFalse(seen[player, steps - 1], $"participants {participants} / sector {sector}");
                    seen[player, steps - 1] = true;
                }
            }
        }

        [Test]
        public void RotationForSectorCenterの角度はそのセクターと判定される()
        {
            for (int i = 0; i < Count; i++)
            {
                float rotation = RouletteMath.RotationForSectorCenter(i, Count);
                Assert.AreEqual(i, RouletteMath.ResultFromRotation(rotation, Count), $"sector {i}");
            }
        }

        [Test]
        public void NextRotationForは現在角より前方で目的のセクターに止まる()
        {
            float[] currents = { 0f, 17f, 359f, 1234.5f, -720f };
            foreach (float current in currents)
            {
                for (int i = 0; i < Count; i++)
                {
                    float target = RouletteMath.NextRotationFor(current, i, Count, 2);
                    Assert.GreaterOrEqual(target, current, $"current {current} / sector {i}");
                    Assert.AreEqual(
                        i, RouletteMath.ResultFromRotation(target, Count), $"current {current} / sector {i}");
                }
            }
        }

        [Test]
        public void NextRotationForは余分な周回ぶんだけ長く回す()
        {
            float noTurn = RouletteMath.NextRotationFor(0f, 3, Count, 0);
            float threeTurns = RouletteMath.NextRotationFor(0f, 3, Count, 3);
            Assert.AreEqual(noTurn + 360f * 3f, threeTurns, 0.001f);
        }

        [Test]
        public void NearestRotationForSectorCenterは半セクター以内で中心へ寄せる()
        {
            // 停止予測角を「その角度が属するセクター」の中心へ寄せる使い方（＝離した瞬間に出目を確定させる）。
            float[] rotations = { 0f, 17f, 359f, 1234.5f, -720f, -37.25f };
            foreach (float rotation in rotations)
            {
                int sector = RouletteMath.ResultFromRotation(rotation, Count);
                float snapped = RouletteMath.NearestRotationForSectorCenter(rotation, sector, Count);

                Assert.AreEqual(
                    sector,
                    RouletteMath.ResultFromRotation(snapped, Count),
                    $"rotation {rotation}");
                // 寄せ幅は半セクター以内（＝予測した止まり方をほとんど変えない）。
                Assert.LessOrEqual(
                    Math.Abs(snapped - rotation), SectorDeg * 0.5f + 0.001f, $"rotation {rotation}");
            }
        }
    }
}
