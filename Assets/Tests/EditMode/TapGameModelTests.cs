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

        [Test]
        public void 人数指定Setupで参加者数ぶんが連打数0になる()
        {
            _model.Setup(playerCount: 4, seed: 1);

            Assert.AreEqual(4, _model.ParticipantCount);
            for (int p = 0; p < _model.ParticipantCount; p++)
            {
                Assert.AreEqual(0, _model.TapCountOf(p));
            }
            Assert.IsTrue(_model.IsPlayerWin); // 全員 0 は同数＝プレイヤー勝ち扱い
        }

        [Test]
        public void 人数は最低1にクランプされる()
        {
            _model.Setup(playerCount: 0, seed: 1);
            Assert.AreEqual(1, _model.ParticipantCount);
        }

        [Test]
        public void CPUはTickで自動連打しプレイヤー無操作なら負ける()
        {
            _model.Setup(playerCount: 3, seed: 1);
            _model.StartPlaying(5f);

            for (int i = 0; i < 50; i++)
            {
                _model.Tick(0.1f); // 合計 5 秒
            }

            Assert.AreEqual(0, _model.TapCountOf(0));    // プレイヤーは無操作
            Assert.Greater(_model.TapCountOf(1), 0);     // CPU は連打している
            Assert.IsFalse(_model.IsPlayerWin);
        }

        [Test]
        public void プレイヤーがCPUより多く連打すれば勝つ()
        {
            _model.Setup(playerCount: 2, seed: 1);
            _model.StartPlaying(5f);

            for (int i = 0; i < 100; i++)
            {
                _model.Tap(); // CPU を Tick しないので CPU=0
            }

            Assert.AreEqual(100, _model.TapCountOf(0));
            Assert.AreEqual(0, _model.TapCountOf(1));
            Assert.IsTrue(_model.IsPlayerWin);
        }

        [Test]
        public void TickはPlaying中以外では何もしない()
        {
            _model.Setup(playerCount: 3, seed: 1);
            _model.Tick(1f); // Ready 中
            Assert.AreEqual(0, _model.TapCountOf(1));
        }

        [Test]
        public void 同一シードならCPUの連打が再現する()
        {
            TapGameModel a = new();
            TapGameModel b = new();
            try
            {
                a.Setup(playerCount: 4, seed: 42);
                b.Setup(playerCount: 4, seed: 42);
                a.StartPlaying(5f);
                b.StartPlaying(5f);

                for (int i = 0; i < 50; i++)
                {
                    a.Tick(0.1f);
                    b.Tick(0.1f);
                }

                for (int p = 0; p < 4; p++)
                {
                    Assert.AreEqual(a.TapCountOf(p), b.TapCountOf(p));
                }
            }
            finally
            {
                a.Dispose();
                b.Dispose();
            }
        }
    }
}
