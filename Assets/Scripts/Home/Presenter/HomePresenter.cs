using Common.SceneManagement;
using Common.SoundManagement;
using Common.Store;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Home.Presenter
{
    // 見出し（タイトルロゴ）を表示し、画面のどこかがタップされたら次のシーンへ遷移する。
    [RequireComponent(typeof(UIDocument))]
    public class HomePresenter : MonoBehaviour
    {
        private SceneTransitioner _sceneTransitioner;
        private SoundStore _soundStore;
        private SoundPlayer _soundPlayer;
        [SerializeField] private Scenes _nextScene;

        private UIDocument _uiDocument;
        private VisualElement _root;
        private bool _transiting;

        [Inject]
        public void Construct(SceneTransitioner sceneTransitioner, SoundStore soundStore, SoundPlayer soundPlayer)
        {
            _sceneTransitioner = sceneTransitioner;
            _soundStore = soundStore;
            _soundPlayer = soundPlayer;
        }

        private void Awake()
        {
            _uiDocument = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            _root = _uiDocument.rootVisualElement;
            if (_root == null)
            {
                Debug.LogError("Home の rootVisualElement が見つかりませんでした。");
                return;
            }
            _root.RegisterCallback<PointerDownEvent>(OnPointerDown);
        }

        private void OnDisable()
        {
            if (_root != null) _root.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            _root = null;
        }

        private void OnPointerDown(PointerDownEvent _)
        {
            if (_transiting) return;
            _transiting = true;
            _soundPlayer.PlaySE(_soundStore.Enter1SE);
            _sceneTransitioner.Transit(_nextScene).Forget();
        }
    }
}
