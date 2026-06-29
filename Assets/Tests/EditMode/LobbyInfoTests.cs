using Matching;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class LobbyInfoTests
    {
        [Test]
        public void コンストラクタで各値が正しく格納される()
        {
            LobbyInfo info = new LobbyInfo("id-001", "TestRoom", 1, 2);

            Assert.AreEqual("id-001", info.LobbyId);
            Assert.AreEqual("TestRoom", info.Name);
            Assert.AreEqual(1, info.PlayerCount);
            Assert.AreEqual(2, info.MaxPlayers);
        }
    }
}
