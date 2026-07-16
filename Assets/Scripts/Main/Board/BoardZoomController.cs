using System;
using System.Collections.Generic;
using System.Threading;
using Common.Store;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Main.Board
{
    /// <summary>
    /// 盤面リング領域（<see cref="BoardPresenter"/> の BoardArea）のズームと、ドラッグ移動（パン）を担う。
    /// ズームは「一度に画面幅へ収める列数」を段階で切り替える方式（既定 4 列。ズームアウト＝より多くの列＝
    /// 6 → 8 列、ズームイン＝より少ない列＝3 → 2 列）。各段階に対応するスケールを基準レイアウト
    /// （<see cref="BoardLayoutCalculator"/> が基準列数で割り付けたもの）へ掛けて表示する。横長盤面は既定でも
    /// 画面外へはみ出すので、ドラッグで横にパンして残りの列を見る。
    /// スケール算出（<see cref="ScaleForColumns"/>）・表示段階の生成（<see cref="BuildColumnLevels"/>）・
    /// パン量クランプ（<see cref="ClampPan"/> / <see cref="ClampPanAxis"/>）は static な純粋関数に切り出し、単体テスト可能。
    /// ネームプレートやポップアップ等の兄弟オーバーレイは対象外。
    /// </summary>
    public sealed class BoardZoomController : IDisposable
    {
        // 虫眼鏡ボタンに貼る画像のアドレス（未配置なら +／− の文字のまま残す。Addressable は大文字小文字を区別する）。
        private const string MagnifierAddress = "Image/lupe";

        private readonly VisualElement _boardArea;
        private readonly VisualElement _dragLayer;
        private readonly VisualElement _zoomPill;
        private readonly VisualElement _zoomMagnifier;
        private readonly Button _zoomInButton;
        private readonly Button _zoomOutButton;
        private readonly BoardLayoutCalculator _layout;
        private readonly AddressableSpriteLoader _spriteLoader = new();

        // 表示段階（昇順の列数配列）と現在位置。index が小さいほど少ない列＝拡大、大きいほど多い列＝縮小。
        private readonly int[] _columnLevels;
        private readonly int _baseColumns;
        private readonly float _fillRatio;
        private int _levelIndex;

        private float _zoom = 1f;
        private Vector2 _pan = Vector2.zero;
        // 既定でスタート（左上）が見える位置へ寄せたか。ジオメトリ確定後に 1 度だけ寄せる。
        private bool _startAligned;

        // ドラッグ開始時のポインタ位置とパン量（ドラッグ中の差分計算に使う）。
        private int _dragPointerId = -1;
        private Vector2 _dragStartPointer;
        private Vector2 _dragStartPan;

        /// <summary>
        /// <paramref name="root"/> から虫眼鏡ボタン・ドラッグ層を取得して配線する。ズーム段階は
        /// <paramref name="requestedColumnLevels"/>（例: 2/3/4/6/8）を <paramref name="gridColumns"/> と
        /// <paramref name="baseColumns"/> に合わせて整えたもの。要素が見つからないときは何もしない。
        /// </summary>
        public BoardZoomController(
            VisualElement root,
            VisualElement boardArea,
            BoardLayoutCalculator layout,
            int baseColumns,
            int gridColumns,
            float fillRatio,
            int[] requestedColumnLevels)
        {
            _boardArea = boardArea;
            _layout = layout;
            _baseColumns = Mathf.Clamp(baseColumns, 2, Mathf.Max(2, gridColumns));
            _fillRatio = fillRatio;
            _columnLevels = BuildColumnLevels(requestedColumnLevels, _baseColumns, gridColumns);
            _levelIndex = Array.IndexOf(_columnLevels, _baseColumns);
            if (_levelIndex < 0)
            {
                _levelIndex = 0;
            }
            _zoom = ScaleForColumns(_columnLevels[_levelIndex], _baseColumns, _fillRatio);

            _dragLayer = root?.Q<VisualElement>("BoardDragLayer");
            _zoomPill = root?.Q<VisualElement>("ZoomPill");
            _zoomMagnifier = root?.Q<VisualElement>("ZoomMagnifier");
            _zoomInButton = root?.Q<Button>("ZoomInButton");
            _zoomOutButton = root?.Q<Button>("ZoomOutButton");

            if (_boardArea == null || _dragLayer == null || _zoomInButton == null || _zoomOutButton == null)
            {
                Debug.LogWarning("ズーム用の UI 要素が見つかりませんでした。ズーム機能は無効になります。");
                return;
            }

            _zoomInButton.clicked += () => Step(+1);
            _zoomOutButton.clicked += () => Step(-1);

            // ドラッグでのパン。捕捉は自前で行い、盤面外で離しても PointerUp を受け取れるようにする。
            _dragLayer.RegisterCallback<PointerDownEvent>(OnPointerDown);
            _dragLayer.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _dragLayer.RegisterCallback<PointerUpEvent>(OnPointerUp);

            OnLayoutChanged();
        }

        /// <summary>
        /// 虫眼鏡画像をロードしてピル中央の飾りアイコンに貼り、表示する。未配置・失敗なら中央アイコンは
        /// 非表示のまま（+ と − が直接隣り合う）。
        /// </summary>
        public async UniTaskVoid LoadMagnifierIconAsync(CancellationToken ct)
        {
            if (_zoomMagnifier == null || _zoomPill == null)
            {
                return;
            }
            Sprite sprite = await _spriteLoader.TryLoadAsync(MagnifierAddress, "ズームボタンの虫眼鏡画像", ct);
            if (sprite == null)
            {
                return;
            }
            _zoomMagnifier.style.backgroundImage = new StyleBackground(sprite);
            _zoomMagnifier.style.display = DisplayStyle.Flex;
            _zoomPill.AddToClassList("zoom-pill--has-icon");
        }

        /// <summary>
        /// レイアウト確定・リサイズのたびに呼ぶ。まだ既定位置へ寄せていなければスタート（左上）へ寄せ、
        /// そうでなければ現在のパンを新しい寸法でクランプし直し、ボタン・ドラッグ層の状態を更新する。
        /// </summary>
        public void OnLayoutChanged()
        {
            if (_boardArea == null)
            {
                return;
            }
            if (!TryGetGeometry(out Vector2 center, out Vector2 scaledExtent, out Vector2 container))
            {
                return; // 寸法未確定。次の GeometryChanged で再試行する。
            }

            if (!_startAligned)
            {
                // 大きな値を渡してクランプ＝可動範囲の左上端（スタート側）へ寄せる。
                _pan = ClampPan(new Vector2(1e9f, 1e9f), center, scaledExtent, container);
                _startAligned = true;
            }
            else
            {
                _pan = ClampPan(_pan, center, scaledExtent, container);
            }

            ApplyTransform();
            UpdateInteractivity(scaledExtent, container);
        }

        /// <summary>
        /// リング領域内の正規化位置 <paramref name="normalized"/>（各軸 0..1・左上原点）が画面中央に来るようパンする。
        /// <paramref name="resetZoom"/> が true なら先にズーム倍率を基準列数（既定表示）へ戻す。
        /// コマ移動の開始時（ズームを戻して中央寄せ）と移動中の追従に <see cref="BoardPresenter"/> から呼ぶ。
        /// 盤面が画面を覆い続ける範囲へクランプするので、盤面端付近では中央に寄り切らない。
        /// </summary>
        public void CenterOn(Vector2 normalized, bool resetZoom)
        {
            if (_boardArea == null)
            {
                return;
            }
            if (resetZoom)
            {
                _levelIndex = Array.IndexOf(_columnLevels, _baseColumns);
                if (_levelIndex < 0)
                {
                    _levelIndex = 0;
                }
                _zoom = ScaleForColumns(_columnLevels[_levelIndex], _baseColumns, _fillRatio);
            }

            if (!TryGetGeometry(out Vector2 center, out Vector2 scaledExtent, out Vector2 container))
            {
                ApplyTransform(); // 寸法未確定でもズームだけは反映しておく。
                return;
            }

            // 対象点の、リング領域中心を原点としたローカル座標（スケール前）。
            BoardLayoutCalculator.AreaLayout area = _layout.Area;
            Vector2 localFromCenter = new Vector2(
                (normalized.x - 0.5f) * area.Width,
                (normalized.y - 0.5f) * area.Height);
            // 対象点の画面位置は center + pan + zoom*localFromCenter。これがコンテナ中央に来る pan を解く。
            Vector2 pan = container * 0.5f - center - _zoom * localFromCenter;
            _pan = ClampPan(pan, center, scaledExtent, container);
            ApplyTransform();
            UpdateInteractivity(scaledExtent, container);
        }

        /// <summary>ズーム段階を <paramref name="direction"/>（+1 拡大＝列を減らす / -1 縮小＝列を増やす）動かす。</summary>
        private void Step(int direction)
        {
            // index が小さい＝少ない列＝拡大。拡大(+1)は index を下げる。
            int next = Mathf.Clamp(_levelIndex - direction, 0, _columnLevels.Length - 1);
            if (next == _levelIndex)
            {
                return;
            }
            float previousZoom = _zoom;
            _levelIndex = next;
            _zoom = ScaleForColumns(_columnLevels[_levelIndex], _baseColumns, _fillRatio);

            if (TryGetGeometry(out Vector2 center, out Vector2 scaledExtent, out Vector2 container))
            {
                // いま画面中央に見えている盤面上の点を動かさないようにパンを補正してから拡大縮小する
                // （＝カメラ中心ズーム。右上を見ている状態でズームすればそのまま右上が拡大される）。
                _pan = ZoomAroundViewportCenter(_pan, previousZoom, _zoom, center, container);
                _pan = ClampPan(_pan, center, scaledExtent, container);
                ApplyTransform();
                UpdateInteractivity(scaledExtent, container);
            }
            else
            {
                ApplyTransform();
            }
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            _dragPointerId = evt.pointerId;
            _dragStartPointer = evt.position;
            _dragStartPan = _pan;
            _dragLayer.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (_dragPointerId != evt.pointerId || !_dragLayer.HasPointerCapture(evt.pointerId))
            {
                return;
            }
            if (!TryGetGeometry(out Vector2 center, out Vector2 scaledExtent, out Vector2 container))
            {
                return;
            }
            Vector2 delta = (Vector2)evt.position - _dragStartPointer;
            _pan = ClampPan(_dragStartPan + delta, center, scaledExtent, container);
            ApplyTransform();
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (_dragPointerId != evt.pointerId)
            {
                return;
            }
            if (_dragLayer.HasPointerCapture(evt.pointerId))
            {
                _dragLayer.ReleasePointer(evt.pointerId);
            }
            _dragPointerId = -1;
            evt.StopPropagation();
        }

        private void ApplyTransform()
        {
            _boardArea.style.scale = new Scale(new Vector2(_zoom, _zoom));
            _boardArea.style.translate = new Translate(_pan.x, _pan.y);
        }

        /// <summary>ボタンの有効/無効と、ドラッグ層の反応可否（盤面が画面からはみ出しているときだけ有効）を更新する。</summary>
        private void UpdateInteractivity(Vector2 scaledExtent, Vector2 container)
        {
            _zoomInButton.SetEnabled(_levelIndex > 0);                        // まだ拡大（列を減らす）余地がある
            _zoomOutButton.SetEnabled(_levelIndex < _columnLevels.Length - 1); // まだ縮小（列を増やす）余地がある
            bool pannable = scaledExtent.x > container.x + 0.5f || scaledExtent.y > container.y + 0.5f;
            _dragLayer.pickingMode = pannable ? PickingMode.Position : PickingMode.Ignore;
        }

        /// <summary>
        /// 現在のレイアウトから、リング領域（マス実寸を含めた外形）の中心・拡大後の外形サイズ・コンテナ寸法を求める。
        /// 寸法が未確定（レイアウト前）なら false。
        /// </summary>
        private bool TryGetGeometry(out Vector2 center, out Vector2 scaledExtent, out Vector2 container)
        {
            center = default;
            scaledExtent = default;
            container = default;
            if (_layout == null || _boardArea?.parent == null)
            {
                return false;
            }
            BoardLayoutCalculator.AreaLayout area = _layout.Area;
            if (area.Width <= 0f || area.Height <= 0f)
            {
                return false;
            }
            container = new Vector2(_boardArea.parent.resolvedStyle.width, _boardArea.parent.resolvedStyle.height);
            if (container.x <= 0f || container.y <= 0f)
            {
                return false;
            }
            // リング領域の中心（マスは領域端から半マスはみ出すので外形は Width/Height + CellSize）。
            center = new Vector2(area.Left + area.Width * 0.5f, area.Top + area.Height * 0.5f);
            float boundingWidth = area.Width + area.CellSize;
            float boundingHeight = area.Height + area.CellSize;
            scaledExtent = new Vector2(boundingWidth * _zoom, boundingHeight * _zoom);
            return true;
        }

        /// <summary>
        /// 基準列数 <paramref name="baseColumns"/> で画面幅に割り付けたレイアウトを基準（スケール 1）としたとき、
        /// 画面幅に <paramref name="columns"/> 列を収めるためのスケールを返す純粋関数。
        /// 列を増やすほど &lt; 1（縮小）、減らすほど &gt; 1（拡大）になる。
        /// </summary>
        public static float ScaleForColumns(int columns, int baseColumns, float fillRatio)
        {
            float spanBase = (baseColumns - 1) + fillRatio;
            float spanColumns = (columns - 1) + fillRatio;
            if (spanColumns <= 0f)
            {
                return 1f;
            }
            return spanBase / spanColumns;
        }

        /// <summary>
        /// ズーム倍率が <paramref name="previousZoom"/>→<paramref name="newZoom"/> に変わるとき、
        /// 画面（コンテナ）中央に見えている盤面上の点が動かないようにパン量を補正する純粋関数。
        /// 盤面はリング中心 <paramref name="center"/>（コンテナ座標・パン 0 のときの位置）を原点に拡大縮小し、
        /// パン <paramref name="pan"/> で平行移動する。焦点＝コンテナ中央 <paramref name="container"/>/2 と原点の差 f を用いて
        /// pan' = ratio·pan + f·(1 - ratio)（ratio = newZoom/previousZoom）を返す。クランプは呼び出し側で行う。
        /// </summary>
        public static Vector2 ZoomAroundViewportCenter(Vector2 pan, float previousZoom, float newZoom, Vector2 center, Vector2 container)
        {
            if (previousZoom <= 0f)
            {
                return pan;
            }
            float ratio = newZoom / previousZoom;
            Vector2 focal = container * 0.5f - center; // コンテナ中央 − 拡大原点
            return ratio * pan + focal * (1f - ratio);
        }

        /// <summary>
        /// 要求された表示列数 <paramref name="requested"/> を、盤面の列数 <paramref name="gridColumns"/> を上限に
        /// 丸め、2 未満を除き、基準列数 <paramref name="baseColumns"/> を必ず含めて昇順・重複なしにした配列を返す純粋関数。
        /// （存在する列数より多くは表示できないため上限で頭打ちにする。）
        /// </summary>
        public static int[] BuildColumnLevels(int[] requested, int baseColumns, int gridColumns)
        {
            int cap = Mathf.Max(2, gridColumns);
            SortedSet<int> set = new();
            if (requested != null)
            {
                foreach (int columns in requested)
                {
                    if (columns >= 2)
                    {
                        set.Add(Mathf.Min(columns, cap));
                    }
                }
            }
            set.Add(Mathf.Clamp(baseColumns, 2, cap)); // 既定列は必ず段階に含める
            int[] levels = new int[set.Count];
            set.CopyTo(levels);
            return levels; // 昇順
        }

        /// <summary>
        /// パン量 <paramref name="pan"/> を、盤面外形が画面（コンテナ）を覆い続ける範囲にクランプする純粋関数。
        /// 各軸で <see cref="ClampPanAxis"/> を適用する。
        /// </summary>
        public static Vector2 ClampPan(Vector2 pan, Vector2 center, Vector2 scaledExtent, Vector2 container)
        {
            return new Vector2(
                ClampPanAxis(pan.x, center.x, scaledExtent.x, container.x),
                ClampPanAxis(pan.y, center.y, scaledExtent.y, container.y));
        }

        /// <summary>
        /// 1 軸のパン量をクランプする純粋関数。中心 <paramref name="center"/> に外形サイズ
        /// <paramref name="scaledExtent"/> の盤面があるとき、盤面がコンテナ（幅 <paramref name="containerExtent"/>）を
        /// 覆い続ける（＝隙間ができない）範囲へ収める。盤面がコンテナより小さい軸は動かせないので 0 を返す。
        /// </summary>
        public static float ClampPanAxis(float pan, float center, float scaledExtent, float containerExtent)
        {
            float half = scaledExtent * 0.5f;
            float min = containerExtent - center - half; // 盤面の遠い端をコンテナの遠い端まで
            float max = -center + half;                  // 盤面の近い端をコンテナの近い端まで
            if (min > max)
            {
                return 0f; // 盤面がコンテナより小さい＝動かす余地なし（レイアウトのまま）
            }
            return Mathf.Clamp(pan, min, max);
        }

        public void Dispose()
        {
            _spriteLoader.Dispose();
        }
    }
}
