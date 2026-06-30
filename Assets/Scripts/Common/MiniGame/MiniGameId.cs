namespace Common.MiniGame
{
    /// <summary>
    /// ミニゲームの種類。中身は Addressables で差し替える前提で、将来最大 5 種類まで追加する。
    /// </summary>
    public enum MiniGameId
    {
        /// <summary>5 秒間のタップ連打数を競う。</summary>
        Tap = 0
    }
}
