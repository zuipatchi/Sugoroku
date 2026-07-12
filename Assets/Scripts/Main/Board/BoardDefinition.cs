using System.Collections.Generic;
using UnityEngine;

namespace Main.Board
{
    /// <summary>
    /// すごろく 1 盤面ぶんのデータ。方眼キャンバス（<see cref="GridColumns"/>×<see cref="GridRows"/>）の上に、
    /// マス（<see cref="BoardCellDefinition"/>）を経路順に並べて保持する。index 0 がスタート＝ゴール。
    /// アセットとして作成・編集し（メニュー「Sugoroku/Board Definition」または盤面エディタ）、
    /// 実行時は <see cref="BoardPresenter"/> がこのデータを読んで盤面を描画する。
    /// アセット未割り当て時は <see cref="CreateRectangular"/> が従来の矩形リングを生成してフォールバックする。
    /// </summary>
    [CreateAssetMenu(menuName = "Sugoroku/Board Definition", fileName = "BoardDefinition")]
    public sealed class BoardDefinition : ScriptableObject
    {
        /// <summary>
        /// スタート＝ゴール（経路 index 0）のマスに使う固定の画像アドレス（Addressables）。
        /// イベント種別ごとの画像（<see cref="EventIconAddress"/>）より優先する。未配置なら記号表示にフォールバックする。
        /// </summary>
        public const string StartCellIconAddress = "Board/Start";

        /// <summary>
        /// アイテム取得マス（<see cref="BoardCellEvent.Item"/>）に使う既定の画像アドレス（Addressables）。
        /// 盤面がイベント画像（<see cref="EventIconAddress"/>）を明示設定していないときのフォールバックに使う。
        /// </summary>
        public const string ItemCellIconAddress = "Board/Item";

        [SerializeField] private string _displayName = string.Empty;
        [SerializeField] private int _gridColumns = 5;
        [SerializeField] private int _gridRows = 7;
        [SerializeField] private string _frameAddress = string.Empty;
        [SerializeField] private List<BoardEventArt> _eventArt = new();
        [SerializeField] private List<BoardCellDefinition> _cells = new();

        /// <summary>盤面の表示名（任意）。</summary>
        public string DisplayName => _displayName;

        /// <summary>全マス共通で画像の上に重ねる枠画像の Addressables アドレス（任意）。</summary>
        public string FrameAddress => _frameAddress;

        /// <summary>枠画像が指定されているか。</summary>
        public bool HasFrame => !string.IsNullOrEmpty(_frameAddress);

        /// <summary>
        /// イベント種別 <paramref name="cellEvent"/> のマスに貼る画像の Addressables アドレスを返す。
        /// 未設定なら空文字（呼び出し側は記号表示にフォールバックする）。
        /// </summary>
        public string EventIconAddress(BoardCellEvent cellEvent)
        {
            for (int i = 0; i < _eventArt.Count; i++)
            {
                if (_eventArt[i].Event == cellEvent)
                {
                    return _eventArt[i].Address;
                }
            }
            return string.Empty;
        }

        /// <summary>方眼キャンバスの列数（座標の正規化とレイアウト比に使う）。</summary>
        public int GridColumns => _gridColumns;

        /// <summary>方眼キャンバスの行数。</summary>
        public int GridRows => _gridRows;

        /// <summary>経路上のマス総数。</summary>
        public int CellCount => _cells.Count;

        /// <summary>経路 <paramref name="index"/> 番目のマス定義（0 がスタート＝ゴール）。</summary>
        public BoardCellDefinition Cell(int index)
        {
            return _cells[index];
        }

        /// <summary>経路順のマス一覧（読み取り専用）。</summary>
        public IReadOnlyList<BoardCellDefinition> Cells => _cells;

        /// <summary>
        /// 従来の矩形リング盤面をメモリ上に生成する（アセット未割り当て時のフォールバック）。
        /// 経路・座標は <see cref="BoardMath"/> が計算し、イベント・色は既定（None／未指定）にする。
        /// </summary>
        public static BoardDefinition CreateRectangular(int columns, int rows)
        {
            BoardDefinition definition = CreateInstance<BoardDefinition>();
            definition._displayName = $"Rectangular {columns}x{rows}";
            definition._gridColumns = columns;
            definition._gridRows = rows;

            int cellCount = BoardMath.PerimeterCellCount(columns, rows);
            definition._cells = new List<BoardCellDefinition>(cellCount);
            for (int i = 0; i < cellCount; i++)
            {
                (int column, int row) = BoardMath.CellGridPosition(i, columns, rows);
                definition._cells.Add(new BoardCellDefinition(new Vector2Int(column, row)));
            }
            return definition;
        }

        // --- 以下は盤面エディタ専用の編集 API。実行時のゲームロジックからは呼ばない。---

        public void SetDisplayName(string displayName)
        {
            _displayName = displayName ?? string.Empty;
        }

        public void SetFrameAddress(string address)
        {
            _frameAddress = address ?? string.Empty;
        }

        /// <summary>イベント種別ごとの画像アドレスを設定する（既存があれば更新、なければ追加）。</summary>
        public void SetEventIconAddress(BoardCellEvent cellEvent, string address)
        {
            for (int i = 0; i < _eventArt.Count; i++)
            {
                if (_eventArt[i].Event == cellEvent)
                {
                    _eventArt[i].SetAddress(address);
                    return;
                }
            }
            _eventArt.Add(new BoardEventArt(cellEvent, address));
        }

        public void SetGridSize(int columns, int rows)
        {
            _gridColumns = Mathf.Max(2, columns);
            _gridRows = Mathf.Max(2, rows);
        }

        public void AddCell(BoardCellDefinition cell)
        {
            _cells.Add(cell);
        }

        public void RemoveCellAt(int index)
        {
            _cells.RemoveAt(index);
        }

        public void ClearCells()
        {
            _cells.Clear();
        }

        /// <summary>方眼位置 <paramref name="grid"/> のマスが経路にあればその index、なければ -1。</summary>
        public int IndexOfGrid(Vector2Int grid)
        {
            for (int i = 0; i < _cells.Count; i++)
            {
                if (_cells[i].Grid == grid)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
