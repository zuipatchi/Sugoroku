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
        /// マスの説明文。<paramref name="game"/> はミニゲームマスで遊ぶゲーム、
        /// <paramref name="miniGameReward"/> はミニゲームに勝ったときの報酬額。
        /// 進む／戻るマス数とお金の増減額はマスごとの設定ではなくルール側の定数から組み立てるので引数に取らない。
        /// </summary>
        public static string Of(BoardCellEvent cellEvent, MiniGameId game, int miniGameReward)
        {
            switch (cellEvent)
            {
                case BoardCellEvent.Forward:
                    return $"止まるとさらに {MoveRangeText()} マス進む（止まるたびにランダム）。"
                        + "進んだ先のマスの効果もそのまま発動する。";
                case BoardCellEvent.Back:
                    return $"止まると {MoveRangeText()} マス戻る（止まるたびにランダム）。"
                        + "戻った先のマスの効果もそのまま発動する。";
                case BoardCellEvent.MiniGame:
                    return $"止まるとミニゲーム「{MiniGameCatalog.Find(game).DisplayName}」に挑戦する。"
                        + $"勝つと所持金が {miniGameReward} 増える。";
                case BoardCellEvent.MoneyUp:
                    return $"止まると所持金が {MoneyRangeText()} 増える（止まるたびにランダム）。";
                case BoardCellEvent.MoneyDown:
                    return $"止まると所持金が {MoneyRangeText()} 減る（止まるたびにランダム）。所持金はマイナスにもなる。";
                case BoardCellEvent.Territory:
                    return "止まると自分の陣地になる。相手の陣地でも上書きして奪える。"
                        + "盤面の陣地マスを決められた数だけ先に集めた人が勝ち。";
                case BoardCellEvent.Item:
                    return "止まるとアイテムショップが開き、所持金でアイテムを買える。"
                        + "買ったアイテムは手札に入り、自分の手番に使える。";
                default:
                    return "特に何も起こらないマス。";
            }
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
