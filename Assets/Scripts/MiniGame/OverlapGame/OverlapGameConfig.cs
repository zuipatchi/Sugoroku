namespace MiniGame.OverlapGame
{
    /// <summary>
    /// 「被っちゃやーよ」の数値パラメータ。提示するアイテム枚数は参加者数（<see cref="OverlapGameModel"/> の
    /// playerCount）と一致させる。
    /// </summary>
    public static class OverlapGameConfig
    {
        /// <summary>
        /// 参加者数のフォールバック値（一人用モードの [Human, Cpu] ＝ 2 人）。
        /// 実際の人数は起動側が <c>MiniGameSessionModel.PlayerCount</c> へ渡し（本番の盤面ミニゲームは 2 固定、
        /// MiniGameTest シーンはステッパーで 2〜8 を選ぶ）、<see cref="OverlapGamePlay"/> がそれを使う。
        /// セッションに人数が入っていない（0 以下の）ときだけこの値へ戻す。
        /// </summary>
        public const int DefaultPlayerCount = 2;

        /// <summary>
        /// アイテムを選べる制限時間（秒）。カウントの間にクリックできなかったプレイヤーは
        /// 無効票（未選択）となり獲得できない（<see cref="OverlapGameModel.TimeOut"/>）。
        /// </summary>
        public const float ChoiceSeconds = 3f;
    }
}
