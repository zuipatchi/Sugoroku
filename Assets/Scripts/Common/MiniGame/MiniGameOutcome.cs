using System;
using System.Collections.Generic;

namespace Common.MiniGame
{
    /// <summary>
    /// ミニゲーム 1 回分をプレイし終えたときに、ゲーム側（<c>MiniGame</c> シーンの GamePlay）が返す結果。
    /// <see cref="MiniGameResult"/> は起動側（盤面）が受け取る形で、こちらはゲーム側が組み立てる形。
    /// </summary>
    public readonly struct MiniGameOutcome
    {
        private static readonly int[] NoRanks = Array.Empty<int>();

        public MiniGameOutcome(int score, int value, IReadOnlyList<int> ranks = null)
        {
            Score = score;
            Value = value;
            Ranks = ranks ?? NoRanks;
        }

        /// <summary>自分の勝敗（勝ち=1／負け=0）。一人用モードの勝敗判定に使う。</summary>
        public int Score { get; }

        /// <summary>
        /// 自分の生の結果値（連打数・ゴールタイム ms・選んだカード index）。
        /// オンライン対戦では全員ぶんを持ち寄って <see cref="MiniGameRanking"/> にかける。
        /// </summary>
        public int Value { get; }

        /// <summary>
        /// 参加者ごとの 1 始まりの順位（index＝参加者・0＝自分）。順位が付かない参加者は 0（圏外）。
        /// **一人用モードで順位別の賞金を配るのに使う**（相手はゲーム内の CPU なので、
        /// 全員ぶんの順位を知っているのはゲーム側だけ。オンラインは各自の
        /// <see cref="Value"/> を持ち寄って盤面側が <see cref="MiniGameRanking.RanksOf"/> で出す）。
        /// 順位が定義できないゲーム（被っちゃやーよ）は空。
        /// </summary>
        public IReadOnlyList<int> Ranks { get; }
    }
}
