namespace Common.MiniGame
{
    /// <summary>
    /// ミニゲーム 1 回分の結果。<see cref="Score"/> の意味はゲームごとに異なる（Tap はタップ数）。
    /// </summary>
    public readonly struct MiniGameResult
    {
        public MiniGameId Game { get; }
        public int Score { get; }

        public MiniGameResult(MiniGameId game, int score)
        {
            Game = game;
            Score = score;
        }
    }
}
