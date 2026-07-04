namespace MiniGame.RaceGame
{
    /// <summary>タイミングメーター式 2D レースの進行フェーズ。</summary>
    public enum RaceGamePhase
    {
        /// <summary>開始前（待機）。</summary>
        Ready = 0,
        /// <summary>3・2・1 のカウントダウン中。</summary>
        Countdown = 1,
        /// <summary>レース中（この間だけメーター入力と前進が有効）。</summary>
        Racing = 2,
        /// <summary>ゴール到達で決着。</summary>
        Finished = 3
    }
}
