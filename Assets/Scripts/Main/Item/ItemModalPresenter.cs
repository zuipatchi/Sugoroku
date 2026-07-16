using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Main.Item
{
    /// <summary>
    /// 手札のアイテムをクリックしたときに開く詳細モーダル。アイテム絵・名前・効果説明を表示し、
    /// 「使用する」で <see cref="ItemModel.Use"/> による消費（効果の発動は未実装）、
    /// 「閉じる」または暗幕クリックで閉じる。<c>BoardPresenter</c> が生成して手札クリックから
    /// <see cref="Open"/> を呼ぶ協調クラス（<c>BoardLandingPresentation</c> と同様）。
    /// </summary>
    public sealed class ItemModalPresenter
    {
        private const string OpenClass = "item-modal--open";
        private const string ImageEmptyClass = "item-modal__image--empty";
        // モーダルを開いている間だけ Board の UIDocument を前面へ持ち上げる SortingOrder。
        // ルーレット(10)・ミニゲームトリガ(20)より上、Common のオプションオーバーレイ(1000+)より下。
        private const float RaisedSortingOrder = 100f;

        private readonly VisualElement _overlay;
        private readonly VisualElement _image;
        private readonly Label _name;
        private readonly Label _description;
        private readonly ItemModel _items;
        private readonly int _player;
        // アイテム絵はロード済みキャッシュ（BoardPresenter._itemSprites）から引く。未ロードなら null。
        private readonly Func<ItemId, Sprite> _spriteResolver;
        // モーダルを開いている間、Board の UIDocument を回転中のルーレット等より前面に出すため
        // SortingOrder を一時的に上げ、閉じたら元へ戻す。
        private readonly UIDocument _document;
        private float _baseSortingOrder;
        private ItemId _current;

        public ItemModalPresenter(VisualElement overlay, ItemModel items, int player, Func<ItemId, Sprite> spriteResolver, UIDocument document)
        {
            _overlay = overlay;
            _items = items;
            _player = player;
            _spriteResolver = spriteResolver;
            _document = document;
            _image = overlay.Q<VisualElement>("ItemModalImage");
            _name = overlay.Q<Label>("ItemModalName");
            _description = overlay.Q<Label>("ItemModalDescription");

            Button useButton = overlay.Q<Button>("ItemModalUseButton");
            if (useButton != null)
            {
                useButton.clicked += UseCurrent;
            }

            Button closeButton = overlay.Q<Button>("ItemModalCloseButton");
            if (closeButton != null)
            {
                closeButton.clicked += Close;
            }

            // 暗幕のクリックでも閉じる。カード内のクリックは target が暗幕にならないため閉じない。
            overlay.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == _overlay)
                {
                    Close();
                }
            });
        }

        /// <summary>アイテム <paramref name="item"/> の詳細モーダルを開く。</summary>
        public void Open(ItemId item)
        {
            _current = item;

            ItemDefinition def = ItemCatalog.Find(item);
            if (_name != null)
            {
                _name.text = def?.DisplayName ?? item.ToString();
            }
            if (_description != null)
            {
                _description.text = def?.Description ?? string.Empty;
            }
            if (_image != null)
            {
                Sprite sprite = _spriteResolver?.Invoke(item);
                if (sprite != null)
                {
                    _image.style.backgroundImage = new StyleBackground(sprite);
                    _image.RemoveFromClassList(ImageEmptyClass);
                }
                else
                {
                    _image.AddToClassList(ImageEmptyClass);
                }
            }

            // 既に開いている状態で別カードから再度開かれても、持ち上げ済みの値を基準として
            // 取り込まないよう、閉→開の遷移でだけ SortingOrder を退避・変更する。
            if (_document != null && !_overlay.ClassListContains(OpenClass))
            {
                _baseSortingOrder = _document.sortingOrder;
                _document.sortingOrder = RaisedSortingOrder;
            }

            _overlay.AddToClassList(OpenClass);
        }

        /// <summary>
        /// 表示中のアイテムを 1 つ消費して閉じる。手札 UI の更新は <see cref="ItemModel.Used"/> の
        /// 購読側（BoardPresenter）が行うため、ここでは Model を呼ぶだけ。
        /// </summary>
        private void UseCurrent()
        {
            _items.Use(_player, _current);
            Close();
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
