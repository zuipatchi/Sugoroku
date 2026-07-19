using Common.GameSession;
using Main.Turn;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class TurnModelTests
    {
        private static TurnModel SinglePlayerTurn()
        {
            GameSessionModel session = new();
            session.SetSinglePlayer();
            return new TurnModel(new GameParticipants(session, new PlayerCountSessionModel()));
        }

        private static TurnModel OnlineTurn()
        {
            return new TurnModel(new GameParticipants(new GameSessionModel(), new PlayerCountSessionModel()));
        }

        [Test]
        public void 初期手番は先攻の0()
        {
            using TurnModel turn = SinglePlayerTurn();
            Assert.AreEqual(0, turn.CurrentPlayer.CurrentValue);
        }

        [Test]
        public void Nextで2人の手番が0と1で巡回する()
        {
            using TurnModel turn = SinglePlayerTurn();
            turn.Next();
            Assert.AreEqual(1, turn.CurrentPlayer.CurrentValue);
            turn.Next();
            Assert.AreEqual(0, turn.CurrentPlayer.CurrentValue);
        }

        [Test]
        public void オンラインは2人の手番が0と1で巡回する()
        {
            // オンラインは最低 2 人（単独プレイ廃止）なので単独プレイと同じく 0→1→0 で巡回する。
            using TurnModel turn = OnlineTurn();
            turn.Next();
            Assert.AreEqual(1, turn.CurrentPlayer.CurrentValue);
            turn.Next();
            Assert.AreEqual(0, turn.CurrentPlayer.CurrentValue);
        }
    }
}
