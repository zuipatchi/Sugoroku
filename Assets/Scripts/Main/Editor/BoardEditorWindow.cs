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
    /// 方眼グリッドの描画は <see cref="BoardEditorGridView"/>、右パネルの編集 UI は
    /// <see cref="BoardCellInspector"/> に委譲し、本体はツールバーとアセット操作・状態の統括を担う。
    /// </summary>
    public sealed class BoardEditorWindow : EditorWindow
    {
        private BoardDefinition _target;
        private int _selectedIndex = -1;

        private ObjectField _objectField;
        private IntegerField _columnsField;
        private IntegerField _rowsField;
        private Label _infoLabel;
        private BoardEditorGridView _gridView;
        private BoardCellInspector _cellInspector;

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

            VisualElement gridContainer = new();
            gridContainer.style.flexGrow = 1f;
            body.Add(gridContainer);

            VisualElement inspectorContainer = new();
            inspectorContainer.style.width = 220f;
            inspectorContainer.style.marginLeft = 8f;
            body.Add(inspectorContainer);

            _gridView = new BoardEditorGridView(gridContainer, OnGridCellClicked);
            _cellInspector = new BoardCellInspector(inspectorContainer, Rebuild, RebuildGrid, RemoveSelected);

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
            _cellInspector.Rebuild(_target, _selectedIndex);
            UpdateInfo();
        }

        /// <summary>方眼グリッドのみ再描画する（数値・色の変更など、選択やインスペクタが変わらないとき）。</summary>
        private void RebuildGrid()
        {
            _gridView.Rebuild(_target, _selectedIndex);
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
