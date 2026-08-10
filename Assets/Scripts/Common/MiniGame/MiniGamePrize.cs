namespace Common.MiniGame
{
    /// <summary>
    /// ミニゲームの賞金を決める純粋ロジック。**順位が付くゲーム（タップ連打・2Dレース）は順位別**で、
    /// 1 位から <see cref="ByRank"/> の並びどおりに配り、それより下の順位は 0（もらえない）。
    /// **順位が付かないゲーム（被っちゃやーよ）は勝った人へ一律 <see cref="Win"/>**（誰とも被らなければ
    /// 勝ちで、複数人が同時に勝ちうるので順位が定義できない）。
    ///
    /// 実際の残高移動は <c>MoneyModel</c>、順位付けは <see cref="MiniGameRanking.RanksOf"/>（オンライン）と
    /// 各ミニゲームの順位表（一人用）が担う。<c>MoneyCellRule</c> と同じく、
    /// ルールの数字はここ 1 か所に置いてマスの説明文（<c>BoardEventDescription</c>）もここから組み立てる。
    /// </summary>
    public static class MiniGamePrize
    {
        /// <summary>1 位から順の賞金。並びの長さより下の順位は 0。</summary>
        private static readonly int[] ByRank = { 500, 300, 100 };

        /// <summary>順位が付かないゲーム（被っちゃやーよ）で勝ったときの賞金。1 位と同額。</summary>
        public const int Win = 500;

        /// <summary>賞金が出る順位の数（＝ここまでの順位ならもらえる）。説明文の組み立てに使う。</summary>
        public static int PaidRanks => ByRank.Length;

        /// <summary>
        /// 1 始まりの順位 <paramref name="rank"/> の賞金。圏外（0 以下＝順位なし）と
        /// <see cref="PaidRanks"/> より下の順位は 0。
        /// </summary>
        public static int ForRank(int rank)
        {
            return rank >= 1 && rank <= ByRank.Length ? ByRank[rank - 1] : 0;
        }

        /// <summary>
        /// <paramref name="game"/> に順位が付くか（＝順位別の賞金を配るか）。
        /// 被っちゃやーよだけは「誰とも被らなければ勝ち」で順位が定義できないので false。
        /// </summary>
        public static bool HasRanking(MiniGameId game)
        {
            return game != MiniGameId.Overlap;
        }

        /// <summary>
        /// 順位と賞金を並べた文言（「1位 500 / 2位 300 / 3位 100 / 4位 0」）。
        /// ミニゲームマスの説明（<c>BoardEventDescription</c>）とミニゲームアイテムの説明（<c>ItemCatalog</c>）が
        /// 同じ文言を使えるようにここへ置く（賞金額を変えれば説明文も一緒に変わる）。
        /// 賞金の出る順位に「その次の順位（＝0 円）」まで並べるのは、「それ以下は 0」と書くより
        /// 4 人対戦の全順位がそのまま並ぶほうが読み取りやすいため。
        /// </summary>
        public static string RankPrizeText()
        {
            int lastRank = PaidRanks + 1;
            string[] parts = new string[lastRank];
            for (int rank = 1; rank <= lastRank; rank++)
            {
                parts[rank - 1] = $"{rank}位 {ForRank(rank)}";
            }
            return string.Join(" / ", parts);
        }
    }
}
