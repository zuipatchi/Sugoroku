using UnityEngine;
using UnityEngine.UIElements;

namespace Main.Board
{
    /// <summary>
    /// 盤面のマスをタップしたときに開く説明モーダル。マスの絵・名前・効果の説明と、
    /// 陣地マスの占拠状況のような補足行を表示して「閉じる」または暗幕クリックで閉じる。
    /// 見せるだけで盤面には触らないので、誰の手番でも・演出中でも開ける。
    /// 文言の組み立ては <see cref="BoardPresenter"/>（説明文の元は <see cref="BoardEventDescription"/>）が担い、
    /// ここは受け取ったものを出すだけ。<c>ItemModalPresenter</c> と同じく BoardPresenter が <c>new</c> する協調クラス。
    /// </summary>
    public sealed class BoardCellInfoPresenter
    {
        private const string OpenClass = "item-modal--open";
        private const string ImageEmptyClass = "item-modal__image--empty";
        // モーダルを開いている間だけ Board の UIDocument を前面へ持ち上げる SortingOrder
        // （ItemModalPresenter と同じ値。ルーレット・ミニゲームトリガより上、オプションより下）。
        private const float RaisedSortingOrder = 100f;

        private readonly VisualElement _overlay;
        private readonly VisualElement _image;
        private readonly Label _name;
        private readonly Label _description;
        private readonly Label _status;
        private readonly UIDocument _document;
        private float _baseSortingOrder;

        public BoardCellInfoPresenter(VisualElement overlay, UIDocument document)
        {
            _overlay = overlay;
            _document = document;
            _image = overlay.Q<VisualElement>("CellInfoImage");
            _name = overlay.Q<Label>("CellInfoName");
            _description = overlay.Q<Label>("CellInfoDescription");
            _status = overlay.Q<Label>("CellInfoStatus");

            Button closeButton = overlay.Q<Button>("CellInfoCloseButton");
            if (closeButton != null)
            {
                closeButton.clicked += Close;
            }

            // 暗幕のクリックでも閉じる（カード内のクリックは target が暗幕にならないので閉じない）。
            overlay.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == _overlay)
                {
                    Close();
                }
            });
        }

        /// <summary>開いているか（同じタップで開き直さないための判定に使う）。</summary>
        public bool IsOpen => _overlay.ClassListContains(OpenClass);

        /// <summary>
        /// マスの説明モーダルを開く。<paramref name="status"/> は陣地の占拠状況のような補足行で、
        /// 空なら行ごと隠す。<paramref name="sprite"/> が null のときは絵なしのプレースホルダで出す。
        /// </summary>
        public void Open(string title, string description, string status, Sprite sprite)
        {
            if (_name != null)
            {
                _name.text = title;
            }
            if (_description != null)
            {
                _description.text = description;
            }
            if (_status != null)
            {
                _status.text = status ?? string.Empty;
                _status.style.display = string.IsNullOrEmpty(status) ? DisplayStyle.None : DisplayStyle.Flex;
            }
            if (_image != null)
            {
                if (sprite != null)
                {
                    _image.style.backgroundImage = new StyleBackground(sprite);
                    _image.RemoveFromClassList(ImageEmptyClass);
                }
                else
                {
                    _image.style.backgroundImage = StyleKeyword.None;
                    _image.AddToClassList(ImageEmptyClass);
                }
            }

            // 閉→開の遷移でだけ SortingOrder を退避・変更する（開いたまま別のマスで開き直しても基準値を失わない）。
            if (_document != null && !IsOpen)
            {
                _baseSortingOrder = _document.sortingOrder;
                _document.sortingOrder = RaisedSortingOrder;
            }

            _overlay.AddToClassList(OpenClass);
        }

        /// <summary>モーダルを閉じる（開いていなければ何もしない）。</summary>
        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }
            _overlay.RemoveFromClassList(OpenClass);
            if (_document != null)
            {
                _document.sortingOrder = _baseSortingOrder;
            }
        }
    }
}
