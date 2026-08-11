using System.Collections.Generic;

namespace Main.Board
{
    /// <summary>
    /// 盤面のイベント構成を数える純粋ヘルパー。マップ選択で「どんなマップか」を内訳表示するために使う。
    /// スタート（経路 index 0）と通常マス（<see cref="BoardCellEvent.None"/>）は除外し、
    /// プレイに影響するイベントだけを表示順に並べて数える。
    /// </summary>
    public static class BoardEventTally
    {
        /// <summary>
        /// 内訳に出すイベントと並び順（勝敗を左右する陣地を先頭に、以降は影響の大きい順）。
        /// マップ選択の内訳だけでなく、Home のルール説明でマスの種類を並べるのにも使う
        /// （どちらも同じ順で読めるよう、並びはここ 1 か所に置く）。
        /// </summary>
        public static readonly IReadOnlyList<BoardCellEvent> DisplayOrder = new[]
        {
            BoardCellEvent.Territory,
            BoardCellEvent.Item,
            BoardCellEvent.MiniGame,
            BoardCellEvent.MoneyUp,
            BoardCellEvent.MoneyDown,
            BoardCellEvent.Forward,
            BoardCellEvent.Back,
        };

        /// <summary>
        /// <paramref name="board"/> に含まれるイベントを表示順に数え、1 個以上あるものだけを返す。
        /// スタート（index 0）は数えない。<paramref name="board"/> が null なら空。
        /// </summary>
        public static IReadOnlyList<(BoardCellEvent Event, int Count)> Summarize(BoardDefinition board)
        {
            List<(BoardCellEvent, int)> result = new();
            if (board == null)
            {
                return result;
            }

            Dictionary<BoardCellEvent, int> counts = new();
            for (int i = 1; i < board.CellCount; i++)
            {
                BoardCellEvent cellEvent = board.Cell(i).Event;
                counts.TryGetValue(cellEvent, out int current);
                counts[cellEvent] = current + 1;
            }

            foreach (BoardCellEvent cellEvent in DisplayOrder)
            {
                if (counts.TryGetValue(cellEvent, out int count) && count > 0)
                {
                    result.Add((cellEvent, count));
                }
            }
            return result;
        }
    }
}
