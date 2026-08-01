namespace Main.Board
{
    /// <summary>
    /// マスのイベントから効果を判定する純粋関数群。お金イベント
    /// （<see cref="BoardCellEvent.MoneyUp"/> / <see cref="BoardCellEvent.MoneyDown"/>）と
    /// 移動イベント（<see cref="BoardCellEvent.Forward"/> / <see cref="BoardCellEvent.Back"/>）を扱う。
    /// <see cref="Money.MoneyModel"/> への実際の加算とコマの移動は <see cref="BoardPresenter"/> が行う。
    /// </summary>
    public static class CellEventResolver
    {
        /// <summary>
        /// 着地マスのイベント <paramref name="cellEvent"/> がコマの移動（進む／戻る）なら true を返し、
        /// 続けて動くマス数を <paramref name="steps"/> に入れる（進む＝+<paramref name="amount"/>、
        /// 戻る＝-<paramref name="amount"/>）。移動イベント以外は false（steps は 0）。
        ///
        /// マス数は盤面データ（<see cref="BoardCellDefinition.Amount"/>）そのものなので全クライアントで一致する。
        /// つまりオンラインでも移動を配る必要はなく、各クライアントが同じ連鎖を再現できる。
        /// </summary>
        public static bool TryGetMoveSteps(BoardCellEvent cellEvent, int amount, out int steps)
        {
            switch (cellEvent)
            {
                case BoardCellEvent.Forward:
                    steps = amount;
                    return true;
                case BoardCellEvent.Back:
                    steps = -amount;
                    return true;
                default:
                    steps = 0;
                    return false;
            }
        }

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
