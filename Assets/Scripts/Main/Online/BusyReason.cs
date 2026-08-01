namespace Main.Online
{
    /// <summary>
    /// 「1 人の操作を他のプレイヤーが待っている」状態の理由。
    ///
    /// モーダル操作やミニゲームのように、決める人の手元で時間がかかる処理は、他のクライアントから見ると
    /// 画面が何も動かない時間になる。その間だけ待機表示（<see cref="GameActionType.Busy"/>）を出すために、
    /// 何を待っているのかをこの種別で配る。
    /// </summary>
    public enum BusyReason
    {
        /// <summary>待っていない（＝待機表示を消す）。</summary>
        None = 0,

        /// <summary>アイテムショップで買い物中（着地イベント）。</summary>
        ItemShop = 1,

        /// <summary>ミニゲームの選択・プレイ中（アイテム効果）。</summary>
        MiniGame = 2,

        /// <summary>陣地獲得アイテムで奪うマスを選択中（アイテム効果）。</summary>
        TerritorySelect = 3,
    }
}
