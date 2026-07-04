using UnityEngine;
using UnityEngine.UIElements;

namespace Main.Roulette
{
    /// <summary>
    /// ルーレット円盤の Painter2D 描画一式。虹色セクター・区切り線・外周リング・当たり強調・
    /// キャラコインの下地・中心ハブを描く。<see cref="Draw"/> を generateVisualContent に登録して使う。
    /// アイコン配置に必要な幾何（コイン径・配置半径）のヘルパーもここに持つ。
    /// </summary>
    public sealed class RouletteWheelRenderer
    {
        // ポップなボードゲーム調の配色。セクター色は数字ごとに HSV で虹色に振り分ける（セクター数に追随）。
        private static readonly Color _dividerColor = new(1f, 1f, 1f, 0.9f);
        private static readonly Color _ringColor = new(1f, 1f, 1f, 0.95f);
        private static readonly Color _winOutlineColor = new(235f / 255f, 200f / 255f, 90f / 255f);
        private static readonly Color _hubOuterColor = new(45f / 255f, 45f / 255f, 70f / 255f);
        private static readonly Color _hubInnerColor = new(245f / 255f, 245f / 255f, 250f / 255f);
        private static readonly Color _hubAccentColor = new(235f / 255f, 200f / 255f, 90f / 255f);
        // キャラアイコンの下地（コイン風）。白い座面をゴールドのリングで縁取り、虹色セクター上でアバターを浮き立たせる。
        private static readonly Color _coinBaseColor = new(250f / 255f, 250f / 255f, 252f / 255f);
        private static readonly Color _coinRingColor = new(235f / 255f, 200f / 255f, 90f / 255f);

        // セクター中心線上でのアバター配置半径（円盤半径に対する比）。数字はアバターの子バッジなので独立配置は不要。
        private const float IconRadiusFactor = 0.62f;
        // 隣のコインと重ならないよう、コイン直径を隣接中心間距離（弦長）のこの割合までに収める。
        private const float CoinChordFillRatio = 0.88f;
        // セクター数が少ないときにコインが大きくなりすぎないよう、直径を円盤半径のこの割合で頭打ちにする。
        private const float CoinDiameterCapRatio = 0.62f;
        // 白座面の外に出すゴールドリングの太さ（px）。
        private const float CoinRingWidth = 3f;
        // アバター画像はコイン白座面のさらに内側に収める割合。
        private const float AvatarInsetRatio = 0.84f;

        private readonly int _sectorCount;

        public RouletteWheelRenderer(int sectorCount)
        {
            _sectorCount = sectorCount;
        }

        /// <summary>当たりセクターの index（0 始まり）。負値で強調なし。変更後は MarkDirtyRepaint が必要。</summary>
        public int HighlightIndex { get; set; } = -1;

        /// <summary>セクター中心線上のアイコン配置半径（px）。</summary>
        public float IconRadius(float wheelRadius)
        {
            return wheelRadius * IconRadiusFactor;
        }

        /// <summary>コイン白座面の内側に収めるアバター画像の一辺（px）。</summary>
        public float AvatarSize(float wheelRadius)
        {
            return CoinDiameter(wheelRadius) * AvatarInsetRatio;
        }

        public void Draw(MeshGenerationContext mgc)
        {
            Rect rect = mgc.visualElement.contentRect;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            Painter2D painter = mgc.painter2D;
            Vector2 center = rect.center;
            float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
            float sector = RouletteMath.SectorAngle(_sectorCount);

            // 1) セクター（数字ごとに虹色。当たりセクターは彩度・明度を上げて強調）。
            for (int i = 0; i < _sectorCount; i++)
            {
                float startFromTop = i * sector;
                float endFromTop = (i + 1) * sector;
                int steps = Mathf.Max(2, Mathf.CeilToInt(sector / 5f));

                painter.fillColor = SectorColor(i, _sectorCount, i == HighlightIndex);
                painter.BeginPath();
                painter.MoveTo(center);
                for (int s = 0; s <= steps; s++)
                {
                    float a = Mathf.Lerp(startFromTop, endFromTop, s / (float)steps) * Mathf.Deg2Rad;
                    Vector2 edge = center + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * radius;
                    painter.LineTo(edge);
                }
                painter.ClosePath();
                painter.Fill();
            }

            // 2) セクター境界の放射状の区切り線。
            painter.strokeColor = _dividerColor;
            painter.lineWidth = 5f;
            for (int i = 0; i < _sectorCount; i++)
            {
                float a = i * sector * Mathf.Deg2Rad;
                Vector2 edge = center + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * radius;
                painter.BeginPath();
                painter.MoveTo(center);
                painter.LineTo(edge);
                painter.Stroke();
            }

            // 3) 外周のリング。
            painter.strokeColor = _ringColor;
            painter.lineWidth = 8f;
            StrokeArc(painter, center, radius - 4f, 0f, 360f);

            // 4) 当たりセクターの強調アウトライン（ゴールドの太い縁取り）。
            if (HighlightIndex >= 0 && HighlightIndex < _sectorCount)
            {
                float start = HighlightIndex * sector;
                float end = (HighlightIndex + 1) * sector;
                painter.strokeColor = _winOutlineColor;
                painter.lineWidth = 6f;
                painter.BeginPath();
                painter.MoveTo(center);
                int steps = Mathf.Max(2, Mathf.CeilToInt(sector / 5f));
                for (int s = 0; s <= steps; s++)
                {
                    float a = Mathf.Lerp(start, end, s / (float)steps) * Mathf.Deg2Rad;
                    Vector2 edge = center + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * (radius - 3f);
                    painter.LineTo(edge);
                }
                painter.ClosePath();
                painter.Stroke();
            }

            // 5) キャラアイコンの下地コイン（ゴールドのリング → 白い座面）。アイコン要素はこの上（子要素）に描画される。
            //    円形なので円盤が回転しても見た目は変わらない（周回はするが傾かない）。数字バッジはアイコン側（USS）で描く。
            float iconRadius = radius * IconRadiusFactor;
            float coinRingRadius = CoinDiameter(radius) * 0.5f;
            float coinBaseRadius = coinRingRadius - CoinRingWidth;
            for (int i = 0; i < _sectorCount; i++)
            {
                float angleFromTop = (i + 0.5f) * sector * Mathf.Deg2Rad;
                Vector2 dir = new(Mathf.Sin(angleFromTop), -Mathf.Cos(angleFromTop));
                Vector2 iconCenter = center + dir * iconRadius;
                painter.fillColor = _coinRingColor;
                FillCircle(painter, iconCenter, coinRingRadius);
                painter.fillColor = _coinBaseColor;
                FillCircle(painter, iconCenter, coinBaseRadius);
            }

            // 6) 中心ハブ（軸キャップ）。暗→明→ゴールドの三重円で立体感を出す。
            float hubR = radius * 0.16f;
            painter.fillColor = _hubOuterColor;
            FillCircle(painter, center, hubR);
            painter.fillColor = _hubInnerColor;
            FillCircle(painter, center, hubR * 0.72f);
            painter.fillColor = _hubAccentColor;
            FillCircle(painter, center, hubR * 0.34f);
        }

        // 隣り合うコインが重ならないコイン直径（px）を、円盤半径とセクター数から求める。
        // 隣接するアイコン中心間の弦長 = 2r·sin(π/n)。その一定割合をコイン直径とし、少数セクターでは上限で頭打ちにする。
        private float CoinDiameter(float wheelRadius)
        {
            float iconRadius = wheelRadius * IconRadiusFactor;
            float chord = 2f * iconRadius * Mathf.Sin(Mathf.PI / _sectorCount);
            return Mathf.Min(chord * CoinChordFillRatio, wheelRadius * CoinDiameterCapRatio);
        }

        private static Color SectorColor(int index, int count, bool highlight)
        {
            float hue = (count <= 0) ? 0f : (float)index / count;
            float saturation = highlight ? 0.85f : 0.6f;
            float value = highlight ? 1f : 0.86f;
            return Color.HSVToRGB(hue, saturation, value);
        }

        private static void StrokeArc(Painter2D painter, Vector2 center, float radius, float startDeg, float endDeg)
        {
            int steps = 72;
            painter.BeginPath();
            for (int s = 0; s <= steps; s++)
            {
                float a = Mathf.Lerp(startDeg, endDeg, s / (float)steps) * Mathf.Deg2Rad;
                Vector2 p = center + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * radius;
                if (s == 0)
                {
                    painter.MoveTo(p);
                }
                else
                {
                    painter.LineTo(p);
                }
            }
            painter.Stroke();
        }

        private static void FillCircle(Painter2D painter, Vector2 center, float radius)
        {
            int steps = 40;
            painter.BeginPath();
            for (int s = 0; s <= steps; s++)
            {
                float a = (s / (float)steps) * 360f * Mathf.Deg2Rad;
                Vector2 p = center + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * radius;
                if (s == 0)
                {
                    painter.MoveTo(p);
                }
                else
                {
                    painter.LineTo(p);
                }
            }
            painter.ClosePath();
            painter.Fill();
        }
    }
}
