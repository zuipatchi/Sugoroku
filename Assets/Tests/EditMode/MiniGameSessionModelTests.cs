using Common.MiniGame;
using NUnit.Framework;

namespace Tests.EditMode
{
    /// <summary>
    /// 起動側とミニゲームホストを仲介する <see cref="MiniGameSessionModel"/> の Begin 周りの検証。
    /// 参加者数（人間＋CPU）が Begin で設定され、被っちゃやーよ側が参照できることを保証する。
    /// </summary>
    public class MiniGameSessionModelTests
    {
        [Test]
        public void Begin_SetsCurrentGameAndPlayerCount()
        {
            MiniGameSessionModel session = new();

            session.Begin(MiniGameId.Overlap, 5);

            Assert.AreEqual(MiniGameId.Overlap, session.CurrentGame);
            Assert.AreEqual(5, session.PlayerCount);
        }

        [Test]
        public void Begin_OverwritesPreviousValues()
        {
            MiniGameSessionModel session = new();

            session.Begin(MiniGameId.Tap, 2);
            session.Begin(MiniGameId.Race, 8);

            Assert.AreEqual(MiniGameId.Race, session.CurrentGame);
            Assert.AreEqual(8, session.PlayerCount);
        }
    }
}
