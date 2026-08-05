namespace Main.Board
{
    /// <summary>
    /// すごろくのマスに割り当てるイベントの種類。盤面データ（<see cref="BoardDefinition"/>）が
    /// 保持し、盤面エディタで編集する。お金イベント（<see cref="MoneyUp"/> / <see cref="MoneyDown"/>）は
    /// 着地時に所持金を増減し、陣地マス（<see cref="Territory"/>）は着地時に占拠して勝敗を決める。
    /// コマ移動（<see cref="Forward"/> / <see cref="Back"/>）は着地時にそのマス数ぶん続けて動く（連鎖する）。
    /// ミニゲーム起動（<see cref="MiniGame"/>）は着地時にそのマスに設定されたミニゲームを遊び、勝てば所持金報酬をもらう。
    ///
    /// 値は <see cref="BoardDefinition"/> アセットに int で保存されるので、**既存の値は変えない**
    /// （3 は廃止した「休み」の欠番。詰めると保存済みの盤面のイベントがずれる）。
    /// </summary>
    public enum BoardCellEvent
    {
        /// <summary>何も起きない通常マス。</summary>
        None = 0,

        /// <summary>止まると N マス進む（N = 着地のたびにランダム＝<see cref="MoveCellRule"/>）。</summary>
        Forward = 1,

        /// <summary>止まると N マス戻る（N は <see cref="Forward"/> と同じくランダム）。</summary>
        Back = 2,

        // 3 は廃止した「休み」の欠番（既存アセットとの互換のため詰めない）。

        /// <summary>止まるとそのマスに設定されたミニゲーム（<see cref="BoardCellDefinition.MiniGame"/>）が始まる。勝つと所持金報酬。</summary>
        MiniGame = 4,

        /// <summary>止まると所持金が N（= 着地のたびにランダム＝<see cref="Money.MoneyCellRule"/>）増える。</summary>
        MoneyUp = 5,

        /// <summary>止まると所持金が N 減る（N は <see cref="MoneyUp"/> と同じくランダム）。</summary>
        MoneyDown = 6,

        /// <summary>止まるとそのマスを占拠する（相手の陣地でも上書き）。盤面の陣地マス総数をプレイヤー数で割った数（端数切り上げ）を占拠すると勝ち。</summary>
        Territory = 7,

        /// <summary>止まるとアイテムショップが開き、ランダムなラインナップ（<see cref="Item.ItemCatalog"/> から抽選）を所持金で購入できる。</summary>
        Item = 8
    }
}
