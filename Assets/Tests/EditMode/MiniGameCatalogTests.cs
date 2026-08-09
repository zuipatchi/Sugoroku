using System;
using System.Collections.Generic;
using Common.MiniGame;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class MiniGameCatalogTests
    {
        [Test]
        public void RandomGameはカタログ内のゲームを返す()
        {
            Random rng = new(12345);
            for (int i = 0; i < 30; i++)
            {
                MiniGameId id = MiniGameCatalog.RandomGame(rng);
                Assert.AreEqual(id, MiniGameCatalog.Find(id).Id);
            }
        }

        [Test]
        public void RandomGameは同じseedで決定的()
        {
            // ミニゲームマスの抽選は着地した人が決めて配るので同期自体は seed に依存しないが、
            // テストで固定できることが MoneyCellRule / MoveCellRule と揃った規約になっている。
            Assert.AreEqual(MiniGameCatalog.RandomGame(new Random(7)), MiniGameCatalog.RandomGame(new Random(7)));
        }

        [Test]
        public void RandomGameは乱数源がnullなら先頭を返す()
        {
            Assert.AreEqual(MiniGameCatalog.All[0].Id, MiniGameCatalog.RandomGame(null));
        }

        [Test]
        public void 十分な試行で全てのゲームが出る()
        {
            // 抽選の範囲が狭い実装ミス（rng.Next の上限を間違える等）を検出する。
            Random rng = new(42);
            HashSet<MiniGameId> seen = new();
            for (int i = 0; i < 500; i++)
            {
                seen.Add(MiniGameCatalog.RandomGame(rng));
            }
            Assert.AreEqual(MiniGameCatalog.All.Count, seen.Count, "抽選されないミニゲームがあります。");
        }

        [Test]
        public void 全ミニゲームに表示名とUXMLアドレスがある()
        {
            foreach (MiniGameDefinition definition in MiniGameCatalog.All)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(definition.DisplayName), $"{definition.Id} の表示名が空です。");
                Assert.IsFalse(string.IsNullOrWhiteSpace(definition.UxmlAddress), $"{definition.Id} の UXML アドレスが空です。");
            }
        }
    }
}
