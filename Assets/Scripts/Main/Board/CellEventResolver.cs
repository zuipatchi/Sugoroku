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
        /// 着地マスのイベント <paramref name="cellEvent"/>（数値パラメータ <paramref name="amount"/>）が
        /// お金イベントなら true を返し、所持金の変化量を <paramref name="delta"/> に入れる
        /// （MoneyUp は +amount、MoneyDown は -amount）。お金イベント以外は false（delta は 0）。
        /// </summary>
        public static bool TryGetMoneyDelta(BoardCellEvent cellEvent, int amount, out int delta)
        {
            switch (cellEvent)
            {
                case BoardCellEvent.MoneyUp:
                    delta = amount;
                    return true;
                case BoardCellEvent.MoneyDown:
                    delta = -amount;
                    return true;
                default:
                    delta = 0;
                    return false;
            }
        }
    }
}
