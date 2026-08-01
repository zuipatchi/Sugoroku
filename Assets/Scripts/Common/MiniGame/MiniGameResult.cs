namespace Common.MiniGame
{
    /// <summary>
    /// ミニゲーム 1 回分の結果。
    /// <see cref="Score"/> は勝敗（勝ち=1／負け=0）で、一人用モードの報酬判定に使う。
    /// <see cref="Value"/> は生の結果値（連打数・ゴールタイム・選んだカード index）で、
    /// オンライン対戦では全員ぶんを集めて <see cref="MiniGameRanking.Resolve"/> にかけ、勝者を決める。
    /// </summary>
    public readonly struct MiniGameResult
    {
        public MiniGameId Game { get; }
        public int Score { get; }
        public int Value { get; }

        public MiniGameResult(MiniGameId game, int score)
            : this(game, score, score)
        {
        }

        public MiniGameResult(MiniGameId game, int score, int value)
        {
            Game = game;
            Score = score;
            Value = value;
        }
    }
}
