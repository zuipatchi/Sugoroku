using Main.Roulette;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class RouletteModelTests
    {
        private RouletteModel _model;

        [SetUp]
        public void SetUp()
        {
            _model = new RouletteModel();
        }

        [TearDown]
        public void TearDown()
        {
            _model.Dispose();
        }

        [Test]
        public void 初期状態はIdleで出目は0で進む人は未確定()
        {
            Assert.AreEqual(RouletteState.Idle, _model.State.CurrentValue);
            Assert.AreEqual(0, _model.Result.CurrentValue);
            Assert.AreEqual(-1, _model.AdvancingPlayer.CurrentValue);
        }

        [Test]
        public void BeginSpinで状態がSpinningになる()
        {
            _model.BeginSpin();
            Assert.AreEqual(RouletteState.Spinning, _model.State.CurrentValue);
        }

        [Test]
        public void CompleteSpinで出目と進む人が確定し状態がStoppedになる()
        {
            _model.BeginSpin();
            _model.CompleteSpin(4, 2);
            Assert.AreEqual(4, _model.Result.CurrentValue);
            Assert.AreEqual(2, _model.AdvancingPlayer.CurrentValue);
            Assert.AreEqual(RouletteState.Stopped, _model.State.CurrentValue);
        }
    }
}
