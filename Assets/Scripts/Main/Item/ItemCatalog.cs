using System;
using System.Collections.Generic;
using Common.MiniGame;

namespace Main.Item
{
    /// <summary>
    /// アイテム 1 種類分のメタデータ。表示名・効果説明・画像（Addressable アドレス）・購入価格を持つ。
    /// </summary>
    public sealed class ItemDefinition
    {
        public ItemDefinition(
            ItemId id, string displayName, string description, string imageAddress, int price,
            bool purchasable = true, float cardWeight = 1f)
        {
            Id = id;
            DisplayName = displayName;
            Description = description ?? string.Empty;
            ImageAddress = imageAddress ?? string.Empty;
            Price = price;
            Purchasable = purchasable;
            CardWeight = cardWeight;
        }

        public ItemId Id { get; }

        /// <summary>手札などに出す表示名。</summary>
        public string DisplayName { get; }

        /// <summary>アイテムモーダルの本文に出す効果の説明文。</summary>
        public string Description { get; }

        /// <summary>アイテム絵の Addressable アドレス。未配置ならプレースホルダ（表示名テキスト）にフォールバックする。</summary>
        public string ImageAddress { get; }

        /// <summary>アイテムショップでの購入価格（所持金から支払う）。種類ごとに固定。買えないものは 0。</summary>
        public int Price { get; }

        /// <summary>
        /// アイテムショップに並べるか（<see cref="ItemCatalog.Purchasable"/>）。false のものは手札に入ることが
        /// 無いので効果も価格も持たない（＝ミニゲーム「被っちゃやーよ」で選ぶカード専用）。
        /// </summary>
        public bool Purchasable { get; }

        /// <summary>
        /// ミニゲーム「被っちゃやーよ」の選択カードに出やすさ（<see cref="ItemCatalog.RandomCards"/> の抽選の重み）。
        /// 既定は 1 で、小さいほど並びにくい（<see cref="ItemId.InstantWin"/>＝勝利は 0.5＝他の半分）。
        /// <see cref="Purchasable"/> とは独立で、ショップのラインナップ抽選には影響しない。
        /// </summary>
        public float CardWeight { get; }
    }

    /// <summary>
    /// 取得できるアイテムの一覧（表示順）。UI 非依存の純粋データで、<see cref="CharacterCatalog"/> と同じく静的に持つ。
    /// アイテム絵は各 Addressable アドレスにアセットを割り当てて用意する。
    /// <see cref="ItemDefinition.Price"/> はアイテムショップでの購入価格（初期所持金 1000 を基準にバランス調整する）。
    /// **どこに出すかは種類ごとに違う**：買い物の抽選は <see cref="Purchasable"/> から引き（ショップに並ばない
    /// アイテムがあるため）、ミニゲーム「被っちゃやーよ」の選択カードは <see cref="RandomCards"/> が
    /// <see cref="All"/> から <see cref="ItemDefinition.CardWeight"/> の重み付きで引く。
    ///
    /// <see cref="ItemDefinition.Description"/> は**効果の実装と読み比べて食い違わない書き方**にする。
    /// 数値まで書くものは、その数値をルール側の定数（<see cref="MiniGamePrize"/> など）から組み立てるので、
    /// ルールを変えれば説明文も一緒に変わる（マスの説明＝<c>BoardEventDescription</c> と同じ方針）。
    /// 逆に**あえて数値を書かない**ものもある（お金よこどりの奪う額＝使ってからのお楽しみ）。
    /// </summary>
    public static class ItemCatalog
    {
        public static readonly IReadOnlyList<ItemDefinition> All = new[]
        {
            new ItemDefinition(ItemId.StealTerritory, "陣地獲得", "自分のもの以外の陣地マスを 1 つ選んで占拠する（相手の陣地も奪える）。", "Image/Item/StealTerritory", 800),
            // 奪う相手は 1 人ではなく自分以外の全員。割合（MoneyStealRule）はあえて書かず「いくらか」に
            // ぼかしてある＝いくら奪えるかは使ってからのお楽しみ。
            new ItemDefinition(ItemId.StealMoney, "お金よこどり", "全員からいくらかのお金を適当に奪う。", "Image/Item/StealMoney", 200),
            new ItemDefinition(ItemId.MiniGame, "ミニゲーム", MiniGameDescription(), "Image/Item/MiniGame", 300),
            // 「被っちゃやーよ」のカードには他の半分の重みでしか並ばない（当たりが強いので出やすさを抑える）。
            new ItemDefinition(ItemId.InstantWin, "勝利", "使った瞬間にゲームに勝利する。", "Image/Item/Victory", 2500, cardWeight: 0.5f),
            // ショップには並ばない「被っちゃやーよ」のカード専用（下の Purchasable 参照）。
            // 絵はお金アップのマスと同じものを使い回す（BoardEventArtCatalog の "Board/MoneyUp"）。
            // アイテム絵を出すところはどこも正方形＋切り抜きなので、正方形のマスの絵をそのまま置ける。
            new ItemDefinition(ItemId.MoneyUp, "お金アップ", "所持金がランダムに増える。", "Board/MoneyUp", 0, purchasable: false),
        };

        /// <summary>
        /// アイテムショップに並べるアイテム（<see cref="ItemDefinition.Purchasable"/> が true のもの）。
        /// **カタログにはショップに並ばないアイテムも入っている**ので、
        /// 買い物の抽選（<see cref="RandomLineup"/>）は <see cref="All"/> ではなくこちらから引く。
        /// </summary>
        public static readonly IReadOnlyList<ItemDefinition> Purchasable = Filter(item => item.Purchasable);

        // All から条件に合うものだけを抜き出した配列（用途別の一覧を作る）。
        private static ItemDefinition[] Filter(Func<ItemDefinition, bool> predicate)
        {
            List<ItemDefinition> filtered = new(All.Count);
            for (int i = 0; i < All.Count; i++)
            {
                if (predicate(All[i]))
                {
                    filtered.Add(All[i]);
                }
            }
            return filtered.ToArray();
        }

        /// <summary>
        /// ミニゲーム「被っちゃやーよ」の選択カードに並べるアイテムを <paramref name="count"/> 枚
        /// （カタログ総数でクランプ）、<b>重複なし</b>で抽選する。カードはショップと違って
        /// カタログ全体（<see cref="All"/>＝買えない「お金アップ」も含む）から配る。
        ///
        /// **出やすさは種類ごとに違う**（<see cref="ItemDefinition.CardWeight"/>）ので、均等シャッフルではなく
        /// 重み付きで 1 枚ずつ引いては候補から外す。1 枚だけ引くときは重みの比がそのまま出やすさの比になり、
        /// 複数枚のときも重みが小さいほど並びにくい（勝利は 0.5＝他の半分）。
        /// 乱数源 <paramref name="rng"/> は呼び出し側が渡す（テストで seed 固定できる）。
        /// null なら先頭から順に採って決定的（<see cref="RandomLineup"/> と同じ規約）。
        /// </summary>
        public static IReadOnlyList<ItemDefinition> RandomCards(Random rng, int count)
        {
            int take = Math.Clamp(count, 0, All.Count);
            List<ItemDefinition> pool = new(All);
            List<ItemDefinition> picked = new(take);
            for (int i = 0; i < take; i++)
            {
                picked.Add(TakeWeighted(pool, rng));
            }
            return picked;
        }

        // 候補 pool から重み（CardWeight）に比例した確率で 1 つ選び、pool から取り除いて返す。
        private static ItemDefinition TakeWeighted(List<ItemDefinition> pool, Random rng)
        {
            double total = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                total += Math.Max(0, pool[i].CardWeight);
            }

            // 重みがすべて 0（または乱数源なし）なら先頭を採る＝決定的。
            double roll = rng == null || total <= 0 ? 0 : rng.NextDouble() * total;
            for (int i = 0; i < pool.Count; i++)
            {
                roll -= Math.Max(0, pool[i].CardWeight);
                // 最後の 1 つは丸め誤差で溢れても必ず採る。
                if (roll < 0 || i == pool.Count - 1)
                {
                    ItemDefinition picked = pool[i];
                    pool.RemoveAt(i);
                    return picked;
                }
            }
            return null; // pool が空のときだけ（呼び出し側が take でクランプ済み）
        }

        // ミニゲームアイテムの説明。順位と賞金の並びはミニゲームマスの説明と共通（MiniGamePrize が持つ）。
        private static string MiniGameDescription()
        {
            return $"好きなミニゲームで遊び、順位に応じて所持金がもらえる（{MiniGamePrize.RankPrizeText()}）。";
        }

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
        /// **買えないアイテム（<see cref="ItemDefinition.Purchasable"/> が false）も返す**ので、
        /// 手札に入れる用途には使わない（ショップは <see cref="RandomLineup"/>、カードは <see cref="RandomCards"/>）。
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
        /// の範囲でランダムに決め（<see cref="Purchasable"/> の総数でクランプ）、その枚数ぶんの<b>重複なし</b>
        /// アイテムをシャッフルして返す。買えないアイテム（被っちゃやーよのカード専用）は並ばない。
        /// 乱数源 <paramref name="rng"/> は呼び出し側が渡す（テストで seed 固定できる）。カタログが空なら空配列。
        /// </summary>
        public static IReadOnlyList<ItemDefinition> RandomLineup(Random rng, int minCount, int maxCount)
        {
            int catalog = Purchasable.Count;
            if (catalog == 0)
            {
                return Array.Empty<ItemDefinition>();
            }

            int lo = Math.Max(1, minCount);
            int hi = Math.Min(Math.Max(lo, maxCount), catalog);
            lo = Math.Min(lo, hi);
            int count = rng == null ? hi : rng.Next(lo, hi + 1);

            // 買えるアイテムの複製を Fisher-Yates でシャッフルし、先頭 count 枚を採る（重複なし）。
            ItemDefinition[] pool = new ItemDefinition[catalog];
            for (int i = 0; i < catalog; i++)
            {
                pool[i] = Purchasable[i];
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
