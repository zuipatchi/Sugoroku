using System;

namespace Main.Board
{
    /// <summary>
    /// 進む／戻るのマス（<see cref="BoardCellEvent.Forward"/> / <see cref="BoardCellEvent.Back"/>）に
    /// 着地したときに続けて動くマス数を決める純粋ロジック。マスごとの固定値
    /// （<see cref="BoardCellDefinition.Amount"/>）ではなく、着地のたびに <see cref="MinSteps"/>〜
    /// <see cref="MaxSteps"/> のランダムなマス数を返す。状態は持たず、乱数源は呼び出し側が渡す
    /// （テストで seed 固定できる）。進む／戻るの符号付けは <see cref="CellEventResolver.TryGetMoveSteps"/>、
    /// 実際の移動と演出の統括は <see cref="BoardPresenter"/> が行う。
    ///
    /// マス数が着地ごとに変わるので、**オンラインでは着地した本人が決めて配る**
    /// （<see cref="Online.GameActionType.MoveLanding"/>）。お金マス（<see cref="Money.MoneyCellRule"/>）と同じ規約。
    /// </summary>
    public static class MoveCellRule
    {
        /// <summary>動くマス数の下限（含む）。</summary>
        public const int MinSteps = 1;

        /// <summary>動くマス数の上限（含む）。</summary>
        public const int MaxSteps = 5;

        /// <summary>
        /// 続けて動くマス数（絶対値）を <see cref="MinSteps"/>〜<see cref="MaxSteps"/> からランダムに返す。
        /// 進む／戻るの符号付けは呼び出し側が行う。乱数源 <paramref name="rng"/> が null のときは
        /// <see cref="MinSteps"/> で決定的に返す。
        /// </summary>
        public static int Steps(Random rng)
        {
            return rng == null ? MinSteps : rng.Next(MinSteps, MaxSteps + 1);
        }
    }
}
