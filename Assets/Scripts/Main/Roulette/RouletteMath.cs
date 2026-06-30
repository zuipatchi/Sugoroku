using System;

namespace Main.Roulette
{
    /// <summary>
    /// ルーレットの「円盤の回転角度」から「針の真下にあるセクター」を求める純粋関数群。
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

        private static float Mod(float a, float m)
        {
            return ((a % m) + m) % m;
        }
    }
}
