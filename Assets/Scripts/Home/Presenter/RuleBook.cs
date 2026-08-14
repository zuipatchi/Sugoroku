using System.Collections.Generic;
using Common.GameSession;
using Common.MiniGame;
using Main.Board;
using Main.Item;
using Main.Money;
using Main.Roulette;
using UnityEngine;

namespace Home.Presenter
{
    /// <summary>
    /// ルール説明モーダルの 1 項目（マスの種類・アイテム・ミニゲームのような箇条書き）。
    /// </summary>
    public sealed class RuleEntry
    {
        public RuleEntry(string name, string description, Color? accent = null, string note = null)
        {
            Name = name;
            Description = description ?? string.Empty;
            Accent = accent;
            Note = note;
        }

        /// <summary>項目名（マス名・アイテム名など）。</summary>
        public string Name { get; }

        /// <summary>効果の説明。</summary>
        public string Description { get; }

        /// <summary>行頭に置く色ドット。null なら出さない（マスの種類だけが盤面と同じ色を持つ）。</summary>
        public Color? Accent { get; }

        /// <summary>名前の右に添える補足（アイテムの価格など）。null なら出さない。</summary>
        public string Note { get; }
    }

    /// <summary>
    /// ルール説明モーダルの 1 節。<see cref="Body"/> は文章、<see cref="Entries"/> は箇条書き（どちらも任意）。
    /// </summary>
    public sealed class RuleSection
    {
        public RuleSection(string title, string body = null, IReadOnlyList<RuleEntry> entries = null)
        {
            Title = title;
            Body = body;
            Entries = entries;
        }

        public string Title { get; }

        public string Body { get; }

        public IReadOnlyList<RuleEntry> Entries { get; }
    }

    /// <summary>
    /// Home のルール説明モーダルに出す内容を組み立てる純粋ロジック。
    /// **文言と数値はゲーム側の単一の情報源から引く**（マス＝<see cref="BoardEventLabel"/> /
    /// <see cref="BoardEventDescription"/> / <see cref="BoardEventColors"/>・並び順＝<see cref="BoardEventTally.DisplayOrder"/>、
    /// アイテム＝<see cref="ItemCatalog"/>、ミニゲーム＝<see cref="MiniGameCatalog"/> / <see cref="MiniGamePrize"/>、
    /// 所持金＝<see cref="MoneyModel"/>、人数＝<see cref="PlayerCountSessionModel"/>）ので、
    /// ルールを変えればルール説明も一緒に変わる。ここに直接書くのは、
    /// どのクラスも単独では持っていない「遊びの流れ」だけにとどめる。
    /// </summary>
    public static class RuleBook
    {
        /// <summary>ルール説明の全節（表示順）。</summary>
        public static IReadOnlyList<RuleSection> Sections()
        {
            return new RuleSection[]
            {
                new RuleSection("勝かた", WinConditionBody()),
                new RuleSection("ゲームの流れ", FlowBody()),
                new RuleSection("マスの種類", null, CellEntries()),
                new RuleSection("アイテム", ItemBody(), ItemEntries()),
                new RuleSection("ミニゲーム", MiniGameBody(), MiniGameEntries()),
                new RuleSection("画面の見かた", ScreenBody()),
            };
        }

        // 勝利条件。必要な占拠数（TerritoryModel.RequiredToWin）は盤面ごとに変わるので数は書かない。
        private static string WinConditionBody()
        {
            return "陣地マスに止まると、そのマスは自分の陣地になる。相手の陣地でも、止まれば上書きして奪える。一定数陣地を獲得すると勝利。";
        }

        // 手番とルーレット。円盤の数字の枚数は RoulettePresenter の設定なので枚数は書かず、
        // 数字の範囲だけ抽選側の情報源（RouletteNumberLayout）から引く。
        private static string FlowBody()
        {
            return $"プレイ人数は {PlayerCountSessionModel.Min}〜{PlayerCountSessionModel.Max} 人。\n"
                + "手番が回ってきたらルーレットを長押しして円盤を回し、指を離すと減速して止まる。\n"
                + "円盤には参加者全員のコインが同じ枚数ずつ並んでいて、止まったセクターのキャラが"
                + "そこに書かれた数だけ進む。回した人が進むとは限らない。\n"
                + $"コインの数字は {RouletteNumberLayout.MinNumber}〜{RouletteNumberLayout.MaxNumber} から"
                + "回すたびに抽選され、同じキャラに同じ数字は並ばない（別のキャラと同じ数字になることはある）。";
        }

        // マスの種類。並び順・名前・説明・色はすべて盤面側の情報源から引く
        // （スタートと通常マスは BoardEventTally の内訳には出ないので、ここで前後に足す）。
        private static IReadOnlyList<RuleEntry> CellEntries()
        {
            List<RuleEntry> entries = new()
            {
                // スタートの地色は黒でカードの地に沈むので、ドットには縁取り色（金）を使う。
                new RuleEntry(
                    BoardEventDescription.StartLabel,
                    BoardEventDescription.StartDescription,
                    BoardEventColors.StartOutline),
            };

            foreach (BoardCellEvent cellEvent in BoardEventTally.DisplayOrder)
            {
                entries.Add(new RuleEntry(
                    BoardEventLabel.Of(cellEvent),
                    BoardEventDescription.Of(cellEvent),
                    BoardEventColors.Of(cellEvent)));
            }

            entries.Add(new RuleEntry(
                BoardEventLabel.Of(BoardCellEvent.None),
                BoardEventDescription.Of(BoardCellEvent.None),
                BoardEventColors.Of(BoardCellEvent.None)));
            return entries;
        }

        // アイテムの入手と使いどころ。初期所持金は MoneyModel の定数から組み立てる。
        private static string ItemBody()
        {
            return $"所持金は {MoneyModel.InitialMoney} から始まる\n"
                + "アイテムマスに止まるとショップが開き、並んだ品から所持金で買える（買わずに閉じてもよい）。\n"
                + "買ったアイテムは画面右下の手札に入り、自分の手番でルーレットを回す前ならいつでも使える";
        }

        // 買えるアイテムだけを価格付きで並べる（ショップに並ばないものは手札に入らないので出さない）。
        // 価格の書き方はアイテムショップの商品カード（ItemShopPresenter）と揃える。
        private static IReadOnlyList<RuleEntry> ItemEntries()
        {
            List<RuleEntry> entries = new(ItemCatalog.Purchasable.Count);
            foreach (ItemDefinition item in ItemCatalog.Purchasable)
            {
                entries.Add(new RuleEntry(item.DisplayName, item.Description, note: $"{item.Price} G"));
            }
            return entries;
        }

        // ミニゲームの入り口と賞金。賞金の並びは MiniGamePrize が持つ（マスの説明と共通の文言）。
        private static string MiniGameBody()
        {
            ItemDefinition item = ItemCatalog.Find(ItemId.MiniGame);
            string itemName = item == null ? "ミニゲーム" : item.DisplayName;
            return $"ミニゲームマスに止まるか、アイテム「{itemName}」を使うと遊べる。\n"
                + $"順位に応じて所持金がもらえる（{MiniGamePrize.RankPrizeText()}）。";
        }

        private static IReadOnlyList<RuleEntry> MiniGameEntries()
        {
            List<RuleEntry> entries = new(MiniGameCatalog.All.Count);
            foreach (MiniGameDefinition game in MiniGameCatalog.All)
            {
                entries.Add(new RuleEntry(game.DisplayName, game.Description));
            }
            return entries;
        }

        // 盤面の見かたと操作。
        private static string ScreenBody()
        {
            return "画面上部のネームプレートには、全員の所持金と占領地（占拠数 / 勝利に必要な数）が出る。\n"
                + "プレートをタップすると、その人の所持アイテムまで見られる（CPU・他プレイヤーのぶんも見られる）。\n"
                + "マスをタップすればそのマスの効果を確認でき、盤面は左下の虫眼鏡ボタンで拡大縮小・"
                + "ドラッグで動かせる。";
        }
    }
}
