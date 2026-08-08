using UnityEngine;
using UnityEngine.UIElements;

namespace Main.Board
{
    /// <summary>
    /// 盤面データ（<see cref="BoardDefinition"/>）の形を Painter2D で簡易サムネイル描画する純粋ヘルパー。
    /// マスをグリッド座標に四角で並べ、経路順に線でつないでループを閉じる。画像アセットは使わない。
    /// 各マスはイベント種別ごとの色（<see cref="BoardEventColors"/>・盤面エディタと共通）で塗り、
    /// どんなイベント構成のマップかをサムネイルだけで見分けられるようにする。
    /// マップ選択のカード（小）／大プレビュー、オンラインのルーム作成マップ選択で共用する
    /// （コールバック配線は呼び出し側が持つ）。Main 型にしか依存しないため Main に置く。
    ///
    /// 座標の正規化には方眼キャンバスの寸法（<see cref="BoardDefinition.GridColumns"/> 等）ではなく
    /// 「実際にマスが占めている範囲」（<see cref="BoundsOf"/>）を使う。キャンバスにはマスを置いていない
    /// 行・列が残ることがあり、キャンバス基準で正規化すると盤面が片寄って空白ができてしまうため。
    /// </summary>
    public static class BoardSchematicView
    {
        private static readonly Color LineColor = new(1f, 1f, 1f, 0.35f);
        private static readonly Color StartColor = new(0.92f, 0.78f, 0.35f, 1f);

        /// <summary>盤面のマスが占めている方眼上の範囲（1 マス＝1 単位）。</summary>
        public readonly struct CellBounds
        {
            public CellBounds(Vector2Int min, Vector2Int size)
            {
                Min = min;
                Size = size;
            }

            /// <summary>範囲の左上のマスの方眼位置。</summary>
            public Vector2Int Min { get; }

            /// <summary>範囲に含まれるマス数（列・行）。ともに 1 以上。</summary>
            public Vector2Int Size { get; }

            /// <summary>範囲の縦横比。横長ほど 1 より大きい。</summary>
            public float Aspect => Size.x / (float)Size.y;
        }

        /// <summary>
        /// <paramref name="board"/> の全マスを含む方眼上の範囲を返す純粋関数。
        /// マスが無い／null なら 1×1。
        /// </summary>
        public static CellBounds BoundsOf(BoardDefinition board)
        {
            if (board == null || board.CellCount == 0)
            {
                return new CellBounds(Vector2Int.zero, Vector2Int.one);
            }

            Vector2Int min = board.Cell(0).Grid;
            Vector2Int max = min;
            for (int i = 1; i < board.CellCount; i++)
            {
                Vector2Int grid = board.Cell(i).Grid;
                min = Vector2Int.Min(min, grid);
                max = Vector2Int.Max(max, grid);
            }
            return new CellBounds(min, max - min + Vector2Int.one);
        }

        /// <summary>
        /// <paramref name="board"/> のマスが占めている範囲の縦横比。横長マップほど 1 より大きい。
        /// プレビュー枠をマップの形に合わせるのに使う。null／マス無しなら 1（正方形）。
        /// </summary>
        public static float AspectOf(BoardDefinition board)
        {
            return BoundsOf(board).Aspect;
        }

        /// <summary>
        /// <paramref name="ctx"/> の要素に <paramref name="board"/> の形を描く。
        /// generateVisualContent から呼ぶこと。<paramref name="board"/> が null／マス不足なら何も描かない。
        /// マスが占めている範囲（<see cref="BoundsOf"/>）の縦横比を保って要素へ内接させ、中央に置く
        /// （正方形の枠に横長マップを引き伸ばさない・空の行や列で片寄らせない）。
        /// </summary>
        public static void Draw(MeshGenerationContext ctx, BoardDefinition board)
        {
            if (board == null || board.CellCount < 2)
            {
                return;
            }

            Rect rect = ctx.visualElement.contentRect;
            float pad = Mathf.Min(rect.width, rect.height) * 0.06f;
            float availableWidth = rect.width - pad * 2f;
            float availableHeight = rect.height - pad * 2f;
            if (availableWidth <= 1f || availableHeight <= 1f)
            {
                return;
            }

            // マスが占めている範囲の縦横比を保ったまま利用可能領域へ内接させ、余白は中央寄せで振り分ける。
            CellBounds bounds = BoundsOf(board);
            float aspect = bounds.Aspect;
            float w = Mathf.Min(availableWidth, availableHeight * aspect);
            float h = w / aspect;
            float originX = pad + (availableWidth - w) * 0.5f;
            float originY = pad + (availableHeight - h) * 0.5f;

            // 1 マスぶんの間隔（範囲の縦横比を保っているので縦横で同じ値になる）。
            // マス中心は範囲の左上から数えて (n + 0.5) マスの位置に来る。
            float unit = w / bounds.Size.x;

            Vector2 ToPoint(int index)
            {
                Vector2Int grid = board.Cell(index).Grid;
                return new Vector2(
                    originX + (grid.x - bounds.Min.x + 0.5f) * unit,
                    originY + (grid.y - bounds.Min.y + 0.5f) * unit);
            }

            Painter2D painter = ctx.painter2D;

            // 経路の接続線（最後→最初でループを閉じる）。
            painter.strokeColor = LineColor;
            painter.lineWidth = Mathf.Clamp(unit * 0.12f, 1.5f, 6f);
            painter.lineJoin = LineJoin.Round;
            painter.lineCap = LineCap.Round;
            painter.BeginPath();
            painter.MoveTo(ToPoint(0));
            for (int i = 1; i < board.CellCount; i++)
            {
                painter.LineTo(ToPoint(i));
            }
            painter.LineTo(ToPoint(0));
            painter.Stroke();

            // 各マスを小さな四角で描く。index 0（スタート）は別色、それ以外はイベント種別ごとの色で塗り分ける。
            float half = Mathf.Max(1.5f, unit * 0.2f);
            for (int i = 0; i < board.CellCount; i++)
            {
                Vector2 c = ToPoint(i);
                painter.fillColor = i == 0 ? StartColor : BoardEventColors.Of(board.Cell(i).Event);
                painter.BeginPath();
                painter.MoveTo(new Vector2(c.x - half, c.y - half));
                painter.LineTo(new Vector2(c.x + half, c.y - half));
                painter.LineTo(new Vector2(c.x + half, c.y + half));
                painter.LineTo(new Vector2(c.x - half, c.y + half));
                painter.ClosePath();
                painter.Fill();
            }
        }
    }
}
