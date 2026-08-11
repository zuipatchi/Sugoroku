using UnityEngine.UIElements;

namespace Home.Presenter
{
    /// <summary>
    /// Home の全画面モーダル（クレジット・ルール説明）の開閉。
    /// 暗幕のフェードとカードのスケールインは USS の transition が担うが、
    /// **display を出したフレームでクラスを足しても補間されない**ので、
    /// 開くときは 1 フレーム置いてから <see cref="VisibleClass"/> を足し、
    /// 閉じるときはフェードが終わってから display を戻す
    /// （その間に開き直された場合に備えて <see cref="IsOpen"/> で弾く）。
    /// 開閉の SE・遷移ガードは呼び出し側（<see cref="HomePresenter"/>）が持つ。
    /// </summary>
    public sealed class HomeModal
    {
        // 表示中に足すクラス。Home.uss の .home-overlay--visible と揃える。
        private const string VisibleClass = "home-overlay--visible";

        // Home.uss の .home-overlay の transition-duration と揃える。
        private const long FadeMilliseconds = 180;

        private readonly VisualElement _overlay;
        private bool _open;

        public HomeModal(VisualElement overlay)
        {
            _overlay = overlay;
        }

        /// <summary>開いている（開こうとしている）か。</summary>
        public bool IsOpen => _open;

        public void Open()
        {
            if (_overlay == null || _open)
            {
                return;
            }
            _open = true;
            _overlay.style.display = DisplayStyle.Flex;
            _overlay.schedule.Execute(ShowIfOpen);
        }

        public void Close()
        {
            if (_overlay == null || !_open)
            {
                return;
            }
            _open = false;
            _overlay.RemoveFromClassList(VisibleClass);
            _overlay.schedule.Execute(HideIfClosed).ExecuteLater(FadeMilliseconds);
        }

        private void ShowIfOpen()
        {
            if (!_open)
            {
                return;
            }
            _overlay.AddToClassList(VisibleClass);
        }

        private void HideIfClosed()
        {
            if (_open)
            {
                return;
            }
            _overlay.style.display = DisplayStyle.None;
        }
    }
}
