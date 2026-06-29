using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UIElements;

namespace Common.Transition
{
    public class TransitionPresenter : MonoBehaviour
    {
        [SerializeField] private float _duration = 0.4f;

        private VisualElement _overlay;
        private Tweener _currentTween;

        private void Awake()
        {
            UIDocument uiDocument = GetComponent<UIDocument>();
            VisualElement root = uiDocument.rootVisualElement;

            _overlay = new VisualElement
            {
                pickingMode = PickingMode.Ignore
            };
            _overlay.style.position = Position.Absolute;
            _overlay.style.width = new Length(100, LengthUnit.Percent);
            _overlay.style.height = new Length(100, LengthUnit.Percent);
            _overlay.style.backgroundColor = Color.black;
            _overlay.style.opacity = 0f;
            _overlay.style.display = DisplayStyle.None;

            root.Add(_overlay);
        }

        private void OnDestroy()
        {
            _currentTween?.Kill();
            _currentTween = null;
        }

        public async UniTask CoverAsync()
        {
            await AnimateAsync(from: 0f, to: 1f, Ease.InQuad, hideOnComplete: false);
        }

        public async UniTask RevealAsync()
        {
            await AnimateAsync(from: 1f, to: 0f, Ease.OutQuad, hideOnComplete: true);
        }

        private async UniTask AnimateAsync(float from, float to, Ease ease, bool hideOnComplete)
        {
            float opacity = from;
            _overlay.style.opacity = opacity;
            _overlay.style.display = DisplayStyle.Flex;

            _currentTween?.Kill();
            UniTaskCompletionSource tcs = new();
            _currentTween = DOTween.To(
                () => opacity,
                v =>
                {
                    opacity = v;
                    _overlay.style.opacity = v;
                },
                to,
                _duration
            )
            .SetEase(ease)
            .OnComplete(() =>
            {
                if (hideOnComplete)
                {
                    _overlay.style.display = DisplayStyle.None;
                }
                tcs.TrySetResult();
            })
            .OnKill(() => tcs.TrySetResult());

            await tcs.Task;
        }
    }
}
