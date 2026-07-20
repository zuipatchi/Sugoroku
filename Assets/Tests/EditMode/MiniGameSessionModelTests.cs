using Common.Character;
using Common.MiniGame;
using NUnit.Framework;

namespace Tests.EditMode
{
    /// <summary>
    /// 起動側とミニゲームホストを仲介する <see cref="MiniGameSessionModel"/> の Begin 周りの検証。
    /// 参加者数（人間＋CPU）と参加者キャラが Begin で設定され、各ミニゲームが参照できることを保証する。
    /// </summary>
    public class MiniGameSessionModelTests
    {
        [Test]
        public void Begin_SetsCurrentGameAndPlayerCount()
        {
            MiniGameSessionModel session = new();

            CharacterId[] characters = { CharacterId.Character2, CharacterId.Character5 };
            session.Begin(MiniGameId.Overlap, 5, characters);

            Assert.AreEqual(MiniGameId.Overlap, session.CurrentGame);
            Assert.AreEqual(5, session.PlayerCount);
            CollectionAssert.AreEqual(characters, session.Characters);
        }

        [Test]
        public void Begin_OverwritesPreviousValues()
        {
            MiniGameSessionModel session = new();

            session.Begin(MiniGameId.Tap, 2, new[] { CharacterId.Character1 });
            session.Begin(MiniGameId.Race, 8, null);

            Assert.AreEqual(MiniGameId.Race, session.CurrentGame);
            Assert.AreEqual(8, session.PlayerCount);
        }

        [Test]
        public void Begin_NullCharactersは空リストになる()
        {
            MiniGameSessionModel session = new();

            session.Begin(MiniGameId.Race, 4, null);

            Assert.IsNotNull(session.Characters);
            Assert.AreEqual(0, session.Characters.Count);
        }
    }
}
