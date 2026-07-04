namespace MiniGame.RaceGame
{
    /// <summary>
    /// 2D レースの数値パラメータ。進捗は 0（スタート＝右端）→ 1（ゴール＝左端）で表す。
    /// CPU はプレイヤーと同じベース速度で進み、ランダムな間隔で Great/Good/Miss を抽選して前進する
    /// （プレイヤーのタップと同じブースト量を使う）。既定は <see cref="Default"/>。
    /// </summary>
    public readonly struct RaceGameConfig
    {
        public RaceGameConfig(
            float baseSpeed,
            float greatBoost,
            float goodBoost,
            float greatHalfWidth,
            float goodHalfWidth,
            float cpuTapIntervalMin,
            float cpuTapIntervalMax,
            float cpuGreatChance,
            float cpuGoodChance)
        {
            BaseSpeed = baseSpeed;
            GreatBoost = greatBoost;
            GoodBoost = goodBoost;
            GreatHalfWidth = greatHalfWidth;
            GoodHalfWidth = goodHalfWidth;
            CpuTapIntervalMin = cpuTapIntervalMin;
            CpuTapIntervalMax = cpuTapIntervalMax;
            CpuGreatChance = cpuGreatChance;
            CpuGoodChance = cpuGoodChance;
        }

        /// <summary>全走者に共通のベース速度（進捗/秒）。すごくゆっくり。</summary>
        public float BaseSpeed { get; }

        /// <summary>Great の前進量（進捗）。プレイヤーのタップと CPU の抽選で共用。</summary>
        public float GreatBoost { get; }

        /// <summary>Good の前進量（進捗）。プレイヤーのタップと CPU の抽選で共用。</summary>
        public float GoodBoost { get; }

        /// <summary>メーター中央 0.5 から Great と判定される片側幅。</summary>
        public float GreatHalfWidth { get; }

        /// <summary>メーター中央 0.5 から Good と判定される片側幅（Great を含む外側まで）。</summary>
        public float GoodHalfWidth { get; }

        /// <summary>CPU が次に抽選するまでの最短秒数。</summary>
        public float CpuTapIntervalMin { get; }

        /// <summary>CPU が次に抽選するまでの最長秒数。</summary>
        public float CpuTapIntervalMax { get; }

        /// <summary>CPU の 1 回の抽選で Great を引く確率（低め）。</summary>
        public float CpuGreatChance { get; }

        /// <summary>CPU の 1 回の抽選で Good を引く確率。残り（1 - Great - Good）が Miss。</summary>
        public float CpuGoodChance { get; }

        /// <summary>本編で使う既定パラメータ。</summary>
        public static RaceGameConfig Default => new(
            baseSpeed: 0.015f,
            greatBoost: 0.06f,
            goodBoost: 0.025f,
            greatHalfWidth: 0.06f,
            goodHalfWidth: 0.18f,
            cpuTapIntervalMin: 0.4f,
            cpuTapIntervalMax: 0.8f,
            cpuGreatChance: 0.12f,
            cpuGoodChance: 0.55f);
    }
}
