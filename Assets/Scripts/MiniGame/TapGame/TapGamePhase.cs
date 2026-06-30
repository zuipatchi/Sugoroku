namespace MiniGame.TapGame
{
    /// <summary>タップ連打ミニゲームの進行フェーズ。</summary>
    public enum TapGamePhase
    {
        /// <summary>開始前（待機）。</summary>
        Ready = 0,
        /// <summary>3・2・1 のカウントダウン中。</summary>
        Countdown = 1,
        /// <summary>計測中（この間だけタップが数えられる）。</summary>
        Playing = 2,
        /// <summary>計測終了。</summary>
        Finished = 3
    }
}
