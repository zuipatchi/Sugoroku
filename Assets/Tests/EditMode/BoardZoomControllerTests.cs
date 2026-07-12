using Main.Board;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public class BoardZoomControllerTests
    {
        private const float Fill = 0.62f;

        [Test]
        public void ScaleForColumnsは基準列数で等倍になる()
        {
            Assert.AreEqual(1f, BoardZoomController.ScaleForColumns(4, 4, Fill), 1e-4f);
        }

        [Test]
        public void ScaleForColumnsは列を減らすほど拡大する()
        {
            // 4 列 → 3 列 → 2 列と減らすほどスケールが大きくなる。
            float s3 = BoardZoomController.ScaleForColumns(3, 4, Fill);
            float s2 = BoardZoomController.ScaleForColumns(2, 4, Fill);
            Assert.Greater(s3, 1f);
            Assert.Greater(s2, s3);
            Assert.AreEqual(3.62f / 1.62f, s2, 1e-4f);
        }

        [Test]
        public void ScaleForColumnsは列を増やすほど縮小する()
        {
            // 4 列 → 6 列 → 8 列と増やすほどスケールが小さくなる（<1）。
            float s6 = BoardZoomController.ScaleForColumns(6, 4, Fill);
            float s8 = BoardZoomController.ScaleForColumns(8, 4, Fill);
            Assert.Less(s6, 1f);
            Assert.Less(s8, s6);
            Assert.AreEqual(3.62f / 7.62f, s8, 1e-4f);
        }

        [Test]
        public void BuildColumnLevelsは要求列を昇順で返し基準列を含む()
        {
            int[] levels = BoardZoomController.BuildColumnLevels(new[] { 2, 3, 4, 6, 8 }, 4, 10);
            Assert.AreEqual(new[] { 2, 3, 4, 6, 8 }, levels);
        }

        [Test]
        public void BuildColumnLevelsは盤面の列数を上限に丸めて重複を除く()
        {
            // 4 列盤面では 6・8 は 4 に丸められ、重複が除かれて [2,3,4] になる。
            int[] levels = BoardZoomController.BuildColumnLevels(new[] { 2, 3, 4, 6, 8 }, 4, 4);
            Assert.AreEqual(new[] { 2, 3, 4 }, levels);
        }

        [Test]
        public void BuildColumnLevelsは基準列が要求に無くても必ず含める()
        {
            int[] levels = BoardZoomController.BuildColumnLevels(new[] { 2, 3 }, 4, 10);
            Assert.AreEqual(new[] { 2, 3, 4 }, levels);
        }

        [Test]
        public void ClampPanAxisは盤面がコンテナより小さければ動かせない()
        {
            // 中心 500・外形 400・コンテナ 1000 → 盤面がコンテナより小さいので常に 0。
            Assert.AreEqual(0f, BoardZoomController.ClampPanAxis(100f, 500f, 400f, 1000f), 1e-4f);
            Assert.AreEqual(0f, BoardZoomController.ClampPanAxis(-100f, 500f, 400f, 1000f), 1e-4f);
        }

        [Test]
        public void ClampPanAxisは中央配置の盤面で対称に可動する()
        {
            // 中心 500（=コンテナ中央）・外形 1600・コンテナ 1000 → 可動範囲 ±300。
            Assert.AreEqual(100f, BoardZoomController.ClampPanAxis(100f, 500f, 1600f, 1000f), 1e-4f);
            Assert.AreEqual(300f, BoardZoomController.ClampPanAxis(999f, 500f, 1600f, 1000f), 1e-4f);
            Assert.AreEqual(-300f, BoardZoomController.ClampPanAxis(-999f, 500f, 1600f, 1000f), 1e-4f);
        }

        [Test]
        public void ClampPanAxisは中心がずれた盤面で非対称に可動する()
        {
            // 中心 520（上マージンぶん下寄り）・外形 1600・コンテナ 1000。
            // max = -520 + 800 = 280、min = 1000 - 520 - 800 = -320。
            Assert.AreEqual(280f, BoardZoomController.ClampPanAxis(999f, 520f, 1600f, 1000f), 1e-4f);
            Assert.AreEqual(-320f, BoardZoomController.ClampPanAxis(-999f, 520f, 1600f, 1000f), 1e-4f);
        }

        [Test]
        public void ClampPanは軸ごとにクランプする()
        {
            // X は可動（外形 1600 > 1000）、Y は不動（外形 400 < 1000）。
            Vector2 clamped = BoardZoomController.ClampPan(
                new Vector2(999f, 999f),
                new Vector2(500f, 500f),
                new Vector2(1600f, 400f),
                new Vector2(1000f, 1000f));
            Assert.AreEqual(300f, clamped.x, 1e-4f);
            Assert.AreEqual(0f, clamped.y, 1e-4f);
        }

        [Test]
        public void ZoomAroundViewportCenterは盤面中心が画面中央でpan0なら動かさない()
        {
            // 盤面中心＝画面中央・パン 0 のときは、拡大してもパンは 0 のまま。
            Vector2 pan = BoardZoomController.ZoomAroundViewportCenter(
                Vector2.zero, 1f, 1.5f, new Vector2(500f, 500f), new Vector2(1000f, 1000f));
            Assert.AreEqual(0f, pan.x, 1e-4f);
            Assert.AreEqual(0f, pan.y, 1e-4f);
        }

        [Test]
        public void ZoomAroundViewportCenterは拡大時に画面中央の盤面点を固定する()
        {
            AssertViewportCenterFixed(
                new Vector2(1000f, 1000f), new Vector2(500f, 520f), new Vector2(200f, -100f), 1f, 1.5f);
        }

        [Test]
        public void ZoomAroundViewportCenterは縮小時にも画面中央の盤面点を固定する()
        {
            AssertViewportCenterFixed(
                new Vector2(1200f, 800f), new Vector2(700f, 380f), new Vector2(-150f, 60f), 1.4f, 0.9f);
        }

        /// <summary>
        /// ズーム前に画面中央に見えている盤面上の点が、<see cref="BoardZoomController.ZoomAroundViewportCenter"/> で
        /// 補正したパンでズームした後もまだ画面中央に来ること（＝カメラ中心ズームの不変条件）を検証する。
        /// </summary>
        private static void AssertViewportCenterFixed(
            Vector2 container, Vector2 center, Vector2 pan0, float z0, float z1)
        {
            Vector2 viewportCenter = container * 0.5f;
            // 画面中央に見えている盤面点の局所オフセット u（拡大原点 center からの座標）。
            Vector2 u = (viewportCenter - center - pan0) / z0;

            Vector2 pan1 = BoardZoomController.ZoomAroundViewportCenter(pan0, z0, z1, center, container);

            // ズーム後の同じ点の画面位置が、まだ画面中央に一致する。
            Vector2 afterScreen = center + pan1 + z1 * u;
            Assert.AreEqual(viewportCenter.x, afterScreen.x, 1e-3f);
            Assert.AreEqual(viewportCenter.y, afterScreen.y, 1e-3f);
        }
    }
}
