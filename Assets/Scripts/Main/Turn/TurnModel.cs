using System;
using R3;

namespace Main.Turn
{
    /// <summary>
    /// 現在の手番プレイヤーを保持する Model。手番の巡回は参加者数に従う。
    /// 勝者（1 周ゴールしたプレイヤー）は盤面が検出するため <see cref="Board.BoardModel"/> が持つ。
    /// </summary>
    public sealed class TurnModel : IDisposable
    {
        private readonly int _playerCount;
        private readonly ReactiveProperty<int> _currentPlayer = new(0);

        public TurnModel(GameParticipants participants)
        {
            _playerCount = participants.Count;
        }

        /// <summary>現在の手番プレイヤー index（0 が先攻＝人間）。</summary>
        public ReadOnlyReactiveProperty<int> CurrentPlayer => _currentPlayer;

        /// <summary>次のプレイヤーへ手番を移す。参加者数で巡回する。</summary>
        public void Next()
        {
            _currentPlayer.Value = (_currentPlayer.Value + 1) % _playerCount;
        }

        public void Dispose()
        {
            _currentPlayer.Dispose();
        }
    }
}
