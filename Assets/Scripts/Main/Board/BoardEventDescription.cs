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
        /// マスの説明文。<paramref name="game"/> はミニゲームマスで遊ぶゲーム。
        /// 進む／戻るマス数・お金の増減額・ミニゲームの賞金はマスごとの設定ではなくルール側の定数
        /// （<see cref="MoveCellRule"/> / <see cref="MoneyCellRule"/> / <see cref="MiniGamePrize"/>）から
        /// 組み立てるので引数に取らない。
        /// </summary>
        public static string Of(BoardCellEvent cellEvent, MiniGameId game)
        {
            switch (cellEvent)
            {
                case BoardCellEvent.Forward:
                    return $"このマスに止まると、ランダムで {MoveRangeText()} マス進む。"
                        + "進んだ先のマスの効果もそのまま発動する。";
                case BoardCellEvent.Back:
                    return $"このマスに止まると、ランダムで {MoveRangeText()} マス戻る。"
                        + "戻った先のマスの効果もそのまま発動する。";
                case BoardCellEvent.MiniGame:
                    return $"このマスに止まると、ミニゲーム「{MiniGameCatalog.Find(game).DisplayName}」に挑戦する。"
                        + PrizeText(game);
                case BoardCellEvent.MoneyUp:
                    return $"このマスに止まると、所持金がランダムに {MoneyRangeText()} 増える。";
                case BoardCellEvent.MoneyDown:
                    return $"このマスに止まると、所持金がランダムに {MoneyRangeText()} 減る。";
                case BoardCellEvent.Territory:
                    return "このマスに止まると、自分の陣地になる。相手の陣地なら上書きして奪う。";
                case BoardCellEvent.Item:
                    return "このマスに止まると、アイテムショップに訪れる。";
                default:
                    return "特に何も起こらないマス。";
            }
        }

        // ミニゲームの賞金の説明（ルール側＝MiniGamePrize から組み立てる）。
        // 順位が付くゲームは「1位 500 / 2位 300 …」、順位が定義できないゲームは勝ったときの一律額。
        private static string PrizeText(MiniGameId game)
        {
            if (!MiniGamePrize.HasRanking(game))
            {
                return $"勝つと所持金が {MiniGamePrize.Win} 増える。";
            }

            string[] parts = new string[MiniGamePrize.PaidRanks];
            for (int rank = 1; rank <= MiniGamePrize.PaidRanks; rank++)
            {
                parts[rank - 1] = $"{rank}位 {MiniGamePrize.ForRank(rank)}";
            }
            return $"順位に応じて所持金がもらえる（{string.Join(" / ", parts)}・それ以下は 0）。";
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
