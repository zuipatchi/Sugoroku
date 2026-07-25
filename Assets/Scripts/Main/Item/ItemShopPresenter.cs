using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Main.Item
{
    /// <summary>
    /// アイテム取得マスに止まったときに開く「アイテムショップ」モーダル。ランダムなラインナップ
    /// （<see cref="ItemCatalog.RandomLineup"/>）を絵・名前・効果説明・価格のカードで並べ、タップで 1 つ購入する
    /// （代金の支払い・手札への追加は呼び出し側＝<c>BoardPresenter</c> が担う）。カードは一度に 2 枚だけ見せ、
    /// 残りは前後ボタン（‹ ›）＋ドットのカルーセルでページ送りする。所持金（<c>budget</c>）で買えない
    /// カードは無効表示にする。「買わずに閉じる」ボタン・破棄では null（買わない）を返す。
    /// 誤タップ防止のため、他のモーダルと違い暗幕（モーダル外）クリックでは閉じない。
    /// <see cref="MiniGameSelectPresenter"/> と同じく <c>BoardPresenter</c> が new する協調クラスで、開いている間だけ
    /// Board の <see cref="UIDocument"/> の sortingOrder を上げてスピンボタン等より前面に出す。
    /// UI は Board.uxml の <c>ItemShopModal</c>。
    /// </summary>
    public sealed class ItemShopPresenter
    {
        private const string OpenClass = "item-modal--open";
        private const string CardImageEmptyClass = "item-shop-card__image--empty";
        private const string CardDisabledClass = "item-shop-card--disabled";
        private const string PriceUnaffordableClass = "item-shop-card__price--unaffordable";
        private const string DotActiveClass = "item-shop__dot--active";
        // モーダルを開いている間だけ Board の UIDocument を前面へ持ち上げる SortingOrder（他のモーダルと同値）。
        private const float RaisedSortingOrder = 100f;
        // 一度に見せるカード枚数と、カード 1 枚が占める横幅（.item-shop-card の width 130 + 左右 margin 6+6）。
        // ビューポート幅・1 ページのスライド量は USS の値と一致させる（ズレるとページ境界が合わない）。
        private const int CardsPerPage = 2;
        private const float CardSlotWidthPx = 142f;
        private const float PageWidthPx = CardsPerPage * CardSlotWidthPx;

        private readonly VisualElement _overlay;
        private readonly VisualElement _list;
        private readonly UIDocument _document;
        // アイテム絵をロードしてキャッシュへ入れるローダ（BoardPresenter.LoadItemSpriteAsync）。
        // ここで読ませることで、購入後の手札サムネイルも同じキャッシュから引ける。
        private readonly Func<ItemDefinition, CancellationToken, UniTask<Sprite>> _spriteLoader;
        // 画像ロードを打ち切るためのトークン（シーン破棄）。
        private readonly CancellationToken _ct;
        private float _baseSortingOrder;
        // 選択結果を受け渡す完了ソース（買ったアイテム／キャンセル・破棄で null）。開いている間だけ非 null。
        private UniTaskCompletionSource<ItemId?> _selectionSource;
        // カルーセル（一度に 2 枚・残りは前後ボタンでページ送り）の要素と現在ページ。BuildCards で組み立てる。
        private VisualElement _track;
        private Button _prevButton;
        private Button _nextButton;
        private VisualElement _dots;
        private int _pageIndex;
        private int _pageCount;

        public ItemShopPresenter(VisualElement overlay, UIDocument document,
            Func<ItemDefinition, CancellationToken, UniTask<Sprite>> spriteLoader, CancellationToken ct)
        {
            _overlay = overlay;
            _document = document;
            _spriteLoader = spriteLoader;
            _ct = ct;
            _list = overlay.Q<VisualElement>("ItemShopList");

            // 購入ショップは他のモーダルと違い、暗幕（モーダル外）クリックでは閉じない。
            // 誤タップでの購入機会の取りこぼしを防ぐため、閉じるのは明示的な「買わずに閉じる」ボタンだけにする。
            Button cancel = overlay.Q<Button>("ItemShopCancel");
            if (cancel != null)
            {
                cancel.clicked += () => Resolve(null);
            }
        }

        /// <summary>
        /// ラインナップ <paramref name="lineup"/> のショップを開いて、買われたアイテムを待つ。
        /// 価格が所持金 <paramref name="budget"/> を超えるカードは無効（買えない）にする。
        /// 「買わずに閉じる」ボタン・<paramref name="ct"/> の破棄では null（買わない）。暗幕クリックでは閉じない。
        /// </summary>
        public async UniTask<ItemId?> SelectAsync(IReadOnlyList<ItemDefinition> lineup, int budget, CancellationToken ct)
        {
            // 二重オープンは前の選択を破棄してから開き直す。
            _selectionSource?.TrySetResult(null);

            BuildCards(lineup, budget);

            UniTaskCompletionSource<ItemId?> source = new();
            _selectionSource = source;
            Open();

            try
            {
                using (ct.Register(() => source.TrySetResult(null)))
                {
                    return await source.Task;
                }
            }
            finally
            {
                Close();
                _selectionSource = null;
            }
        }

        /// <summary>
        /// ラインナップをカルーセルで並べる。一度に <see cref="CardsPerPage"/> 枚（＝2 枚）だけビューポートに見せ、
        /// 残りは前後ボタン（‹ ›）とドットでページ送りする。ラインナップが 2 枚以下ならページ送り UI は隠す。
        /// </summary>
        private void BuildCards(IReadOnlyList<ItemDefinition> lineup, int budget)
        {
            if (_list == null)
            {
                return;
            }
            _list.Clear();
            _track = null;
            _prevButton = null;
            _nextButton = null;
            _dots = null;
            _pageIndex = 0;
            _pageCount = 0;
            if (lineup == null || lineup.Count == 0)
            {
                return;
            }

            _pageCount = Mathf.CeilToInt(lineup.Count / (float)CardsPerPage);

            // カルーセル本体（前ボタン・ビューポート(トラック)・次ボタン）。
            VisualElement carousel = new();
            carousel.AddToClassList("item-shop__carousel");

            _prevButton = new Button(() => ShowPage(_pageIndex - 1)) { text = "‹" };
            _prevButton.AddToClassList("item-shop__nav");
            carousel.Add(_prevButton);

            VisualElement viewport = new();
            viewport.AddToClassList("item-shop__viewport");
            _track = new VisualElement();
            _track.AddToClassList("item-shop__track");
            viewport.Add(_track);
            carousel.Add(viewport);

            _nextButton = new Button(() => ShowPage(_pageIndex + 1)) { text = "›" };
            _nextButton.AddToClassList("item-shop__nav");
            carousel.Add(_nextButton);

            _list.Add(carousel);

            for (int i = 0; i < lineup.Count; i++)
            {
                _track.Add(BuildCard(lineup[i], lineup[i].Price <= budget));
            }

            // ページ送りのドット（1 ページ 1 個）。
            _dots = new VisualElement();
            _dots.AddToClassList("item-shop__dots");
            for (int p = 0; p < _pageCount; p++)
            {
                VisualElement dot = new();
                dot.AddToClassList("item-shop__dot");
                _dots.Add(dot);
            }
            _list.Add(_dots);

            // 1 ページ（2 枚以下）ならページ送り UI は不要なので隠す。
            bool paged = _pageCount > 1;
            DisplayStyle navDisplay = paged ? DisplayStyle.Flex : DisplayStyle.None;
            _prevButton.style.display = navDisplay;
            _nextButton.style.display = navDisplay;
            _dots.style.display = navDisplay;

            ShowPage(0);
        }

        /// <summary>
        /// アイテム 1 種類分のカード（絵＋名前＋効果説明＋価格）を作る。買えないカード（価格 &gt; 所持金）は
        /// 無効クラスを付けてタップを受けない。画像は遅延ロードで貼る（未配置は暗い座面のまま）。
        /// </summary>
        private VisualElement BuildCard(ItemDefinition def, bool affordable)
        {
            VisualElement card = new();
            card.AddToClassList("item-shop-card");
            if (affordable)
            {
                ItemId id = def.Id;
                card.RegisterCallback<ClickEvent>(_ => Resolve(id));
            }
            else
            {
                card.AddToClassList(CardDisabledClass);
            }

            VisualElement image = new();
            image.AddToClassList("item-shop-card__image");
            image.AddToClassList(CardImageEmptyClass);
            card.Add(image);

            Label name = new() { text = def.DisplayName };
            name.AddToClassList("item-shop-card__name");
            card.Add(name);

            Label desc = new() { text = def.Description };
            desc.AddToClassList("item-shop-card__description");
            card.Add(desc);

            Label price = new() { text = $"{def.Price} G" };
            price.AddToClassList("item-shop-card__price");
            if (!affordable)
            {
                price.AddToClassList(PriceUnaffordableClass);
            }
            card.Add(price);

            LoadCardImageAsync(def, image).Forget();
            return card;
        }

        /// <summary>
        /// カルーセルを <paramref name="page"/> ページ目へ送る（範囲外はクランプ）。トラックを 1 ページ幅ぶん
        /// 左へスライドさせ、前後ボタンの有効／無効とドットの現在位置を更新する。
        /// </summary>
        private void ShowPage(int page)
        {
            if (_track == null || _pageCount <= 0)
            {
                return;
            }
            _pageIndex = Mathf.Clamp(page, 0, _pageCount - 1);

            _track.style.translate = new Translate(
                new Length(-_pageIndex * PageWidthPx, LengthUnit.Pixel),
                new Length(0f, LengthUnit.Pixel));

            _prevButton?.SetEnabled(_pageIndex > 0);
            _nextButton?.SetEnabled(_pageIndex < _pageCount - 1);

            if (_dots != null)
            {
                for (int i = 0; i < _dots.childCount; i++)
                {
                    _dots[i].EnableInClassList(DotActiveClass, i == _pageIndex);
                }
            }
        }

        /// <summary>カード 1 枚のアイテム絵をロードして貼る。未配置・キャンセルなら何もしない（名前と価格のみ表示）。</summary>
        private async UniTaskVoid LoadCardImageAsync(ItemDefinition def, VisualElement image)
        {
            if (_spriteLoader == null || def == null)
            {
                return;
            }
            Sprite sprite = await _spriteLoader(def, _ct);
            if (sprite == null || image == null)
            {
                return;
            }
            image.style.backgroundImage = new StyleBackground(sprite);
            image.RemoveFromClassList(CardImageEmptyClass);
        }

        private void Open()
        {
            // 閉→開の遷移でだけ SortingOrder を退避・変更する（既に持ち上げ済みの値を基準にしない）。
            if (_document != null && !_overlay.ClassListContains(OpenClass))
            {
                _baseSortingOrder = _document.sortingOrder;
                _document.sortingOrder = RaisedSortingOrder;
            }
            _overlay.AddToClassList(OpenClass);
        }

        private void Resolve(ItemId? id)
        {
            _selectionSource?.TrySetResult(id);
        }

        private void Close()
        {
            _overlay.RemoveFromClassList(OpenClass);
            if (_document != null)
            {
                _document.sortingOrder = _baseSortingOrder;
            }
        }
    }
}
