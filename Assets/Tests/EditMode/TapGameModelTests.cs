using MiniGame.TapGame;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class TapGameModelTests
    {
        private TapGameModel _model;

        [SetUp]
        public void SetUp()
        {
            _model = new TapGameModel();
        }

        [TearDown]
        public void TearDown()
        {
            _model.Dispose();
        }

        [Test]
        public void 初期状態はReadyでタップ数は0()
        {
            Assert.AreEqual(TapGamePhase.Ready, _model.Phase.CurrentValue);
            Assert.AreEqual(0, _model.TapCount.CurrentValue);
        }

        [Test]
        public void Ready中のTapはカウントされない()
        {
            _model.Tap();
            Assert.AreEqual(0, _model.TapCount.CurrentValue);
        }

        [Test]
        public void Countdown中のTapはカウントされない()
        {
            _model.BeginCountdown();
            _model.Tap();
            Assert.AreEqual(0, _model.TapCount.CurrentValue);
        }

        [Test]
        public void Playing中のTapだけがカウントされる()
        {
            _model.StartPlaying(5f);
            _model.Tap();
            _model.Tap();
            _model.Tap();
            Assert.AreEqual(3, _model.TapCount.CurrentValue);
        }

        [Test]
        public void Finish後のTapはカウントされない()
        {
            _model.StartPlaying(5f);
            _model.Tap();
            _model.Finish();
            _model.Tap();
            Assert.AreEqual(1, _model.TapCount.CurrentValue);
        }

        [Test]
        public void StartPlayingでタップ数がリセットされる()
        {
            _model.StartPlaying(5f);
            _model.Tap();
            _model.Tap();
            _model.StartPlaying(5f);
            Assert.AreEqual(0, _model.TapCount.CurrentValue);
        }

        [Test]
        public void フェーズはReadyからCountdownPlayingFinishedの順に遷移する()
        {
            Assert.AreEqual(TapGamePhase.Ready, _model.Phase.CurrentValue);
            _model.BeginCountdown();
            Assert.AreEqual(TapGamePhase.Countdown, _model.Phase.CurrentValue);
            _model.StartPlaying(5f);
            Assert.AreEqual(TapGamePhase.Playing, _model.Phase.CurrentValue);
            _model.Finish();
            Assert.AreEqual(TapGamePhase.Finished, _model.Phase.CurrentValue);
        }

        [Test]
        public void StartPlayingで残り秒数がセットされる()
        {
            _model.StartPlaying(5f);
            Assert.AreEqual(5f, _model.RemainingSeconds.CurrentValue);
        }

        [Test]
        public void UpdateRemainingはPlaying中のみ反映され負値は0に丸められる()
        {
            // Ready 中は無視される
            _model.UpdateRemaining(3f);
            Assert.AreEqual(0f, _model.RemainingSeconds.CurrentValue);

            _model.StartPlaying(5f);
            _model.UpdateRemaining(2.5f);
            Assert.AreEqual(2.5f, _model.RemainingSeconds.CurrentValue);

            _model.UpdateRemaining(-1f);
            Assert.AreEqual(0f, _model.RemainingSeconds.CurrentValue);
        }

        [Test]
        public void Finishで残り秒数が0になる()
        {
            _model.StartPlaying(5f);
            _model.Finish();
            Assert.AreEqual(0f, _model.RemainingSeconds.CurrentValue);
        }
    }
}
