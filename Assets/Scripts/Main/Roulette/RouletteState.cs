namespace Main.Roulette
{
    /// <summary>
    /// ルーレットの状態。
    /// </summary>
    public enum RouletteState
    {
        /// <summary>停止していて回せる状態（初期状態）。</summary>
        Idle,

        /// <summary>回転中。</summary>
        Spinning,

        /// <summary>出目が確定して停止した状態。</summary>
        Stopped,
    }
}
