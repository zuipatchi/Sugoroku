using Main.Board;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class BoardEventArtCatalogTests
    {
        [TestCase(BoardCellEvent.MoneyUp, "Board/MoneyUp")]
        [TestCase(BoardCellEvent.MoneyDown, "Board/MoneyDown")]
        [TestCase(BoardCellEvent.Territory, "Board/Territory")]
        [TestCase(BoardCellEvent.Item, "Board/Item")]
        public void Addressは画像のあるイベントの共通アドレスを返す(BoardCellEvent cellEvent, string expected)
        {
            Assert.AreEqual(expected, BoardEventArtCatalog.Address(cellEvent));
        }

        [TestCase(BoardCellEvent.None)]
        [TestCase(BoardCellEvent.Forward)]
        [TestCase(BoardCellEvent.Back)]
        [TestCase(BoardCellEvent.Rest)]
        [TestCase(BoardCellEvent.MiniGame)]
        public void Addressは画像の無いイベントで空文字を返す(BoardCellEvent cellEvent)
        {
            Assert.AreEqual(string.Empty, BoardEventArtCatalog.Address(cellEvent));
        }

        [Test]
        public void StartAddressはスタート画像の固定アドレス()
        {
            Assert.AreEqual("Board/Start", BoardEventArtCatalog.StartAddress);
        }
    }
}
