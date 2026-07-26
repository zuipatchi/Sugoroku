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
        public void 一人用モードは最大人数ぶん作れて先頭以外は全員Cpu()
        {
            // 上限は PlayerCountSessionModel.Max（現状 4）。定数参照でクランプ後の人数を検証する。
            int max = PlayerCountSessionModel.Max;
            GameParticipants participants = SinglePlayer(max);

            Assert.AreEqual(max, participants.Count);
            Assert.AreEqual(PlayerKind.Human, participants.KindOf(0));
            for (int player = 1; player < max; player++)
            {
                Assert.AreEqual(PlayerKind.Cpu, participants.KindOf(player));
            }
        }

        [Test]
        public void オンラインはSession未設定なら人数選択に依らずHumanが2人でCpuなし()
        {
            // 単独プレイは廃止＝最低 2 人。Session 未設定（テスト）は下限 2 で全員 Human。
            GameParticipants participants = Online();
            Assert.AreEqual(2, participants.Count);
            Assert.AreEqual(PlayerKind.Human, participants.KindOf(0));
            Assert.AreEqual(PlayerKind.Human, participants.KindOf(1));
            Assert.IsFalse(participants.HasCpu);
        }

        [Test]
        public void OnlinePlayerCountFromはSession未設定なら2()
        {
            Assert.AreEqual(2, GameParticipants.OnlinePlayerCountFrom(null));
        }

        [TestCase(4, 4)]  // ルーム定員をそのまま反映
        [TestCase(3, 3)]
        [TestCase(2, 2)]
        [TestCase(1, 2)]  // 2 未満は下限 2 にクランプ
        public void OnlinePlayerCountFromはルーム定員を反映し下限2にクランプする(int maxPlayers, int expected)
        {
            Assert.AreEqual(expected, GameParticipants.OnlinePlayerCountFrom(maxPlayers));
        }
    }
}
