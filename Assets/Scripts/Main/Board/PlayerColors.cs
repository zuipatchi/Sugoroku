namespace Main.Board
{
    /// <summary>
    /// プレイヤー別の色（コマ・陣地占拠・ネームプレート上辺とプレイヤー詳細モーダル上辺のアクセント）の共通ヘルパ。
    /// 実際の色は USS の <c>board-piece--p{N}</c> / <c>board-cell--owned-p{N}</c> /
    /// <c>board-nameplate--p{N}</c> / <c>player-detail__card--p{N}</c>（N は 0..<see cref="Count"/>-1）に定義する。
    /// 8 色（p0..p7）を用意しておき（現状のプレイ人数上限 4 に余裕を持たせた頭数）、想定外に多い index は末尾色にクランプする。
    /// </summary>
    internal static class PlayerColors
    {
        /// <summary>用意している色数（p0..p7）。プレイ人数上限（4）以上の頭数を確保している。</summary>
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
