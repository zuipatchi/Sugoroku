using Common.GameSession;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class PlayerCountSessionModelTests
    {
        [Test]
        public void 既定は最小人数()
        {
            PlayerCountSessionModel model = new();
            Assert.AreEqual(PlayerCountSessionModel.Min, model.Count);
        }

        [TestCase(2, 2)]
        [TestCase(3, 3)]
        [TestCase(4, 4)]
        public void 範囲内はそのまま選べる(int input, int expected)
        {
            PlayerCountSessionModel model = new();
            model.Select(input);
            Assert.AreEqual(expected, model.Count);
        }

        [TestCase(1, PlayerCountSessionModel.Min)]  // 下限未満は Min
        [TestCase(0, PlayerCountSessionModel.Min)]
        [TestCase(-3, PlayerCountSessionModel.Min)]
        [TestCase(9, PlayerCountSessionModel.Max)]  // 上限超えは Max
        [TestCase(100, PlayerCountSessionModel.Max)]
        public void 範囲外はクランプされる(int input, int expected)
        {
            PlayerCountSessionModel model = new();
            model.Select(input);
            Assert.AreEqual(expected, model.Count);
        }
    }
}
