using System;
using R3;

namespace Main.Board
{
    /// <summary>
    /// すごろく盤のコマ位置と進行状態を保持する Model。
    /// 位置の前進・周回判定は <see cref="BoardMath"/>、移動演出は <see cref="BoardPresenter"/> が担う。
    /// </summary>
    public sealed class BoardModel : IDisposable
    {
        private readonly ReactiveProperty<int> _position = new(0);
        private readonly ReactiveProperty<bool> _isMoving = new(false);
        private readonly ReactiveProperty<bool> _isCleared = new(false);

        /// <summary>現在のコマ位置（マス index、0 がスタート＝ゴール）。</summary>
        public ReadOnlyReactiveProperty<int> Position => _position;

        /// <summary>コマが移動演出中かどうか。</summary>
        public ReadOnlyReactiveProperty<bool> IsMoving => _isMoving;

        /// <summary>1 周してゴールに到達したか（クリア済み）。</summary>
        public ReadOnlyReactiveProperty<bool> IsCleared => _isCleared;

        /// <summary>移動演出の開始を通知する。</summary>
        public void BeginMove()
        {
            _isMoving.Value = true;
        }

        /// <summary>コマ位置を 1 マス単位で更新する。</summary>
        public void SetPosition(int position)
        {
            _position.Value = position;
        }

        /// <summary>移動演出の完了を通知する。<paramref name="cleared"/> が true ならクリア状態にする。</summary>
        public void CompleteMove(bool cleared)
        {
            _isMoving.Value = false;
            if (cleared)
            {
                _isCleared.Value = true;
            }
        }

        public void Dispose()
        {
            _position.Dispose();
            _isMoving.Dispose();
            _isCleared.Dispose();
        }
    }
}
