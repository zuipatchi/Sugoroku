using MiniGame.RaceGame;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class RaceGameModelTests
    {
        private const float Delta = 1e-4f;

        private RaceGameModel _model;

        [SetUp]
        public void SetUp()
        {
            _model = new RaceGameModel();
        }

        [TearDown]
        public void TearDown()
        {
            _model.Dispose();
        }

        private static void StartRace(RaceGameModel model)
        {
            model.BeginCountdown();
            model.StartRacing();
        }

        // 検証用コンフィグ。既定では CPU 抽選の間隔を極端に長くして CPU を実質不活性にし、
        // プレイヤー側の検証を邪魔しないようにする。必要なテストだけ CPU を活性化する。
        private static RaceGameConfig FixedConfig(
            float baseSpeed = 0.01f,
            float greatBoost = 0.06f,
            float goodBoost = 0.025f,
            float cpuTapIntervalMin = 1000f,
            float cpuTapIntervalMax = 1000f,
            float cpuGreatChance = 0.1f,
            float cpuGoodChance = 0.5f)
        {
            return new RaceGameConfig(
                baseSpeed: baseSpeed,
                greatBoost: greatBoost,
                goodBoost: goodBoost,
                greatHalfWidth: 0.06f,
                goodHalfWidth: 0.18f,
                cpuTapIntervalMin: cpuTapIntervalMin,
                cpuTapIntervalMax: cpuTapIntervalMax,
                cpuGreatChance: cpuGreatChance,
                cpuGoodChance: cpuGoodChance);
        }

        [Test]
        public void Setupで進捗0のReadyになり勝者はいない()
        {
            _model.Setup(FixedConfig(), seed: 1);

            Assert.AreEqual(RaceGamePhase.Ready, _model.Phase.CurrentValue);
            Assert.AreEqual(0f, _model.PlayerProgress, Delta);
            Assert.AreEqual(0f, _model.CpuProgress, Delta);
            Assert.IsFalse(_model.WinnerIndex.HasValue);
            Assert.IsFalse(_model.IsPlayerWin);
        }

        [Test]
        public void Judgeは中央でGreat中間でGood端でMissになる()
        {
            _model.Setup(FixedConfig(), seed: 1);

            Assert.AreEqual(MeterJudgement.Great, _model.Judge(0.5f));
            Assert.AreEqual(MeterJudgement.Great, _model.Judge(0.55f)); // 距離 0.05 <= 0.06
            Assert.AreEqual(MeterJudgement.Good, _model.Judge(0.6f));   // 距離 0.10 <= 0.18
            Assert.AreEqual(MeterJudgement.Good, _model.Judge(0.35f));  // 距離 0.15 <= 0.18
            Assert.AreEqual(MeterJudgement.Miss, _model.Judge(0.9f));   // 距離 0.40 > 0.18
            Assert.AreEqual(MeterJudgement.Miss, _model.Judge(0f));
        }

        [Test]
        public void ApplyTapはレース中のみ有効で判定に応じて前進する()
        {
            _model.Setup(FixedConfig(greatBoost: 0.06f, goodBoost: 0.025f), seed: 1);

            // Ready 中は無効
            Assert.AreEqual(MeterJudgement.Miss, _model.ApplyTap(0.5f));
            Assert.AreEqual(0f, _model.PlayerProgress, Delta);

            StartRace(_model);

            Assert.AreEqual(MeterJudgement.Great, _model.ApplyTap(0.5f));
            Assert.AreEqual(0.06f, _model.PlayerProgress, Delta);

            Assert.AreEqual(MeterJudgement.Good, _model.ApplyTap(0.6f));
            Assert.AreEqual(0.085f, _model.PlayerProgress, Delta);

            Assert.AreEqual(MeterJudgement.Miss, _model.ApplyTap(0.95f));
            Assert.AreEqual(0.085f, _model.PlayerProgress, Delta); // Miss は進まない
        }

        [Test]
        public void CPUはプレイヤーと同じベース速度で進む()
        {
            // CPU 抽選を不活性（間隔 1000 秒）にすると、1 秒経過ではベース速度ぶんだけ進む。
            _model.Setup(FixedConfig(baseSpeed: 0.01f), seed: 1);
            StartRace(_model);

            _model.Tick(1f);

            Assert.AreEqual(0.01f, _model.PlayerProgress, Delta);
            Assert.AreEqual(0.01f, _model.CpuProgress, Delta);
        }

        [Test]
        public void CPUはランダム抽選ぶんプレイヤーのベースより前に出る()
        {
            // 抽選が必ず前進する設定（Great+Good=1・Miss なし）で頻繁に抽選させる。
            _model.Setup(
                FixedConfig(
                    baseSpeed: 0.01f,
                    cpuTapIntervalMin: 0.2f,
                    cpuTapIntervalMax: 0.2f,
                    cpuGreatChance: 0.5f,
                    cpuGoodChance: 0.5f),
                seed: 1);
            StartRace(_model);

            for (int i = 0; i < 10; i++)
            {
                _model.Tick(0.1f); // 合計 1 秒
            }

            // プレイヤーはベースのみ、CPU はベース＋抽選ぶんで前に出る。
            Assert.AreEqual(0.01f, _model.PlayerProgress, Delta);
            Assert.Greater(_model.CpuProgress, _model.PlayerProgress);
        }

        [Test]
        public void Tickはレース中以外では何もしない()
        {
            _model.Setup(FixedConfig(), seed: 1);
            _model.Tick(1f); // Ready 中
            Assert.AreEqual(0f, _model.PlayerProgress, Delta);
            Assert.AreEqual(0f, _model.CpuProgress, Delta);
        }

        [Test]
        public void プレイヤーがゴールに到達すると勝者はPlayerで以降の操作は無視される()
        {
            _model.Setup(FixedConfig(greatBoost: 1.5f), seed: 1);
            StartRace(_model);

            _model.ApplyTap(0.5f); // Great で 1.5 → クランプして 1

            Assert.AreEqual(RaceGamePhase.Finished, _model.Phase.CurrentValue);
            Assert.AreEqual(0, _model.WinnerIndex);
            Assert.IsTrue(_model.IsPlayerWin);
            Assert.AreEqual(1f, _model.PlayerProgress, Delta); // [0,1] にクランプ

            // 以降の Tap / Tick は無視される
            float cpuBefore = _model.CpuProgress;
            _model.ApplyTap(0.5f);
            _model.Tick(1f);
            Assert.AreEqual(1f, _model.PlayerProgress, Delta);
            Assert.AreEqual(cpuBefore, _model.CpuProgress, Delta);
        }

        [Test]
        public void CPUが先にゴールすると勝者はCpuになる()
        {
            // CPU が毎回 Great を高頻度で引く設定。プレイヤーはベースのみで置いていかれる。
            _model.Setup(
                FixedConfig(
                    baseSpeed: 0.001f,
                    greatBoost: 0.5f,
                    cpuTapIntervalMin: 0.1f,
                    cpuTapIntervalMax: 0.1f,
                    cpuGreatChance: 1f,
                    cpuGoodChance: 0f),
                seed: 1);
            StartRace(_model);

            for (int i = 0; i < 50 && _model.Phase.CurrentValue == RaceGamePhase.Racing; i++)
            {
                _model.Tick(0.1f);
            }

            Assert.AreEqual(RaceGamePhase.Finished, _model.Phase.CurrentValue);
            Assert.AreEqual(1, _model.WinnerIndex);
            Assert.IsFalse(_model.IsPlayerWin);
            Assert.AreEqual(1f, _model.CpuProgress, Delta);
            Assert.Less(_model.PlayerProgress, 1f);
        }

        [Test]
        public void 同一シードと同一操作列なら結果が再現する()
        {
            RaceGameModel a = new();
            RaceGameModel b = new();
            try
            {
                RaceGameConfig config = new(
                    baseSpeed: 0.01f,
                    greatBoost: 0.06f,
                    goodBoost: 0.025f,
                    greatHalfWidth: 0.06f,
                    goodHalfWidth: 0.18f,
                    cpuTapIntervalMin: 0.3f,
                    cpuTapIntervalMax: 0.7f,
                    cpuGreatChance: 0.12f,
                    cpuGoodChance: 0.55f);

                a.Setup(config, seed: 2024);
                b.Setup(config, seed: 2024);
                StartRace(a);
                StartRace(b);

                for (int i = 0; i < 20; i++)
                {
                    a.Tick(0.1f);
                    b.Tick(0.1f);
                    a.ApplyTap(0.55f);
                    b.ApplyTap(0.55f);
                }

                Assert.AreEqual(a.PlayerProgress, b.PlayerProgress, Delta);
                Assert.AreEqual(a.CpuProgress, b.CpuProgress, Delta);
                Assert.AreEqual(a.WinnerIndex, b.WinnerIndex);
            }
            finally
            {
                a.Dispose();
                b.Dispose();
            }
        }

        [Test]
        public void 人数指定Setupで走者数ぶんが進捗0のReadyになる()
        {
            _model.Setup(FixedConfig(), playerCount: 4, seed: 1);

            Assert.AreEqual(4, _model.RunnerCount);
            Assert.AreEqual(RaceGamePhase.Ready, _model.Phase.CurrentValue);
            for (int runner = 0; runner < _model.RunnerCount; runner++)
            {
                Assert.AreEqual(0f, _model.Progress(runner), Delta);
            }
            Assert.IsFalse(_model.WinnerIndex.HasValue);
        }

        [Test]
        public void 人数は最低2走者にクランプされる()
        {
            _model.Setup(FixedConfig(), playerCount: 1, seed: 1);

            Assert.AreEqual(2, _model.RunnerCount);
        }

        [Test]
        public void 複数CPUのときプレイヤーが遅いとCPUの誰かが勝ちプレイヤーは負ける()
        {
            // CPU は毎回 Great を高頻度で引く。プレイヤーはベースのみで置いていかれる。
            _model.Setup(
                FixedConfig(
                    baseSpeed: 0.001f,
                    greatBoost: 0.5f,
                    cpuTapIntervalMin: 0.1f,
                    cpuTapIntervalMax: 0.1f,
                    cpuGreatChance: 1f,
                    cpuGoodChance: 0f),
                playerCount: 5,
                seed: 7);
            StartRace(_model);

            for (int i = 0; i < 50 && _model.Phase.CurrentValue == RaceGamePhase.Racing; i++)
            {
                _model.Tick(0.1f);
            }

            Assert.AreEqual(RaceGamePhase.Finished, _model.Phase.CurrentValue);
            Assert.IsTrue(_model.WinnerIndex.HasValue);
            Assert.GreaterOrEqual(_model.WinnerIndex.Value, 1); // 勝者はいずれかの CPU
            Assert.Less(_model.WinnerIndex.Value, _model.RunnerCount);
            Assert.IsFalse(_model.IsPlayerWin);
        }

        [Test]
        public void 人数指定でも同一シードなら結果が再現する()
        {
            RaceGameModel a = new();
            RaceGameModel b = new();
            try
            {
                RaceGameConfig config = FixedConfig(
                    baseSpeed: 0.01f,
                    cpuTapIntervalMin: 0.2f,
                    cpuTapIntervalMax: 0.4f,
                    cpuGreatChance: 0.2f,
                    cpuGoodChance: 0.5f);

                a.Setup(config, playerCount: 6, seed: 99);
                b.Setup(config, playerCount: 6, seed: 99);
                StartRace(a);
                StartRace(b);

                for (int i = 0; i < 20; i++)
                {
                    a.Tick(0.1f);
                    b.Tick(0.1f);
                }

                Assert.AreEqual(a.RunnerCount, b.RunnerCount);
                for (int runner = 0; runner < a.RunnerCount; runner++)
                {
                    Assert.AreEqual(a.Progress(runner), b.Progress(runner), Delta);
                }
                Assert.AreEqual(a.WinnerIndex, b.WinnerIndex);
            }
            finally
            {
                a.Dispose();
                b.Dispose();
            }
        }
    }
}
