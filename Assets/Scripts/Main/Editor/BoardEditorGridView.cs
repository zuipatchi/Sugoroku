using System;
using Main.Board;
using UnityEngine;
using UnityEngine.UIElements;

namespace Main.EditorTools
{
    /// <summary>
    /// 盤面エディタ（<see cref="BoardEditorWindow"/>）の方眼グリッド表示部。
    /// 盤面データの方眼をボタンのマス目として構築し、経路上のマスには経路番号（0 は S/G）と
    /// データの色を反映する。マスのクリックはコンストラクタで受けたコールバックへ通知する。
    /// </summary>
    internal sealed class BoardEditorGridView
    {
        private const int CellSize = 26;

        private readonly VisualElement _container;
        private readonly Action<Vector2Int> _onCellClicked;

        /// <param name="container">グリッドを構築する親要素。</param>
        /// <param name="onCellClicked">方眼のマスがクリックされたときの通知（引数は方眼位置）。</param>
        public BoardEditorGridView(VisualElement container, Action<Vector2Int> onCellClicked)
        {
            _container = container;
            _onCellClicked = onCellClicked;
        }

        /// <summary>グリッド全体を作り直す。<paramref name="target"/> が null なら空にする。</summary>
        public void Rebuild(BoardDefinition target, int selectedIndex)
        {
            _container.Clear();
            if (target == null)
            {
                return;
            }

            for (int row = 0; row < target.GridRows; row++)
            {
                VisualElement rowElement = new();
                rowElement.style.flexDirection = FlexDirection.Row;
                for (int column = 0; column < target.GridColumns; column++)
                {
                    rowElement.Add(BuildGridCell(target, selectedIndex, new Vector2Int(column, row)));
                }
                _container.Add(rowElement);
            }
        }

        private VisualElement BuildGridCell(BoardDefinition target, int selectedIndex, Vector2Int grid)
        {
            int pathIndex = target.IndexOfGrid(grid);

            Button cell = new(() => _onCellClicked(grid));
            cell.text = pathIndex >= 0 ? (pathIndex == 0 ? "S/G" : pathIndex.ToString()) : string.Empty;
            cell.style.width = CellSize;
            cell.style.height = CellSize;
            cell.style.marginLeft = 1f;
            cell.style.marginRight = 1f;
            cell.style.marginTop = 1f;
            cell.style.marginBottom = 1f;
            cell.style.fontSize = 9f;

            Color background;
            if (pathIndex < 0)
            {
                background = new Color(0.2f, 0.2f, 0.24f);
            }
            else
            {
                BoardCellDefinition definition = target.Cell(pathIndex);
                background = definition.HasCustomColor
                    ? definition.Color
                    : (pathIndex == 0 ? new Color(0.27f, 0.35f, 0.7f) : new Color(0.35f, 0.45f, 0.55f));
            }
            cell.style.backgroundColor = background;

            bool selected = pathIndex >= 0 && pathIndex == selectedIndex;
            float borderWidth = selected ? 2f : 1f;
            Color borderColor = selected ? Color.white
                : (pathIndex == 0 ? new Color(1f, 0.85f, 0.4f) : new Color(0f, 0f, 0f, 0.4f));
            cell.style.borderLeftWidth = borderWidth;
            cell.style.borderRightWidth = borderWidth;
            cell.style.borderTopWidth = borderWidth;
            cell.style.borderBottomWidth = borderWidth;
            cell.style.borderLeftColor = borderColor;
            cell.style.borderRightColor = borderColor;
            cell.style.borderTopColor = borderColor;
            cell.style.borderBottomColor = borderColor;

            return cell;
        }
    }
}
