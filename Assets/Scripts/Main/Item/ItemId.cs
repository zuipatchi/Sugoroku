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

        /// <summary>相手の所持金の一部（ランダムな割合）を奪って自分に足すアイテム。</summary>
        StealMoney = 1,

        /// <summary>好きなミニゲームを選んで遊び、勝てば所持金報酬をもらえるアイテム。</summary>
        MiniGame = 2,

        /// <summary>使った瞬間にゲームに勝利するアイテム（一発逆転）。</summary>
        InstantWin = 3
    }
}
