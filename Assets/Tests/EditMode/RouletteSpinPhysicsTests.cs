using Main.Roulette;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class RouletteSpinPhysicsTests
    {
        private const float MinSpeed = 360f;
        private const float MaxSpeed = 1200f;
        private const float Acceleration = 2400f;
        private const float Delta = 1f / 60f;

        private static RouletteSpinPhysics Create()
        {
            return new RouletteSpinPhysics(MinSpeed, MaxSpeed, Acceleration);
        }

        [Test]
        public void 押下中は加速して回り続ける()
        {
            RouletteSpinPhysics physics = Create();
            physics.BeginHold();

            for (int i = 0; i < 30; i++)
            {
                Assert.IsTrue(physics.Tick(Delta));
            }

            Assert.IsTrue(physics.IsHolding);
            Assert.IsFalse(physics.HasStopped);
            Assert.Greater(physics.CurrentRotation, 0f);
        }

        [Test]
        public void Releaseは停止時間が過ぎると止まる()
        {
            RouletteSpinPhysics physics = Create();
            physics.BeginHold();
            physics.Tick(Delta);
            physics.Release(1f);

            for (int i = 0; i < 70; i++)
            {
                physics.Tick(Delta);
            }

            Assert.IsTrue(physics.HasStopped);
        }

        [Test]
        public void ReleaseToは指定した角度でちょうど止まる()
        {
            RouletteSpinPhysics physics = Create();
            physics.BeginHold();
            physics.Tick(Delta);

            float target = physics.CurrentRotation + 1080f + 37.5f;
            physics.ReleaseTo(target, 1f);

            // 停止時間ぶん回しても止まっていない間は目標に達していない。
            for (int i = 0; i < 59; i++)
            {
                physics.Tick(Delta);
                Assert.IsFalse(physics.HasStopped, $"tick {i}");
            }
            // 停止時間を越えるところまで進めると、目標角ちょうどで止まる。
            for (int i = 0; i < 5; i++)
            {
                physics.Tick(Delta);
            }

            Assert.IsTrue(physics.HasStopped);
            Assert.AreEqual(target, physics.CurrentRotation, 0.001f);
        }

        [Test]
        public void ReleaseToの減速中は逆回転しない()
        {
            RouletteSpinPhysics physics = Create();
            physics.BeginHold();
            physics.Tick(Delta);

            float target = physics.CurrentRotation + 720f + 12f;
            physics.ReleaseTo(target, 1.5f);

            float previous = physics.CurrentRotation;
            for (int i = 0; i < 120; i++)
            {
                physics.Tick(Delta);
                Assert.GreaterOrEqual(physics.CurrentRotation, previous, $"tick {i}");
                previous = physics.CurrentRotation;
            }

            Assert.AreEqual(target, physics.CurrentRotation, 0.001f);
        }

        [Test]
        public void ReleaseToは押下していなければ何もしない()
        {
            RouletteSpinPhysics physics = Create();

            physics.ReleaseTo(720f, 1f);
            physics.Tick(Delta);

            Assert.AreEqual(0f, physics.CurrentRotation, 0.001f);
        }

        [Test]
        public void ReleaseToに現在角より手前を渡してもその場で止まる()
        {
            RouletteSpinPhysics physics = Create();
            physics.BeginHold();
            for (int i = 0; i < 10; i++)
            {
                physics.Tick(Delta);
            }

            float before = physics.CurrentRotation;
            physics.ReleaseTo(before - 500f, 0.5f);
            for (int i = 0; i < 40; i++)
            {
                physics.Tick(Delta);
            }

            Assert.IsTrue(physics.HasStopped);
            Assert.AreEqual(before, physics.CurrentRotation, 0.001f);
        }

        [Test]
        public void Haltは目標角モードも打ち切る()
        {
            RouletteSpinPhysics physics = Create();
            physics.BeginHold();
            physics.Tick(Delta);
            physics.ReleaseTo(physics.CurrentRotation + 720f, 2f);

            physics.Halt();

            Assert.IsTrue(physics.HasStopped);
        }
    }
}
