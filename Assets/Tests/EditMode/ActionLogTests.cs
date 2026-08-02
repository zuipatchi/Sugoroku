using System.Collections.Generic;
using Main.Online;
using NUnit.Framework;

namespace Tests.EditMode
{
    /// <summary>
    /// ホストが配ったアクションの台帳。再接続してきたクライアントへ「取りこぼしたぶんだけ」を
    /// 送り直すための純粋ロジックなので、採番の連続性と切り出し範囲を押さえる。
    /// </summary>
    public class ActionLogTests
    {
        [Test]
        public void 追加するたびに1始まりの連番が振られる()
        {
            ActionLog log = new();

            GameAction first = log.Append(GameAction.SpinStart(0));
            GameAction second = log.Append(GameAction.Spin(0, 3));

            Assert.AreEqual(1, first.Seq);
            Assert.AreEqual(2, second.Seq);
            Assert.AreEqual(2, log.LastSeq);
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void 採番しても中身は変わらない()
        {
            ActionLog log = new();

            GameAction numbered = log.Append(GameAction.MoneyLanding(1, -300));

            Assert.AreEqual(GameActionType.MoneyLanding, numbered.Type);
            Assert.AreEqual(1, numbered.Seat);
            Assert.AreEqual(-300, numbered.MoneyDelta);
        }

        [Test]
        public void Sinceは申告より後のぶんだけを配信順で返す()
        {
            ActionLog log = new();
            log.Append(GameAction.SpinStart(0));      // seq 1
            log.Append(GameAction.Spin(0, 3));        // seq 2
            log.Append(GameAction.MoneyLanding(0, 500)); // seq 3

            IReadOnlyList<GameAction> missing = log.Since(1);

            Assert.AreEqual(2, missing.Count);
            Assert.AreEqual(2, missing[0].Seq);
            Assert.AreEqual(GameActionType.Spin, missing[0].Type);
            Assert.AreEqual(3, missing[1].Seq);
            Assert.AreEqual(GameActionType.MoneyLanding, missing[1].Type);
        }

        [Test]
        public void 全部受け取っている相手には何も返さない()
        {
            ActionLog log = new();
            log.Append(GameAction.SpinStart(0));
            log.Append(GameAction.Spin(0, 3));

            Assert.AreEqual(0, log.Since(2).Count);
            // 相手の申告が進みすぎていても（あり得ないが）空を返して壊れないこと。
            Assert.AreEqual(0, log.Since(99).Count);
        }

        [Test]
        public void 何も受け取っていない相手には最初から全部返す()
        {
            ActionLog log = new();
            log.Append(GameAction.SpinStart(0));
            log.Append(GameAction.Spin(0, 3));

            IReadOnlyList<GameAction> missing = log.Since(GameAction.NoSeq);

            Assert.AreEqual(2, missing.Count);
            Assert.AreEqual(1, missing[0].Seq);
        }

        [Test]
        public void 空の台帳は常に空を返す()
        {
            ActionLog log = new();

            Assert.AreEqual(GameAction.NoSeq, log.LastSeq);
            Assert.AreEqual(0, log.Count);
            Assert.AreEqual(0, log.Since(0).Count);
        }
    }
}
