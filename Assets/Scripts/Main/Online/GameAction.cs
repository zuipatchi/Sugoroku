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

        public GameAction(GameActionType type, int seat, int[] args)
        {
            Type = type;
            Seat = seat;
            _args = args ?? EmptyArgs;
        }

        /// <summary>アクションの種別。</summary>
        public GameActionType Type { get; }

        /// <summary>このアクションを発行した席（参加者 index）。</summary>
        public int Seat { get; }

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

        /// <summary>ルーレットが止まったセクター index（<see cref="GameActionType.Spin"/>）。</summary>
        public int Sector => ArgAt(0, -1);

        /// <summary>お金マスの増減額（<see cref="GameActionType.MoneyLanding"/>）。</summary>
        public int MoneyDelta => ArgAt(0);

        /// <summary>買ったアイテム（<see cref="GameActionType.ShopResult"/>）。負値なら買わなかった。</summary>
        public int ShopItemId => ArgAt(0, -1);

        /// <summary>使ったアイテム（<see cref="GameActionType.ItemUse"/>）。</summary>
        public int UsedItemId => ArgAt(0, -1);

        /// <summary>アイテム効果のパラメータ数（<see cref="GameActionType.ItemUse"/>）。</summary>
        public int EffectArgCount => ArgCount > 0 ? ArgCount - 1 : 0;

        /// <summary>アイテム効果のパラメータ <paramref name="index"/>（<see cref="GameActionType.ItemUse"/>）。</summary>
        public int EffectArgAt(int index, int fallback = 0)
        {
            return ArgAt(index + 1, fallback);
        }

        /// <summary>ルーレットの停止（<paramref name="sector"/> = 止まったセクター index）。</summary>
        public static GameAction Spin(int seat, int sector)
        {
            return new GameAction(GameActionType.Spin, seat, new[] { sector });
        }

        /// <summary>お金マスの着地（<paramref name="delta"/> = 符号付きの増減額）。</summary>
        public static GameAction MoneyLanding(int seat, int delta)
        {
            return new GameAction(GameActionType.MoneyLanding, seat, new[] { delta });
        }

        /// <summary>アイテムショップの結果（<paramref name="itemId"/> が負なら買わなかった）。</summary>
        public static GameAction ShopResult(int seat, int itemId)
        {
            return new GameAction(GameActionType.ShopResult, seat, new[] { itemId });
        }

        /// <summary>
        /// アイテム使用（<paramref name="itemId"/> と効果パラメータ <paramref name="effectArgs"/>）。
        /// 効果パラメータの意味はアイテムごとに決まる（陣地獲得＝対象マス index、
        /// お金よこどり＝席ごとの奪取額、ミニゲーム＝所持金報酬）。
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

        /// <summary>退出通知。</summary>
        public static GameAction Leave(int seat)
        {
            return new GameAction(GameActionType.Leave, seat, EmptyArgs);
        }
    }
}
