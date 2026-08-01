using Main.Roulette;
using NUnit.Framework;
using R3;

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
        public void DecideSpinは止まる前に停止位置を流し状態はSpinningのまま()
        {
            // 減速に入った時点で「止まるセクター」が確定する（オンラインへ回り終わる前に配るため）。
            SpinDecision received = new(-1, 0f);
            int count = 0;
            using (_model.Decided.Subscribe(decision =>
            {
                received = decision;
                count++;
            }))
            {
                _model.BeginSpin();
                _model.DecideSpin(new SpinDecision(5, 2.75f));
            }

            Assert.AreEqual(1, count);
            Assert.AreEqual(5, received.Sector);
            Assert.AreEqual(2.75f, received.StopSeconds, 0.0001f);
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
