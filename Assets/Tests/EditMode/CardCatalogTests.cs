using System;
using Main.Card;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class CardCatalogTests
    {
        [Test]
        public void カタログは空でない()
        {
            Assert.Greater(CardCatalog.All.Count, 0);
        }

        [Test]
        public void Findは対応するカード定義を返す()
        {
            CardDefinition def = CardCatalog.Find(CardId.StealTerritory);
            Assert.IsNotNull(def);
            Assert.AreEqual(CardId.StealTerritory, def.Id);
            Assert.IsFalse(string.IsNullOrEmpty(def.ImageAddress));
        }

        [Test]
        public void RandomCardはカタログ内のカードを返す()
        {
            Random rng = new(12345);
            for (int i = 0; i < 20; i++)
            {
                CardDefinition def = CardCatalog.RandomCard(rng);
                Assert.IsNotNull(def);
                Assert.IsNotNull(CardCatalog.Find(def.Id));
            }
        }

        [Test]
        public void RandomCardは同じseedで決定的()
        {
            CardDefinition a = CardCatalog.RandomCard(new Random(7));
            CardDefinition b = CardCatalog.RandomCard(new Random(7));
            Assert.AreEqual(a.Id, b.Id);
        }
    }
}
