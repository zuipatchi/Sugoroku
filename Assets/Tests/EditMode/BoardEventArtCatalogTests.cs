using Main.Board;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class BoardEventArtCatalogTests
    {
        [TestCase(BoardCellEvent.Forward, "Board/Forward")]
        [TestCase(BoardCellEvent.Back, "Board/Back")]
        [TestCase(BoardCellEvent.MoneyUp, "Board/MoneyUp")]
        [TestCase(BoardCellEvent.MoneyDown, "Board/MoneyDown")]
        [TestCase(BoardCellEvent.Territory, "Board/Territory")]
        [TestCase(BoardCellEvent.Item, "Board/Item")]
        [TestCase(BoardCellEvent.MiniGame, "Image/MiniGame/Minigame")]
        [TestCase(BoardCellEvent.None, "Image/Board/Glass")]
        public void Addressは画像のあるイベントの共通アドレスを返す(BoardCellEvent cellEvent, string expected)
        {
            Assert.AreEqual(expected, BoardEventArtCatalog.Address(cellEvent));
        }

        [Test]
        public void StartAddressはスタート画像の固定アドレス()
        {
            Assert.AreEqual("Board/Start", BoardEventArtCatalog.StartAddress);
        }

        [Test]
        public void AddressForはスタートを経路の先頭マスとして特別扱いする()
        {
            // index 0 はイベント種別に依らずスタート画像。
            BoardCellDefinition cell = new();
            cell.SetEvent(BoardCellEvent.Territory);

            Assert.AreEqual("Board/Start", BoardEventArtCatalog.AddressFor(cell, 0));
        }

        [Test]
        public void AddressForはミニゲームマスに全マス共通の絵を使う()
        {
            // 遊ぶゲームは着地のたびの抽選なので、マスの絵で特定のゲームを指すことはできない
            // （どのゲームが当たったかは着地の告知でサムネイルを出して見せる）。
            BoardCellDefinition cell = new();
            cell.SetEvent(BoardCellEvent.MiniGame);

            Assert.AreEqual(BoardEventArtCatalog.MiniGameAddress, BoardEventArtCatalog.AddressFor(cell, 1));
        }

        [Test]
        public void AddressForはイベント種別の共通画像で解決する()
        {
            BoardCellDefinition cell = new();
            cell.SetEvent(BoardCellEvent.Item);

            Assert.AreEqual("Board/Item", BoardEventArtCatalog.AddressFor(cell, 1));
        }
    }
}
