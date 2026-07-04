namespace MiniGame.RaceGame
{
    /// <summary>メーターを止めた位置の判定。前進量は <see cref="RaceGameConfig"/> が決める。</summary>
    public enum MeterJudgement
    {
        /// <summary>Good 域の外。前進しない。</summary>
        Miss = 0,
        /// <summary>中央寄り。少し前進する。</summary>
        Good = 1,
        /// <summary>中央。大きく前進する。</summary>
        Great = 2
    }
}
