namespace Main.Item
{
    /// <summary>
    /// アイテムの種類の識別子。アイテム取得マス（<see cref="Board.BoardCellEvent.Item"/>）に止まると
    /// この中からランダムに 1 つもらえる。アイテムの表示名・画像アドレスは <see cref="ItemCatalog"/> が持つ。
    /// 新しいアイテムを増やすときはここに 1 行足し、<see cref="ItemCatalog"/> にも定義を追加する。
    /// </summary>
    public enum ItemId
    {
        /// <summary>好きな陣地マス（自分以外が持つマス）を 1 つ選んで占拠するアイテム。</summary>
        StealTerritory = 0,

        /// <summary>相手の所持金を奪うアイテム（効果の発動は将来対応）。</summary>
        StealMoney = 1,

        /// <summary>ミニゲームを起こすアイテム（効果の発動は将来対応）。</summary>
        MiniGame = 2
    }
}
