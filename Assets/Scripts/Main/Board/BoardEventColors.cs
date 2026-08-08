using UnityEngine;

namespace Main.Board
{
    /// <summary>
    /// 盤面イベント種別ごとの表示色（ランタイム共通）。盤面エディタの塗り分け・凡例（Main.EditorTools）と
    /// マップ選択のサムネイル・凡例（MapSelect）で同じ配色を使うための単一の情報源。
    /// </summary>
    public static class BoardEventColors
    {
        /// <summary>
        /// イベント種別ごとのマス色。<see cref="BoardCellEvent.None"/>（通常マス）は白＝
        /// 何も起きないマスであることが、色の付いたイベントマスとひと目で見分けられるようにする。
        /// </summary>
        public static Color Of(BoardCellEvent cellEvent)
        {
            switch (cellEvent)
            {
                case BoardCellEvent.Forward:
                    return new Color(0.24f, 0.62f, 0.5f);
                case BoardCellEvent.Back:
                    return new Color(0.82f, 0.52f, 0.22f);
                case BoardCellEvent.MiniGame:
                    return new Color(0.56f, 0.36f, 0.72f);
                case BoardCellEvent.MoneyUp:
                    return new Color(0.3f, 0.66f, 0.32f);
                case BoardCellEvent.MoneyDown:
                    return new Color(0.76f, 0.3f, 0.3f);
                case BoardCellEvent.Territory:
                    return new Color(0.3f, 0.46f, 0.76f);
                case BoardCellEvent.Item:
                    return new Color(0.86f, 0.66f, 0.24f);
                default:
                    return Color.white;
            }
        }
    }
}
