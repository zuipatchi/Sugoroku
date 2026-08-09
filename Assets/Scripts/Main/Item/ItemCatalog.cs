using System;
using System.Collections.Generic;

namespace Main.Item
{
    /// <summary>
    /// アイテム 1 種類分のメタデータ。表示名・効果説明・画像（Addressable アドレス）・購入価格を持つ。
    /// </summary>
    public sealed class ItemDefinition
    {
        public ItemDefinition(ItemId id, string displayName, string description, string imageAddress, int price)
        {
            Id = id;
            DisplayName = displayName;
            Description = description ?? string.Empty;
            ImageAddress = imageAddress ?? string.Empty;
            Price = price;
        }

        public ItemId Id { get; }

        /// <summary>手札などに出す表示名。</summary>
        public string DisplayName { get; }

        /// <summary>アイテムモーダルの本文に出す効果の説明文。</summary>
        public string Description { get; }

        /// <summary>アイテム絵の Addressable アドレス。未配置ならプレースホルダ（表示名テキスト）にフォールバックする。</summary>
        public string ImageAddress { get; }

        /// <summary>アイテムショップでの購入価格（所持金から支払う）。種類ごとに固定。</summary>
        public int Price { get; }
    }

    /// <summary>
    /// 取得できるアイテムの一覧（表示順）。UI 非依存の純粋データで、<see cref="CharacterCatalog"/> と同じく静的に持つ。
    /// アイテム絵は各 Addressable アドレスにアセットを割り当てて用意する。
    /// <see cref="ItemDefinition.Price"/> はアイテムショップでの購入価格（初期所持金 1000 を基準にバランス調整する）。
    /// </summary>
    public static class ItemCatalog
    {
        public static readonly IReadOnlyList<ItemDefinition> All = new[]
        {
            new ItemDefinition(ItemId.StealTerritory, "陣地獲得", "好きな陣地マスを 1 つ選んで自分のものにする（相手の陣地も奪える）。", "Image/Item/StealTerritory", 800),
            new ItemDefinition(ItemId.StealMoney, "お金よこどり", "相手の所持金の一部を奪う。", "Image/Item/StealMoney", 500),
            new ItemDefinition(ItemId.MiniGame, "ミニゲーム", "好きなミニゲームを選んで遊び、順位に応じて所持金がもらえる。", "Image/Item/MiniGame", 300),
            new ItemDefinition(ItemId.InstantWin, "勝利", "使った瞬間にゲームに勝利する。", "Image/Item/Victory", 2500),
        };

        /// <summary>識別子 <paramref name="id"/> に対応するアイテム定義。無ければ null。</summary>
        public static ItemDefinition Find(ItemId id)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i].Id == id)
                {
                    return All[i];
                }
            }
            return null;
        }

        /// <summary>
        /// カタログからランダムに 1 つ選ぶ。乱数源 <paramref name="rng"/> は呼び出し側が渡す（テストで seed 固定できる）。
        /// カタログが空なら null。
        /// </summary>
        public static ItemDefinition RandomItem(Random rng)
        {
            if (rng == null || All.Count == 0)
            {
                return All.Count == 0 ? null : All[0];
            }
            return All[rng.Next(All.Count)];
        }

        /// <summary>
        /// アイテムショップに並べるラインナップを抽選する。並ぶ枚数を [<paramref name="minCount"/>, <paramref name="maxCount"/>]
        /// の範囲でランダムに決め（カタログ総数でクランプ）、その枚数ぶんの<b>重複なし</b>アイテムをシャッフルして返す。
        /// 乱数源 <paramref name="rng"/> は呼び出し側が渡す（テストで seed 固定できる）。カタログが空なら空配列。
        /// </summary>
        public static IReadOnlyList<ItemDefinition> RandomLineup(Random rng, int minCount, int maxCount)
        {
            int catalog = All.Count;
            if (catalog == 0)
            {
                return Array.Empty<ItemDefinition>();
            }

            int lo = Math.Max(1, minCount);
            int hi = Math.Min(Math.Max(lo, maxCount), catalog);
            lo = Math.Min(lo, hi);
            int count = rng == null ? hi : rng.Next(lo, hi + 1);

            // カタログの複製を Fisher-Yates でシャッフルし、先頭 count 枚を採る（重複なし）。
            ItemDefinition[] pool = new ItemDefinition[catalog];
            for (int i = 0; i < catalog; i++)
            {
                pool[i] = All[i];
            }
            if (rng != null)
            {
                for (int i = catalog - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (pool[i], pool[j]) = (pool[j], pool[i]);
                }
            }

            ItemDefinition[] result = new ItemDefinition[count];
            Array.Copy(pool, result, count);
            return result;
        }
    }
}
