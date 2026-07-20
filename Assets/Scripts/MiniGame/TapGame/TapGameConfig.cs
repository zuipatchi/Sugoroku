namespace MiniGame.TapGame
{
    /// <summary>
    /// タップ連打の数値パラメータ。CPU の連打速度（回/秒）の範囲とタップ間隔のゆらぎを持つ。
    /// 各 CPU は Setup 時に下限〜上限からランダムに基準速度を引き、その速度で自動連打する。
    /// 既定は <see cref="Default"/>（5 秒で概ね 25〜40 回＝旧 CPU 想定値と同等の強さ）。
    /// </summary>
    public readonly struct TapGameConfig
    {
        public TapGameConfig(float cpuTapsPerSecondMin, float cpuTapsPerSecondMax, float cpuIntervalJitter)
        {
            CpuTapsPerSecondMin = cpuTapsPerSecondMin;
            CpuTapsPerSecondMax = cpuTapsPerSecondMax;
            CpuIntervalJitter = cpuIntervalJitter;
        }

        /// <summary>CPU の連打速度（回/秒）の下限。</summary>
        public float CpuTapsPerSecondMin { get; }

        /// <summary>CPU の連打速度（回/秒）の上限。</summary>
        public float CpuTapsPerSecondMax { get; }

        /// <summary>1 タップごとの間隔ゆらぎ（±割合）。0.3 なら基準間隔の 0.7〜1.3 倍で揺れる。</summary>
        public float CpuIntervalJitter { get; }

        /// <summary>本編で使う既定パラメータ。</summary>
        public static TapGameConfig Default => new(
            cpuTapsPerSecondMin: 5f,
            cpuTapsPerSecondMax: 8f,
            cpuIntervalJitter: 0.3f);
    }
}
