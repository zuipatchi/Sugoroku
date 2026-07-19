namespace Main.Board
{
    /// <summary>
    /// すごろくのマスに割り当てるイベントの種類。盤面データ（<see cref="BoardDefinition"/>）が
    /// 保持し、盤面エディタで編集する。お金イベント（<see cref="MoneyUp"/> / <see cref="MoneyDown"/>）は
    /// 着地時に所持金を増減し、陣地マス（<see cref="Territory"/>）は着地時に占拠して勝敗を決める。
    /// それ以外（コマ移動・休み・ミニゲーム起動）は今は表示のみで将来対応する。
    /// </summary>
    public enum BoardCellEvent
    {
        /// <summary>何も起きない通常マス。</summary>
        None = 0,

        /// <summary>止まると N マス進む（N = <see cref="BoardCellDefinition.Amount"/>）。</summary>
        Forward = 1,

        /// <summary>止まると N マス戻る。</summary>
        Back = 2,

        /// <summary>止まると N ターン休み。</summary>
        Rest = 3,

        /// <summary>止まるとミニゲームが発生する。</summary>
        MiniGame = 4,

        /// <summary>止まると所持金が N（= <see cref="BoardCellDefinition.Amount"/>）増える。</summary>
        MoneyUp = 5,

        /// <summary>止まると所持金が N 減る。</summary>
        MoneyDown = 6,

        /// <summary>止まるとそのマスを占拠する（相手の陣地でも上書き）。盤面の陣地マス総数をプレイヤー数で割った数（端数切り上げ）を占拠すると勝ち。</summary>
        Territory = 7,

        /// <summary>止まるとランダムなアイテムを 1 つもらえる（<see cref="Item.ItemCatalog"/> から抽選。効果の発動は将来対応）。</summary>
        Item = 8
    }
}
