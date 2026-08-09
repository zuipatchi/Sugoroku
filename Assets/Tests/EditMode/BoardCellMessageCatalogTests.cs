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

        // 既定文言を流し込んだ資産（本番の割り当て済みと同じ状態）。テストごとに作り直す。
        private BoardCellMessageCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = UnityEngine.ScriptableObject.CreateInstance<BoardCellMessageCatalog>();
            _catalog.ResetToDefaults();
        }

        [TearDown]
        public void TearDown()
        {
            if (_catalog != null)
            {
                UnityEngine.Object.DestroyImmediate(_catalog);
                _catalog = null;
            }
        }

        private static IEnumerable<BoardCellEvent> AllEvents()
        {
            foreach (BoardCellEvent cellEvent in Enum.GetValues(typeof(BoardCellEvent)))
            {
                yield return cellEvent;
            }
        }

        [Test]
        public void すべてのイベントに既定の文言がある()
        {
            foreach (BoardCellEvent cellEvent in AllEvents())
            {
                IReadOnlyList<string> pool = BoardCellMessageDefaults.Messages(cellEvent);
                Assert.IsNotNull(pool, $"{cellEvent} の既定の文言プールが null です。");
                Assert.Greater(pool.Count, 0, $"{cellEvent} の既定の文言が 1 件もありません。");
            }
        }

        [Test]
        public void スタート専用の文言がある()
        {
            Assert.Greater(BoardCellMessageDefaults.StartMessages.Count, 0);
            Assert.Greater(_catalog.StartMessages.Count, 0);
            Assert.IsNotNull(_catalog.Pick(BoardCellEvent.None, true, new Random(1)));
        }

        [Test]
        public void 既定の文言に空のものが混ざっていない()
        {
            foreach (BoardCellEvent cellEvent in AllEvents())
            {
                foreach (string message in BoardCellMessageDefaults.Messages(cellEvent))
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(message), $"{cellEvent} に空の文言があります。");
                }
            }
            foreach (string message in BoardCellMessageDefaults.StartMessages)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(message), "スタートに空の文言があります。");
            }
        }

        [Test]
        public void 既定の文言は句点で終わらない()
        {
            // 文末の「。」は使わない決まり（言い切り・「！」・「…」・「？」で終える）。テンポを出すためと、
            // 小さい帯に載せる短文なので終止符が間延びして見えるため。文言を足すときの取り違えを検出する。
            foreach (BoardCellEvent cellEvent in AllEvents())
            {
                foreach (string message in BoardCellMessageDefaults.Messages(cellEvent))
                {
                    Assert.IsFalse(message.EndsWith("。"), $"{cellEvent} の「{message}」が句点で終わっています。");
                }
            }
            foreach (string message in BoardCellMessageDefaults.StartMessages)
            {
                Assert.IsFalse(message.EndsWith("。"), $"スタートの「{message}」が句点で終わっています。");
            }
        }

        [Test]
        public void 既定に戻した資産は既定の文言と一致する()
        {
            CollectionAssert.AreEqual(BoardCellMessageDefaults.StartMessages, _catalog.StartMessages);
            foreach (BoardCellEvent cellEvent in AllEvents())
            {
                CollectionAssert.AreEqual(
                    BoardCellMessageDefaults.Messages(cellEvent),
                    _catalog.Messages(cellEvent),
                    $"{cellEvent} の文言が既定と一致しません。");
            }
        }

        [Test]
        public void 資産が未割り当てなら既定の文言から選ぶ()
        {
            // インスペクタ未割り当てでも従来どおり動く（BoardPresenter はこの静的版を呼ぶ）。
            foreach (BoardCellEvent cellEvent in AllEvents())
            {
                string picked = BoardCellMessageCatalog.Pick(null, cellEvent, false, new Random(3));
                CollectionAssert.Contains(BoardCellMessageDefaults.Messages(cellEvent), picked);
            }
            CollectionAssert.Contains(
                BoardCellMessageDefaults.StartMessages,
                BoardCellMessageCatalog.Pick(null, BoardCellEvent.None, true, new Random(3)));
        }

        [Test]
        public void 資産を割り当てたら資産の文言から選ぶ()
        {
            _catalog.SetMessages(BoardCellEvent.MoneyUp, new[] { "資産の文言" });
            Assert.AreEqual(
                "資産の文言",
                BoardCellMessageCatalog.Pick(_catalog, BoardCellEvent.MoneyUp, false, new Random(5)));
        }

        [Test]
        public void プールを空にしたマスでは文言が出ない()
        {
            // 空プールは「そのマスでは文言を出さない」という意図的な設定なので、既定へは戻さない。
            _catalog.SetMessages(BoardCellEvent.None, Array.Empty<string>());
            Assert.IsNull(BoardCellMessageCatalog.Pick(_catalog, BoardCellEvent.None, false, new Random(5)));
        }

        [Test]
        public void 同じseedなら同じ文言を返す()
        {
            // 乱数源は呼び出し側が渡す規約（MoneyCellRule と同じ）なので、seed を固定すれば再現できる。
            foreach (BoardCellEvent cellEvent in AllEvents())
            {
                string first = _catalog.Pick(cellEvent, false, new Random(20260806));
                string second = _catalog.Pick(cellEvent, false, new Random(20260806));
                Assert.AreEqual(first, second, $"{cellEvent} の抽選が seed 固定で再現しません。");
            }
        }

        [Test]
        public void 選ばれた文言は必ずそのイベントのプールに含まれる()
        {
            foreach (BoardCellEvent cellEvent in AllEvents())
            {
                IReadOnlyList<string> pool = _catalog.Messages(cellEvent);
                Random rng = new(7);
                for (int i = 0; i < Draws; i++)
                {
                    string picked = _catalog.Pick(cellEvent, false, rng);
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
                    _catalog.Messages(cellEvent)[0],
                    _catalog.Pick(cellEvent, false, null),
                    $"{cellEvent} で null の乱数源が先頭を返しませんでした。");
            }
            Assert.AreEqual(_catalog.StartMessages[0], _catalog.Pick(BoardCellEvent.None, true, null));
        }

        [Test]
        public void 何度も引けばプールのすべての文言が出る()
        {
            foreach (BoardCellEvent cellEvent in AllEvents())
            {
                IReadOnlyList<string> pool = _catalog.Messages(cellEvent);
                HashSet<string> seen = new();
                Random rng = new(31);
                for (int i = 0; i < Draws; i++)
                {
                    seen.Add(_catalog.Pick(cellEvent, false, rng));
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
                    string picked = _catalog.Pick(cellEvent, true, rng);
                    CollectionAssert.Contains(
                        _catalog.StartMessages,
                        picked,
                        $"{cellEvent} のスタート指定でスタート以外の文言が出ました。");
                }
            }
        }

        [Test]
        public void PickIndexで選んだindexをMessageAtに渡すと同じ文言に戻る()
        {
            // オンラインはホストが PickIndex で引いた index を配り、受信側が MessageAt で文言に戻す。
            // ここがずれると席ごとに違う文言が出るので、同じ抽選と同じ復元になること。
            foreach (BoardCellEvent cellEvent in AllEvents())
            {
                Random pickRng = new(20260809);
                Random expectRng = new(20260809);
                for (int i = 0; i < Draws; i++)
                {
                    int index = BoardCellMessageCatalog.PickIndex(_catalog, cellEvent, false, pickRng);
                    Assert.AreEqual(
                        _catalog.Pick(cellEvent, false, expectRng),
                        BoardCellMessageCatalog.MessageAt(_catalog, cellEvent, false, index),
                        $"{cellEvent} で index からの復元が抽選と一致しません。");
                }
            }
        }

        [Test]
        public void PickIndexとMessageAtは資産が未割り当てでも既定の文言でそろう()
        {
            // 資産の有無は各クライアントで同じ（同じビルド）だが、未割り当てのフォールバックでも
            // index → 文言の対応が保たれること。
            int index = BoardCellMessageCatalog.PickIndex(null, BoardCellEvent.MoneyUp, false, new Random(11));
            Assert.AreEqual(
                BoardCellMessageDefaults.Messages(BoardCellEvent.MoneyUp)[index],
                BoardCellMessageCatalog.MessageAt(null, BoardCellEvent.MoneyUp, false, index));

            int startIndex = BoardCellMessageCatalog.PickIndex(null, BoardCellEvent.None, true, new Random(11));
            Assert.AreEqual(
                BoardCellMessageDefaults.StartMessages[startIndex],
                BoardCellMessageCatalog.MessageAt(null, BoardCellEvent.None, true, startIndex));
        }

        [Test]
        public void 空のプールはPickIndexが負値を返しMessageAtは文言なしになる()
        {
            // 空プール＝そのマスでは文言を出さない設定。配る値が負値になり、受信側も文言なしになること。
            _catalog.SetMessages(BoardCellEvent.None, Array.Empty<string>());

            int index = BoardCellMessageCatalog.PickIndex(_catalog, BoardCellEvent.None, false, new Random(5));
            Assert.Less(index, 0);
            Assert.IsNull(BoardCellMessageCatalog.MessageAt(_catalog, BoardCellEvent.None, false, index));
        }

        [Test]
        public void 範囲外のindexは文言なしになる()
        {
            // 壊れた値や資産の食い違いが届いても落ちず、文言が出ないだけで進行は続く。
            Assert.IsNull(BoardCellMessageCatalog.MessageAt(_catalog, BoardCellEvent.MoneyUp, false, 9999));
            Assert.IsNull(BoardCellMessageCatalog.MessageAt(_catalog, BoardCellEvent.MoneyUp, false, -1));
        }

        [Test]
        public void 編集した文言が資産に残る()
        {
            _catalog.SetStartMessages(new[] { "スタートの文言" });
            _catalog.SetMessages(BoardCellEvent.Item, new[] { "ショップ1", "ショップ2" });

            CollectionAssert.AreEqual(new[] { "スタートの文言" }, _catalog.StartMessages);
            CollectionAssert.AreEqual(new[] { "ショップ1", "ショップ2" }, _catalog.Messages(BoardCellEvent.Item));
        }

        [Test]
        public void 編集欄をそろえても既存の文言は消えない()
        {
            // 新しい BoardCellEvent を足した後の古い資産でも、エディタに全種別の編集欄が出るようにする API。
            // 足りないぶんを空で補うだけで、すでに入っている文言には触らない。
            _catalog.SetMessages(BoardCellEvent.Territory, new[] { "陣地の文言" });

            _catalog.EnsurePools();

            foreach (BoardCellEvent cellEvent in AllEvents())
            {
                Assert.IsNotNull(_catalog.Messages(cellEvent), $"{cellEvent} のプールがありません。");
            }
            CollectionAssert.AreEqual(new[] { "陣地の文言" }, _catalog.Messages(BoardCellEvent.Territory));
        }
    }
}
