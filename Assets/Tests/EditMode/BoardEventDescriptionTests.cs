using System;
using Common.MiniGame;
using Main.Board;
using Main.Money;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class BoardEventDescriptionTests
    {
        private const int Reward = 500;

        [Test]
        public void すべてのイベントに説明文がある()
        {
            foreach (BoardCellEvent cellEvent in Enum.GetValues(typeof(BoardCellEvent)))
            {
                string description = BoardEventDescription.Of(cellEvent, MiniGameId.Tap, Reward);
                Assert.IsFalse(string.IsNullOrWhiteSpace(description), $"{cellEvent} の説明文が空です。");
            }
        }

        [Test]
        public void 進むと戻るはルールの動くマス数の範囲を説明に含む()
        {
            string forward = BoardEventDescription.Of(BoardCellEvent.Forward, MiniGameId.Tap, Reward);
            StringAssert.Contains(MoveCellRule.MinSteps.ToString(), forward);
            StringAssert.Contains(MoveCellRule.MaxSteps.ToString(), forward);
            StringAssert.Contains("進む", forward);

            string back = BoardEventDescription.Of(BoardCellEvent.Back, MiniGameId.Tap, Reward);
            StringAssert.Contains(MoveCellRule.MinSteps.ToString(), back);
            StringAssert.Contains(MoveCellRule.MaxSteps.ToString(), back);
            StringAssert.Contains("戻る", back);
        }

        [Test]
        public void 進むと戻るは毎回変わることを説明に含む()
        {
            // マスごとの固定値だと誤解されないよう、ランダムである旨を必ず書く（お金マスと同じ）。
            StringAssert.Contains("ランダム", BoardEventDescription.Of(BoardCellEvent.Forward, MiniGameId.Tap, Reward));
            StringAssert.Contains("ランダム", BoardEventDescription.Of(BoardCellEvent.Back, MiniGameId.Tap, Reward));
        }

        [Test]
        public void お金マスはルールの増減額の範囲を説明に含む()
        {
            string up = BoardEventDescription.Of(BoardCellEvent.MoneyUp, MiniGameId.Tap, Reward);
            StringAssert.Contains((MoneyCellRule.Unit * MoneyCellRule.MinN).ToString(), up);
            StringAssert.Contains((MoneyCellRule.Unit * MoneyCellRule.MaxN).ToString(), up);
            StringAssert.Contains("増える", up);
            StringAssert.Contains("減る", BoardEventDescription.Of(BoardCellEvent.MoneyDown, MiniGameId.Tap, Reward));
        }

        [Test]
        public void ミニゲームマスは遊ぶゲーム名と報酬額を説明に含む()
        {
            string description = BoardEventDescription.Of(BoardCellEvent.MiniGame, MiniGameId.Race, Reward);
            StringAssert.Contains(MiniGameCatalog.Find(MiniGameId.Race).DisplayName, description);
            StringAssert.Contains(Reward.ToString(), description);
        }

        [Test]
        public void 説明文はイベントごとに違う()
        {
            // 説明を足し忘れて既定の文言のままになっていないか（None 以外が既定と同じなら足し忘れ）。
            string none = BoardEventDescription.Of(BoardCellEvent.None, MiniGameId.Tap, Reward);
            foreach (BoardCellEvent cellEvent in Enum.GetValues(typeof(BoardCellEvent)))
            {
                if (cellEvent == BoardCellEvent.None)
                {
                    continue;
                }
                Assert.AreNotEqual(
                    none,
                    BoardEventDescription.Of(cellEvent, MiniGameId.Tap, Reward),
                    $"{cellEvent} の説明文が通常マスと同じです。");
            }
        }
    }
}
