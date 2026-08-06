using System;
using System.Collections.Generic;

namespace Main.Board
{
    /// <summary>
    /// マスに止まったときに見せる文言（フレーバーテキスト）のカタログ（ランタイム共通）。
    /// イベント種別ごとに文言をいくつか用意しておき、着地のたびに 1 つをランダムに選んで
    /// <see cref="BoardPresenter"/> がマス画像の下に表示する。
    ///
    /// 文言はマスごとの設定ではなくイベント種別ごとに全マップ共通で持つ（マスの画像を種別ごとに解決する
    /// <see cref="BoardEventArtCatalog"/>・説明文の <see cref="BoardEventDescription"/> と同じ静的カタログ方式）。
    /// 文言を足すときは該当プールの配列に 1 行足すだけでよい。
    ///
    /// 抽選は各クライアントがローカルに行う（見た目だけで盤面の状態には影響しないので、オンラインでも
    /// アクションストリームに載せて配る必要がない）。乱数源は呼び出し側が渡し、null なら先頭を返して
    /// 決定的にふるまう（<see cref="Money.MoneyCellRule"/> と同じ規約）。
    /// </summary>
    public static class BoardCellMessageCatalog
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

        /// <summary>スタート＝ゴール（経路 index 0）の文言プール。</summary>
        public static IReadOnlyList<string> StartMessages => StartPool;

        /// <summary>イベント <paramref name="cellEvent"/> の文言プール（未定義の種別は通常マスのプール）。</summary>
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
        /// 着地したマスで見せる文言を 1 つ選ぶ。<paramref name="isStart"/> が true（経路 index 0）なら
        /// イベント種別に依らずスタート専用プールから選ぶ。プールが空のときだけ null を返す。
        /// 乱数源 <paramref name="rng"/> が null のときはプールの先頭を返す（決定的）。
        /// </summary>
        public static string Pick(BoardCellEvent cellEvent, bool isStart, Random rng)
        {
            IReadOnlyList<string> pool = isStart ? StartMessages : Messages(cellEvent);
            if (pool == null || pool.Count == 0)
            {
                return null;
            }
            return pool[rng == null ? 0 : rng.Next(pool.Count)];
        }
    }
}
