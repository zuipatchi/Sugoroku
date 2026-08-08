using System.Collections.Generic;

namespace Main.Board
{
    /// <summary>
    /// マスに止まったときに見せる文言（フレーバーテキスト）の**既定値**。
    ///
    /// 文言の編集は <see cref="BoardCellMessageCatalog"/> 資産（Cell Message Editor）で行うのが本筋で、
    /// ここは「資産をまだ作っていない／割り当てていない」ときのフォールバックと、
    /// 資産の新規作成時に流し込む初期値の 2 役を持つ（資産を割り当てたらそちらが唯一の情報源になり、
    /// ここは一切参照されない）。
    ///
    /// 文言はマスごとの設定ではなくイベント種別ごとに全マップ共通で持つ（マスの画像を種別ごとに解決する
    /// <see cref="BoardEventArtCatalog"/>・説明文の <see cref="BoardEventDescription"/> と同じ方式）。
    /// </summary>
    public static class BoardCellMessageDefaults
    {
        // スタート＝ゴール（経路 index 0）はイベント種別ではなく位置で決まるので専用プールを持つ
        // （BoardEventDescription.StartDescription と同じ扱い）。
        private static readonly string[] StartPool =
        {
            "スタート地点に帰ってきた！",
            "ここが始まりの場所。",
            "ぐるりと一周してきた。",
            "旅はまだまだ続く。"
        };

        private static readonly string[] NonePool =
        {
            "ひと息ついた。",
            "何事もなく通り過ぎた。",
            "空を見上げた。いい天気だ。",
            "足を止めて深呼吸した。"
        };

        private static readonly string[] ForwardPool =
        {
            "追い風が吹いた！",
            "近道を見つけた！",
            "足が軽い！どんどん行こう！",
            "風に乗ってひとっ飛び！",
            "調子に乗って走り出した！"
        };

        private static readonly string[] BackPool =
        {
            "道を間違えた…",
            "忘れ物を取りに戻った。",
            "石につまずいて転んだ！",
            "来た道を引き返すはめに…",
            "強い向かい風に押し戻された！"
        };

        private static readonly string[] MiniGamePool =
        {
            "勝負の時間だ！",
            "腕の見せどころ！",
            "挑戦者があらわれた！",
            "ここで一勝負といこうか。"
        };

        private static readonly string[] MoneyUpPool =
        {
            "道ばたでお金を拾った！",
            "臨時収入が入った！",
            "宝箱を見つけた！",
            "落とし物を届けてお礼をもらった！",
            "くじが当たった！"
        };

        private static readonly string[] MoneyDownPool =
        {
            "お金を落とした！",
            "財布に穴が空いていた…",
            "うっかり買いすぎてしまった…",
            "通行料を取られた…",
            "スリに狙われた！"
        };

        private static readonly string[] TerritoryPool =
        {
            "ここは私の土地だ！",
            "旗を立てて宣言した！",
            "見晴らしのいい土地を手に入れた！",
            "この場所、いただき！"
        };

        private static readonly string[] ItemPool =
        {
            "掘り出し物があるかも？",
            "いらっしゃいませ！",
            "店主が手招きしている。",
            "何かいい道具はないだろうか。"
        };

        /// <summary>スタート＝ゴール（経路 index 0）の既定文言。</summary>
        public static IReadOnlyList<string> StartMessages => StartPool;

        /// <summary>イベント <paramref name="cellEvent"/> の既定文言（未定義の種別は通常マスのプール）。</summary>
        public static IReadOnlyList<string> Messages(BoardCellEvent cellEvent)
        {
            switch (cellEvent)
            {
                case BoardCellEvent.Forward:
                    return ForwardPool;
                case BoardCellEvent.Back:
                    return BackPool;
                case BoardCellEvent.MiniGame:
                    return MiniGamePool;
                case BoardCellEvent.MoneyUp:
                    return MoneyUpPool;
                case BoardCellEvent.MoneyDown:
                    return MoneyDownPool;
                case BoardCellEvent.Territory:
                    return TerritoryPool;
                case BoardCellEvent.Item:
                    return ItemPool;
                default:
                    return NonePool;
            }
        }

        /// <summary>
        /// 既定文言のプールを引く。<paramref name="isStart"/> が true（経路 index 0）なら
        /// イベント種別に依らずスタート専用プールを返す。
        /// </summary>
        public static IReadOnlyList<string> Pool(BoardCellEvent cellEvent, bool isStart)
        {
            return isStart ? StartMessages : Messages(cellEvent);
        }
    }
}
