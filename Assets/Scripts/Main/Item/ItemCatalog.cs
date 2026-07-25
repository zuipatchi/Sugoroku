using System;
using System.Collections.Generic;

namespace Main.Item
{
    /// <summary>
    /// アイテム 1 種類分のメタデータ。表示名・効果説明・画像（Addressable アドレス）を持つ。
    /// </summary>
    public sealed class ItemDefinition
    {
        public ItemDefinition(ItemId id, string displayName, string description, string imageAddress)
        {
            Id = id;
            DisplayName = displayName;
            Description = description ?? string.Empty;
            ImageAddress = imageAddress ?? string.Empty;
        }

        public ItemId Id { get; }

        /// <summary>手札などに出す表示名。</summary>
        public string DisplayName { get; }

        /// <summary>アイテムモーダルの本文に出す効果の説明文。</summary>
        public string Description { get; }

        /// <summary>アイテム絵の Addressable アドレス。未配置ならプレースホルダ（表示名テキスト）にフォールバックする。</summary>
        public string ImageAddress { get; }
    }

    /// <summary>
    /// 取得できるアイテムの一覧（表示順）。UI 非依存の純粋データで、<see cref="CharacterCatalog"/> と同じく静的に持つ。
    /// アイテム絵は各 Addressable アドレスにアセットを割り当てて用意する。
    /// </summary>
    public static class ItemCatalog
    {
        public static readonly IReadOnlyList<ItemDefinition> All = new[]
        {
            new ItemDefinition(ItemId.StealTerritory, "陣地獲得", "好きな陣地マスを 1 つ選んで自分のものにする（相手の陣地も奪える）。", "Image/Item/StealTerritory"),
            new ItemDefinition(ItemId.StealMoney, "お金よこどり", "相手の所持金の一部を奪う。", "Image/Item/StealMoney"),
            new ItemDefinition(ItemId.MiniGame, "ミニゲーム", "好きなミニゲームを選んで遊び、勝てば所持金がもらえる。", "Image/Item/MiniGame"),
            new ItemDefinition(ItemId.InstantWin, "勝利", "使った瞬間にゲームに勝利する。", "Image/Item/InstantWin"),
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
    }
}
