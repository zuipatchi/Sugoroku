namespace Main.Roulette
{
    /// <summary>
    /// 1 回のスピンの結果。「止まったキャラが進む」方式のため、進む参加者（<see cref="Player"/>）と
    /// 進むマス数（<see cref="Steps"/>）の両方を返す。<see cref="Turn.GameFlowController"/> がこれを使って前進させる。
    /// </summary>
    public readonly struct RouletteOutcome
    {
        /// <summary>止まったセクターのキャラ＝進む参加者の index。</summary>
        public readonly int Player;

        /// <summary>止まったセクターの出目＝進むマス数。</summary>
        public readonly int Steps;

        public RouletteOutcome(int player, int steps)
        {
            Player = player;
            Steps = steps;
        }
    }
}
