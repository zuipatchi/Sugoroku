using System;
using System.Collections.Generic;
using Main.Board;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class BoardCellMessageCatalogTests
    {
        // 抽選のばらつきを見るテストで使う試行回数。プールは数件なので、これだけ引けば
        // seed 固定でも全要素がそろう（引けなければ抽選が偏っている＝バグ）。
        private const int Draws = 500;

        private static IEnumerable<BoardCellEvent> AllEvents()
        {
            foreach (BoardCellEvent cellEvent in Enum.GetValues(typeof(BoardCellEvent)))
            {
                yield return cellEvent;
            }
        }

        [Test]
        public void すべてのイベントに文言がある()
        {
            foreach (BoardCellEvent cellEvent in AllEvents())
            {
                IReadOnlyList<string> pool = BoardCellMessageCatalog.Messages(cellEvent);
                Assert.IsNotNull(pool, $"{cellEvent} の文言プールが null です。");
                Assert.Greater(pool.Count, 0, $"{cellEvent} の文言が 1 件もありません。");
                Assert.IsNotNull(
                    BoardCellMessageCatalog.Pick(cellEvent, false, new Random(1)),
                    $"{cellEvent} の文言が選べませんでした。");
            }
        }

        [Test]
        public void スタート専用の文言がある()
        {
            Assert.Greater(BoardCellMessageCatalog.StartMessages.Count, 0);
            Assert.IsNotNull(BoardCellMessageCatalog.Pick(BoardCellEvent.None, true, new Random(1)));
        }

        [Test]
        public void 文言に空のものが混ざっていない()
        {
            foreach (BoardCellEvent cellEvent in AllEvents())
            {
                foreach (string message in BoardCellMessageCatalog.Messages(cellEvent))
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(message), $"{cellEvent} に空の文言があります。");
                }
            }
            foreach (string message in BoardCellMessageCatalog.StartMessages)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(message), "スタートに空の文言があります。");
            }
        }

        [Test]
        public void 同じseedなら同じ文言を返す()
        {
            // 乱数源は呼び出し側が渡す規約（MoneyCellRule と同じ）なので、seed を固定すれば再現できる。
            foreach (BoardCellEvent cellEvent in AllEvents())
            {
                string first = BoardCellMessageCatalog.Pick(cellEvent, false, new Random(20260806));
                string second = BoardCellMessageCatalog.Pick(cellEvent, false, new Random(20260806));
                Assert.AreEqual(first, second, $"{cellEvent} の抽選が seed 固定で再現しません。");
            }
        }

        [Test]
        public void 選ばれた文言は必ずそのイベントのプールに含まれる()
        {
            foreach (BoardCellEvent cellEvent in AllEvents())
            {
                IReadOnlyList<string> pool = BoardCellMessageCatalog.Messages(cellEvent);
                Random rng = new(7);
                for (int i = 0; i < Draws; i++)
                {
                    string picked = BoardCellMessageCatalog.Pick(cellEvent, false, rng);
                    CollectionAssert.Contains(pool, picked, $"{cellEvent} でプール外の文言が出ました。");
                }
            }
        }

        [Test]
        public void 乱数源がnullならプールの先頭を返す()
        {
            foreach (BoardCellEvent cellEvent in AllEvents())
            {
                Assert.AreEqual(
                    BoardCellMessageCatalog.Messages(cellEvent)[0],
                    BoardCellMessageCatalog.Pick(cellEvent, false, null),
                    $"{cellEvent} で null の乱数源が先頭を返しませんでした。");
            }
            Assert.AreEqual(
                BoardCellMessageCatalog.StartMessages[0],
                BoardCellMessageCatalog.Pick(BoardCellEvent.None, true, null));
        }

        [Test]
        public void 何度も引けばプールのすべての文言が出る()
        {
            foreach (BoardCellEvent cellEvent in AllEvents())
            {
                IReadOnlyList<string> pool = BoardCellMessageCatalog.Messages(cellEvent);
                HashSet<string> seen = new();
                Random rng = new(31);
                for (int i = 0; i < Draws; i++)
                {
                    seen.Add(BoardCellMessageCatalog.Pick(cellEvent, false, rng));
                }
                Assert.AreEqual(pool.Count, seen.Count, $"{cellEvent} で出ない文言があります（抽選が偏っています）。");
            }
        }

        [Test]
        public void スタート指定ならスタート専用の文言を返す()
        {
            // スタート＝ゴールは位置で決まるので、そのマスに設定されたイベント種別に引きずられない。
            foreach (BoardCellEvent cellEvent in AllEvents())
            {
                Random rng = new(99);
                for (int i = 0; i < Draws; i++)
                {
                    string picked = BoardCellMessageCatalog.Pick(cellEvent, true, rng);
                    CollectionAssert.Contains(
                        BoardCellMessageCatalog.StartMessages,
                        picked,
                        $"{cellEvent} のスタート指定でスタート以外の文言が出ました。");
                }
            }
        }
    }
}
