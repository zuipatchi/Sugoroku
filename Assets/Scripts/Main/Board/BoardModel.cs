using System;
using Main.Turn;
using R3;

namespace Main.Board
{
    /// <summary>
    /// すごろく盤のコマ位置と進行状態を保持する Model。参加者ごとにコマ位置を持つ。
    /// 位置の前進・周回判定は <see cref="BoardMath"/>、移動演出は <see cref="BoardPresenter"/> が担う。
    /// </summary>
    public sealed class BoardModel : IDisposable
    {
        private readonly ReactiveProperty<int>[] _positions;
        private readonly ReactiveProperty<bool> _isMoving = new(false);
        private readonly ReactiveProperty<int> _winner = new(-1);

        public BoardModel(GameParticipants participants)
        {
            int count = participants.Count;
            _positions = new ReactiveProperty<int>[count];
            for (int i = 0; i < count; i++)
            {
                _positions[i] = new ReactiveProperty<int>(0);
            }
        }

        /// <summary>コマ（プレイヤー）の数。</summary>
        public int PlayerCount => _positions.Length;

        /// <summary>プレイヤー <paramref name="player"/> のコマ位置（マス index、0 がスタート＝ゴール）。</summary>
        public ReadOnlyReactiveProperty<int> Position(int player) => _positions[player];

        /// <summary>いずれかのコマが移動演出中かどうか（同時に動くコマは 1 つ）。</summary>
        public ReadOnlyReactiveProperty<bool> IsMoving => _isMoving;

        /// <summary>1 周してゴールに到達した勝者プレイヤー index。未決なら -1。</summary>
        public ReadOnlyReactiveProperty<int> Winner => _winner;

        /// <summary>勝者が確定しているか（ゲーム終了）。</summary>
        public bool IsFinished => _winner.CurrentValue >= 0;

        /// <summary>移動演出の開始を通知する。</summary>
        public void BeginMove()
        {
            _isMoving.Value = true;
        }

        /// <summary>プレイヤー <paramref name="player"/> のコマ位置を 1 マス単位で更新する。</summary>
        public void SetPosition(int player, int position)
        {
            _positions[player].Value = position;
        }

        /// <summary>
        /// 移動演出の完了を通知する。<paramref name="cleared"/> が true かつ勝者未決なら、
        /// <paramref name="player"/> を勝者にする。
        /// </summary>
        public void CompleteMove(int player, bool cleared)
        {
            _isMoving.Value = false;
            if (cleared && _winner.CurrentValue < 0)
            {
                _winner.Value = player;
            }
        }

        public void Dispose()
        {
            foreach (ReactiveProperty<int> position in _positions)
            {
                position.Dispose();
            }
            _isMoving.Dispose();
            _winner.Dispose();
        }
    }
}
