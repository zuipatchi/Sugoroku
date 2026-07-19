namespace Main.Board
{
    /// <summary>
    /// プレイヤー別の色（コマ・陣地占拠・ネームプレート上辺のアクセント）の共通ヘルパ。
    /// 実際の色は USS の <c>board-piece--p{N}</c> / <c>board-cell--owned-p{N}</c> /
    /// <c>board-nameplate--p{N}</c>（N は 0..<see cref="Count"/>-1）に定義する。
    /// プレイヤー数の上限（最大 8 人）ぶんの色を用意し、想定外に多い index は末尾色にクランプする。
    /// </summary>
    internal static class PlayerColors
    {
        /// <summary>用意している色数（p0..p7）。プレイヤー数の上限に一致。</summary>
        public const int Count = 8;

        /// <summary>プレイヤー index を色 index（0..<see cref="Count"/>-1）へクランプする。</summary>
        public static int IndexOf(int player)
        {
            if (player < 0)
            {
                return 0;
            }
            return player >= Count ? Count - 1 : player;
        }
    }
}
