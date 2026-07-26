using System.Collections.Generic;
using Main.Board;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public class BoardEventTallyTests
    {
        private static BoardDefinition BuildBoard(params BoardCellEvent[] events)
        {
            BoardDefinition definition = ScriptableObject.CreateInstance<BoardDefinition>();
            for (int i = 0; i < events.Length; i++)
            {
                BoardCellDefinition cell = new(new Vector2Int(i, 0));
                cell.SetEvent(events[i]);
                definition.AddCell(cell);
            }
            return definition;
        }

        [Test]
        public void nullは空を返す()
        {
            Assert.IsEmpty(BoardEventTally.Summarize(null));
        }

        [Test]
        public void スタート_index0_と通常マスは数えない()
        {
            // index 0（スタート）は Item だが除外され、通常マス（None）も出ない。
            BoardDefinition board = BuildBoard(
                BoardCellEvent.Item, BoardCellEvent.None, BoardCellEvent.None);
            Assert.IsEmpty(BoardEventTally.Summarize(board));
            Object.DestroyImmediate(board);
        }

        [Test]
        public void 同じイベントは合算される()
        {
            BoardDefinition board = BuildBoard(
                BoardCellEvent.None, BoardCellEvent.Territory, BoardCellEvent.Territory, BoardCellEvent.Territory);
            IReadOnlyList<(BoardCellEvent Event, int Count)> result = BoardEventTally.Summarize(board);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(BoardCellEvent.Territory, result[0].Event);
            Assert.AreEqual(3, result[0].Count);
            Object.DestroyImmediate(board);
        }

        [Test]
        public void 陣地が先頭_表示順で並ぶ()
        {
            // 出現順は Money→Item→Territory だが、表示順は Territory→Item→MoneyUp。
            BoardDefinition board = BuildBoard(
                BoardCellEvent.None, BoardCellEvent.MoneyUp, BoardCellEvent.Item, BoardCellEvent.Territory);
            IReadOnlyList<(BoardCellEvent Event, int Count)> result = BoardEventTally.Summarize(board);
            Assert.AreEqual(BoardCellEvent.Territory, result[0].Event);
            Assert.AreEqual(BoardCellEvent.Item, result[1].Event);
            Assert.AreEqual(BoardCellEvent.MoneyUp, result[2].Event);
            Object.DestroyImmediate(board);
        }
    }
}
