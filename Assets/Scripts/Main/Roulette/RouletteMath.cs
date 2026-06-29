using System;

namespace Main.Roulette
{
    /// <summary>
    /// ルーレットの「出目」と「円盤の回転角度」を相互変換する純粋関数群。
    ///
    /// 角度の規約:
    /// - 円盤は時計回りに回転する（UI Toolkit の <see cref="UnityEngine.UIElements.Rotate"/> 正値と一致）。
    /// - 針は画面上部（12 時方向）に固定。
    /// - セクター i（0 始まり）は、回転 0 のとき上部から時計回りに
    ///   [i * sectorAngle, (i+1) * sectorAngle) の範囲を占め、表示数字は i + 1。
    /// </summary>
    public static class RouletteMath
    {
        /// <summary>1 セクターあたりの中心角（度）。</summary>
        public static float SectorAngle(int count)
        {
            return 360f / count;
        }

        /// <summary>
        /// セクター <paramref name="resultIndex"/> の中心を針（上部）に合わせる回転角度（度）。
        /// <paramref name="turns"/> は演出用の追加回転数。
        /// </summary>
        public static float StopRotation(int resultIndex, int count, int turns)
        {
            return turns * 360f - (resultIndex + 0.5f) * SectorAngle(count);
        }

        /// <summary>
        /// 回転角度 <paramref name="rotationDegrees"/> のとき、針の真下にあるセクターの index（0 始まり）。
        /// </summary>
        public static int ResultFromRotation(float rotationDegrees, int count)
        {
            float sector = SectorAngle(count);
            // 円盤を rotation 回した後の上部は、円盤ローカルでは -rotation の位置に当たる。
            float local = Mod(-rotationDegrees, 360f);
            int index = (int)Math.Floor(local / sector);
            if (index < 0)
            {
                index = 0;
            }
            if (index >= count)
            {
                index = count - 1;
            }
            return index;
        }

        /// <summary>
        /// 現在の回転角度 <paramref name="current"/> から、セクター <paramref name="resultIndex"/> を
        /// 針に合わせて停止させるための「次の絶対回転角度」。常に <paramref name="current"/> より大きく（時計回りに進む）、
        /// <paramref name="turns"/> 回転以上回る。
        /// </summary>
        public static float NextRotation(float current, int resultIndex, int count, int turns)
        {
            float desiredMod = Mod(StopRotation(resultIndex, count, 0), 360f);
            float currentMod = Mod(current, 360f);
            float delta = turns * 360f + Mod(desiredMod - currentMod, 360f);
            return current + delta;
        }

        private static float Mod(float a, float m)
        {
            return ((a % m) + m) % m;
        }
    }
}
