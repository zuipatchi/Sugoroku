using System;

namespace Main.Money
{
    /// <summary>
    /// お金アップ/ダウンのマス（<see cref="Board.BoardCellEvent.MoneyUp"/> /
    /// <see cref="Board.BoardCellEvent.MoneyDown"/>）に着地したときの増減額を決める純粋ロジック。
    /// マスごとの固定値ではなく、着地のたびに n × <see cref="Unit"/>（n は <see cref="MinN"/>〜
    /// <see cref="MaxN"/> のランダム）の額を返す。状態は持たず、乱数源は呼び出し側が渡す
    /// （テストで seed 固定できる）。実際の残高移動は <see cref="MoneyModel"/> が担い、
    /// 符号付けと演出の統括は <see cref="Board.BoardPresenter"/> が行う。
    /// </summary>
    public static class MoneyCellRule
    {
        /// <summary>金額の刻み。n × この値が増減額になる。</summary>
        public const int Unit = 100;

        /// <summary>係数 n の下限（含む）。</summary>
        public const int MinN = 1;

        /// <summary>係数 n の上限（含む）。</summary>
        public const int MaxN = 5;

        /// <summary>
        /// お金マスの増減額（絶対値）を返す。n を <see cref="MinN"/>〜<see cref="MaxN"/> で
        /// ランダムに選び n × <see cref="Unit"/> を返す。増額・減額の符号付けは呼び出し側が行う。
        /// 乱数源 <paramref name="rng"/> が null のときは <see cref="MinN"/> で決定的に計算する。
        /// </summary>
        public static int Amount(Random rng)
        {
            int n = rng == null ? MinN : rng.Next(MinN, MaxN + 1);
            return n * Unit;
        }
    }
}
