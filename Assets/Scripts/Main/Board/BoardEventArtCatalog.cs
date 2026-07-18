namespace Main.Board
{
    /// <summary>
    /// 盤面マスの画像アドレス（Addressables）を全マップ共通で解決する静的カタログ。
    /// マスの見た目（イベント種別ごとの画像）はマップに依らず共通なので、盤面データ
    /// （<see cref="BoardDefinition"/>）ごとに持たせず、ここ 1 か所を唯一のソースにする。
    /// 画像アドレスは <c>Board/&lt;イベント名&gt;</c> 規約で、対応するスプライトを Addressables に登録して用意する。
    /// 画像が無いイベント（None／進む／戻る／休み／ミニゲーム）は空文字を返し、呼び出し側は記号表示にフォールバックする。
    /// <see cref="CharacterCatalog"/> / <see cref="Item.ItemCatalog"/> と同じ静的カタログ方式。
    /// </summary>
    public static class BoardEventArtCatalog
    {
        /// <summary>
        /// スタート＝ゴール（経路 index 0）のマスに使う固定画像アドレス。イベント種別に依らない特別扱いで、
        /// <see cref="Address"/> より優先して使う（解決は <see cref="BoardIconLoader"/>）。
        /// </summary>
        public const string StartAddress = "Board/Start";

        /// <summary>
        /// イベント種別 <paramref name="cellEvent"/> のマスに貼る共通画像の Addressables アドレス。
        /// 画像を用意していないイベントは空文字（記号表示にフォールバック）。
        /// </summary>
        public static string Address(BoardCellEvent cellEvent)
        {
            switch (cellEvent)
            {
                case BoardCellEvent.MoneyUp:
                    return "Board/MoneyUp";
                case BoardCellEvent.MoneyDown:
                    return "Board/MoneyDown";
                case BoardCellEvent.Territory:
                    return "Board/Territory";
                case BoardCellEvent.Item:
                    return "Board/Item";
                default:
                    return string.Empty;
            }
        }
    }
}
