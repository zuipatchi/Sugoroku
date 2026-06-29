namespace Main.Board
{
    /// <summary>
    /// すごろく盤（ループ）の純粋関数群。コマ位置の前進・周回判定と、
    /// 外周リング上のマス index を矩形グリッドの (列, 行) 座標へ変換する。
    ///
    /// リングの規約:
    /// - マス 0 は左上（スタート＝ゴール）。そこから時計回りに番号が増える。
    /// - 上辺 → 右辺 → 下辺 → 左辺 の順に外周をたどる。
    /// - 外周のマス総数は <see cref="PerimeterCellCount"/>（= 2 * 列 + 2 * 行 - 4）。
    /// </summary>
    public static class BoardMath
    {
        /// <summary>列数 <paramref name="columns"/>・行数 <paramref name="rows"/> の矩形リングを構成する外周マス数。</summary>
        public static int PerimeterCellCount(int columns, int rows)
        {
            return 2 * columns + 2 * rows - 4;
        }

        /// <summary>
        /// 現在位置 <paramref name="current"/> から <paramref name="steps"/> マス進めた位置。
        /// スタート（0）を越えるとループして先頭へ戻る。
        /// </summary>
        public static int Advance(int current, int steps, int cellCount)
        {
            return Mod(current + steps, cellCount);
        }

        /// <summary>
        /// 現在位置 <paramref name="current"/> から <paramref name="steps"/> 進むと、
        /// スタート（＝ゴール、0）に到達または通過して周回が完了するか。
        /// </summary>
        public static bool CompletesLap(int current, int steps, int cellCount)
        {
            return current + steps >= cellCount;
        }

        /// <summary>
        /// 外周リング上のマス <paramref name="index"/> を、矩形グリッドの (列, 行) 座標へ変換する。
        /// 0 は左上 (0, 0) で、時計回りに上辺 → 右辺 → 下辺 → 左辺をたどる。
        /// </summary>
        public static (int Column, int Row) CellGridPosition(int index, int columns, int rows)
        {
            int topEnd = columns;                 // 上辺: index [0, columns-1]
            int rightEnd = topEnd + (rows - 1);   // 右辺: index [columns, columns+rows-2]
            int bottomEnd = rightEnd + (columns - 1); // 下辺: index [..]

            if (index < topEnd)
            {
                // 上辺を左→右
                return (index, 0);
            }
            if (index < rightEnd)
            {
                // 右辺を上→下
                return (columns - 1, index - topEnd + 1);
            }
            if (index < bottomEnd)
            {
                // 下辺を右→左
                return (columns - 1 - (index - rightEnd + 1), rows - 1);
            }
            // 左辺を下→上
            return (0, rows - 1 - (index - bottomEnd + 1));
        }

        private static int Mod(int a, int m)
        {
            return ((a % m) + m) % m;
        }
    }
}
