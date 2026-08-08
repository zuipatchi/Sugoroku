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
        /// <remarks>
        /// 色は種別ごとに色相を離して選ぶ。意味から素直に決まるもの（お金アップ＝緑／お金ダウン＝赤／
        /// アイテム＝金／陣地＝青／ミニゲーム＝紫）を先に置き、意味で色が決まらない進む/戻るを
        /// 残った色相（シアン／ピンク）に割り当てている。**イベントを足すときは既存の色相と
        /// 隣り合わない色を選ぶ**（進む＝緑系とお金アップ＝緑、戻る＝橙とアイテム＝金が似ていて
        /// 盤面エディタとマップ選択のサムネイルで見分けられなかったため入れ替えた）。
        /// </remarks>
        public static Color Of(BoardCellEvent cellEvent)
        {
            switch (cellEvent)
            {
                case BoardCellEvent.Forward:
                    return new Color(0.16f, 0.7f, 0.78f);
                case BoardCellEvent.Back:
                    return new Color(0.9f, 0.38f, 0.62f);
                case BoardCellEvent.MiniGame:
                    return new Color(0.56f, 0.36f, 0.72f);
                case BoardCellEvent.MoneyUp:
                    return new Color(0.3f, 0.66f, 0.32f);
                case BoardCellEvent.MoneyDown:
                    return new Color(0.76f, 0.3f, 0.3f);
                case BoardCellEvent.Territory:
                    return new Color(0.26f, 0.42f, 0.82f);
                case BoardCellEvent.Item:
                    return new Color(0.86f, 0.66f, 0.24f);
                default:
                    return Color.white;
            }
        }
    }
}
