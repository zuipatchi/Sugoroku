using Matching;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class MatchingModelTests
    {
        private MatchingModel _model;

        [SetUp]
        public void SetUp()
        {
            _model = new MatchingModel();
        }

        [Test]
        public void Stateの初期値がIdle()
        {
            Assert.AreEqual(MatchingState.Idle, _model.State.Value);
        }

        [Test]
        public void Roomsの初期値が空()
        {
            Assert.IsNotNull(_model.Rooms.Value);
            Assert.AreEqual(0, _model.Rooms.Value.Count);
        }
    }
}
