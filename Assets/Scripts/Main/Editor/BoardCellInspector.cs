using System;
using Common.MiniGame;
using Main.Board;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Main.EditorTools
{
    /// <summary>
    /// 盤面エディタ（<see cref="BoardEditorWindow"/>）の右パネル。選択中マスの
    /// イベント・数値（お金イベントは「金額」）・色・アイコンアドレスの編集 UI と削除ボタンを構築する。
    /// 値の変更は Undo 記録と SetDirty を行い、再描画・削除はコンストラクタで受けたコールバックへ依頼する。
    /// </summary>
    internal sealed class BoardCellInspector
    {
        private readonly VisualElement _container;
        private readonly Action _requestRebuild;
        private readonly Action _requestGridRefresh;
        private readonly Action _removeSelected;

        /// <param name="container">編集 UI を構築する親要素。</param>
        /// <param name="requestRebuild">グリッド・インスペクタ全体の再構築依頼（イベント種別の変更時）。</param>
        /// <param name="requestGridRefresh">グリッドのみの再描画依頼（数値・色の変更時）。</param>
        /// <param name="removeSelected">選択中マスの削除依頼。</param>
        public BoardCellInspector(
            VisualElement container,
            Action requestRebuild,
            Action requestGridRefresh,
            Action removeSelected)
        {
            _container = container;
            _requestRebuild = requestRebuild;
            _requestGridRefresh = requestGridRefresh;
            _removeSelected = removeSelected;
        }

        /// <summary>編集 UI を作り直す。選択がない（index が範囲外）なら空にする。</summary>
        public void Rebuild(BoardDefinition target, int selectedIndex)
        {
            _container.Clear();
            if (target == null || selectedIndex < 0 || selectedIndex >= target.CellCount)
            {
                return;
            }

            BoardCellDefinition cell = target.Cell(selectedIndex);

            Label title = new(selectedIndex == 0 ? "マス 0（スタート＝ゴール）" : $"マス {selectedIndex}");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 4f;
            _container.Add(title);

            EnumField eventField = new("イベント", cell.Event);
            eventField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(target, "イベント変更");
                cell.SetEvent((BoardCellEvent)evt.newValue);
                EditorUtility.SetDirty(target);
                _requestRebuild();
            });
            _container.Add(eventField);

            if (cell.Event == BoardCellEvent.Forward
                || cell.Event == BoardCellEvent.Back
                || cell.Event == BoardCellEvent.Rest
                || cell.Event == BoardCellEvent.MoneyUp
                || cell.Event == BoardCellEvent.MoneyDown)
            {
                IntegerField amountField = new(AmountLabel(cell.Event)) { value = cell.Amount };
                amountField.RegisterValueChangedCallback(evt =>
                {
                    Undo.RecordObject(target, "数値変更");
                    cell.SetAmount(Mathf.Max(1, evt.newValue));
                    EditorUtility.SetDirty(target);
                    _requestGridRefresh();
                });
                _container.Add(amountField);
            }

            if (cell.Event == BoardCellEvent.MiniGame)
            {
                EnumField miniGameField = new("ミニゲーム", cell.MiniGame);
                miniGameField.RegisterValueChangedCallback(evt =>
                {
                    Undo.RecordObject(target, "ミニゲーム変更");
                    cell.SetMiniGame((MiniGameId)evt.newValue);
                    EditorUtility.SetDirty(target);
                });
                _container.Add(miniGameField);
            }

            ColorField colorField = new("色") { value = cell.HasCustomColor ? cell.Color : BoardCellDefinition.UnsetColor };
            colorField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(target, "色変更");
                cell.SetColor(evt.newValue);
                EditorUtility.SetDirty(target);
                _requestGridRefresh();
            });
            _container.Add(colorField);

            TextField iconField = new("アイコンアドレス") { value = cell.IconAddress };
            iconField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(target, "アイコン変更");
                cell.SetIconAddress(evt.newValue);
                EditorUtility.SetDirty(target);
            });
            _container.Add(iconField);

            Button removeButton = new(_removeSelected) { text = "このマスを削除" };
            removeButton.style.marginTop = 8f;
            _container.Add(removeButton);
        }

        /// <summary>数値フィールドのラベル。お金イベントは「金額」、それ以外は汎用の「数値」。</summary>
        private static string AmountLabel(BoardCellEvent cellEvent)
        {
            return cellEvent == BoardCellEvent.MoneyUp || cellEvent == BoardCellEvent.MoneyDown
                ? "金額"
                : "数値";
        }
    }
}
