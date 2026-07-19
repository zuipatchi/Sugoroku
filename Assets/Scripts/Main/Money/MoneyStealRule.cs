using System;

namespace Main.Money
{
    /// <summary>
    /// お金よこどりアイテム（<see cref="Item.ItemId.StealMoney"/>）で相手から奪う額を決める純粋ロジック。
    /// 相手の所持金の一定割合（<see cref="MinFraction"/>〜<see cref="MaxFraction"/>）をランダムに奪う。
    /// 状態は持たず、乱数源は呼び出し側が渡す（テストで seed 固定できる）。実際の残高移動は
    /// <see cref="MoneyModel"/> が担い、演出・消費の統括は <see cref="Board.BoardPresenter"/> が行う。
    /// </summary>
    public static class MoneyStealRule
    {
        /// <summary>奪う割合の下限（相手の所持金に対する比率）。</summary>
        public const float MinFraction = 0.2f;

        /// <summary>奪う割合の上限（相手の所持金に対する比率）。全額は奪わないよう 1 未満にする。</summary>
        public const float MaxFraction = 0.5f;

        /// <summary>
        /// 相手の所持金 <paramref name="opponentMoney"/> から奪う額を返す。
        /// 所持金が正のときだけ <see cref="MinFraction"/>〜<see cref="MaxFraction"/> の割合をランダムに奪い、
        /// 端数は切り捨てつつ最低 1・上限は相手の所持金にクランプする。0 以下なら奪える額は無い（0）。
        /// 乱数源 <paramref name="rng"/> が null のときは下限割合で決定的に計算する。
        /// </summary>
        public static int Amount(int opponentMoney, Random rng)
        {
            if (opponentMoney <= 0)
            {
                return 0;
            }

            double fraction = rng == null
                ? MinFraction
                : MinFraction + rng.NextDouble() * (MaxFraction - MinFraction);
            int amount = (int)(opponentMoney * fraction);
            return Math.Clamp(amount, 1, opponentMoney);
        }
    }
}
