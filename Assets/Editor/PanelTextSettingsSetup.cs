using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Common.EditorTools
{
    /// <summary>
    /// 全 UI の既定フォント（NotoSansJP Bold）を、テーマ（<c>UnityDefaultRuntimeTheme.tss</c>）ではなく
    /// <see cref="PanelSettings.textSettings"/> で当てるためのセットアップ。
    ///
    /// テーマ（.tss）は ScriptedImporter の成果物なので、そこから参照したフォント資産が再インポートされると
    /// テーマの成果物も作り直しになる。フォント資産は Atlas Population Mode = Dynamic で、**新しい文字を
    /// 描くたびに TextCore がエディタ上で書き戻して再インポートする**（`TextEditorResourceManager`）ため、
    /// 作り直しの最中に <c>UIDocument.OnEnable</c> が走ると <c>PanelSettings.themeUss</c> が null になり
    /// 「No Theme Style Sheet set to PanelSettings ...」の警告とともに既定スタイルが当たらず表示が崩れる
    /// （Play のたびに強制 Refresh が走る Multiplayer Play Mode の仮想プレイヤーで特に踏みやすい）。
    ///
    /// <see cref="PanelSettings"/> は素の資産（ScriptedImporter を通さない）なので、フォントの参照を
    /// そちらへ移せばこの再インポートの連鎖が切れる。テーマにはフォント指定を残さない。
    ///
    /// 冪等なので、設定が消えた・作り直したいときはいつでも実行してよい。
    /// </summary>
    public static class PanelTextSettingsSetup
    {
        private const string FontAssetPath = "Assets/Font/NotoSansJP-Bold SDF.asset";
        private const string PanelSettingsPath = "Assets/Scripts/Panel Settings.asset";
        private const string TextSettingsPath = "Assets/UI Toolkit/PanelTextSettings.asset";

        [MenuItem("Window/Sugoroku/Setup Panel Text Settings")]
        public static void Setup()
        {
            FontAsset font = AssetDatabase.LoadAssetAtPath<FontAsset>(FontAssetPath);
            if (font == null)
            {
                Debug.LogError($"既定フォントが見つかりませんでした: {FontAssetPath}");
                return;
            }

            PanelSettings panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panelSettings == null)
            {
                Debug.LogError($"PanelSettings が見つかりませんでした: {PanelSettingsPath}");
                return;
            }

            PanelTextSettings textSettings = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(TextSettingsPath);
            bool created = textSettings == null;
            if (created)
            {
                textSettings = ScriptableObject.CreateInstance<PanelTextSettings>();
                AssetDatabase.CreateAsset(textSettings, TextSettingsPath);
            }

            textSettings.defaultFontAsset = font;
            panelSettings.textSettings = textSettings;

            EditorUtility.SetDirty(textSettings);
            EditorUtility.SetDirty(panelSettings);
            AssetDatabase.SaveAssets();

            Debug.Log(created
                ? $"{TextSettingsPath} を作成し、既定フォントに {font.name} を設定して PanelSettings へ割り当てました。"
                : $"{TextSettingsPath} の既定フォントを {font.name} に更新し、PanelSettings へ割り当てました。");
        }
    }
}
