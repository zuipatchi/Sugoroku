using UnityEngine;
using UnityEngine.UIElements;

namespace Main.Board
{
    /// <summary>
    /// すごろく盤のレイアウト計算と描画補助。リング領域の中央配置（<see cref="LayoutBoardArea"/>）、
    /// マスのリサイズ、マス中心座標の算出（%／ピクセル）、マス中心を経路順に結ぶ接続線の描画を担う。
    /// 計算の本体は static な純粋関数（<see cref="CalculateArea"/> / <see cref="CellCenterPercent"/> /
    /// <see cref="CellCenterPixels"/>）に切り出してあり、単体テスト可能。
    /// </summary>
    public sealed class BoardLayoutCalculator
    {
        /// <summary>リング領域の配置結果（ピクセル）。</summary>
        public readonly struct AreaLayout
        {
            public AreaLayout(float left, float top, float width, float height, float cellSize)
            {
                Left = left;
                Top = top;
                Width = width;
                Height = height;
                CellSize = cellSize;
            }

            /// <summary>リング領域（マス中心の矩形）の左端。</summary>
            public float Left { get; }

            /// <summary>リング領域の上端。</summary>
            public float Top { get; }

            /// <summary>リング領域の幅（マス中心間の矩形）。</summary>
            public float Width { get; }

            /// <summary>リング領域の高さ。</summary>
            public float Height { get; }

            /// <summary>各マスの一辺。</summary>
            public float CellSize { get; }
        }

        private readonly BoardDefinition _definition;
        private readonly VisualElement _boardArea;
        private readonly VisualElement _linesElement;
        private readonly VisualElement[] _cells;
        private readonly float _cellFillRatio;
        private readonly int _gridColumns;
        private readonly int _gridRows;
        private readonly int _cellCount;

        public BoardLayoutCalculator(
            BoardDefinition definition,
            VisualElement boardArea,
            VisualElement linesElement,
            VisualElement[] cells,
            float cellFillRatio)
        {
            _definition = definition;
            _boardArea = boardArea;
            _linesElement = linesElement;
            _cells = cells;
            _cellFillRatio = cellFillRatio;
            _gridColumns = definition.GridColumns;
            _gridRows = definition.GridRows;
            _cellCount = definition.CellCount;
        }

        /// <summary>
        /// 盤面リング領域を、グリッドの (列-1):(行-1) のアスペクト比を保って利用可能領域に内接させ、
        /// 中央へ配置する。%指定のままだと縦横でマス間隔が不均一になるため、ここでピクセルで整える。
        /// 上マージンを取り、右上のオプションアイコン（Common）を避ける。
        /// </summary>
        public void LayoutBoardArea()
        {
            VisualElement container = _boardArea?.parent;
            if (container == null)
            {
                return;
            }

            float containerWidth = container.resolvedStyle.width;
            float containerHeight = container.resolvedStyle.height;
            if (containerWidth <= 0f || containerHeight <= 0f)
            {
                return;
            }

            AreaLayout layout = CalculateArea(containerWidth, containerHeight, _gridColumns, _gridRows, _cellFillRatio);
            _boardArea.style.left = layout.Left;
            _boardArea.style.top = layout.Top;
            _boardArea.style.width = layout.Width;
            _boardArea.style.height = layout.Height;

            ResizeCells(layout.CellSize);

            // 接続線はマス中心を結ぶので、領域サイズが変わったら再描画する。
            _linesElement?.MarkDirtyRepaint();
        }

        /// <summary>
        /// コンテナ寸法からリング領域の配置を計算する純粋関数。
        /// マス中心間隔 spacing を、マスの実寸まで含めた盤面が利用可能領域に収まるように決める。
        /// 最外周マスは領域端から半マス（= spacing * cellFillRatio / 2）ずつ外へはみ出すので、
        /// 端の 1 マスぶん（両側で cellFillRatio）を余分に見込む。これで画面端に余白が残る。
        /// </summary>
        public static AreaLayout CalculateArea(
            float containerWidth,
            float containerHeight,
            int gridColumns,
            int gridRows,
            float cellFillRatio)
        {
            // 余白（右上のオプションアイコンは上マージンで避ける）。
            float marginX = containerWidth * 0.08f;
            float marginTop = containerHeight * 0.12f;
            float marginBottom = containerHeight * 0.08f;
            float availableWidth = containerWidth - marginX * 2f;
            float availableHeight = containerHeight - marginTop - marginBottom;

            float spanColumns = (gridColumns - 1) + cellFillRatio;
            float spanRows = (gridRows - 1) + cellFillRatio;
            float spacing = Mathf.Min(availableWidth / spanColumns, availableHeight / spanRows);

            float cellSize = spacing * cellFillRatio;
            float areaWidth = spacing * (gridColumns - 1);   // リング領域＝マス中心の矩形
            float areaHeight = spacing * (gridRows - 1);
            float boundingWidth = areaWidth + cellSize;        // マスの実寸まで含めた見た目の外形
            float boundingHeight = areaHeight + cellSize;

            // 外形を余白内で中央寄せし、リング領域（マス中心の矩形）は半マス内側へ置く。
            float left = (containerWidth - boundingWidth) * 0.5f + cellSize * 0.5f;
            float top = marginTop + (availableHeight - boundingHeight) * 0.5f + cellSize * 0.5f;
            return new AreaLayout(left, top, areaWidth, areaHeight, cellSize);
        }

        /// <summary>各マスの一辺を <paramref name="cellSize"/> に合わせる（マス中心間隔より小さくして隙間を作る）。</summary>
        private void ResizeCells(float cellSize)
        {
            if (_cells == null || cellSize <= 0f)
            {
                return;
            }
            foreach (VisualElement cell in _cells)
            {
                if (cell == null)
                {
                    continue;
                }
                cell.style.width = cellSize;
                cell.style.height = cellSize;
            }
        }

        /// <summary>マス <paramref name="index"/> の中心（%座標）に要素を配置する。</summary>
        public void PlaceAtCell(VisualElement element, int index)
        {
            if (index < 0 || index >= _definition.CellCount)
            {
                return;
            }
            Vector2 percent = CellCenterPercent(_definition.Cell(index).Grid, _gridColumns, _gridRows);
            element.style.left = Length.Percent(percent.x);
            element.style.top = Length.Percent(percent.y);
        }

        /// <summary>方眼位置 <paramref name="grid"/> のマス中心を、リング領域内の %座標（0〜100）で返す純粋関数。</summary>
        public static Vector2 CellCenterPercent(Vector2Int grid, int gridColumns, int gridRows)
        {
            float left = gridColumns > 1 ? grid.x / (float)(gridColumns - 1) * 100f : 50f;
            float top = gridRows > 1 ? grid.y / (float)(gridRows - 1) * 100f : 50f;
            return new Vector2(left, top);
        }

        /// <summary>方眼位置 <paramref name="grid"/> のマス中心を、寸法 <paramref name="width"/>×<paramref name="height"/> の領域内のピクセル座標で返す純粋関数。</summary>
        public static Vector2 CellCenterPixels(Vector2Int grid, int gridColumns, int gridRows, float width, float height)
        {
            float x = gridColumns > 1 ? grid.x / (float)(gridColumns - 1) * width : width * 0.5f;
            float y = gridRows > 1 ? grid.y / (float)(gridRows - 1) * height : height * 0.5f;
            return new Vector2(x, y);
        }

        /// <summary>マス中心を経路順に結ぶ接続線を描く（最後のマスからスタートへ戻ってループを閉じる）。</summary>
        public void DrawConnectingLines(MeshGenerationContext context)
        {
            if (_cellCount < 2)
            {
                return;
            }

            Rect content = context.visualElement.contentRect;
            if (content.width <= 0f || content.height <= 0f)
            {
                return;
            }

            float spacing = _gridColumns > 1 ? content.width / (_gridColumns - 1) : content.width;
            Painter2D painter = context.painter2D;
            painter.lineWidth = Mathf.Clamp(spacing * 0.1f, 2f, 8f);
            painter.strokeColor = new Color(1f, 1f, 1f, 0.35f);
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;

            painter.BeginPath();
            painter.MoveTo(CellCenterInContent(0, content));
            for (int i = 1; i < _cellCount; i++)
            {
                painter.LineTo(CellCenterInContent(i, content));
            }
            painter.LineTo(CellCenterInContent(0, content)); // ループを閉じる
            painter.Stroke();
        }

        /// <summary>マス <paramref name="index"/> の中心を、線描画領域 <paramref name="content"/> 内のピクセル座標で返す。</summary>
        private Vector2 CellCenterInContent(int index, Rect content)
        {
            return CellCenterPixels(_definition.Cell(index).Grid, _gridColumns, _gridRows, content.width, content.height);
        }
    }
}
