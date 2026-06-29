using Common.GameSession;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class GameSessionModelTests
    {
        private GameSessionModel _model;

        [SetUp]
        public void SetUp()
        {
            _model = new GameSessionModel();
        }

        [TearDown]
        public void TearDown()
        {
            _model.Dispose();
        }

        [Test]
        public void Session未設定時のIsHostがfalse()
        {
            Assert.IsFalse(_model.IsHost);
        }

        [Test]
        public void Session未設定時のSessionがnull()
        {
            Assert.IsNull(_model.Session);
        }

        [Test]
        public void Session未設定時のHasSessionがfalse()
        {
            Assert.IsFalse(_model.HasSession);
        }

        [Test]
        public void Session未設定時のLeaveCurrentSessionAsyncは例外なく完了する()
        {
            Assert.DoesNotThrow(() => _model.LeaveCurrentSessionAsync().GetAwaiter().GetResult());
        }
    }
}
