using Main.Board;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public class BoardSchematicViewTests
    {
        // 指定した方眼位置にマスを並べた盤面を作る（イベントは既定＝None）。
        private static BoardDefinition BuildBoard(params Vector2Int[] grids)
        {
            BoardDefinition definition = ScriptableObject.CreateInstance<BoardDefinition>();
            foreach (Vector2Int grid in grids)
            {
                definition.AddCell(new BoardCellDefinition(grid));
            }
            return definition;
        }

        [Test]
        public void nullは1x1の範囲()
        {
            BoardSchematicView.CellBounds bounds = BoardSchematicView.BoundsOf(null);
            Assert.AreEqual(Vector2Int.zero, bounds.Min);
            Assert.AreEqual(Vector2Int.one, bounds.Size);
            Assert.AreEqual(1f, bounds.Aspect);
        }

        [Test]
        public void マスの占める範囲を返す()
        {
            BoardDefinition board = BuildBoard(
                new Vector2Int(1, 2), new Vector2Int(3, 2), new Vector2Int(3, 4));
            BoardSchematicView.CellBounds bounds = BoardSchematicView.BoundsOf(board);
            Assert.AreEqual(new Vector2Int(1, 2), bounds.Min);
            Assert.AreEqual(new Vector2Int(3, 3), bounds.Size); // 列 1〜3・行 2〜4
            Object.DestroyImmediate(board);
        }

        [Test]
        public void 方眼キャンバスの空き行は範囲に含めない()
        {
            // 既定のキャンバスは 5 列 7 行だが、マスは 2×2 の範囲にしか無い。
            BoardDefinition board = BuildBoard(
                new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(1, 1), new Vector2Int(0, 1));
            Assert.AreEqual(new Vector2Int(2, 2), BoardSchematicView.BoundsOf(board).Size);
            Assert.AreEqual(1f, BoardSchematicView.AspectOf(board));
            Object.DestroyImmediate(board);
        }

        [Test]
        public void 横一直線のマップは横長の縦横比になる()
        {
            BoardDefinition board = BuildBoard(
                new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(3, 0));
            Assert.AreEqual(4f, BoardSchematicView.AspectOf(board));
            Object.DestroyImmediate(board);
        }

        [Test]
        public void 縦長のマップは1より小さい縦横比になる()
        {
            BoardDefinition board = BuildBoard(
                new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(1, 3), new Vector2Int(0, 3));
            Assert.AreEqual(0.5f, BoardSchematicView.AspectOf(board)); // 2 列 × 4 行
            Object.DestroyImmediate(board);
        }
    }
}
