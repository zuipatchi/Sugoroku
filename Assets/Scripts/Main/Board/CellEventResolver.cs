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
        /// 符号を付けた移動マス数を <paramref name="magnitude"/> から求めて <paramref name="steps"/> に入れる
        /// （進む＝+<paramref name="magnitude"/>、戻る＝-<paramref name="magnitude"/>）。
        /// 移動イベント以外は false（steps は 0）。
        ///
        /// <paramref name="magnitude"/> は着地のたびに <see cref="MoveCellRule.Steps"/> で決まる正のマス数で、
        /// マスごとの固定値ではない。値が着地ごとに変わるのでオンラインでは着地した本人が決めて配る
        /// （<see cref="Money.MoneyCellRule"/> のお金マスと同じ規約）。
        /// </summary>
        public static bool TryGetMoveSteps(BoardCellEvent cellEvent, int magnitude, out int steps)
        {
            switch (cellEvent)
            {
                case BoardCellEvent.Forward:
                    steps = magnitude;
                    return true;
                case BoardCellEvent.Back:
                    steps = -magnitude;
                    return true;
                default:
                    steps = 0;
                    return false;
            }
        }

        /// <summary>
        /// 着地マスのイベント <paramref name="cellEvent"/> がコマの移動（進む／戻る）を伴うか。
        /// 動くマス数（ランダム）を決める前に「このマスは移動マスか」だけを全クライアントで一致して
        /// 判定するために使う（オンライン同期）。<see cref="IsMoneyEvent"/> と同じ役割。
        /// </summary>
        public static bool IsMoveEvent(BoardCellEvent cellEvent)
        {
            return cellEvent == BoardCellEvent.Forward || cellEvent == BoardCellEvent.Back;
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
