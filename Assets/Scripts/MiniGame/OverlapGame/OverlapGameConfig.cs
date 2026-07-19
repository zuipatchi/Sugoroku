namespace MiniGame.OverlapGame
{
    /// <summary>
    /// 「被っちゃやーよ」の数値パラメータ。提示するアイテム枚数は参加者数（<see cref="OverlapGameModel"/> の
    /// playerCount）と一致させる。
    /// </summary>
    public static class OverlapGameConfig
    {
        /// <summary>
        /// 参加者数の既定値。現状は一人用モードの [Human, Cpu] ＝ 2 人に合わせる。
        /// 将来プレイヤー数が増えたら <c>GameParticipants.Count</c> をここへ供給する
        /// （MiniGame シーンは別スコープで <c>GameParticipants</c> を直接注入できないため、
        /// セッション経由で渡すなどの連携が必要になる）。
        /// </summary>
        public const int DefaultPlayerCount = 2;

        /// <summary>
        /// アイテムを選べる制限時間（秒）。カウントの間にクリックできなかったプレイヤーは
        /// 無効票（未選択）となり獲得できない（<see cref="OverlapGameModel.TimeOut"/>）。
        /// </summary>
        public const float ChoiceSeconds = 3f;
    }
}
