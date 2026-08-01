namespace Main.Board
{
    /// <summary>
    /// 盤面イベント種別の日本語表示名（ランタイム共通）。盤面エディタの凡例（Main.EditorTools）と
    /// マップ選択の内訳表示（MapSelect）で同じ文言を使うための単一の情報源。
    /// </summary>
    public static class BoardEventLabel
    {
        /// <summary><paramref name="cellEvent"/> の日本語表示名。</summary>
        public static string Of(BoardCellEvent cellEvent)
        {
            switch (cellEvent)
            {
                case BoardCellEvent.None:
                    return "通常マス";
                case BoardCellEvent.Forward:
                    return "進む";
                case BoardCellEvent.Back:
                    return "戻る";
                case BoardCellEvent.MiniGame:
                    return "ミニゲーム";
                case BoardCellEvent.MoneyUp:
                    return "お金アップ";
                case BoardCellEvent.MoneyDown:
                    return "お金ダウン";
                case BoardCellEvent.Territory:
                    return "陣地";
                case BoardCellEvent.Item:
                    return "アイテム";
                default:
                    return cellEvent.ToString();
            }
        }
    }
}
