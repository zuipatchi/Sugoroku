using System;
using Main.Item;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class ItemCatalogTests
    {
        [Test]
        public void カタログは空でない()
        {
            Assert.Greater(ItemCatalog.All.Count, 0);
        }

        [Test]
        public void 全アイテムに効果説明文がある()
        {
            for (int i = 0; i < ItemCatalog.All.Count; i++)
            {
                Assert.IsFalse(string.IsNullOrEmpty(ItemCatalog.All[i].Description),
                    $"{ItemCatalog.All[i].Id} の Description が空です");
            }
        }

        [Test]
        public void Findは対応するアイテム定義を返す()
        {
            ItemDefinition def = ItemCatalog.Find(ItemId.StealTerritory);
            Assert.IsNotNull(def);
            Assert.AreEqual(ItemId.StealTerritory, def.Id);
            Assert.IsFalse(string.IsNullOrEmpty(def.ImageAddress));
        }

        [Test]
        public void 勝利アイテムがカタログに含まれメタデータを持つ()
        {
            ItemDefinition def = ItemCatalog.Find(ItemId.InstantWin);
            Assert.IsNotNull(def, "InstantWin がカタログに見つかりません");
            Assert.AreEqual(ItemId.InstantWin, def.Id);
            Assert.IsFalse(string.IsNullOrEmpty(def.DisplayName));
            Assert.IsFalse(string.IsNullOrEmpty(def.Description));
            Assert.IsFalse(string.IsNullOrEmpty(def.ImageAddress));
        }

        [Test]
        public void RandomItemはカタログ内のアイテムを返す()
        {
            Random rng = new(12345);
            for (int i = 0; i < 20; i++)
            {
                ItemDefinition def = ItemCatalog.RandomItem(rng);
                Assert.IsNotNull(def);
                Assert.IsNotNull(ItemCatalog.Find(def.Id));
            }
        }

        [Test]
        public void RandomItemは同じseedで決定的()
        {
            ItemDefinition a = ItemCatalog.RandomItem(new Random(7));
            ItemDefinition b = ItemCatalog.RandomItem(new Random(7));
            Assert.AreEqual(a.Id, b.Id);
        }

        [Test]
        public void 全アイテムに正の購入価格がある()
        {
            for (int i = 0; i < ItemCatalog.All.Count; i++)
            {
                Assert.Greater(ItemCatalog.All[i].Price, 0,
                    $"{ItemCatalog.All[i].Id} の Price が 0 以下です");
            }
        }

        [Test]
        public void RandomLineupは枚数が範囲内でカタログ内の重複なしアイテムを返す()
        {
            Random rng = new(12345);
            int catalog = ItemCatalog.All.Count;
            for (int i = 0; i < 30; i++)
            {
                System.Collections.Generic.IReadOnlyList<ItemDefinition> lineup =
                    ItemCatalog.RandomLineup(rng, 2, 4);

                int expectedMax = Math.Min(4, catalog);
                Assert.GreaterOrEqual(lineup.Count, Math.Min(2, catalog));
                Assert.LessOrEqual(lineup.Count, expectedMax);

                System.Collections.Generic.HashSet<ItemId> seen = new();
                foreach (ItemDefinition def in lineup)
                {
                    Assert.IsNotNull(ItemCatalog.Find(def.Id));
                    Assert.IsTrue(seen.Add(def.Id), $"{def.Id} が重複しています");
                }
            }
        }

        [Test]
        public void RandomLineupは同じseedで決定的()
        {
            System.Collections.Generic.IReadOnlyList<ItemDefinition> a = ItemCatalog.RandomLineup(new Random(7), 2, 4);
            System.Collections.Generic.IReadOnlyList<ItemDefinition> b = ItemCatalog.RandomLineup(new Random(7), 2, 4);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Id, b[i].Id);
            }
        }

        [Test]
        public void RandomLineupは枚数をカタログ総数でクランプする()
        {
            System.Collections.Generic.IReadOnlyList<ItemDefinition> lineup =
                ItemCatalog.RandomLineup(new Random(1), 100, 200);
            Assert.AreEqual(ItemCatalog.All.Count, lineup.Count);
        }
    }
}
