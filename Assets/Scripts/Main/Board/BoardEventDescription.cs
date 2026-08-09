using Common.MiniGame;
using Main.Money;

namespace Main.Board
{
    /// <summary>
    /// 盤面イベントの説明文（ランタイム共通）。マスをタップしたときに開く説明モーダル
    /// （<see cref="BoardCellInfoPresenter"/>）が使う単一の情報源で、表示名は <see cref="BoardEventLabel"/> が持つ。
    /// 金額・マス数のような「ルール側で決まっている数値」は各ルールの定数（<see cref="MoneyCellRule"/> など）から
    /// 組み立てるので、ルールを変えれば説明文も一緒に変わる。
    /// </summary>
    public static class BoardEventDescription
    {
        /// <summary>スタート＝ゴール（経路 index 0）の表示名。イベント種別ではなく位置で決まる。</summary>
        public const string StartLabel = "スタート／ゴール";

        /// <summary>スタート＝ゴールの説明。</summary>
        public const string StartDescription =
            "スタート地点。ここを通過しても止まらず、そのまま盤面を回り続ける。";

        /// <summary>
        /// マスの説明文。**文章はどれも「このマスに止まると、〜」で始める**。
        /// 進む／戻るマス数・お金の増減額・ミニゲームの賞金はマスごとの設定ではなくルール側の定数
        /// （<see cref="MoveCellRule"/> / <see cref="MoneyCellRule"/> / <see cref="MiniGamePrize"/>）から
        /// 組み立てるので引数に取らない（遊ぶミニゲームも着地のたびの抽選なのでマスからは決まらない）。
        /// </summary>
        public static string Of(BoardCellEvent cellEvent)
        {
            switch (cellEvent)
            {
                case BoardCellEvent.Forward:
                    return $"このマスに止まると、ランダムで {MoveRangeText()} マス進む。";
                case BoardCellEvent.Back:
                    return $"このマスに止まると、ランダムで {MoveRangeText()} マス戻る。";
                case BoardCellEvent.MiniGame:
                    return "このマスに止まると、ランダムに選ばれたミニゲームに挑戦する。" + PrizeText();
                case BoardCellEvent.MoneyUp:
                    return $"このマスに止まると、所持金がランダムに {MoneyRangeText()} 増える。";
                case BoardCellEvent.MoneyDown:
                    return $"このマスに止まると、所持金がランダムに {MoneyRangeText()} 減る（0 より下がることはない）。";
                case BoardCellEvent.Territory:
                    return "このマスに止まると、自分の陣地になる。相手の陣地なら上書きして奪う。";
                case BoardCellEvent.Item:
                    return "このマスに止まると、アイテムショップに訪れる。";
                default:
                    return "特に何も起こらないマス。";
            }
        }

        // ミニゲームの賞金の説明（ルール側＝MiniGamePrize から組み立てる）。
        // 遊ぶゲームは着地のたびの抽選なので、どのゲームでも共通の「順位別」だけを書く
        // （順位が定義できない被っちゃやーよは勝てば 1 位と同額なので、この説明から外れない）。
        private static string PrizeText()
        {
            // 賞金の出る順位に「その次の順位（＝0 円）」まで並べる。「それ以下は 0」と書くより、
            // 4 人対戦の全順位がそのまま並ぶほうが読み取りやすい。
            int lastRank = MiniGamePrize.PaidRanks + 1;
            string[] parts = new string[lastRank];
            for (int rank = 1; rank <= lastRank; rank++)
            {
                parts[rank - 1] = $"{rank}位 {MiniGamePrize.ForRank(rank)}";
            }
            return $"順位に応じて所持金がもらえる（{string.Join(" / ", parts)}）。";
        }

        // お金マスの増減額の範囲（ルール側の定数から組み立てる）。
        private static string MoneyRangeText()
        {
            return $"{MoneyCellRule.Unit * MoneyCellRule.MinN}〜{MoneyCellRule.Unit * MoneyCellRule.MaxN}";
        }

        // 進む／戻るマスで動くマス数の範囲（ルール側の定数から組み立てる）。
        private static string MoveRangeText()
        {
            return $"{MoveCellRule.MinSteps}〜{MoveCellRule.MaxSteps}";
        }
    }
}
