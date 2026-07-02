using Common.GameSession;
using Main.Board;
using Main.Turn;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class BoardModelTests
    {
        private static BoardModel TwoPlayerBoard()
        {
            GameSessionModel session = new();
            session.SetSinglePlayer();
            return new BoardModel(new GameParticipants(session));
        }

        [Test]
        public void 初期状態は全コマ0で移動中でも勝者確定でもない()
        {
            using BoardModel board = TwoPlayerBoard();
            Assert.AreEqual(2, board.PlayerCount);
            Assert.AreEqual(0, board.Position(0).CurrentValue);
            Assert.AreEqual(0, board.Position(1).CurrentValue);
            Assert.IsFalse(board.IsMoving.CurrentValue);
            Assert.AreEqual(-1, board.Winner.CurrentValue);
            Assert.IsFalse(board.IsFinished);
        }

        [Test]
        public void SetPositionはプレイヤーごとに独立して更新する()
        {
            using BoardModel board = TwoPlayerBoard();
            board.SetPosition(1, 5);
            Assert.AreEqual(5, board.Position(1).CurrentValue);
            Assert.AreEqual(0, board.Position(0).CurrentValue);
        }

        [Test]
        public void BeginMoveとCompleteMoveでIsMovingが切り替わる()
        {
            using BoardModel board = TwoPlayerBoard();
            board.BeginMove();
            Assert.IsTrue(board.IsMoving.CurrentValue);
            board.CompleteMove(0, false);
            Assert.IsFalse(board.IsMoving.CurrentValue);
        }

        [Test]
        public void CompleteMoveでclearedならそのプレイヤーが勝者になる()
        {
            using BoardModel board = TwoPlayerBoard();
            board.CompleteMove(1, true);
            Assert.AreEqual(1, board.Winner.CurrentValue);
            Assert.IsTrue(board.IsFinished);
        }

        [Test]
        public void 勝者は最初に確定した1人で後から上書きされない()
        {
            using BoardModel board = TwoPlayerBoard();
            board.CompleteMove(1, true);
            board.CompleteMove(0, true);
            Assert.AreEqual(1, board.Winner.CurrentValue);
        }
    }
}
