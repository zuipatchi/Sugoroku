namespace Main.Online
{
    /// <summary>
    /// ゲームを進める「決定」の種別。手番の人（あるいは着地した人・アイテムを使った人）が決めて
    /// <see cref="OnlineGameSync.Publish"/> し、全クライアントが受信した順に適用する。
    /// コマ移動・陣地占拠・勝敗判定は「誰が何マス進むか」と盤面から決定論的に導けるため送らない。
    /// </summary>
    public enum GameActionType
    {
        /// <summary>
        /// ルーレットの停止位置が確定した（＝手番の人が押下を離した）。
        /// 引数 0 = 停止セクター index、引数 1 = 減速時間（ミリ秒）。
        /// 円盤が止まる**前**に配られるので、受信側も同じタイミングで減速に入れる。
        /// </summary>
        Spin = 0,

        /// <summary>お金マスへの着地。引数 0 = 所持金の増減額（符号付き）。</summary>
        MoneyLanding = 1,

        /// <summary>アイテムショップの購入結果。引数 0 = 買った <see cref="Item.ItemId"/>（買わなかったら負値）。</summary>
        ShopResult = 2,

        /// <summary>アイテム使用。引数 0 = <see cref="Item.ItemId"/>、引数 1 以降 = 効果パラメータ。</summary>
        ItemUse = 3,

        /// <summary>退出通知（対戦の続行が不可能になったことを伝える）。</summary>
        Leave = 4,

        /// <summary>
        /// ルーレットを回し始めた（手番の人が押した／CPU が回し出した）合図。引数なし。
        /// 受け取ったクライアントは自分の円盤も回し始め、<see cref="Spin"/> が届くまで回し続ける
        /// （相手が回している間こちらの画面が止まって見えるのを防ぐ）。
        /// </summary>
        SpinStart = 5,

        /// <summary>
        /// 待機表示の切り替え。引数 0 = <see cref="BusyReason"/>（<see cref="BusyReason.None"/> なら解除）。
        /// 盤面を進める決定ではなく「いま自分の手元で時間のかかる操作をしている」というお知らせで、
        /// モーダルやミニゲームを開く前に配って、他のクライアントに待機表示を出してもらう。
        /// </summary>
        Busy = 6,

        /// <summary>
        /// ミニゲームマスへの着地。引数 0 = ゲームの内容を組み立てる種。
        /// 着地した人が種を配り、**全員が同じ内容のミニゲームを同時に遊ぶ**（オンライン）。
        /// 遊ぶゲームの種類はマスのデータから全員が導けるので配らない。
        /// </summary>
        MiniGameLanding = 7,

        /// <summary>
        /// ミニゲームの自分の結果値。引数 0 = 生の結果値（連打数・ゴールタイム・選んだカード index）。
        /// 全参加者ぶんが揃ったら <see cref="Common.MiniGame.MiniGameRanking"/> で勝者を決めるので、
        /// 誰かが判定役になる必要は無い（全クライアントが同じ結論に至る）。
        /// </summary>
        MiniGameScore = 8,
    }
}
