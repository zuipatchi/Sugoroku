using Common.MiniGame;
using Main.Board;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public class BoardDefinitionTests
    {
        private const int Columns = 6;
        private const int Rows = 5;

        [Test]
        public void CreateRectangularは従来リングと同じマス数を持つ()
        {
            BoardDefinition definition = BoardDefinition.CreateRectangular(Columns, Rows);
            Assert.AreEqual(BoardMath.PerimeterCellCount(Columns, Rows), definition.CellCount);
            Object.DestroyImmediate(definition);
        }

        [Test]
        public void CreateRectangularは各マスをBoardMathと同じ座標に置く()
        {
            BoardDefinition definition = BoardDefinition.CreateRectangular(Columns, Rows);
            for (int i = 0; i < definition.CellCount; i++)
            {
                (int column, int row) = BoardMath.CellGridPosition(i, Columns, Rows);
                Assert.AreEqual(new Vector2Int(column, row), definition.Cell(i).Grid, $"index {i}");
            }
            Object.DestroyImmediate(definition);
        }

        [Test]
        public void CreateRectangularのグリッドサイズは引数どおり()
        {
            BoardDefinition definition = BoardDefinition.CreateRectangular(Columns, Rows);
            Assert.AreEqual(Columns, definition.GridColumns);
            Assert.AreEqual(Rows, definition.GridRows);
            Object.DestroyImmediate(definition);
        }

        [Test]
        public void 新規マスの既定はイベントなし_色未指定()
        {
            BoardCellDefinition cell = new(new Vector2Int(1, 2));
            Assert.AreEqual(new Vector2Int(1, 2), cell.Grid);
            Assert.AreEqual(BoardCellEvent.None, cell.Event);
            Assert.IsFalse(cell.HasCustomColor);
        }

        [Test]
        public void SetColorでアルファが立つとHasCustomColorがtrueになる()
        {
            BoardCellDefinition cell = new(Vector2Int.zero);
            cell.SetColor(new Color(1f, 0f, 0f, 1f));
            Assert.IsTrue(cell.HasCustomColor);
        }

        [Test]
        public void イベント値はSetで更新される()
        {
            BoardCellDefinition cell = new(Vector2Int.zero);
            cell.SetEvent(BoardCellEvent.MiniGame);
            cell.SetMiniGame(MiniGameId.Tap);
            cell.SetAmount(3);
            Assert.AreEqual(BoardCellEvent.MiniGame, cell.Event);
            Assert.AreEqual(MiniGameId.Tap, cell.MiniGame);
            Assert.AreEqual(3, cell.Amount);
        }

        [Test]
        public void AddCellとRemoveCellAtとClearCellsで経路が増減する()
        {
            BoardDefinition definition = ScriptableObject.CreateInstance<BoardDefinition>();
            definition.AddCell(new BoardCellDefinition(new Vector2Int(0, 0)));
            definition.AddCell(new BoardCellDefinition(new Vector2Int(1, 0)));
            Assert.AreEqual(2, definition.CellCount);

            definition.RemoveCellAt(0);
            Assert.AreEqual(1, definition.CellCount);
            Assert.AreEqual(new Vector2Int(1, 0), definition.Cell(0).Grid);

            definition.ClearCells();
            Assert.AreEqual(0, definition.CellCount);
            Object.DestroyImmediate(definition);
        }

        [Test]
        public void IndexOfGridは一致する経路indexを返し_無ければマイナス1()
        {
            BoardDefinition definition = ScriptableObject.CreateInstance<BoardDefinition>();
            definition.AddCell(new BoardCellDefinition(new Vector2Int(0, 0)));
            definition.AddCell(new BoardCellDefinition(new Vector2Int(2, 3)));
            Assert.AreEqual(1, definition.IndexOfGrid(new Vector2Int(2, 3)));
            Assert.AreEqual(-1, definition.IndexOfGrid(new Vector2Int(9, 9)));
            Object.DestroyImmediate(definition);
        }

        [Test]
        public void EventIconAddressは未設定なら空文字を返す()
        {
            BoardDefinition definition = ScriptableObject.CreateInstance<BoardDefinition>();
            Assert.AreEqual(string.Empty, definition.EventIconAddress(BoardCellEvent.MoneyUp));
            Object.DestroyImmediate(definition);
        }

        [Test]
        public void SetEventIconAddressで設定した値をEventIconAddressが返す()
        {
            BoardDefinition definition = ScriptableObject.CreateInstance<BoardDefinition>();
            definition.SetEventIconAddress(BoardCellEvent.MoneyUp, "Board/MoneyUp");
            Assert.AreEqual("Board/MoneyUp", definition.EventIconAddress(BoardCellEvent.MoneyUp));
            Object.DestroyImmediate(definition);
        }

        [Test]
        public void SetEventIconAddressは同じイベントを上書きする()
        {
            BoardDefinition definition = ScriptableObject.CreateInstance<BoardDefinition>();
            definition.SetEventIconAddress(BoardCellEvent.Forward, "Board/Old");
            definition.SetEventIconAddress(BoardCellEvent.Forward, "Board/New");
            Assert.AreEqual("Board/New", definition.EventIconAddress(BoardCellEvent.Forward));
            Object.DestroyImmediate(definition);
        }

        [Test]
        public void SetEventIconAddressはイベントごとに独立して保持する()
        {
            BoardDefinition definition = ScriptableObject.CreateInstance<BoardDefinition>();
            definition.SetEventIconAddress(BoardCellEvent.MoneyUp, "Board/MoneyUp");
            definition.SetEventIconAddress(BoardCellEvent.MoneyDown, "Board/MoneyDown");
            Assert.AreEqual("Board/MoneyUp", definition.EventIconAddress(BoardCellEvent.MoneyUp));
            Assert.AreEqual("Board/MoneyDown", definition.EventIconAddress(BoardCellEvent.MoneyDown));
            Object.DestroyImmediate(definition);
        }

        [Test]
        public void FrameAddressはSetFrameAddressで読み書きできる()
        {
            BoardDefinition definition = ScriptableObject.CreateInstance<BoardDefinition>();
            Assert.IsFalse(definition.HasFrame);
            definition.SetFrameAddress("Board/Frame");
            Assert.AreEqual("Board/Frame", definition.FrameAddress);
            Assert.IsTrue(definition.HasFrame);
            Object.DestroyImmediate(definition);
        }
    }
}
