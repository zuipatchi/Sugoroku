using Common.Board;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class BoardSessionModelTests
    {
        private BoardSessionModel _model;

        [SetUp]
        public void SetUp()
        {
            _model = new BoardSessionModel();
        }

        [Test]
        public void 初期状態は未選択()
        {
            Assert.AreEqual(string.Empty, _model.SelectedId);
            Assert.IsFalse(_model.HasSelection);
        }

        [Test]
        public void Selectで識別子が保存されHasSelectionがtrueになる()
        {
            _model.Select("Cross");
            Assert.AreEqual("Cross", _model.SelectedId);
            Assert.IsTrue(_model.HasSelection);
        }

        [Test]
        public void Selectにnullを渡すと空文字扱いで未選択に戻る()
        {
            _model.Select("Cross");
            _model.Select(null);
            Assert.AreEqual(string.Empty, _model.SelectedId);
            Assert.IsFalse(_model.HasSelection);
        }
    }
}
