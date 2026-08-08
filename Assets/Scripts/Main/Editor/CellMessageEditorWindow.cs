using System;
using System.Collections.Generic;
using Main.Board;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Main.EditorTools
{
    /// <summary>
    /// マスに止まったときに見せる文言（<see cref="BoardCellMessageCatalog"/> 資産）を編集するエディタウィンドウ。
    /// メニュー「Window > Sugoroku > Cell Message Editor」で開く。
    ///
    /// 左のプール一覧（スタート＋イベント種別）から編集対象を選び、右で文言を 1 行ずつ足す・直す・消す。
    /// 資産なので**編集しても再コンパイルは走らない**（べた書きだった頃との違い）。
    /// 盤面エディタ（<see cref="BoardEditorWindow"/>）と同じく、資産の新規作成／保存もここから行う。
    /// </summary>
    public sealed class CellMessageEditorWindow : EditorWindow
    {
        /// <summary>左の一覧に並べるプール 1 つぶんの情報（スタート専用プールもここに 1 件として混ぜる）。</summary>
        private readonly struct PoolRef
        {
            public PoolRef(bool isStart, BoardCellEvent cellEvent, string label, Color color)
            {
                IsStart = isStart;
                Event = cellEvent;
                Label = label;
                Color = color;
            }

            public bool IsStart { get; }
            public BoardCellEvent Event { get; }
            public string Label { get; }
            public Color Color { get; }
        }

        // プレビューをゲーム画面と同じ折り返しにするための寸法。**実機のレイアウトからそのまま持ってくる**：
        // 幅 = 540（Panel Settings の ReferenceResolution.x。ScreenMatchMode は幅合わせなので端末に依らず一定）
        //      × 90%（Board.uss の .cell-message の max-width）＝ 486px。UI Toolkit は border-box なので
        //      左右パディング（24px×2）はこの内側に入る。
        // これらを変えたときはここも合わせる（ずれるとプレビューの折り返しが実機と食い違う）。
        private const float PreviewMaxWidthPx = 486f;
        private const float PreviewFontSizePx = 30f;
        private const float PreviewPaddingXPx = 24f;
        private const float PreviewPaddingYPx = 8f;
        // 全角は 1em＝文字サイズと同じ幅なので、1 行に入るのは (最大幅 - 左右パディング) ÷ 文字サイズ 文字。
        private const int FullWidthCharsPerLine =
            (int)((PreviewMaxWidthPx - (PreviewPaddingXPx * 2f)) / PreviewFontSizePx);
        // ゲームの既定フォント（Panel Settings の PanelTextSettings が参照しているものと同じ資産）。
        private const string GameFontPath = "Assets/Font/NotoSansJP-Bold SDF.asset";

        private readonly List<PoolRef> _pools = new();
        // 選択中のプールの文言の作業コピー。編集のたびに資産へ書き戻す（Apply）。
        private readonly List<string> _working = new();
        // プレビューで「別の文言を見る」を押したときの抽選に使う乱数源（見た目の確認用）。
        private readonly System.Random _previewRng = new();

        private BoardCellMessageCatalog _target;
        private int _selected;
        // ゲームと同じフォント（プレビューの折り返し用）。見つからなければ null のままエディタ既定を使う。
        private FontAsset _gameFont;
        private bool _gameFontLoaded;

        private ObjectField _objectField;
        private VisualElement _poolList;
        private VisualElement _detail;
        private Label _infoLabel;

        [MenuItem("Window/Sugoroku/Cell Message Editor")]
        public static void Open()
        {
            CellMessageEditorWindow window = GetWindow<CellMessageEditorWindow>();
            window.titleContent = new GUIContent("Cell Message Editor");
            // プレビューを実機と同じ幅（PreviewMaxWidthPx）で出すので、プール一覧と並べても切れない幅を確保する。
            window.minSize = new Vector2(780f, 460f);
        }

        private void CreateGUI()
        {
            BuildPoolRefs();

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

            ScrollView listColumn = new();
            listColumn.style.width = 190f;
            listColumn.style.flexShrink = 0f;
            body.Add(listColumn);

            _poolList = new VisualElement();
            listColumn.Add(_poolList);

            ScrollView detailColumn = new();
            detailColumn.style.flexGrow = 1f;
            detailColumn.style.marginLeft = 10f;
            body.Add(detailColumn);

            _detail = new VisualElement();
            detailColumn.Add(_detail);

            _infoLabel = new Label();
            _infoLabel.style.marginTop = 6f;
            root.Add(_infoLabel);

            LoadWorking();
            Rebuild();
        }

        /// <summary>左の一覧に並べるプール（スタート＋定義済みイベント種別）を組み立てる。</summary>
        private void BuildPoolRefs()
        {
            _pools.Clear();
            _pools.Add(new PoolRef(true, BoardCellEvent.None, "スタート(S/G)", BoardEventColors.StartOutline));
            foreach (BoardCellEvent cellEvent in Enum.GetValues(typeof(BoardCellEvent)))
            {
                _pools.Add(new PoolRef(
                    false, cellEvent, BoardEventLabel.Of(cellEvent), BoardEventColors.Of(cellEvent)));
            }
        }

        private void BuildToolbar(VisualElement root)
        {
            _objectField = new ObjectField("文言データ")
            {
                objectType = typeof(BoardCellMessageCatalog),
                allowSceneObjects = false
            };
            _objectField.RegisterValueChangedCallback(evt =>
            {
                SetTarget(evt.newValue as BoardCellMessageCatalog);
            });
            root.Add(_objectField);

            VisualElement toolbar = new();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.marginTop = 4f;

            Button createButton = new(CreateNewAsset) { text = "新規作成" };
            toolbar.Add(createButton);

            Button resetButton = new(ResetToDefaults) { text = "既定に戻す" };
            resetButton.style.marginLeft = 8f;
            toolbar.Add(resetButton);

            Button saveButton = new(() => AssetDatabase.SaveAssets()) { text = "保存" };
            saveButton.style.marginLeft = 8f;
            toolbar.Add(saveButton);

            root.Add(toolbar);
        }

        private void SetTarget(BoardCellMessageCatalog catalog)
        {
            _target = catalog;
            if (_target != null)
            {
                // 新しいイベント種別を足した後の古い資産でも、その編集欄が出るようにそろえる。
                _target.EnsurePools();
            }
            LoadWorking();
            Rebuild();
        }

        private void CreateNewAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "マスの文言データを作成", "BoardCellMessageCatalog", "asset", "保存先を選択してください");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            BoardCellMessageCatalog catalog = CreateInstance<BoardCellMessageCatalog>();
            // 空の資産だと文言が 1 つも出なくなるので、既定文言を入れた状態から編集を始められるようにする。
            catalog.ResetToDefaults();
            AssetDatabase.CreateAsset(catalog, path);
            AssetDatabase.SaveAssets();

            _objectField.SetValueWithoutNotify(catalog);
            SetTarget(catalog);
        }

        private void ResetToDefaults()
        {
            if (_target == null)
            {
                return;
            }
            if (!EditorUtility.DisplayDialog(
                    "既定に戻す", "すべてのプールを既定の文言で上書きします。よろしいですか？", "戻す", "やめる"))
            {
                return;
            }
            Undo.RecordObject(_target, "文言を既定に戻す");
            _target.ResetToDefaults();
            EditorUtility.SetDirty(_target);
            LoadWorking();
            Rebuild();
        }

        /// <summary>選択中のプールの文言を資産から作業コピーへ読み込む。</summary>
        private void LoadWorking()
        {
            _working.Clear();
            if (_target == null || _selected < 0 || _selected >= _pools.Count)
            {
                return;
            }
            PoolRef pool = _pools[_selected];
            IReadOnlyList<string> messages = pool.IsStart ? _target.StartMessages : _target.Messages(pool.Event);
            for (int i = 0; i < messages.Count; i++)
            {
                _working.Add(messages[i]);
            }
        }

        /// <summary>作業コピーを資産へ書き戻す。</summary>
        private void Apply()
        {
            if (_target == null || _selected < 0 || _selected >= _pools.Count)
            {
                return;
            }
            PoolRef pool = _pools[_selected];
            Undo.RecordObject(_target, "マスの文言を編集");
            if (pool.IsStart)
            {
                _target.SetStartMessages(_working);
            }
            else
            {
                _target.SetMessages(pool.Event, _working);
            }
            EditorUtility.SetDirty(_target);
        }

        private void Rebuild()
        {
            RebuildPoolList();
            RebuildDetail();
            UpdateInfo();
        }

        private void RebuildPoolList()
        {
            _poolList.Clear();
            for (int i = 0; i < _pools.Count; i++)
            {
                int index = i;
                PoolRef pool = _pools[i];
                int count = CountOf(pool);

                Button button = new(() =>
                {
                    _selected = index;
                    LoadWorking();
                    Rebuild();
                });
                button.style.flexDirection = FlexDirection.Row;
                button.style.alignItems = Align.Center;
                button.style.justifyContent = Justify.FlexStart;
                button.style.marginLeft = 0f;
                button.style.marginRight = 0f;
                button.style.marginBottom = 2f;
                button.style.paddingLeft = 6f;
                if (index == _selected)
                {
                    button.style.backgroundColor = new Color(0.25f, 0.42f, 0.62f);
                }

                VisualElement swatch = new();
                swatch.style.width = 12f;
                swatch.style.height = 12f;
                swatch.style.marginRight = 6f;
                swatch.style.flexShrink = 0f;
                swatch.style.backgroundColor = pool.Color;
                button.Add(swatch);

                Label label = new(pool.Label) { pickingMode = PickingMode.Ignore };
                label.style.flexGrow = 1f;
                button.Add(label);

                // 件数はその場で分かるほうが「どのプールが手薄か」を探しやすいので添える。
                Label countLabel = new(count.ToString()) { pickingMode = PickingMode.Ignore };
                countLabel.style.fontSize = 10f;
                countLabel.style.color = count == 0
                    ? new Color(0.95f, 0.5f, 0.4f)
                    : new Color(0.7f, 0.7f, 0.7f);
                button.Add(countLabel);

                _poolList.Add(button);
            }
        }

        private int CountOf(PoolRef pool)
        {
            if (_target == null)
            {
                return 0;
            }
            return pool.IsStart ? _target.StartMessages.Count : _target.Messages(pool.Event).Count;
        }

        private void RebuildDetail()
        {
            _detail.Clear();

            if (_target == null)
            {
                Label hint = new("文言データを選択、または「新規作成」してください。\n"
                    + "未割り当てのままでもゲームは既定の文言（BoardCellMessageDefaults）で動きます。");
                hint.style.whiteSpace = WhiteSpace.Normal;
                _detail.Add(hint);
                return;
            }

            PoolRef pool = _pools[_selected];

            Label title = new(pool.IsStart ? "スタート(S/G) の文言" : $"{pool.Label} のマスの文言");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            _detail.Add(title);

            Label description = new(DescriptionOf(pool));
            description.style.whiteSpace = WhiteSpace.Normal;
            description.style.fontSize = 11f;
            description.style.color = new Color(0.65f, 0.65f, 0.65f);
            description.style.marginBottom = 6f;
            _detail.Add(description);

            BuildPreview();

            HashSet<string> duplicates = FindDuplicates();
            for (int i = 0; i < _working.Count; i++)
            {
                _detail.Add(BuildMessageRow(i, duplicates));
            }

            Button addButton = new(() =>
            {
                _working.Add(string.Empty);
                Apply();
                Rebuild();
            })
            { text = "＋ 文言を追加" };
            addButton.style.marginTop = 6f;
            addButton.style.marginLeft = 0f;
            _detail.Add(addButton);

            if (_working.Count == 0)
            {
                _detail.Add(BuildNote("このプールは空です。このマスに止まっても文言は表示されません。",
                    new Color(0.95f, 0.6f, 0.35f)));
            }
            if (duplicates.Count > 0)
            {
                _detail.Add(BuildNote("同じ文言が複数あります（その文言だけ出やすくなります）。",
                    new Color(0.95f, 0.6f, 0.35f)));
            }
        }

        /// <summary>プールの意味を 1 行で添える（どのマスで出る文言かを取り違えないように）。</summary>
        private static string DescriptionOf(PoolRef pool)
        {
            if (pool.IsStart)
            {
                return "経路 index 0（スタート＝ゴール）に止まったときに出る。"
                    + "そのマスに設定されたイベント種別より優先される。";
            }
            return $"{BoardEventLabel.Of(pool.Event)}のマスに止まったときに出る（全マップ共通）。"
                + " 着地のたびにこの中から 1 つをランダムに選ぶ。";
        }

        /// <summary>
        /// ゲーム画面（Board.uss の <c>.cell-message</c>）と同じ寸法・同じフォントで 1 件を試し表示する。
        /// **折り返しまで実機と同じになる**ようにしてあるので、「1 行に収まるか／何行になるか」をここで確かめられる。
        /// </summary>
        private void BuildPreview()
        {
            VisualElement box = new();
            box.style.marginBottom = 8f;
            box.style.paddingTop = 10f;
            box.style.paddingBottom = 10f;
            box.style.paddingLeft = 10f;
            box.style.paddingRight = 10f;
            box.style.backgroundColor = new Color(0.16f, 0.16f, 0.18f);
            box.style.alignItems = Align.Center;

            string picked = BoardCellMessageCatalog.PickFrom(_working, _previewRng);
            bool empty = string.IsNullOrEmpty(picked);

            // 折り返しの限界（＝実機の最大幅）を目で見えるようにする枠。文言が短いときはピルがこの内側で縮む。
            // 枠線ぶん（左右 1px）を足しておかないと内側が実機より狭くなり、折り返し位置がずれる。
            const float GuideBorderPx = 1f;
            VisualElement bounds = new();
            bounds.style.width = PreviewMaxWidthPx + (GuideBorderPx * 2f);
            bounds.style.flexShrink = 0f;
            bounds.style.alignItems = Align.Center;
            Color guide = new(1f, 1f, 1f, 0.14f);
            bounds.style.borderLeftWidth = GuideBorderPx;
            bounds.style.borderRightWidth = GuideBorderPx;
            bounds.style.borderLeftColor = guide;
            bounds.style.borderRightColor = guide;
            box.Add(bounds);

            // Board.uss の .cell-message（暗い地＋クリーム色の太字）と同じ指定。
            Label preview = new(empty ? "（文言なし）" : picked);
            preview.style.whiteSpace = WhiteSpace.Normal;
            preview.style.maxWidth = PreviewMaxWidthPx;
            preview.style.fontSize = PreviewFontSizePx;
            preview.style.unityFontStyleAndWeight = FontStyle.Bold;
            preview.style.unityTextAlign = TextAnchor.MiddleCenter;
            preview.style.paddingTop = PreviewPaddingYPx;
            preview.style.paddingBottom = PreviewPaddingYPx;
            preview.style.paddingLeft = PreviewPaddingXPx;
            preview.style.paddingRight = PreviewPaddingXPx;
            preview.style.color = empty
                ? new Color(0.55f, 0.55f, 0.55f)
                : new Color(1f, 0.96f, 0.84f);
            preview.style.backgroundColor = new Color(0f, 0f, 0f, 0.82f);

            // 折り返し位置は字幅で決まるので、ゲームと同じフォントを当てないと実機とずれる。
            FontAsset font = LoadGameFont();
            if (font != null)
            {
                preview.style.unityFontDefinition = FontDefinition.FromSDFFont(font);
            }
            bounds.Add(preview);

            Label caption = new(font != null
                ? $"実機と同じ幅 {PreviewMaxWidthPx:0}px・文字サイズ {PreviewFontSizePx:0}px・NotoSansJP Bold で折り返します（全角およそ{FullWidthCharsPerLine}文字/行）。"
                : $"実機と同じ寸法で折り返しますが、{GameFontPath} が見つからないためフォントは代用です（位置は目安）。");
            caption.style.whiteSpace = WhiteSpace.Normal;
            caption.style.fontSize = 10f;
            caption.style.marginTop = 6f;
            caption.style.color = new Color(0.6f, 0.6f, 0.6f);
            box.Add(caption);

            Button shuffle = new(RebuildDetail) { text = "別の文言を見る" };
            shuffle.style.marginTop = 6f;
            shuffle.SetEnabled(_working.Count > 1);
            box.Add(shuffle);

            _detail.Add(box);
        }

        /// <summary>ゲームの既定フォントを 1 度だけ読む（未配置なら null＝エディタ既定のフォントで代用）。</summary>
        private FontAsset LoadGameFont()
        {
            if (!_gameFontLoaded)
            {
                _gameFont = AssetDatabase.LoadAssetAtPath<FontAsset>(GameFontPath);
                _gameFontLoaded = true;
            }
            return _gameFont;
        }

        private VisualElement BuildMessageRow(int index, HashSet<string> duplicates)
        {
            VisualElement row = new();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 2f;

            TextField field = new() { value = _working[index], isDelayed = true };
            field.style.flexGrow = 1f;
            field.style.marginLeft = 0f;
            field.RegisterValueChangedCallback(evt =>
            {
                _working[index] = evt.newValue;
                Apply();
                Rebuild();
            });

            // 空の文言は「空の帯」がゲーム画面に出てしまうので、重複と同じく目立たせる。
            string trimmed = _working[index] == null ? string.Empty : _working[index].Trim();
            bool invalid = trimmed.Length == 0 || duplicates.Contains(trimmed);
            if (invalid)
            {
                field.style.borderLeftWidth = 2f;
                field.style.borderRightWidth = 2f;
                field.style.borderTopWidth = 2f;
                field.style.borderBottomWidth = 2f;
                Color warn = new(0.95f, 0.55f, 0.3f);
                field.style.borderLeftColor = warn;
                field.style.borderRightColor = warn;
                field.style.borderTopColor = warn;
                field.style.borderBottomColor = warn;
            }
            row.Add(field);

            Button remove = new(() =>
            {
                _working.RemoveAt(index);
                Apply();
                Rebuild();
            })
            { text = "×" };
            remove.style.width = 24f;
            remove.style.marginRight = 0f;
            row.Add(remove);

            return row;
        }

        private HashSet<string> FindDuplicates()
        {
            HashSet<string> seen = new();
            HashSet<string> duplicates = new();
            for (int i = 0; i < _working.Count; i++)
            {
                string trimmed = _working[i] == null ? string.Empty : _working[i].Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }
                if (!seen.Add(trimmed))
                {
                    duplicates.Add(trimmed);
                }
            }
            return duplicates;
        }

        private static Label BuildNote(string text, Color color)
        {
            Label note = new(text);
            note.style.whiteSpace = WhiteSpace.Normal;
            note.style.fontSize = 11f;
            note.style.marginTop = 6f;
            note.style.color = color;
            return note;
        }

        private void UpdateInfo()
        {
            if (_target == null)
            {
                _infoLabel.text = "未割り当て：ゲームは既定の文言で動きます。";
                return;
            }

            int total = 0;
            int empty = 0;
            for (int i = 0; i < _pools.Count; i++)
            {
                int count = CountOf(_pools[i]);
                total += count;
                if (count == 0)
                {
                    empty++;
                }
            }
            _infoLabel.text = empty > 0
                ? $"文言 {total} 件／空のプール {empty} 件（そのマスでは文言が出ません）"
                : $"文言 {total} 件";
        }
    }
}
