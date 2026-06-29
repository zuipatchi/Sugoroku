using Common.SceneManagement;
using Common.SoundManagement;
using Common.Store;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Common.Option
{
    public class OptionPresenter : MonoBehaviour
    {
        private OptionModel _optionModel;
        private ModalStore _modalStore;
        private SoundStore _soundStore;
        private SoundPlayer _soundPlayer;
        private SceneTransitioner _sceneTransitioner;
        private VisualElement _overlay;
        private VisualElement _host;
        private readonly CompositeDisposable _disposables = new();

        [Inject]
        public void Construct(ModalStore modalStore, OptionModel optionModel, SoundStore soundStore, SoundPlayer soundPlayer, SceneTransitioner sceneTransitioner)
        {
            _modalStore = modalStore;
            _optionModel = optionModel;
            _soundStore = soundStore;
            _soundPlayer = soundPlayer;
            _sceneTransitioner = sceneTransitioner;
        }

        private void Awake()
        {
            UIDocument uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null)
            {
                Debug.LogError("UIDocument が見つかりませんでした。");
                return;
            }

            VisualElement root = uiDocument.rootVisualElement;
            Image optionSliders = root.Q<Image>("OptionSliders");
            _overlay = root.Q<VisualElement>("ModalOverlay");
            _host = root.Q<VisualElement>("ModalHost");

            optionSliders.RegisterCallback<ClickEvent>(_ => OpenModal());
        }

        private void Start()
        {
            SetupAsync().Forget();
        }

        private void OnDestroy()
        {
            _disposables.Dispose();
        }

        private async UniTask SetupAsync()
        {
            if (_host == null)
            {
                return;
            }

            await UniTask.WhenAll(_modalStore.Loaded, _soundStore.Loaded);
            TemplateContainer modal = _modalStore.Modal.Instantiate();

            OptionModalPresenter modalPresenter = new OptionModalPresenter(_optionModel, OnClickClose, BackToTitle);
            modalPresenter.Setup(modal, _disposables);

            _host.Add(modal);
            _overlay.style.display = DisplayStyle.None;
        }

        private void OpenModal()
        {
            _overlay.style.display = DisplayStyle.Flex;
            _soundPlayer.PlaySE(_soundStore.Enter2SE);
        }

        private void CloseModal()
        {
            _overlay.style.display = DisplayStyle.None;
        }

        private void OnClickClose()
        {
            _soundPlayer.PlaySE(_soundStore.Cancel1SE);
            CloseModal();
        }

        private void BackToTitle()
        {
            _soundPlayer.PlaySE(_soundStore.Enter1SE);
            CloseModal();
            _sceneTransitioner.Transit(Scenes.Title).Forget();
        }
    }
}
