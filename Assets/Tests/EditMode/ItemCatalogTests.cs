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
    }
}
