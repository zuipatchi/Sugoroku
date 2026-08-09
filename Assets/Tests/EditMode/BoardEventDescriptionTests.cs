using System;
using Common.MiniGame;
using Main.Board;
using Main.Money;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class BoardEventDescriptionTests
    {
        [Test]
        public void すべてのイベントに説明文がある()
        {
            foreach (BoardCellEvent cellEvent in Enum.GetValues(typeof(BoardCellEvent)))
            {
                string description = BoardEventDescription.Of(cellEvent);
                Assert.IsFalse(string.IsNullOrWhiteSpace(description), $"{cellEvent} の説明文が空です。");
            }
        }

        [Test]
        public void 進むと戻るはルールの動くマス数の範囲を説明に含む()
        {
            string forward = BoardEventDescription.Of(BoardCellEvent.Forward);
            StringAssert.Contains(MoveCellRule.MinSteps.ToString(), forward);
            StringAssert.Contains(MoveCellRule.MaxSteps.ToString(), forward);
            StringAssert.Contains("進む", forward);

            string back = BoardEventDescription.Of(BoardCellEvent.Back);
            StringAssert.Contains(MoveCellRule.MinSteps.ToString(), back);
            StringAssert.Contains(MoveCellRule.MaxSteps.ToString(), back);
            StringAssert.Contains("戻る", back);
        }

        [Test]
        public void 進むと戻るは毎回変わることを説明に含む()
        {
            // マスごとの固定値だと誤解されないよう、ランダムである旨を必ず書く（お金マスと同じ）。
            StringAssert.Contains("ランダム", BoardEventDescription.Of(BoardCellEvent.Forward));
            StringAssert.Contains("ランダム", BoardEventDescription.Of(BoardCellEvent.Back));
        }

        [Test]
        public void お金マスはルールの増減額の範囲を説明に含む()
        {
            string up = BoardEventDescription.Of(BoardCellEvent.MoneyUp);
            StringAssert.Contains((MoneyCellRule.Unit * MoneyCellRule.MinN).ToString(), up);
            StringAssert.Contains((MoneyCellRule.Unit * MoneyCellRule.MaxN).ToString(), up);
            StringAssert.Contains("増える", up);
            StringAssert.Contains("減る", BoardEventDescription.Of(BoardCellEvent.MoneyDown));
        }

        [Test]
        public void ミニゲームマスはランダムであることと順位別の賞金を説明に含む()
        {
            // 遊ぶゲームは着地のたびの抽選なので、説明にゲーム名は出せない（賞金の表だけ書く）。
            string description = BoardEventDescription.Of(BoardCellEvent.MiniGame);
            StringAssert.Contains("ランダム", description);
            for (int rank = 1; rank <= MiniGamePrize.PaidRanks; rank++)
            {
                StringAssert.Contains(MiniGamePrize.ForRank(rank).ToString(), description);
            }
        }

        [Test]
        public void 説明文はイベントごとに違う()
        {
            // 説明を足し忘れて既定の文言のままになっていないか（None 以外が既定と同じなら足し忘れ）。
            string none = BoardEventDescription.Of(BoardCellEvent.None);
            foreach (BoardCellEvent cellEvent in Enum.GetValues(typeof(BoardCellEvent)))
            {
                if (cellEvent == BoardCellEvent.None)
                {
                    continue;
                }
                Assert.AreNotEqual(
                    none,
                    BoardEventDescription.Of(cellEvent),
                    $"{cellEvent} の説明文が通常マスと同じです。");
            }
        }
    }
}
