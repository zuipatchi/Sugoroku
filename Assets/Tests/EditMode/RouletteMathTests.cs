using System;
using System.Collections.Generic;
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
        public void GenerateSectorNumbersは各参加者に重複なしのK個を配る()
        {
            // 同じキャラが同じ数字を 2 枚持つことはない（別のキャラと同じ数字になるのは可）。
            const int min = 1;
            const int max = 6;
            const int perParticipant = 3;
            for (int participants = 2; participants <= 4; participants++)
            {
                for (int seed = 0; seed < 50; seed++)
                {
                    int[] steps = RouletteMath.GenerateSectorNumbers(
                        participants, perParticipant, min, max, new Random(seed));
                    Assert.AreEqual(participants * perParticipant, steps.Length, $"人数 {participants}");

                    for (int participant = 0; participant < participants; participant++)
                    {
                        List<int> numbers = NumbersOf(steps, participants, participant);
                        Assert.AreEqual(perParticipant, numbers.Count);
                        CollectionAssert.AllItemsAreUnique(
                            numbers, $"人数 {participants} / seed {seed} / 参加者 {participant}");
                        foreach (int number in numbers)
                        {
                            Assert.GreaterOrEqual(number, min);
                            Assert.LessOrEqual(number, max);
                        }
                    }
                }
            }
        }

        [Test]
        public void GenerateSectorNumbersは参加者をまたぐ重複は許す()
        {
            // 数字の種類（3）が全体の枚数（2 人 × 3 枚）に足りなくても配れる＝キャラをまたぐ重複は許される。
            int[] steps = RouletteMath.GenerateSectorNumbers(2, 3, 1, 3, new Random(1));
            Assert.AreEqual(6, steps.Length);
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, NumbersOf(steps, 2, 0));
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, NumbersOf(steps, 2, 1));
        }

        [Test]
        public void GenerateSectorNumbersはKを数字の種類数までにクランプする()
        {
            // 1〜6 なら 1 人 6 枚が上限（重複なしで配れる枚数を超えられない）。
            int[] steps = RouletteMath.GenerateSectorNumbers(2, 10, 1, 6, new Random(0));
            Assert.AreEqual(12, steps.Length);
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3, 4, 5, 6 }, NumbersOf(steps, 2, 0));
        }

        [Test]
        public void GenerateSectorNumbersは同じ種なら同じ表になる()
        {
            // オンラインは抽選結果を配らず、全員が同じ種で引いて同じ表を組む（RouletteNumberLayout）。
            int[] a = RouletteMath.GenerateSectorNumbers(3, 3, 1, 6, new Random(12345));
            int[] b = RouletteMath.GenerateSectorNumbers(3, 3, 1, 6, new Random(12345));
            Assert.AreEqual(a, b);
        }

        [Test]
        public void GenerateSectorNumbersはrngがnullなら最小値から順に配る()
        {
            // 決定的フォールバック（MoneyCellRule と同じ規約）。全参加者が 1,2,3 を 1 枚ずつ持つ。
            int[] steps = RouletteMath.GenerateSectorNumbers(2, 3, 1, 6, null);
            Assert.AreEqual(new[] { 1, 1, 2, 2, 3, 3 }, steps);
        }

        [Test]
        public void セクターと進む人と出目の対応は1対1()
        {
            // 停止セクターの整数 1 つだけをオンラインで配れるのは、この対応が 1 対 1 だから。
            // 重複すると、受信側が「誰が何マス進むか」を一意に復元できなくなる。
            const int numbersPerParticipant = 3;
            for (int participants = 2; participants <= 4; participants++)
            {
                int[] steps = RouletteMath.GenerateSectorNumbers(
                    participants, numbersPerParticipant, 1, 6, new Random(participants));
                HashSet<(int, int)> seen = new();
                for (int sector = 0; sector < steps.Length; sector++)
                {
                    int player = RouletteMath.ParticipantForSector(sector, participants);
                    Assert.IsTrue(
                        seen.Add((player, steps[sector])), $"participants {participants} / sector {sector}");
                }
            }
        }

        [Test]
        public void StableHashは同じ文字列なら同じ非負の値を返す()
        {
            // 実行やクライアントをまたいで同じ値になることが前提（string.GetHashCode は使えない）。
            Assert.AreEqual(
                RouletteNumberLayout.StableHash("session-abc"), RouletteNumberLayout.StableHash("session-abc"));
            Assert.AreNotEqual(
                RouletteNumberLayout.StableHash("session-abc"), RouletteNumberLayout.StableHash("session-abd"));
            string[] ids = { string.Empty, "a", "session-abc", "01234567-89ab-cdef-0123-456789abcdef" };
            foreach (string id in ids)
            {
                Assert.GreaterOrEqual(RouletteNumberLayout.StableHash(id), 0, id);
            }
        }

        [Test]
        public void MixSeedはスピン回数ごとに違う種を返す()
        {
            // スピンのたびに引き直す種。同じ (基準種, 回数) なら全クライアントで同じ値になる必要があり、
            // 回数が 1 違うだけで別の並びになってほしいので、隣り合う回数でも種が衝突しないことを見る。
            Assert.AreEqual(RouletteNumberLayout.MixSeed(1234, 5), RouletteNumberLayout.MixSeed(1234, 5));
            Assert.AreNotEqual(RouletteNumberLayout.MixSeed(1234, 5), RouletteNumberLayout.MixSeed(1234, 6));
            Assert.AreNotEqual(RouletteNumberLayout.MixSeed(1234, 5), RouletteNumberLayout.MixSeed(1235, 5));

            HashSet<int> seeds = new();
            for (int spin = 0; spin < 200; spin++)
            {
                int seed = RouletteNumberLayout.MixSeed(-42, spin);
                Assert.GreaterOrEqual(seed, 0, $"spin {spin}");
                Assert.IsTrue(seeds.Add(seed), $"spin {spin}");
            }
        }

        // 参加者 participant のセクターに並んだ数字（セクター j × 人数 + participant）。
        private static List<int> NumbersOf(int[] steps, int participants, int participant)
        {
            List<int> numbers = new();
            for (int sector = participant; sector < steps.Length; sector += participants)
            {
                numbers.Add(steps[sector]);
            }
            return numbers;
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
