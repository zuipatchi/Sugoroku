namespace Main.Board
{
    /// <summary>
    /// マスのイベントから効果を判定する純粋関数群。現状はお金イベント
    /// （<see cref="BoardCellEvent.MoneyUp"/> / <see cref="BoardCellEvent.MoneyDown"/>）のみ扱う。
    /// <see cref="Money.MoneyModel"/> への実際の加算は <see cref="BoardPresenter"/> が行う。
    /// </summary>
    public static class CellEventResolver
    {
        /// <summary>
        /// 着地マスのイベント <paramref name="cellEvent"/> がお金の増減を伴うか。増減額（ランダム）を決める前に
        /// 「このマスはお金マスか」だけを全クライアントで一致して判定するために使う（オンライン同期）。
        /// </summary>
        public static bool IsMoneyEvent(BoardCellEvent cellEvent)
        {
            return cellEvent == BoardCellEvent.MoneyUp || cellEvent == BoardCellEvent.MoneyDown;
        }

        /// <summary>
        /// 着地マスのイベント <paramref name="cellEvent"/> が正の額の増減額 <paramref name="magnitude"/>
        /// （<see cref="Money.MoneyCellRule.Amount"/> で毎回ランダムに決める）を伴うお金イベントなら true を返し、
        /// 符号を付けた所持金の変化量を <paramref name="delta"/> に入れる（MoneyUp は +magnitude、
        /// MoneyDown は -magnitude）。お金イベント以外は false（delta は 0）。
        /// </summary>
        public static bool TryGetMoneyDelta(BoardCellEvent cellEvent, int magnitude, out int delta)
        {
            switch (cellEvent)
            {
                case BoardCellEvent.MoneyUp:
                    delta = magnitude;
                    return true;
                case BoardCellEvent.MoneyDown:
                    delta = -magnitude;
                    return true;
                default:
                    delta = 0;
                    return false;
            }
        }
    }
}
