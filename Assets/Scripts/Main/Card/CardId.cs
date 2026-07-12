namespace Main.Card
{
    /// <summary>
    /// カードの種類の識別子。カード取得マス（<see cref="Board.BoardCellEvent.Card"/>）に止まると
    /// この中からランダムに 1 枚もらえる。カードの表示名・画像アドレスは <see cref="CardCatalog"/> が持つ。
    /// 新しいカードを増やすときはここに 1 行足し、<see cref="CardCatalog"/> にも定義を追加する。
    /// </summary>
    public enum CardId
    {
        /// <summary>相手の陣地を横取りするカード（効果の発動は将来対応）。</summary>
        StealTerritory = 0,

        /// <summary>相手の所持金を奪うカード（効果の発動は将来対応）。</summary>
        StealMoney = 1,

        /// <summary>ミニゲームを起こすカード（効果の発動は将来対応）。</summary>
        MiniGame = 2
    }
}
