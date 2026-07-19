using Common.GameSession;
using Main.Turn;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class GameParticipantsTests
    {
        private static GameParticipants SinglePlayer(int count)
        {
            GameSessionModel session = new();
            session.SetSinglePlayer();
            PlayerCountSessionModel playerCount = new();
            playerCount.Select(count);
            return new GameParticipants(session, playerCount);
        }

        private static GameParticipants Online()
        {
            // 既定 Mode は Online。人数モデル（一人用の人数）は使われない。
            return new GameParticipants(new GameSessionModel(), new PlayerCountSessionModel());
        }

        [Test]
        public void 一人用モードの既定はHumanとCpuの2人()
        {
            // 人数モデルの既定は Min(2)。
            GameSessionModel session = new();
            session.SetSinglePlayer();
            GameParticipants participants = new(session, new PlayerCountSessionModel());

            Assert.AreEqual(2, participants.Count);
            Assert.AreEqual(PlayerKind.Human, participants.KindOf(0));
            Assert.AreEqual(PlayerKind.Cpu, participants.KindOf(1));
            Assert.IsTrue(participants.HasCpu);
        }

        [Test]
        public void 一人用モードは選んだ人数ぶんの参加者を作り先頭がHuman残りがCpu()
        {
            GameParticipants participants = SinglePlayer(4);

            Assert.AreEqual(4, participants.Count);
            Assert.AreEqual(PlayerKind.Human, participants.KindOf(0));
            Assert.AreEqual(PlayerKind.Cpu, participants.KindOf(1));
            Assert.AreEqual(PlayerKind.Cpu, participants.KindOf(2));
            Assert.AreEqual(PlayerKind.Cpu, participants.KindOf(3));
        }

        [Test]
        public void 一人用モードは最大8人まで作れる()
        {
            GameParticipants participants = SinglePlayer(8);

            Assert.AreEqual(8, participants.Count);
            Assert.AreEqual(PlayerKind.Human, participants.KindOf(0));
            for (int player = 1; player < 8; player++)
            {
                Assert.AreEqual(PlayerKind.Cpu, participants.KindOf(player));
            }
        }

        [Test]
        public void オンラインは人数選択に依らずHumanが2人でCpuなし()
        {
            // 単独プレイは廃止＝最低 2 人。2 人固定ルームに合わせて全員 Human。
            GameParticipants participants = Online();
            Assert.AreEqual(2, participants.Count);
            Assert.AreEqual(PlayerKind.Human, participants.KindOf(0));
            Assert.AreEqual(PlayerKind.Human, participants.KindOf(1));
            Assert.IsFalse(participants.HasCpu);
        }
    }
}
