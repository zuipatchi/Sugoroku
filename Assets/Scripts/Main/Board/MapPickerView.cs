using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Main.Board
{
    /// <summary>
    /// マップ選択の共通 UI コントローラ。<see cref="BoardCatalog"/> のマップを盤面サムネイル付きカードで
    /// グリッド一覧し、選ぶと大プレビュー・タイトル・イベント内訳を更新する。選択状態を保持し、
    /// ユーザーがカードを選ぶたびに <see cref="Selected"/> を発火する。サムネイルは
    /// <see cref="BoardSchematicView"/> が Painter2D で描くため非同期ロードは無い。
    /// 大プレビューの枠は正方形固定ではなく、選択マップの縦横比（<see cref="BoardSchematicView.AspectOf"/>）を
    /// USS が与えた基準ボックスへ内接させた大きさに変える（横長マップなら横長の枠になる）。
    /// マップ選択シーン（<c>MapSelectPresenter</c>）とオンラインのルーム作成マップ選択で共用する
    /// （どの要素を渡すか・SE・確定/キャンセルの配線は呼び出し側が持つ）。
    /// カード/プレビュー/チップの USS クラスは共用（map-card / map-thumb / map-name /
    /// map-card--selected / ms-stat*）なので、埋め込む側の USS に同じクラスを定義しておくこと。
    /// </summary>
    public sealed class MapPickerView
    {
        // 大プレビュー枠の縦横比の下限・上限（一直線のマップなど極端な比率で枠が潰れないように丸める）。
        private const float MinPreviewAspect = 1f / 4f;
        private const float MaxPreviewAspect = 4f;

        private readonly VisualElement _grid;
        private readonly VisualElement _preview;
        private readonly Label _title;
        private readonly Label _cellCount;
        private readonly VisualElement _stats;

        private readonly Dictionary<string, VisualElement> _cards = new();
        private BoardDefinition _previewBoard;

        // 大プレビュー枠の基準ボックス（USS が与えた寸法）。ここへ選択マップの縦横比を内接させて
        // 枠の形をマップに合わせる。レイアウト前は寸法が確定しないので、初回の geometry で 1 度だけ覚える。
        private Vector2 _previewBox;
        private bool _previewBoxCaptured;

        /// <summary>ユーザーがカードを選んで選択が変わったときに発火する（初期構築では発火しない）。</summary>
        public event Action Selected;

        /// <summary>選択中マップの識別子（資産名）。未選択なら空文字。</summary>
        public string SelectedId { get; private set; } = string.Empty;

        /// <summary>選択中マップがあるか。</summary>
        public bool HasSelection => !string.IsNullOrEmpty(SelectedId);

        public MapPickerView(VisualElement grid, VisualElement preview, Label title, Label cellCount, VisualElement stats)
        {
            _grid = grid;
            _preview = preview;
            _title = title;
            _cellCount = cellCount;
            _stats = stats;

            // 大プレビューは選択マップ（_previewBoard）を毎回読み直して描く。
            _preview.generateVisualContent += ctx => BoardSchematicView.Draw(ctx, _previewBoard);
            _preview.RegisterCallback<GeometryChangedEvent>(_ => OnPreviewGeometryChanged());
        }

        // 初回のレイアウトで基準ボックスを確定し、選択マップの縦横比を枠へ反映する。
        private void OnPreviewGeometryChanged()
        {
            if (!_previewBoxCaptured)
            {
                float width = _preview.resolvedStyle.width;
                float height = _preview.resolvedStyle.height;
                if (width <= 0f || height <= 0f)
                {
                    // まだ表示されていない（オーバーレイが display:none 等）。次の geometry で拾う。
                    return;
                }
                _previewBox = new Vector2(width, height);
                _previewBoxCaptured = true;
                ApplyPreviewAspect();
            }
            _preview.MarkDirtyRepaint();
        }

        // 大プレビュー枠の寸法を、選択マップの縦横比を基準ボックスへ内接させた大きさにする
        // （横長マップなら横長の枠、縦長マップなら縦長の枠になる）。
        private void ApplyPreviewAspect()
        {
            if (!_previewBoxCaptured)
            {
                return;
            }
            // 一直線のマップ（全マスが同じ行／列）のように極端な比率でも枠が潰れないよう丸める。
            float aspect = Mathf.Clamp(BoardSchematicView.AspectOf(_previewBoard), MinPreviewAspect, MaxPreviewAspect);
            float width = Mathf.Min(_previewBox.x, _previewBox.y * aspect);
            _preview.style.width = width;
            _preview.style.height = width / aspect;
        }

        /// <summary>
        /// カタログのマップからカード一覧を組み、初期選択（<paramref name="initialId"/>＝資産名。
        /// 見つからなければ先頭）を反映する。カタログ未割り当て・空なら何も並べず未選択のままにする。
        /// </summary>
        public void Build(BoardCatalog catalog, string initialId)
        {
            _grid.Clear();
            _cards.Clear();

            if (catalog == null || catalog.IsEmpty)
            {
                SelectedId = string.Empty;
                _previewBoard = null;
                RefreshSelectionVisual();
                return;
            }

            BoardDefinition initial = !string.IsNullOrEmpty(initialId) ? catalog.Find(initialId) : catalog.Default;
            SelectedId = initial != null ? initial.name : string.Empty;
            _previewBoard = initial;

            foreach (BoardDefinition board in catalog.All)
            {
                if (board == null)
                {
                    continue;
                }

                Button card = new();
                card.AddToClassList("map-card");

                VisualElement thumb = new() { pickingMode = PickingMode.Ignore };
                thumb.AddToClassList("map-thumb");
                thumb.generateVisualContent += ctx => BoardSchematicView.Draw(ctx, board);
                thumb.RegisterCallback<GeometryChangedEvent>(_ => thumb.MarkDirtyRepaint());
                card.Add(thumb);

                Label name = new() { text = DisplayNameOf(board) };
                name.AddToClassList("map-name");
                card.Add(name);

                string id = board.name;
                card.clicked += () => OnCardClicked(id, board);

                _grid.Add(card);
                _cards[id] = card;
            }

            RefreshSelectionVisual();
        }

        private void OnCardClicked(string id, BoardDefinition board)
        {
            if (id == SelectedId)
            {
                return;
            }
            SelectedId = id;
            _previewBoard = board;
            RefreshSelectionVisual();
            Selected?.Invoke();
        }

        // 選択中カードを強調し、大プレビュー・タイトル・内訳を更新する。
        private void RefreshSelectionVisual()
        {
            foreach (KeyValuePair<string, VisualElement> pair in _cards)
            {
                pair.Value.EnableInClassList("map-card--selected", pair.Key == SelectedId);
            }

            _title.text = _previewBoard != null ? DisplayNameOf(_previewBoard) : string.Empty;
            ApplyPreviewAspect();
            _preview.MarkDirtyRepaint();
            UpdateStats();
        }

        // 選択中マップのイベント構成（総マス数＋イベント別の色チップ）を組み直す。
        private void UpdateStats()
        {
            _stats.Clear();
            if (_previewBoard == null)
            {
                _cellCount.text = string.Empty;
                return;
            }

            _cellCount.text = $"全 {_previewBoard.CellCount} マス";
            foreach ((BoardCellEvent cellEvent, int count) in BoardEventTally.Summarize(_previewBoard))
            {
                _stats.Add(BuildStatChip(cellEvent, count));
            }
        }

        // 1 イベント分のチップ（色ドット＋「ラベル ×N」）。ドット色・ラベルはサムネイルの塗り分けと共通。
        private static VisualElement BuildStatChip(BoardCellEvent cellEvent, int count)
        {
            VisualElement chip = new() { pickingMode = PickingMode.Ignore };
            chip.AddToClassList("ms-stat");

            VisualElement dot = new() { pickingMode = PickingMode.Ignore };
            dot.AddToClassList("ms-stat__dot");
            dot.style.backgroundColor = BoardEventColors.Of(cellEvent);
            chip.Add(dot);

            Label label = new($"{BoardEventLabel.Of(cellEvent)} ×{count}") { pickingMode = PickingMode.Ignore };
            label.AddToClassList("ms-stat__label");
            chip.Add(label);

            return chip;
        }

        private static string DisplayNameOf(BoardDefinition board)
        {
            return string.IsNullOrEmpty(board.DisplayName) ? board.name : board.DisplayName;
        }
    }
}
