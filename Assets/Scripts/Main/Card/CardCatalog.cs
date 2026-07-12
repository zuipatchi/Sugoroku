using System;
using System.Collections.Generic;

namespace Main.Card
{
    /// <summary>
    /// カード 1 種類分のメタデータ。表示名と画像（Addressable アドレス）を持つ。
    /// </summary>
    public sealed class CardDefinition
    {
        public CardDefinition(CardId id, string displayName, string imageAddress)
        {
            Id = id;
            DisplayName = displayName;
            ImageAddress = imageAddress ?? string.Empty;
        }

        public CardId Id { get; }

        /// <summary>手札などに出す表示名。</summary>
        public string DisplayName { get; }

        /// <summary>カード絵の Addressable アドレス。未配置ならプレースホルダ（表示名テキスト）にフォールバックする。</summary>
        public string ImageAddress { get; }
    }

    /// <summary>
    /// 取得できるカードの一覧（表示順）。UI 非依存の純粋データで、<see cref="CharacterCatalog"/> と同じく静的に持つ。
    /// カード絵は各 Addressable アドレスにアセットを割り当てて用意する。
    /// </summary>
    public static class CardCatalog
    {
        public static readonly IReadOnlyList<CardDefinition> All = new[]
        {
            new CardDefinition(CardId.StealTerritory, "陣地よこどり", "Image/Card/StealTerritory"),
            new CardDefinition(CardId.StealMoney, "お金よこどり", "Image/Card/StealMoney"),
            new CardDefinition(CardId.MiniGame, "ミニゲーム", "Image/Card/MiniGame"),
        };

        /// <summary>識別子 <paramref name="id"/> に対応するカード定義。無ければ null。</summary>
        public static CardDefinition Find(CardId id)
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
        /// カタログからランダムに 1 枚選ぶ。乱数源 <paramref name="rng"/> は呼び出し側が渡す（テストで seed 固定できる）。
        /// カタログが空なら null。
        /// </summary>
        public static CardDefinition RandomCard(Random rng)
        {
            if (rng == null || All.Count == 0)
            {
                return All.Count == 0 ? null : All[0];
            }
            return All[rng.Next(All.Count)];
        }
    }
}
