using Matching;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class LobbyInfoTests
    {
        [Test]
        public void コンストラクタで各値が正しく格納される()
        {
            LobbyInfo info = new LobbyInfo("id-001", "TestRoom", 1, 2, "Board_Forest");

            Assert.AreEqual("id-001", info.LobbyId);
            Assert.AreEqual("TestRoom", info.Name);
            Assert.AreEqual(1, info.PlayerCount);
            Assert.AreEqual(2, info.MaxPlayers);
            Assert.AreEqual("Board_Forest", info.BoardId);
        }

        [Test]
        public void マップ識別子を省略するとBoardIdは空文字になる()
        {
            LobbyInfo info = new LobbyInfo("id-001", "TestRoom", 1, 2);

            Assert.AreEqual(string.Empty, info.BoardId);
        }
    }
}
