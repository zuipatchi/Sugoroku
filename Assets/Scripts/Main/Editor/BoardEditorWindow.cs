using Common.MiniGame;
using Main.Board;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Main.EditorTools
{
    /// <summary>
    /// すごろく盤面（<see cref="BoardDefinition"/>）をビジュアルに編集するエディタウィンドウ。
    /// 方眼をクリックして経路順にマスを置き（クリック順が経路。0＝スタート＝ゴール）、
    /// 選択したマスのイベント・数値・色・アイコンアドレスを右パネルで編集する。
    /// メニュー「Window > Sugoroku > Board Editor」で開く。
    /// </summary>
    public sealed class BoardEditorWindow : EditorWindow
    {
        private const int CellSize = 26;

        private BoardDefinition _target;
        private int _selectedIndex = -1;

        private ObjectField _objectField;
        private IntegerField _columnsField;
        private IntegerField _rowsField;
        private Label _infoLabel;
        private VisualElement _gridContainer;
        private VisualElement _inspector;

        [MenuItem("Window/Sugoroku/Board Editor")]
        public static void Open()
        {
            BoardEditorWindow window = GetWindow<BoardEditorWindow>();
            window.titleContent = new GUIContent("Board Editor");
            window.minSize = new Vector2(560f, 420f);
        }

        private void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.style.paddingLeft = 8f;
            root.style.paddingRight = 8f;
            root.style.paddingTop = 8f;
            root.style.paddingBottom = 8f;

            BuildToolbar(root);

            VisualElement body = new();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1f;
            body.style.marginTop = 8f;
            root.Add(body);

            _gridContainer = new VisualElement();
            _gridContainer.style.flexGrow = 1f;
            body.Add(_gridContainer);

            _inspector = new VisualElement();
            _inspector.style.width = 220f;
            _inspector.style.marginLeft = 8f;
            body.Add(_inspector);

            Rebuild();
        }

        private void BuildToolbar(VisualElement root)
        {
            _objectField = new ObjectField("盤面データ")
            {
                objectType = typeof(BoardDefinition),
                allowSceneObjects = false
            };
            _objectField.RegisterValueChangedCallback(evt =>
            {
                _target = evt.newValue as BoardDefinition;
                _selectedIndex = -1;
                SyncGridSizeFields();
                Rebuild();
            });
            root.Add(_objectField);

            VisualElement toolbar = new();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.marginTop = 4f;

            Button createButton = new(CreateNewAsset) { text = "新規作成" };
            toolbar.Add(createButton);

            Button clearButton = new(ClearPath) { text = "経路クリア" };
            clearButton.style.marginLeft = 8f;
            toolbar.Add(clearButton);

            Button saveButton = new(() => AssetDatabase.SaveAssets()) { text = "保存" };
            saveButton.style.marginLeft = 8f;
            toolbar.Add(saveButton);

            root.Add(toolbar);

            // 方眼サイズ（列・行）の数値入力。フィールドのラベルは既定で最小幅が広く入力欄を潰すため、
            // ラベル幅を絞ってから十分な入力幅を与える。isDelayed=true で Enter／フォーカスアウト時に確定する。
            VisualElement sizeRow = new();
            sizeRow.style.flexDirection = FlexDirection.Row;
            sizeRow.style.marginTop = 6f;

            _columnsField = new IntegerField("列") { value = 5, isDelayed = true };
            ConfigureSizeField(_columnsField);
            _columnsField.RegisterValueChangedCallback(_ => ApplyGridSize());
            sizeRow.Add(_columnsField);

            _rowsField = new IntegerField("行") { value = 7, isDelayed = true };
            ConfigureSizeField(_rowsField);
            _rowsField.style.marginLeft = 12f;
            _rowsField.RegisterValueChangedCallback(_ => ApplyGridSize());
            sizeRow.Add(_rowsField);

            root.Add(sizeRow);

            _infoLabel = new Label();
            _infoLabel.style.marginTop = 4f;
            root.Add(_infoLabel);
        }

        /// <summary>方眼サイズの数値フィールドのラベル幅を絞り、入力欄に十分な幅を与える。</summary>
        private static void ConfigureSizeField(IntegerField field)
        {
            field.style.width = 130f;
            field.labelElement.style.minWidth = 24f;
            field.labelElement.style.width = 24f;
        }

        private void CreateNewAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "盤面データを作成", "BoardDefinition", "asset", "保存先を選択してください");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            BoardDefinition definition = CreateInstance<BoardDefinition>();
            definition.SetGridSize(_columnsField.value, _rowsField.value);
            AssetDatabase.CreateAsset(definition, path);
            AssetDatabase.SaveAssets();

            _target = definition;
            _selectedIndex = -1;
            _objectField.SetValueWithoutNotify(definition);
            Rebuild();
        }

        private void SyncGridSizeFields()
        {
            if (_target == null)
            {
                return;
            }
            _columnsField.SetValueWithoutNotify(_target.GridColumns);
            _rowsField.SetValueWithoutNotify(_target.GridRows);
        }

        private void ApplyGridSize()
        {
            if (_target == null)
            {
                return;
            }

            int columns = Mathf.Max(2, _columnsField.value);
            int rows = Mathf.Max(2, _rowsField.value);
            // 2 未満を入力したら 2 にスナップして表示へ戻す。
            _columnsField.SetValueWithoutNotify(columns);
            _rowsField.SetValueWithoutNotify(rows);

            Undo.RecordObject(_target, "盤面のサイズ変更");
            _target.SetGridSize(columns, rows);
            EditorUtility.SetDirty(_target);
            Rebuild();
        }

        private void ClearPath()
        {
            if (_target == null)
            {
                return;
            }
            Undo.RecordObject(_target, "経路クリア");
            _target.ClearCells();
            EditorUtility.SetDirty(_target);
            _selectedIndex = -1;
            Rebuild();
        }

        private void Rebuild()
        {
            RebuildGrid();
            RebuildInspector();
            UpdateInfo();
        }

        private void UpdateInfo()
        {
            if (_target == null)
            {
                _infoLabel.text = "盤面データを選択、または「新規作成」してください。";
                return;
            }
            _infoLabel.text = $"マス数: {_target.CellCount}（方眼のマスをクリックで経路に追加／既存マスをクリックで選択）";
        }

        private void RebuildGrid()
        {
            _gridContainer.Clear();
            if (_target == null)
            {
                return;
            }

            for (int row = 0; row < _target.GridRows; row++)
            {
                VisualElement rowElement = new();
                rowElement.style.flexDirection = FlexDirection.Row;
                for (int column = 0; column < _target.GridColumns; column++)
                {
                    rowElement.Add(BuildGridCell(new Vector2Int(column, row)));
                }
                _gridContainer.Add(rowElement);
            }
        }

        private VisualElement BuildGridCell(Vector2Int grid)
        {
            int pathIndex = _target.IndexOfGrid(grid);

            Button cell = new(() => OnGridCellClicked(grid));
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
                BoardCellDefinition definition = _target.Cell(pathIndex);
                background = definition.HasCustomColor
                    ? definition.Color
                    : (pathIndex == 0 ? new Color(0.27f, 0.35f, 0.7f) : new Color(0.35f, 0.45f, 0.55f));
            }
            cell.style.backgroundColor = background;

            bool selected = pathIndex >= 0 && pathIndex == _selectedIndex;
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

        private void OnGridCellClicked(Vector2Int grid)
        {
            if (_target == null)
            {
                return;
            }

            int existing = _target.IndexOfGrid(grid);
            if (existing >= 0)
            {
                _selectedIndex = existing;
                Rebuild();
                return;
            }

            Undo.RecordObject(_target, "マス追加");
            _target.AddCell(new BoardCellDefinition(grid));
            EditorUtility.SetDirty(_target);
            _selectedIndex = _target.CellCount - 1;
            Rebuild();
        }

        private void RebuildInspector()
        {
            _inspector.Clear();
            if (_target == null || _selectedIndex < 0 || _selectedIndex >= _target.CellCount)
            {
                return;
            }

            BoardCellDefinition cell = _target.Cell(_selectedIndex);

            Label title = new(_selectedIndex == 0 ? "マス 0（スタート＝ゴール）" : $"マス {_selectedIndex}");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 4f;
            _inspector.Add(title);

            EnumField eventField = new("イベント", cell.Event);
            eventField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(_target, "イベント変更");
                cell.SetEvent((BoardCellEvent)evt.newValue);
                EditorUtility.SetDirty(_target);
                Rebuild();
            });
            _inspector.Add(eventField);

            if (cell.Event == BoardCellEvent.Forward
                || cell.Event == BoardCellEvent.Back
                || cell.Event == BoardCellEvent.Rest
                || cell.Event == BoardCellEvent.MoneyUp
                || cell.Event == BoardCellEvent.MoneyDown)
            {
                IntegerField amountField = new(AmountLabel(cell.Event)) { value = cell.Amount };
                amountField.RegisterValueChangedCallback(evt =>
                {
                    Undo.RecordObject(_target, "数値変更");
                    cell.SetAmount(Mathf.Max(1, evt.newValue));
                    EditorUtility.SetDirty(_target);
                    RebuildGrid();
                });
                _inspector.Add(amountField);
            }

            if (cell.Event == BoardCellEvent.MiniGame)
            {
                EnumField miniGameField = new("ミニゲーム", cell.MiniGame);
                miniGameField.RegisterValueChangedCallback(evt =>
                {
                    Undo.RecordObject(_target, "ミニゲーム変更");
                    cell.SetMiniGame((MiniGameId)evt.newValue);
                    EditorUtility.SetDirty(_target);
                });
                _inspector.Add(miniGameField);
            }

            ColorField colorField = new("色") { value = cell.HasCustomColor ? cell.Color : BoardCellDefinition.UnsetColor };
            colorField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(_target, "色変更");
                cell.SetColor(evt.newValue);
                EditorUtility.SetDirty(_target);
                RebuildGrid();
            });
            _inspector.Add(colorField);

            TextField iconField = new("アイコンアドレス") { value = cell.IconAddress };
            iconField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(_target, "アイコン変更");
                cell.SetIconAddress(evt.newValue);
                EditorUtility.SetDirty(_target);
            });
            _inspector.Add(iconField);

            Button removeButton = new(RemoveSelected) { text = "このマスを削除" };
            removeButton.style.marginTop = 8f;
            _inspector.Add(removeButton);
        }

        /// <summary>数値フィールドのラベル。お金イベントは「金額」、それ以外は汎用の「数値」。</summary>
        private static string AmountLabel(BoardCellEvent cellEvent)
        {
            return cellEvent == BoardCellEvent.MoneyUp || cellEvent == BoardCellEvent.MoneyDown
                ? "金額"
                : "数値";
        }

        private void RemoveSelected()
        {
            if (_target == null || _selectedIndex < 0 || _selectedIndex >= _target.CellCount)
            {
                return;
            }
            Undo.RecordObject(_target, "マス削除");
            _target.RemoveCellAt(_selectedIndex);
            EditorUtility.SetDirty(_target);
            _selectedIndex = -1;
            Rebuild();
        }
    }
}
