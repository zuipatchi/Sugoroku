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

        /// <summary>経路に含まれない空マスの色。</summary>
        public static readonly Color EmptyCellColor = new(0.2f, 0.2f, 0.24f);

        /// <summary>
        /// スタート（0＝S/G）マスの専用色（イベントに依らず固定）。マップ選択のサムネイルと
        /// 同じ色を使うため、ランタイム共通の <see cref="BoardEventColors.Start"/> に委譲する。
        /// </summary>
        public static Color StartCellColor => BoardEventColors.Start;

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
                background = EmptyCellColor;
            }
            else
            {
                BoardCellDefinition definition = target.Cell(pathIndex);
                // カスタム色があれば最優先。無ければイベント種別ごとの色で塗り、
                // どのマスに何のイベントが設定されているか一目で分かるようにする。
                // スタート（0＝S/G）だけはイベントに依らず専用色で目立たせる。
                background = definition.HasCustomColor
                    ? definition.Color
                    : (pathIndex == 0 ? StartCellColor : EventColor(definition.Event));
            }
            cell.style.backgroundColor = background;
            // 通常マス（イベント None）の白のように明るい地でも経路番号が読めるよう、地の明るさで文字色を変える。
            cell.style.color = IsLightColor(background)
                ? new Color(0.12f, 0.12f, 0.16f)
                : new Color(0.94f, 0.95f, 1f);

            bool selected = pathIndex >= 0 && pathIndex == selectedIndex;
            float borderWidth = selected ? 2f : 1f;
            Color borderColor = selected ? Color.white
                : (pathIndex == 0 ? BoardEventColors.StartOutline : new Color(0f, 0f, 0f, 0.4f));
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

        /// <summary>
        /// イベント種別ごとのマス背景色。カスタム色が未設定のマスの塗り分けと、
        /// エディタの凡例（色→イベントの対応表）で共通に使う。配色の実体は
        /// ランタイム共通の <see cref="BoardEventColors"/>（マップ選択のサムネイルと同じ色）。
        /// </summary>
        public static Color EventColor(BoardCellEvent cellEvent)
        {
            return BoardEventColors.Of(cellEvent);
        }

        /// <summary>マスの地が明るいか（知覚輝度で判定）。マス上の文字色を黒寄り／白寄りに切り替えるのに使う。</summary>
        private static bool IsLightColor(Color color)
        {
            return (0.299f * color.r) + (0.587f * color.g) + (0.114f * color.b) > 0.6f;
        }
    }
}
