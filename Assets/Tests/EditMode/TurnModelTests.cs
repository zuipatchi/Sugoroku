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
            return new TurnModel(new GameParticipants(session));
        }

        private static TurnModel OnlineTurn()
        {
            return new TurnModel(new GameParticipants(new GameSessionModel()));
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
        public void 参加者1人ならNextしても手番は0のまま()
        {
            using TurnModel turn = OnlineTurn();
            turn.Next();
            Assert.AreEqual(0, turn.CurrentPlayer.CurrentValue);
        }
    }
}
