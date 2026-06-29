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
        public void 初期状態はIdleで出目は0()
        {
            Assert.AreEqual(RouletteState.Idle, _model.State.CurrentValue);
            Assert.AreEqual(0, _model.Result.CurrentValue);
        }

        [Test]
        public void BeginSpinで状態がSpinningになる()
        {
            _model.BeginSpin(6);
            Assert.AreEqual(RouletteState.Spinning, _model.State.CurrentValue);
        }

        [Test]
        public void BeginSpinの戻り値は常に1からNの範囲に収まる()
        {
            const int count = 6;
            for (int i = 0; i < 1000; i++)
            {
                int value = _model.BeginSpin(count);
                Assert.GreaterOrEqual(value, 1);
                Assert.LessOrEqual(value, count);
            }
        }

        [Test]
        public void CompleteSpinで出目が確定し状態がStoppedになる()
        {
            _model.BeginSpin(6);
            _model.CompleteSpin(4);
            Assert.AreEqual(4, _model.Result.CurrentValue);
            Assert.AreEqual(RouletteState.Stopped, _model.State.CurrentValue);
        }
    }
}
