using Common.SceneManagement;
using Common.SoundManagement;
using Common.Store;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Title.GameStartButton
{
    // クリックされたらネクストシーンに遷移する
    [RequireComponent(typeof(UIDocument))]
    public class GameStartButtonPresenter : MonoBehaviour
    {
        private SceneTransitioner _sceneTransitioner;
        private SoundStore _soundStore;
        private SoundPlayer _soundPlayer;
        [SerializeField] private Scenes _nextScene;

        private UIDocument _uiDocument;
        private Button _gameStartButton;
        private IVisualElementScheduledItem _blinkSchedule;

        // 「Press start」を明滅させる間隔（ミリ秒）。USS の opacity transition と合わせて滑らかに点滅する。
        private const long BlinkIntervalMs = 700;
        private const string BlinkDimClass = "title-start-button--blink-dim";

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
            VisualElement root = _uiDocument.rootVisualElement;
            _gameStartButton = root.Q<Button>("GameStartButton");
            if (_gameStartButton == null)
            {
                Debug.LogError("GameStartButton が見つかりませんでした。");
                return;
            }
            _gameStartButton.clicked += OnClickGameStart;
            _blinkSchedule = _gameStartButton.schedule
                .Execute(() => _gameStartButton.ToggleInClassList(BlinkDimClass))
                .Every(BlinkIntervalMs);
        }

        private void OnDisable()
        {
            _blinkSchedule?.Pause();
            _blinkSchedule = null;
            if (_gameStartButton != null) _gameStartButton.clicked -= OnClickGameStart;
            _gameStartButton = null;
        }

        private void OnClickGameStart()
        {
            _gameStartButton.SetEnabled(false);
            _blinkSchedule?.Pause();
            _blinkSchedule = null;
            _gameStartButton.RemoveFromClassList(BlinkDimClass);
            _soundPlayer.PlaySE(_soundStore.Enter1SE);
            _sceneTransitioner.Transit(_nextScene).Forget();
        }
    }
}
