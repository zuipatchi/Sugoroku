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
    /// </summary>
    public static class BoardSchematicView
    {
        private static readonly Color LineColor = new(1f, 1f, 1f, 0.35f);
        private static readonly Color StartColor = new(0.92f, 0.78f, 0.35f, 1f);

        /// <summary>
        /// <paramref name="ctx"/> の要素いっぱいに <paramref name="board"/> の形を描く。
        /// generateVisualContent から呼ぶこと。<paramref name="board"/> が null／マス不足なら何も描かない。
        /// </summary>
        public static void Draw(MeshGenerationContext ctx, BoardDefinition board)
        {
            if (board == null || board.CellCount < 2)
            {
                return;
            }

            Rect rect = ctx.visualElement.contentRect;
            float pad = Mathf.Min(rect.width, rect.height) * 0.12f;
            float w = rect.width - pad * 2f;
            float h = rect.height - pad * 2f;
            if (w <= 1f || h <= 1f)
            {
                return;
            }

            int cols = board.GridColumns;
            int rows = board.GridRows;

            Vector2 ToPoint(int index)
            {
                // マス中心の算出は Main のロジックを再利用する（テスト済みの純粋関数）。
                Vector2 local = BoardLayoutCalculator.CellCenterPixels(board.Cell(index).Grid, cols, rows, w, h);
                return new Vector2(pad + local.x, pad + local.y);
            }

            Painter2D painter = ctx.painter2D;

            // 経路の接続線（最後→最初でループを閉じる）。
            painter.strokeColor = LineColor;
            painter.lineWidth = Mathf.Clamp(Mathf.Min(rect.width, rect.height) * 0.03f, 1.5f, 6f);
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
            float half = Mathf.Max(1.5f, Mathf.Min(rect.width, rect.height) * 0.03f);
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
