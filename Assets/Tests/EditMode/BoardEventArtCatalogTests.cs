using Common.MiniGame;
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
        public void Addressは画像のあるイベントの共通アドレスを返す(BoardCellEvent cellEvent, string expected)
        {
            Assert.AreEqual(expected, BoardEventArtCatalog.Address(cellEvent));
        }

        [TestCase(BoardCellEvent.None)]
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

        [Test]
        public void AddressForはスタートを経路の先頭マスとして特別扱いする()
        {
            // index 0 はイベント種別に依らずスタート画像。
            BoardCellDefinition cell = new();
            cell.SetEvent(BoardCellEvent.Territory);

            Assert.AreEqual("Board/Start", BoardEventArtCatalog.AddressFor(cell, 0));
        }

        [TestCase(MiniGameId.Tap, "Image/MiniGame/Renda")]
        [TestCase(MiniGameId.Race, "Image/MiniGame/Lace")]
        public void AddressForはミニゲームマスにそのゲームのサムネイルを使う(MiniGameId game, string expected)
        {
            // どのゲームが始まるマスかを盤面で見分けられるよう、イベント共通ではなくゲーム別の画像を貼る。
            BoardCellDefinition cell = new();
            cell.SetEvent(BoardCellEvent.MiniGame);
            cell.SetMiniGame(game);

            Assert.AreEqual(expected, BoardEventArtCatalog.AddressFor(cell, 1));
        }

        [Test]
        public void AddressForはミニゲーム以外をイベント種別の共通画像で解決する()
        {
            BoardCellDefinition cell = new();
            cell.SetEvent(BoardCellEvent.Item);

            Assert.AreEqual("Board/Item", BoardEventArtCatalog.AddressFor(cell, 1));
        }
    }
}
