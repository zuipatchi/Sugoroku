using System;
using Main.Turn;
using R3;

namespace Main.Money
{
    /// <summary>
    /// 各プレイヤーの所持金を保持する Model。マスのお金イベント
    /// （<see cref="Board.BoardCellEvent.MoneyUp"/> / <see cref="Board.BoardCellEvent.MoneyDown"/>）や、
    /// 将来のミニゲーム報酬から <see cref="Add"/> で増減する。UI は人間プレイヤーの所持金を購読して表示する。
    /// 参加者ごとに所持金を持ち、<see cref="MinMoney"/>（0）より下がらない＝マイナス（借金）は作らない。
    /// 減額が所持金を上回ったときは残高ぶんだけ減って 0 で止まる。
    /// </summary>
    public sealed class MoneyModel : IDisposable
    {
        /// <summary>ゲーム開始時の所持金。</summary>
        public const int InitialMoney = 1000;

        /// <summary>所持金の下限。ここより下がらない（マイナス＝借金は作らない）。</summary>
        public const int MinMoney = 0;

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
        /// プレイヤー <paramref name="player"/> の所持金を <paramref name="delta"/> だけ増減し
        /// （負の値で減算）、**実際に動いた額**を返す。所持金は <see cref="MinMoney"/> より下がらないので、
        /// 減額が所持金を上回ったときは残高ぶんだけ減り、返り値もその額（0 なら 0）になる。
        /// 見せる額と実際の増減をそろえたい呼び出し側（浮遊テキスト・お金よこどりの合計）は返り値を使う。
        /// </summary>
        public int Add(int player, int delta)
        {
            ReactiveProperty<int> money = _money[player];
            int next = Math.Max(MinMoney, money.Value + delta);
            int applied = next - money.Value;
            money.Value = next;
            return applied;
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
