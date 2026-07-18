using System.Threading;
using Common.MiniGame;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace Main.Item
{
    /// <summary>
    /// ミニゲームアイテム使用時に「どのミニゲームを遊ぶか」を選ばせるモーダル。
    /// <see cref="MiniGameCatalog"/> の各ゲームをボタンで並べ、選んだゲームの ID を返す
    /// （キャンセル・暗幕クリック・破棄では null）。<see cref="ItemModalPresenter"/> と同じく
    /// <c>BoardPresenter</c> が new する協調クラスで、開いている間だけ Board の <see cref="UIDocument"/> の
    /// sortingOrder を上げてスピンボタン等より前面に出す。UI は Board.uxml の <c>MiniGameSelectModal</c>。
    /// </summary>
    public sealed class MiniGameSelectPresenter
    {
        private const string OpenClass = "item-modal--open";
        // モーダルを開いている間だけ Board の UIDocument を前面へ持ち上げる SortingOrder（ItemModalPresenter と同値）。
        private const float RaisedSortingOrder = 100f;

        private readonly VisualElement _overlay;
        private readonly VisualElement _list;
        private readonly UIDocument _document;
        private float _baseSortingOrder;
        // 選択結果を受け渡す完了ソース（選んだゲーム／キャンセル・破棄で null）。開いている間だけ非 null。
        private UniTaskCompletionSource<MiniGameId?> _selectionSource;

        public MiniGameSelectPresenter(VisualElement overlay, UIDocument document)
        {
            _overlay = overlay;
            _document = document;
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

            BuildButtons();
        }

        /// <summary>カタログの各ミニゲームを 1 ボタンずつ生成する。増えたら自動で並ぶ。</summary>
        private void BuildButtons()
        {
            if (_list == null)
            {
                return;
            }
            _list.Clear();
            foreach (MiniGameDefinition definition in MiniGameCatalog.All)
            {
                MiniGameId id = definition.Id;
                Button button = new() { text = definition.DisplayName };
                button.AddToClassList("minigame-select__button");
                button.clicked += () => Resolve(id);
                _list.Add(button);
            }
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
