namespace MiniGame.OverlapGame
{
    /// <summary>「被っちゃやーよ」の進行フェーズ。</summary>
    public enum OverlapGamePhase
    {
        /// <summary>開始前（待機）。</summary>
        Ready = 0,
        /// <summary>3・2・1 のカウントダウン中。</summary>
        Countdown = 1,
        /// <summary>選択中（この間だけプレイヤーのアイテム選択が有効）。</summary>
        Choosing = 2,
        /// <summary>全員の選択をオープンして被りを判定した状態。</summary>
        Revealed = 3,
        /// <summary>結果確定。</summary>
        Finished = 4
    }
}
