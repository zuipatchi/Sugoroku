using System.Threading;
using Common.MiniGame;
using Cysharp.Threading.Tasks;
using Main.Board;
using UnityEngine;
using UnityEngine.UIElements;

namespace Main.Item
{
    /// <summary>
    /// ミニゲームアイテム使用時に「どのミニゲームを遊ぶか」を選ばせるモーダル。
    /// <see cref="MiniGameCatalog"/> の各ゲームをサムネイル画像＋ゲーム名のカードで並べ、選んだゲームの ID を返す
    /// （キャンセル・暗幕クリック・破棄では null）。<see cref="ItemModalPresenter"/> と同じく
    /// <c>BoardPresenter</c> が new する協調クラスで、開いている間だけ Board の <see cref="UIDocument"/> の
    /// sortingOrder を上げてスピンボタン等より前面に出す。UI は Board.uxml の <c>MiniGameSelectModal</c>。
    /// </summary>
    public sealed class MiniGameSelectPresenter
    {
        private const string OpenClass = "item-modal--open";
        private const string CardImageEmptyClass = "minigame-card__image--empty";
        // モーダルを開いている間だけ Board の UIDocument を前面へ持ち上げる SortingOrder（ItemModalPresenter と同値）。
        private const float RaisedSortingOrder = 100f;

        private readonly VisualElement _overlay;
        private readonly VisualElement _list;
        private readonly UIDocument _document;
        // カードのサムネイル画像を Addressables からロードするローダ（BoardPresenter が持つ共有インスタンス）。
        private readonly BoardIconLoader _iconLoader;
        // 画像ロードを打ち切るためのトークン（シーン破棄）。
        private readonly CancellationToken _ct;
        private float _baseSortingOrder;
        // 選択結果を受け渡す完了ソース（選んだゲーム／キャンセル・破棄で null）。開いている間だけ非 null。
        private UniTaskCompletionSource<MiniGameId?> _selectionSource;

        public MiniGameSelectPresenter(VisualElement overlay, UIDocument document, BoardIconLoader iconLoader, CancellationToken ct)
        {
            _overlay = overlay;
            _document = document;
            _iconLoader = iconLoader;
            _ct = ct;
            _list = overlay.Q<VisualElement>("MiniGameSelectList");

            Button cancel = overlay.Q<Button>("MiniGameSelectCancel");
            if (cancel != null)
            {
                cancel.clicked += () => Resolve(null);
            }

            // 暗幕のクリックでもキャンセルする。カード内のクリックは target が暗幕にならないため閉じない。
            _overlay.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == _overlay)
                {
                    Resolve(null);
                }
            });

            BuildCards();
        }

        /// <summary>
        /// カタログの各ミニゲームを「サムネイル画像＋ゲーム名」のカードで生成する。増えたら自動で並ぶ。
        /// 画像は Addressables から遅延ロードし、未配置なら名前テキストのプレースホルダにフォールバックする。
        /// </summary>
        private void BuildCards()
        {
            if (_list == null)
            {
                return;
            }
            _list.Clear();
            foreach (MiniGameDefinition definition in MiniGameCatalog.All)
            {
                MiniGameId id = definition.Id;

                VisualElement card = new();
                card.AddToClassList("minigame-card");
                card.RegisterCallback<ClickEvent>(_ => Resolve(id));

                VisualElement image = new();
                image.AddToClassList("minigame-card__image");
                image.AddToClassList(CardImageEmptyClass);
                card.Add(image);

                Label name = new() { text = definition.DisplayName };
                name.AddToClassList("minigame-card__name");
                card.Add(name);

                _list.Add(card);

                LoadCardImageAsync(definition.ImageAddress, image).Forget();
            }
        }

        /// <summary>カード 1 枚のサムネイル画像をロードして貼る。未配置・キャンセルなら何もしない（名前のみ表示）。</summary>
        private async UniTaskVoid LoadCardImageAsync(string address, VisualElement image)
        {
            if (_iconLoader == null || string.IsNullOrEmpty(address))
            {
                return;
            }
            Sprite sprite = await _iconLoader.LoadSpriteAsync(address, "ミニゲーム画像", _ct);
            if (sprite == null || image == null)
            {
                return;
            }
            image.style.backgroundImage = new StyleBackground(sprite);
            image.RemoveFromClassList(CardImageEmptyClass);
        }

        /// <summary>
        /// モーダルを開いて選ばれたミニゲームを待つ。キャンセル・暗幕クリック・<paramref name="ct"/> の破棄では null。
        /// </summary>
        public async UniTask<MiniGameId?> SelectAsync(CancellationToken ct)
        {
            // 二重オープンは前の選択を破棄してから開き直す。
            _selectionSource?.TrySetResult(null);

            UniTaskCompletionSource<MiniGameId?> source = new();
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

        private void Resolve(MiniGameId? id)
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
