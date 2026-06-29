using Common.GameSession;
using Common.SceneManagement;
using Common.SoundManagement;
using Common.Store;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Home.Presenter
{
    // タイトルロゴと2つのモードボタンを表示する。
    // 「一人用モード」は Main へ直接、「オンラインプレイ」は Matching へ遷移する。
    [RequireComponent(typeof(UIDocument))]
    public class HomePresenter : MonoBehaviour
    {
        private SceneTransitioner _sceneTransitioner;
        private SoundStore _soundStore;
        private SoundPlayer _soundPlayer;
        private GameSessionModel _gameSessionModel;

        private UIDocument _uiDocument;
        private VisualElement _root;
        private Button _singlePlayerButton;
        private Button _onlineButton;
        private bool _transiting;

        [Inject]
        public void Construct(
            SceneTransitioner sceneTransitioner,
            SoundStore soundStore,
            SoundPlayer soundPlayer,
            GameSessionModel gameSessionModel)
        {
            _sceneTransitioner = sceneTransitioner;
            _soundStore = soundStore;
            _soundPlayer = soundPlayer;
            _gameSessionModel = gameSessionModel;
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

            _singlePlayerButton = _root.Q<Button>("SinglePlayerButton");
            _onlineButton = _root.Q<Button>("OnlineButton");
            if (_singlePlayerButton == null || _onlineButton == null)
            {
                Debug.LogError("Home のモードボタンが見つかりませんでした。");
                return;
            }

            _singlePlayerButton.clicked += OnSinglePlayerClicked;
            _onlineButton.clicked += OnOnlineClicked;
        }

        private void OnDisable()
        {
            if (_singlePlayerButton != null) _singlePlayerButton.clicked -= OnSinglePlayerClicked;
            if (_onlineButton != null) _onlineButton.clicked -= OnOnlineClicked;
            _singlePlayerButton = null;
            _onlineButton = null;
            _root = null;
        }

        private void OnSinglePlayerClicked()
        {
            if (_transiting) return;
            _transiting = true;
            _soundPlayer.PlaySE(_soundStore.Enter1SE);
            _gameSessionModel.SetSinglePlayer();
            _sceneTransitioner.Transit(Scenes.Main).Forget();
        }

        private void OnOnlineClicked()
        {
            if (_transiting) return;
            _transiting = true;
            _soundPlayer.PlaySE(_soundStore.Enter1SE);
            _sceneTransitioner.Transit(Scenes.Matching).Forget();
        }
    }
}
