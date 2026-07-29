using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Main.Online
{
    /// <summary>
    /// 受信したアクションのバッファ。受信ハンドラ（<see cref="Push"/>）と待機側（<see cref="NextAsync"/>）を
    /// キューで仲介し、送受信の前後関係に依存せず取りこぼさないようにする
    /// （[docs/networking.md](../../../../docs/networking.md) 8. 遅延ハンドラ登録によるメッセージロスト の恒久策）。
    ///
    /// 待機できるのは同時に 1 箇所だけ。ゲーム進行は「手番の待機（<see cref="Turn.GameFlowController"/>）」と
    /// 「着地の待機（<see cref="Board.BoardPresenter"/>）」が交互に所有権を持つ設計で、
    /// 同時に 2 箇所から待つのは進行の組み立てを誤っているサインなので例外にする。
    /// </summary>
    public sealed class ActionStream : IDisposable
    {
        private readonly Queue<GameAction> _queue = new();
        private UniTaskCompletionSource<GameAction> _waiter;
        private bool _disposed;

        /// <summary>まだ取り出されていないアクションの数。</summary>
        public int PendingCount => _queue.Count;

        /// <summary>受信したアクションを流し込む。待機中なら即座に解決し、いなければキューに積む。</summary>
        public void Push(GameAction action)
        {
            if (_disposed)
            {
                return;
            }

            UniTaskCompletionSource<GameAction> waiter = _waiter;
            _waiter = null;
            if (waiter != null && waiter.TrySetResult(action))
            {
                return;
            }
            _queue.Enqueue(action);
        }

        /// <summary>
        /// 次のアクションを取り出す。キューにあれば即座に返し、無ければ届くまで待つ。
        /// </summary>
        public async UniTask<GameAction> NextAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (_queue.Count > 0)
            {
                return _queue.Dequeue();
            }

            if (_waiter != null)
            {
                throw new InvalidOperationException("ActionStream を同時に 2 箇所から待つことはできません。");
            }

            UniTaskCompletionSource<GameAction> waiter = new();
            _waiter = waiter;
            try
            {
                using (ct.Register(() => waiter.TrySetCanceled()))
                {
                    return await waiter.Task;
                }
            }
            finally
            {
                if (ReferenceEquals(_waiter, waiter))
                {
                    _waiter = null;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            UniTaskCompletionSource<GameAction> waiter = _waiter;
            _waiter = null;
            waiter?.TrySetCanceled();
            _queue.Clear();
        }
    }
}
