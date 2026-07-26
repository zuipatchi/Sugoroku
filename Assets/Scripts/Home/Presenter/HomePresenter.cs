using System;
using System.Threading;
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
    // 2つのモードボタン（一人用/オンライン）とクレジットボタンを表示する。
    // 「一人用モード」は CharacterSelect（キャラ選択）へ、「オンラインプレイ」は Matching へ遷移し、
    // 「クレジット」はクレジットモーダルを開く。
    // 背景には固定画像 Image/HomeBackGround を全画面に表示する
    // （前面 UI が読めるよう上に暗いスクリムを重ねる。未配置は色面プレースホルダ）。
    // 表示前に画像のロードを終えるため ISceneReady を実装する。
    [RequireComponent(typeof(UIDocument))]
    public sealed class HomePresenter : MonoBehaviour, ISceneReady
    {
        // 全画面背景に敷くホーム背景画像の Addressables アドレス。
        private const string BackgroundAddress = "Image/HomeBackGround";
        // 背景画像が未配置のときのフォールバック色（テーマの暗い地色）。
        private static readonly Color BackgroundPlaceholderColor = new(0.086f, 0.086f, 0.14f, 1f);

        private SceneTransitioner _sceneTransitioner;
        private SoundStore _soundStore;
        private SoundPlayer _soundPlayer;
        private GameSessionModel _gameSessionModel;

        private UIDocument _uiDocument;
        private VisualElement _root;
        private Button _singlePlayerButton;
        private Button _onlineButton;
        private Button _creditButton;
        private Button _creditCloseButton;
        private VisualElement _creditOverlay;
        private bool _transiting;

        private readonly AddressableSpriteLoader _spriteLoader = new();
        private UniTask _backgroundInitTask;
        private bool _backgroundInitStarted;

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
            _creditButton = _root.Q<Button>("CreditButton");
            _creditCloseButton = _root.Q<Button>("CreditCloseButton");
            _creditOverlay = _root.Q<VisualElement>("CreditOverlay");
            if (_singlePlayerButton == null || _onlineButton == null
                || _creditButton == null || _creditCloseButton == null || _creditOverlay == null)
            {
                Debug.LogError("Home のボタン・クレジットモーダルが見つかりませんでした。");
                return;
            }

            _singlePlayerButton.clicked += OnSinglePlayerClicked;
            _onlineButton.clicked += OnOnlineClicked;
            _creditButton.clicked += OnCreditClicked;
            _creditCloseButton.clicked += OnCreditCloseClicked;
        }

        // 直接起動でも背景を出せるよう Start でも初期化を起動する（ReadyAsync は完了を待つだけ）。
        private void Start()
        {
            EnsureBackgroundStarted();
        }

        // SceneTransitioner がフェードイン前に await する。背景画像のロードが終わるまで暗幕を維持する。
        public async UniTask ReadyAsync(CancellationToken ct)
        {
            EnsureBackgroundStarted();
            await _backgroundInitTask.AttachExternalCancellation(ct);
        }

        private void EnsureBackgroundStarted()
        {
            if (_backgroundInitStarted)
            {
                return;
            }
            _backgroundInitStarted = true;
            _backgroundInitTask = BuildBackgroundAsync(destroyCancellationToken).Preserve();
        }

        // 固定のホーム背景画像（Image/HomeBackGround）を背景（HeroImage）に表示する。
        private async UniTask BuildBackgroundAsync(CancellationToken ct)
        {
            try
            {
                VisualElement root = _uiDocument.rootVisualElement;
                if (root == null)
                {
                    return;
                }

                VisualElement heroImage = root.Q<VisualElement>("HeroImage");
                if (heroImage == null)
                {
                    Debug.LogError("Home の背景画像要素（HeroImage）が見つかりませんでした。");
                    return;
                }

                Sprite background = await _spriteLoader.TryLoadAsync(BackgroundAddress, "ホーム背景画像", ct);

                if (this == null)
                {
                    return;
                }

                if (background != null)
                {
                    heroImage.style.backgroundImage = new StyleBackground(background);
                }
                else
                {
                    // 画像未配置時は色面プレースホルダ。
                    heroImage.style.backgroundColor = BackgroundPlaceholderColor;
                }
            }
            catch (OperationCanceledException)
            {
                // シーン破棄時のキャンセル。ハンドルは OnDestroy で解放する。
            }
        }

        private void OnSinglePlayerClicked()
        {
            if (_transiting) return;
            _transiting = true;
            _soundPlayer.PlaySE(_soundStore.Enter1SE);
            _gameSessionModel.SetSinglePlayer();
            _sceneTransitioner.Transit(Scenes.CharacterSelect).Forget();
        }

        private void OnOnlineClicked()
        {
            if (_transiting) return;
            _transiting = true;
            _soundPlayer.PlaySE(_soundStore.Enter1SE);
            _sceneTransitioner.Transit(Scenes.Matching).Forget();
        }

        private void OnCreditClicked()
        {
            if (_transiting) return;
            _soundPlayer.PlaySE(_soundStore.Enter2SE);
            _creditOverlay.style.display = DisplayStyle.Flex;
        }

        private void OnCreditCloseClicked()
        {
            _soundPlayer.PlaySE(_soundStore.Cancel1SE);
            _creditOverlay.style.display = DisplayStyle.None;
        }

        private void OnDisable()
        {
            if (_singlePlayerButton != null) _singlePlayerButton.clicked -= OnSinglePlayerClicked;
            if (_onlineButton != null) _onlineButton.clicked -= OnOnlineClicked;
            if (_creditButton != null) _creditButton.clicked -= OnCreditClicked;
            if (_creditCloseButton != null) _creditCloseButton.clicked -= OnCreditCloseClicked;
            _singlePlayerButton = null;
            _onlineButton = null;
            _creditButton = null;
            _creditCloseButton = null;
            _creditOverlay = null;
            _root = null;
        }

        private void OnDestroy()
        {
            _spriteLoader.Dispose();
        }
    }
}
