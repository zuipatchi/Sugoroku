using UnityEngine;

namespace Common.Character
{
    /// <summary>
    /// キャラの表示 index から色を作る共通パレット。
    /// 画像未配置時の色面プレースホルダなど、各シーンで同じ配色になるようここへ集約する。
    /// </summary>
    public static class CharacterPalette
    {
        /// <summary>
        /// 画像未配置時の色面プレースホルダ色。index / count で色相を虹色に振り分ける。
        /// 彩度・明度は用途に応じて上書きできる（既定はカード・アイコン用の落ち着いた色味）。
        /// </summary>
        public static Color PlaceholderColor(int index, int count, float saturation = 0.45f, float value = 0.65f)
        {
            float hue = (count <= 0) ? 0f : (float)index / count;
            return Color.HSVToRGB(hue, saturation, value);
        }

        /// <summary>
        /// キャラ識別子からプレースホルダ色を作るショートカット。
        /// カタログ上の表示 index / 総数（<see cref="CharacterCatalog"/>）で色相を決める。
        /// </summary>
        public static Color PlaceholderColorFor(CharacterId id, float saturation = 0.45f, float value = 0.65f)
        {
            return PlaceholderColor(CharacterCatalog.IndexOf(id), CharacterCatalog.All.Count, saturation, value);
        }
    }
}
