using System;

namespace Main.Online
{
    /// <summary>
    /// ゲームを進める 1 つの決定。種別（<see cref="Type"/>）・発行した席（<see cref="Seat"/>）・
    /// 種別ごとの整数パラメータ（<see cref="ArgAt"/>）だけを持つ値型で、
    /// <see cref="GameActionCodec"/> で JSON にして全クライアントへ配る。
    /// 生成は種別ごとの静的ファクトリ（<see cref="Spin"/> など）を使い、読み出しは
    /// 名前付きプロパティ（<see cref="Sector"/> など）を使って引数の意味を呼び出し側に散らさない。
    /// </summary>
    public readonly struct GameAction
    {
        private static readonly int[] EmptyArgs = Array.Empty<int>();

        private readonly int[] _args;

        public GameAction(GameActionType type, int seat, int[] args, int seq = NoSeq)
        {
            Type = type;
            Seat = seat;
            _args = args ?? EmptyArgs;
            Seq = seq;
        }

        /// <summary>まだ通し番号が振られていないことを表す値（発行時＝ホストが配る前）。</summary>
        public const int NoSeq = 0;

        /// <summary>アクションの種別。</summary>
        public GameActionType Type { get; }

        /// <summary>このアクションを発行した席（参加者 index）。</summary>
        public int Seat { get; }

        /// <summary>
        /// ホストが再配信するときに振る通し番号（1 始まり・未採番は <see cref="NoSeq"/>）。
        /// 受信側は「どこまで受け取ったか」をこの値で覚えておき、再接続したときに
        /// <see cref="GameActionType.Resync"/> で申告して取りこぼしを送り直してもらう。
        /// </summary>
        public int Seq { get; }

        /// <summary>通し番号だけ差し替えた複製（採番はホストの再配信時に 1 度だけ行う）。</summary>
        public GameAction WithSeq(int seq)
        {
            return new GameAction(Type, Seat, _args, seq);
        }

        /// <summary>パラメータの個数。</summary>
        public int ArgCount => _args?.Length ?? 0;

        /// <summary>パラメータ <paramref name="index"/>（範囲外なら <paramref name="fallback"/>）。</summary>
        public int ArgAt(int index, int fallback = 0)
        {
            if (_args == null || index < 0 || index >= _args.Length)
            {
                return fallback;
            }
            return _args[index];
        }

        /// <summary>パラメータの複製（シリアライズ用）。</summary>
        public int[] ArgsCopy()
        {
            if (_args == null || _args.Length == 0)
            {
                return EmptyArgs;
            }
            int[] copy = new int[_args.Length];
            Array.Copy(_args, copy, _args.Length);
            return copy;
        }

        /// <summary>ルーレットが止まるセクター index（<see cref="GameActionType.Spin"/>）。</summary>
        public int Sector => ArgAt(0, -1);

        /// <summary>
        /// ルーレットの減速時間（ミリ秒・<see cref="GameActionType.Spin"/>）。
        /// 受信側も同じ時間で減速させて、全員の円盤がほぼ同時に止まるようにする。
        /// </summary>
        public int StopMillis => ArgAt(1);

        /// <summary>お金マスの増減額（<see cref="GameActionType.MoneyLanding"/>）。</summary>
        public int MoneyDelta => ArgAt(0);

        /// <summary>
        /// 進む／戻るマスで続けて動くマス数（<see cref="GameActionType.MoveLanding"/>）。進む＝正・戻る＝負。
        /// </summary>
        public int MoveSteps => ArgAt(0);

        /// <summary>買ったアイテム（<see cref="GameActionType.ShopResult"/>）。負値なら買わなかった。</summary>
        public int ShopItemId => ArgAt(0, -1);

        /// <summary>使ったアイテム（<see cref="GameActionType.ItemUse"/>）。</summary>
        public int UsedItemId => ArgAt(0, -1);

        /// <summary>ミニゲームの内容を組み立てる種（<see cref="GameActionType.MiniGameLanding"/>）。</summary>
        public int MiniGameSeed => ArgAt(0);

        /// <summary>ミニゲームの生の結果値（<see cref="GameActionType.MiniGameScore"/>）。</summary>
        public int MiniGameValue => ArgAt(0);

        /// <summary>待機表示の理由（<see cref="GameActionType.Busy"/>）。<see cref="BusyReason"/> の値。</summary>
        public int BusyReasonId => ArgAt(0);

        /// <summary>復帰を待つ残り秒数（<see cref="GameActionType.Pause"/>）。</summary>
        public int GraceSeconds => ArgAt(0);

        /// <summary>復帰したクライアントが受信済みの最後の seq（<see cref="GameActionType.Resync"/>）。</summary>
        public int LastSeq => ArgAt(0);

        /// <summary>アイテム効果のパラメータ数（<see cref="GameActionType.ItemUse"/>）。</summary>
        public int EffectArgCount => ArgCount > 0 ? ArgCount - 1 : 0;

        /// <summary>アイテム効果のパラメータ <paramref name="index"/>（<see cref="GameActionType.ItemUse"/>）。</summary>
        public int EffectArgAt(int index, int fallback = 0)
        {
            return ArgAt(index + 1, fallback);
        }

        /// <summary>
        /// ルーレットの停止位置の確定（<paramref name="sector"/> = 止まるセクター index、
        /// <paramref name="stopMillis"/> = 減速時間）。押下を離した時点で発行するので円盤はまだ回っている。
        /// </summary>
        public static GameAction Spin(int seat, int sector, int stopMillis = 0)
        {
            return new GameAction(GameActionType.Spin, seat, new[] { sector, stopMillis });
        }

        /// <summary>ルーレットを回し始めた合図（受信側も自分の円盤を回し始める）。</summary>
        public static GameAction SpinStart(int seat)
        {
            return new GameAction(GameActionType.SpinStart, seat, EmptyArgs);
        }

        /// <summary>お金マスの着地（<paramref name="delta"/> = 符号付きの増減額）。</summary>
        public static GameAction MoneyLanding(int seat, int delta)
        {
            return new GameAction(GameActionType.MoneyLanding, seat, new[] { delta });
        }

        /// <summary>
        /// 進む／戻るマスへの着地（<paramref name="steps"/> = 符号付きの移動マス数・進む＝正／戻る＝負）。
        /// マス数は着地のたびのランダムなので、盤面データからは導けず配る必要がある。
        /// </summary>
        public static GameAction MoveLanding(int seat, int steps)
        {
            return new GameAction(GameActionType.MoveLanding, seat, new[] { steps });
        }

        /// <summary>アイテムショップの結果（<paramref name="itemId"/> が負なら買わなかった）。</summary>
        public static GameAction ShopResult(int seat, int itemId)
        {
            return new GameAction(GameActionType.ShopResult, seat, new[] { itemId });
        }

        /// <summary>
        /// アイテム使用（<paramref name="itemId"/> と効果パラメータ <paramref name="effectArgs"/>）。
        /// 効果パラメータの意味はアイテムごとに決まる（陣地獲得＝対象マス index、
        /// お金よこどり＝席ごとの奪取額、ミニゲーム＝遊ぶゲーム＋内容を組み立てる種）。
        /// </summary>
        public static GameAction ItemUse(int seat, int itemId, params int[] effectArgs)
        {
            int extra = effectArgs?.Length ?? 0;
            int[] args = new int[extra + 1];
            args[0] = itemId;
            for (int i = 0; i < extra; i++)
            {
                args[i + 1] = effectArgs[i];
            }
            return new GameAction(GameActionType.ItemUse, seat, args);
        }

        /// <summary>ミニゲームマスへの着地（<paramref name="seed"/> でゲームの内容を全員そろえる）。</summary>
        public static GameAction MiniGameLanding(int seat, int seed)
        {
            return new GameAction(GameActionType.MiniGameLanding, seat, new[] { seed });
        }

        /// <summary>ミニゲームの自分の結果値（<paramref name="value"/> の意味はゲームごと）。</summary>
        public static GameAction MiniGameScore(int seat, int value)
        {
            return new GameAction(GameActionType.MiniGameScore, seat, new[] { value });
        }

        /// <summary>
        /// 待機表示の切り替え（<paramref name="reason"/> が <see cref="BusyReason.None"/> なら解除）。
        /// 盤面は進めないので、受信側は表示を切り替えて次のアクションを待ち続ける。
        /// </summary>
        public static GameAction Busy(int seat, BusyReason reason)
        {
            return new GameAction(GameActionType.Busy, seat, new[] { (int)reason });
        }

        /// <summary>退出通知。</summary>
        public static GameAction Leave(int seat)
        {
            return new GameAction(GameActionType.Leave, seat, EmptyArgs);
        }

        /// <summary>
        /// 席 <paramref name="seat"/> の切断による一時停止（<paramref name="graceSeconds"/> = 復帰を待つ残り秒数）。
        /// 制御メッセージなのでアクションストリームには載らない。
        /// </summary>
        public static GameAction Pause(int seat, int graceSeconds)
        {
            return new GameAction(GameActionType.Pause, seat, new[] { graceSeconds });
        }

        /// <summary>席 <paramref name="seat"/> の復帰による一時停止の解除（制御メッセージ）。</summary>
        public static GameAction Resume(int seat)
        {
            return new GameAction(GameActionType.Resume, seat, EmptyArgs);
        }

        /// <summary>
        /// 復帰したクライアントの「seq <paramref name="lastSeq"/> まで受け取った」申告（制御メッセージ）。
        /// </summary>
        public static GameAction Resync(int seat, int lastSeq)
        {
            return new GameAction(GameActionType.Resync, seat, new[] { lastSeq });
        }
    }
}
