namespace Main.Online
{
    /// <summary>
    /// ゲームを進める「決定」の種別。手番の人（あるいは着地した人・アイテムを使った人）が決めて
    /// <see cref="OnlineGameSync.Publish"/> し、全クライアントが受信した順に適用する。
    /// コマ移動・陣地占拠・勝敗判定は「誰が何マス進むか」と盤面から決定論的に導けるため送らない。
    /// </summary>
    public enum GameActionType
    {
        /// <summary>ルーレットが止まった。引数 0 = 停止セクター index。</summary>
        Spin = 0,

        /// <summary>お金マスへの着地。引数 0 = 所持金の増減額（符号付き）。</summary>
        MoneyLanding = 1,

        /// <summary>アイテムショップの購入結果。引数 0 = 買った <see cref="Item.ItemId"/>（買わなかったら負値）。</summary>
        ShopResult = 2,

        /// <summary>アイテム使用。引数 0 = <see cref="Item.ItemId"/>、引数 1 以降 = 効果パラメータ。</summary>
        ItemUse = 3,

        /// <summary>退出通知（対戦の続行が不可能になったことを伝える）。</summary>
        Leave = 4,
    }
}
