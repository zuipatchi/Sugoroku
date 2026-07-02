using Common.GameSession;
using Main.Turn;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class GameParticipantsTests
    {
        private static GameParticipants SinglePlayer()
        {
            GameSessionModel session = new();
            session.SetSinglePlayer();
            return new GameParticipants(session);
        }

        private static GameParticipants Online()
        {
            // 既定 Mode は Online。
            return new GameParticipants(new GameSessionModel());
        }

        [Test]
        public void 一人用モードはHumanとCpuの2人()
        {
            GameParticipants participants = SinglePlayer();
            Assert.AreEqual(2, participants.Count);
            Assert.AreEqual(PlayerKind.Human, participants.KindOf(0));
            Assert.AreEqual(PlayerKind.Cpu, participants.KindOf(1));
            Assert.IsTrue(participants.HasCpu);
        }

        [Test]
        public void オンラインはHumanの1人でCpuなし()
        {
            GameParticipants participants = Online();
            Assert.AreEqual(1, participants.Count);
            Assert.AreEqual(PlayerKind.Human, participants.KindOf(0));
            Assert.IsFalse(participants.HasCpu);
        }
    }
}
