using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Main.Online;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class ActionStreamTests
    {
        [Test]
        public void 待つ前に届いたアクションも取りこぼさない()
        {
            using ActionStream stream = new();
            stream.Push(GameAction.Spin(1, 5));

            GameAction received = stream.NextAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(GameActionType.Spin, received.Type);
            Assert.AreEqual(5, received.Sector);
            Assert.AreEqual(0, stream.PendingCount);
        }

        [Test]
        public void 待っている最中に届いたアクションも受け取れる()
        {
            using ActionStream stream = new();

            UniTask<GameAction> pending = stream.NextAsync(CancellationToken.None);
            stream.Push(GameAction.MoneyLanding(2, -400));

            GameAction received = pending.GetAwaiter().GetResult();

            Assert.AreEqual(GameActionType.MoneyLanding, received.Type);
            Assert.AreEqual(2, received.Seat);
            Assert.AreEqual(-400, received.MoneyDelta);
        }

        [Test]
        public void 複数まとめて届いても発行順に取り出せる()
        {
            using ActionStream stream = new();
            stream.Push(GameAction.ItemUse(0, 1, 12));
            stream.Push(GameAction.Spin(0, 3));
            stream.Push(GameAction.MoneyLanding(0, 200));

            Assert.AreEqual(3, stream.PendingCount);
            Assert.AreEqual(GameActionType.ItemUse, Next(stream).Type);
            Assert.AreEqual(GameActionType.Spin, Next(stream).Type);
            Assert.AreEqual(GameActionType.MoneyLanding, Next(stream).Type);
            Assert.AreEqual(0, stream.PendingCount);
        }

        [Test]
        public void 同時に2箇所から待つと例外になる()
        {
            using ActionStream stream = new();

            UniTask<GameAction> first = stream.NextAsync(CancellationToken.None);

            Assert.Throws<InvalidOperationException>(
                () => stream.NextAsync(CancellationToken.None).GetAwaiter().GetResult());

            // 後片付け（待機中のタスクを解決しておく）。
            stream.Push(GameAction.Spin(0, 0));
            first.GetAwaiter().GetResult();
        }

        [Test]
        public void キャンセル済みトークンでは即座にキャンセルされる()
        {
            using ActionStream stream = new();
            using CancellationTokenSource cts = new();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(
                () => stream.NextAsync(cts.Token).GetAwaiter().GetResult());
        }

        [Test]
        public void 待機中にキャンセルすると待ちが解ける()
        {
            using ActionStream stream = new();
            using CancellationTokenSource cts = new();

            UniTask<GameAction> pending = stream.NextAsync(cts.Token);
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() => pending.GetAwaiter().GetResult());
        }

        [Test]
        public void 破棄すると待機中の取り出しがキャンセルされる()
        {
            ActionStream stream = new();

            UniTask<GameAction> pending = stream.NextAsync(CancellationToken.None);
            stream.Dispose();

            Assert.Throws<OperationCanceledException>(() => pending.GetAwaiter().GetResult());
        }

        private static GameAction Next(ActionStream stream)
        {
            return stream.NextAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
    }
}
