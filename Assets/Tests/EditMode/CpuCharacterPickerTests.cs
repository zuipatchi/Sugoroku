using System;
using System.Collections.Generic;
using Main.Board;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class CpuCharacterPickerTests
    {
        [Test]
        public void ShuffledNonHumanIndicesは人間を除いた全indexを重複なく返す()
        {
            const int catalogCount = 8;
            const int humanIndex = 3;
            IReadOnlyList<int> pool = CpuCharacterPicker.ShuffledNonHumanIndices(humanIndex, catalogCount, new Random(12345));

            Assert.AreEqual(catalogCount - 1, pool.Count, "人間ぶんを除いた数");
            Assert.IsFalse(new List<int>(pool).Contains(humanIndex), "人間の index は含まない");

            HashSet<int> unique = new(pool);
            Assert.AreEqual(pool.Count, unique.Count, "重複がない");
        }

        [Test]
        public void ShuffledNonHumanIndicesの結果はCPU全員に別キャラを配れる()
        {
            // 8 人（CPU 7 人）ぶん順に取り出しても互いに被らないこと。
            const int catalogCount = 8;
            const int humanIndex = 0;
            IReadOnlyList<int> pool = CpuCharacterPicker.ShuffledNonHumanIndices(humanIndex, catalogCount, new Random(999));

            HashSet<int> assigned = new();
            for (int cpuOrder = 0; cpuOrder < catalogCount - 1; cpuOrder++)
            {
                int index = pool[cpuOrder % pool.Count];
                Assert.IsTrue(assigned.Add(index), $"CPU {cpuOrder} のキャラが他と被っている");
                Assert.AreNotEqual(humanIndex, index, "人間と被らない");
            }
        }
    }
}
