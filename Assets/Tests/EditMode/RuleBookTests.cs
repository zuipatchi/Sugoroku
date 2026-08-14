using System;
using System.Collections.Generic;
using Common.MiniGame;
using Home.Presenter;
using Main.Board;
using Main.Item;
using Main.Roulette;
using NUnit.Framework;

namespace Tests.EditMode
{
    /// <summary>
    /// Home のルール説明（<see cref="RuleBook"/>）の内容。文言はゲーム側のカタログから引くので、
    /// **カタログにマス・アイテム・ミニゲームを足したのに説明を書き忘れた**ときにここで落ちる。
    ///
    /// 項目名は種類をまたいで重なることがある（マスの「ミニゲーム」とアイテムの「ミニゲーム」、
    /// マスの「お金アップ」と被っちゃやーよ専用アイテムの「お金アップ」）ので、
    /// 照合は名前だけでなく「色ドットを持つ＝マス」「価格を持つ＝アイテム」まで見て絞る。
    /// </summary>
    public class RuleBookTests
    {
        // 全節の箇条書きを 1 本に並べる（どの節に入っているかに依存しない検証用）。
        private static IReadOnlyList<RuleEntry> AllEntries()
        {
            List<RuleEntry> entries = new();
            foreach (RuleSection section in RuleBook.Sections())
            {
                if (section.Entries != null)
                {
                    entries.AddRange(section.Entries);
                }
            }
            return entries;
        }

        [Test]
        public void すべての節に見出しと中身がある()
        {
            IReadOnlyList<RuleSection> sections = RuleBook.Sections();
            Assert.IsNotEmpty(sections);
            foreach (RuleSection section in sections)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(section.Title), "見出しの無い節があります。");
                bool hasBody = !string.IsNullOrWhiteSpace(section.Body);
                bool hasEntries = section.Entries != null && section.Entries.Count > 0;
                Assert.IsTrue(hasBody || hasEntries, $"「{section.Title}」の中身が空です。");
            }
        }

        [Test]
        public void すべての箇条書きに名前と説明がある()
        {
            foreach (RuleEntry entry in AllEntries())
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(entry.Name), "名前の無い項目があります。");
                Assert.IsFalse(
                    string.IsNullOrWhiteSpace(entry.Description), $"「{entry.Name}」の説明が空です。");
            }
        }

        [Test]
        public void マスの種類はすべてのイベント種別を盤面と同じ名前色説明で並べる()
        {
            // スタート（位置で決まる）＋ BoardEventTally.DisplayOrder ＋通常マスで全種別を出す。
            // 新しいイベント種別を足して DisplayOrder に入れ忘れると、ここで落ちる。
            foreach (BoardCellEvent cellEvent in Enum.GetValues(typeof(BoardCellEvent)))
            {
                string label = BoardEventLabel.Of(cellEvent);
                Assert.IsTrue(
                    Any(label, entry =>
                        entry.Accent.HasValue
                        && entry.Accent.Value == BoardEventColors.Of(cellEvent)
                        && entry.Description == BoardEventDescription.Of(cellEvent)),
                    $"マス「{label}」がルール説明に（盤面と同じ色・説明で）出ていません。");
            }

            Assert.IsTrue(
                Any(BoardEventDescription.StartLabel, entry =>
                    entry.Description == BoardEventDescription.StartDescription),
                "スタートがルール説明に出ていません。");
        }

        [Test]
        public void 買えるアイテムがすべて価格と効果説明付きで並ぶ()
        {
            foreach (ItemDefinition item in ItemCatalog.Purchasable)
            {
                Assert.IsTrue(
                    Any(item.DisplayName, entry =>
                        entry.Note != null
                        && entry.Note.Contains(item.Price.ToString())
                        && entry.Description == item.Description),
                    $"アイテム「{item.DisplayName}」がルール説明に（価格と効果説明付きで）出ていません。");
            }
        }

        [Test]
        public void 買えないアイテムは並ばない()
        {
            // ショップに並ばない（＝手札に入らない）アイテムはルール説明にも出さない。
            // 名前がマスと重なることがあるので、「価格付きで出ていないか」で判定する。
            foreach (ItemDefinition item in ItemCatalog.All)
            {
                if (item.Purchasable)
                {
                    continue;
                }
                Assert.IsFalse(
                    Any(item.DisplayName, entry => entry.Note != null),
                    $"買えないアイテム「{item.DisplayName}」がルール説明に出ています。");
            }
        }

        [Test]
        public void すべてのミニゲームが遊び方付きで並ぶ()
        {
            foreach (MiniGameDefinition game in MiniGameCatalog.All)
            {
                Assert.IsTrue(
                    Any(game.DisplayName, entry => entry.Description == game.Description),
                    $"ミニゲーム「{game.DisplayName}」の遊び方（MiniGameDefinition.Description）が"
                    + "ルール説明に出ていません。");
                Assert.IsFalse(
                    string.IsNullOrWhiteSpace(game.Description),
                    $"ミニゲーム「{game.DisplayName}」の遊び方が空です。");
            }
        }

        [Test]
        public void ミニゲームの賞金はルール側の文言をそのまま使う()
        {
            // 賞金額を変えたらルール説明も一緒に変わる（MiniGamePrize が単一の情報源）。
            Assert.IsTrue(
                AnyBodyContains(MiniGamePrize.RankPrizeText()),
                "順位別の賞金がルール説明に出ていません。");
        }

        [Test]
        public void ルーレットの数字の範囲はルール側の定数をそのまま使う()
        {
            // 範囲を変えたらルール説明も一緒に変わる（RouletteNumberLayout が単一の情報源）。
            Assert.IsTrue(
                AnyBodyContains($"{RouletteNumberLayout.MinNumber}〜{RouletteNumberLayout.MaxNumber}"),
                "ルーレットの数字の範囲がルール説明に出ていません。");
        }

        // 名前が name の項目のうち、predicate を満たすものがあるか。
        private static bool Any(string name, Func<RuleEntry, bool> predicate)
        {
            foreach (RuleEntry entry in AllEntries())
            {
                if (entry.Name == name && predicate(entry))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool AnyBodyContains(string text)
        {
            foreach (RuleSection section in RuleBook.Sections())
            {
                if (!string.IsNullOrEmpty(section.Body) && section.Body.Contains(text))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
