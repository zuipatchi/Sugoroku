using System;
using Main.Turn;
using R3;

namespace Main.Money
{
    /// <summary>
    /// 各プレイヤーの所持金を保持する Model。マスのお金イベント
    /// （<see cref="Board.BoardCellEvent.MoneyUp"/> / <see cref="Board.BoardCellEvent.MoneyDown"/>）や、
    /// 将来のミニゲーム報酬から <see cref="Add"/> で増減する。UI は人間プレイヤーの所持金を購読して表示する。
    /// 参加者ごとに所持金を持ち、マイナス（借金）も許容する。
    /// </summary>
    public sealed class MoneyModel : IDisposable
    {
        /// <summary>ゲーム開始時の所持金。</summary>
        public const int InitialMoney = 1000;

        private readonly ReactiveProperty<int>[] _money;

        public MoneyModel(GameParticipants participants)
        {
            int count = participants.Count;
            _money = new ReactiveProperty<int>[count];
            for (int i = 0; i < count; i++)
            {
                _money[i] = new ReactiveProperty<int>(InitialMoney);
            }
        }

        /// <summary>所持金を持つプレイヤー（コマ）の数。</summary>
        public int PlayerCount => _money.Length;

        /// <summary>プレイヤー <paramref name="player"/> の所持金。</summary>
        public ReadOnlyReactiveProperty<int> Money(int player) => _money[player];

        /// <summary>
        /// プレイヤー <paramref name="player"/> の所持金を <paramref name="delta"/> だけ増減する
        /// （負の値で減算）。マイナス（借金）も許容するため下限クランプはしない。
        /// </summary>
        public void Add(int player, int delta)
        {
            _money[player].Value += delta;
        }

        public void Dispose()
        {
            foreach (ReactiveProperty<int> money in _money)
            {
                money.Dispose();
            }
        }
    }
}
