namespace Main.Board
{
    /// <summary>
    /// すごろくのマスに割り当てるイベントの種類。盤面データ（<see cref="BoardDefinition"/>）が
    /// 保持し、盤面エディタで編集する。今は表示のみで、実際の発動（コマ移動・休み・ミニゲーム起動）は
    /// 将来対応する。
    /// </summary>
    public enum BoardCellEvent
    {
        /// <summary>何も起きない通常マス。</summary>
        None = 0,

        /// <summary>止まると N マス進む（N = <see cref="BoardCellDefinition.Amount"/>）。</summary>
        Forward = 1,

        /// <summary>止まると N マス戻る。</summary>
        Back = 2,

        /// <summary>止まると N ターン休み。</summary>
        Rest = 3,

        /// <summary>止まるとミニゲームが発生する。</summary>
        MiniGame = 4
    }
}
