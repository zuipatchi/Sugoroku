using System;
using System.Collections.Generic;

namespace Common.MiniGame
{
    /// <summary>
    /// ミニゲーム 1 回分の結果。
    /// <see cref="Score"/> は勝敗（勝ち=1／負け=0）で、一人用モードの勝敗判定に使う。
    /// <see cref="Value"/> は生の結果値（連打数・ゴールタイム・選んだアイテムの <c>ItemId</c>）で、
    /// オンライン対戦では全員ぶんを集めて <see cref="MiniGameRanking.Resolve"/> にかけ、勝者を決める。
    /// <see cref="Ranks"/> は参加者ごとの順位で、一人用モードの順位別の賞金に使う。
    /// </summary>
    public readonly struct MiniGameResult
    {
        private static readonly int[] NoRanks = Array.Empty<int>();

        public MiniGameId Game { get; }
        public int Score { get; }
        public int Value { get; }

        /// <summary>
        /// 参加者ごとの 1 始まりの順位（index＝参加者・0＝自分／順位が付かない参加者は 0）。
        /// 相手をシミュレートする一人用モードでは、CPU の順位を知っているのがゲーム側だけなので
        /// ここで運ぶ（<see cref="MiniGameOutcome.Ranks"/>）。順位が定義できないゲームは空。
        /// </summary>
        public IReadOnlyList<int> Ranks { get; }

        public MiniGameResult(MiniGameId game, int score)
            : this(game, score, score)
        {
        }

        public MiniGameResult(MiniGameId game, int score, int value, IReadOnlyList<int> ranks = null)
        {
            Game = game;
            Score = score;
            Value = value;
            Ranks = ranks ?? NoRanks;
        }
    }
}
