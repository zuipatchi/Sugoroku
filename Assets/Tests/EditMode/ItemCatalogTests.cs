using System;
using Common.MiniGame;
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
        public void お金よこどりの説明は奪う相手が全員であることを含む()
        {
            // 奪う相手は 1 人ではなく自分以外の全員（実装＝BoardPresenter.DecideMoneySteal）。
            // 奪う額（MoneyStealRule の割合）はあえて書かないので、そちらは検証しない。
            StringAssert.Contains("全員", ItemCatalog.Find(ItemId.StealMoney).Description);
        }

        [Test]
        public void ミニゲームアイテムの説明は順位別の賞金を含む()
        {
            string description = ItemCatalog.Find(ItemId.MiniGame).Description;
            for (int rank = 1; rank <= MiniGamePrize.PaidRanks; rank++)
            {
                StringAssert.Contains(MiniGamePrize.ForRank(rank).ToString(), description);
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
        public void ショップに並ぶアイテムには正の購入価格がある()
        {
            // 買えないアイテム（被っちゃやーよのカード専用）は価格を持たないので対象外。
            for (int i = 0; i < ItemCatalog.Purchasable.Count; i++)
            {
                Assert.Greater(ItemCatalog.Purchasable[i].Price, 0,
                    $"{ItemCatalog.Purchasable[i].Id} の Price が 0 以下です");
            }
        }

        [Test]
        public void お金アップはカードに出るがショップには並ばない()
        {
            ItemDefinition moneyUp = ItemCatalog.Find(ItemId.MoneyUp);
            Assert.IsNotNull(moneyUp, "MoneyUp がカタログに見つかりません");
            Assert.IsFalse(moneyUp.Purchasable);
            CollectionAssert.DoesNotContain(ItemCatalog.Purchasable, moneyUp);
            CollectionAssert.Contains(ItemCatalog.RandomCards(null, ItemCatalog.All.Count), moneyUp);
        }

        [Test]
        public void RandomCardsは枚数ぶんを重複なしでカタログから返す()
        {
            Random rng = new(31);
            for (int count = 1; count <= ItemCatalog.All.Count + 2; count++)
            {
                System.Collections.Generic.IReadOnlyList<ItemDefinition> cards =
                    ItemCatalog.RandomCards(rng, count);

                Assert.AreEqual(Math.Min(count, ItemCatalog.All.Count), cards.Count);
                System.Collections.Generic.HashSet<ItemId> seen = new();
                foreach (ItemDefinition def in cards)
                {
                    Assert.IsNotNull(ItemCatalog.Find(def.Id));
                    Assert.IsTrue(seen.Add(def.Id), $"{def.Id} が重複しています");
                }
            }
        }

        [Test]
        public void RandomCardsは同じseedで決定的()
        {
            System.Collections.Generic.IReadOnlyList<ItemDefinition> a = ItemCatalog.RandomCards(new Random(7), 3);
            System.Collections.Generic.IReadOnlyList<ItemDefinition> b = ItemCatalog.RandomCards(new Random(7), 3);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Id, b[i].Id);
            }
        }

        [Test]
        public void 勝利はカードに出る確率が他のアイテムの半分になる()
        {
            // CardWeight 0.5 の効き目を実際の抽選で確かめる（1 枚引きなら重みの比がそのまま出やすさの比）。
            Assert.AreEqual(0.5f, ItemCatalog.Find(ItemId.InstantWin).CardWeight);

            Random rng = new(20260811);
            const int Trials = 4000;
            int instantWin = 0;
            int stealTerritory = 0;
            for (int i = 0; i < Trials; i++)
            {
                ItemId drawn = ItemCatalog.RandomCards(rng, 1)[0].Id;
                if (drawn == ItemId.InstantWin)
                {
                    instantWin++;
                }
                else if (drawn == ItemId.StealTerritory)
                {
                    stealTerritory++;
                }
            }

            // 期待は 1:2。試行のばらつきを見込んで 0.35〜0.65 に収まればよしとする。
            double ratio = (double)instantWin / stealTerritory;
            Assert.That(ratio, Is.InRange(0.35, 0.65),
                $"勝利 {instantWin} 回 / 陣地獲得 {stealTerritory} 回 = {ratio:F2}");
        }

        [Test]
        public void RandomLineupは買えないアイテムを並べない()
        {
            Random rng = new(4242);
            for (int i = 0; i < 30; i++)
            {
                foreach (ItemDefinition def in ItemCatalog.RandomLineup(rng, 2, 4))
                {
                    Assert.IsTrue(def.Purchasable, $"{def.Id} は買えないのにショップへ並んでいます");
                }
            }
        }

        [Test]
        public void RandomLineupは枚数が範囲内でカタログ内の重複なしアイテムを返す()
        {
            Random rng = new(12345);
            // 並ぶ枚数の上限はカタログ全体ではなく「買えるアイテム」の総数でクランプされる。
            int catalog = ItemCatalog.Purchasable.Count;
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
        public void RandomLineupは枚数を買えるアイテムの総数でクランプする()
        {
            System.Collections.Generic.IReadOnlyList<ItemDefinition> lineup =
                ItemCatalog.RandomLineup(new Random(1), 100, 200);
            Assert.AreEqual(ItemCatalog.Purchasable.Count, lineup.Count);
        }
    }
}
