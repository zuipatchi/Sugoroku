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
    // 2つのモードボタン（一人用/オンライン）とルール説明・クレジットボタンを表示する。
    // 「一人で遊ぶ」（一人用モード）は CharacterSelect（キャラ選択）へ、「オンラインプレイ」は Matching へ遷移し、
    // 「ルール説明」は遊び方のモーダル（中身は RuleBook が組み立てる）を、「クレジット」はクレジットモーダルを開く。
    // 背景には固定画像 Image/HomeBackGround を全画面に表示する
    // （前面 UI が読めるよう上に幕を重ねる。未配置は色面プレースホルダ）。
    // 表示前に画像のロードを終えるため ISceneReady を実装する。
    // 背景は動かさず、UI だけを暗幕が開くのに合わせて下から浮かび上がらせる。
    [RequireComponent(typeof(UIDocument))]
    public sealed class HomePresenter : MonoBehaviour, ISceneReady
    {
        // 全画面背景に敷くホーム背景画像の Addressables アドレス。
        private const string BackgroundAddress = "Image/HomeBackGround";
        // 背景画像が未配置のときのフォールバック色（テーマの暗い地色）。
        private static readonly Color BackgroundPlaceholderColor = new(0.086f, 0.086f, 0.14f, 1f);

        // 入場演出。Home.uss の .home-enter を付けた要素へ、遅延ぶんずらして --visible を足す。
        private const string EnterClass = "home-enter";
        private const string EnterVisibleClass = "home-enter--visible";

        // ルール説明モーダルの中身（Home.uss の .rule-* に対応）。
        private const string RuleSectionClass = "rule-section";
        private const string RuleSectionTitleClass = "rule-section__title";
        private const string RuleSectionBodyClass = "rule-section__body";
        private const string RuleEntryClass = "rule-entry";
        private const string RuleEntryDotClass = "rule-entry__dot";
        private const string RuleEntryTextClass = "rule-entry__text";
        private const string RuleEntryHeadClass = "rule-entry__head";
        private const string RuleEntryNameClass = "rule-entry__name";
        private const string RuleEntryNoteClass = "rule-entry__note";
        private const string RuleEntryDescClass = "rule-entry__desc";

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
        private Button _ruleButton;
        private Button _ruleCloseButton;
        private ScrollView _ruleBody;
        private HomeModal _creditModal;
        private HomeModal _ruleModal;
        private bool _transiting;
        private bool _ruleBodyBuilt;

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
            _ruleButton = _root.Q<Button>("RuleButton");
            _ruleCloseButton = _root.Q<Button>("RuleCloseButton");
            VisualElement creditOverlay = _root.Q<VisualElement>("CreditOverlay");
            VisualElement ruleOverlay = _root.Q<VisualElement>("RuleOverlay");
            _ruleBody = _root.Q<ScrollView>("RuleBody");
            if (_singlePlayerButton == null || _onlineButton == null
                || _creditButton == null || _creditCloseButton == null || creditOverlay == null
                || _ruleButton == null || _ruleCloseButton == null || ruleOverlay == null || _ruleBody == null)
            {
                Debug.LogError("Home のボタン・モーダル（クレジット／ルール説明）が見つかりませんでした。");
                return;
            }

            _creditModal = new HomeModal(creditOverlay);
            _ruleModal = new HomeModal(ruleOverlay);

            _singlePlayerButton.clicked += OnSinglePlayerClicked;
            _onlineButton.clicked += OnOnlineClicked;
            _creditButton.clicked += OnCreditClicked;
            _creditCloseButton.clicked += OnCreditCloseClicked;
            _ruleButton.clicked += OnRuleClicked;
            _ruleCloseButton.clicked += OnRuleCloseClicked;
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
            PlayEntranceWhenBackgroundReadyAsync(destroyCancellationToken).Forget();
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

        // 背景が出そろってから入場演出を始める。ReadyAsync はこれを待たないので、
        // 遷移の暗幕が開くのと同時に UI が浮かび上がる（各要素の遅延は USS 側で付ける）。
        private async UniTaskVoid PlayEntranceWhenBackgroundReadyAsync(CancellationToken ct)
        {
            try
            {
                await _backgroundInitTask;
                // 初期状態（透明・少し下）を 1 フレーム描いてからクラスを足す（同フレームでは補間されない）。
                await UniTask.NextFrame(ct);

                VisualElement root = _uiDocument.rootVisualElement;
                if (root == null)
                {
                    return;
                }
                root.Query<VisualElement>(className: EnterClass)
                    .ForEach(element => element.AddToClassList(EnterVisibleClass));
            }
            catch (OperationCanceledException)
            {
                // シーン破棄時のキャンセル。
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
            _creditModal.Open();
        }

        private void OnCreditCloseClicked()
        {
            _soundPlayer.PlaySE(_soundStore.Cancel1SE);
            _creditModal.Close();
        }

        private void OnRuleClicked()
        {
            if (_transiting) return;
            _soundPlayer.PlaySE(_soundStore.Enter2SE);
            // 中身はカタログから毎回同じものが組み上がるので、初回だけ作って使い回す。
            BuildRuleBodyIfNeeded();
            _ruleModal.Open();
        }

        private void OnRuleCloseClicked()
        {
            _soundPlayer.PlaySE(_soundStore.Cancel1SE);
            _ruleModal.Close();
        }

        // ルール説明の中身（節・文章・箇条書き）を RuleBook から組み立てて ScrollView へ並べる。
        // 文言と数値はゲーム側のカタログ・ルール定数が持つので、ここでは並べ方だけを決める。
        private void BuildRuleBodyIfNeeded()
        {
            if (_ruleBodyBuilt || _ruleBody == null)
            {
                return;
            }
            _ruleBodyBuilt = true;
            // 無効化→再有効化で作り直すことがあるので、積み増さないよう一度空にする。
            _ruleBody.Clear();

            foreach (RuleSection section in RuleBook.Sections())
            {
                VisualElement sectionElement = new();
                sectionElement.AddToClassList(RuleSectionClass);

                Label title = new(section.Title);
                title.AddToClassList(RuleSectionTitleClass);
                sectionElement.Add(title);

                if (!string.IsNullOrEmpty(section.Body))
                {
                    Label body = new(section.Body);
                    body.AddToClassList(RuleSectionBodyClass);
                    sectionElement.Add(body);
                }

                if (section.Entries != null)
                {
                    foreach (RuleEntry entry in section.Entries)
                    {
                        sectionElement.Add(BuildRuleEntry(entry));
                    }
                }

                _ruleBody.Add(sectionElement);
            }
        }

        // 箇条書きの 1 項目。色ドット（マスの種類だけが持つ）＋「名前／補足」の行＋説明。
        private static VisualElement BuildRuleEntry(RuleEntry entry)
        {
            VisualElement row = new();
            row.AddToClassList(RuleEntryClass);

            if (entry.Accent.HasValue)
            {
                VisualElement dot = new();
                dot.AddToClassList(RuleEntryDotClass);
                dot.style.backgroundColor = entry.Accent.Value;
                row.Add(dot);
            }

            VisualElement text = new();
            text.AddToClassList(RuleEntryTextClass);

            VisualElement head = new();
            head.AddToClassList(RuleEntryHeadClass);
            Label nameLabel = new(entry.Name);
            nameLabel.AddToClassList(RuleEntryNameClass);
            head.Add(nameLabel);
            if (!string.IsNullOrEmpty(entry.Note))
            {
                Label note = new(entry.Note);
                note.AddToClassList(RuleEntryNoteClass);
                head.Add(note);
            }
            text.Add(head);

            Label description = new(entry.Description);
            description.AddToClassList(RuleEntryDescClass);
            text.Add(description);

            row.Add(text);
            return row;
        }

        private void OnDisable()
        {
            if (_singlePlayerButton != null) _singlePlayerButton.clicked -= OnSinglePlayerClicked;
            if (_onlineButton != null) _onlineButton.clicked -= OnOnlineClicked;
            if (_creditButton != null) _creditButton.clicked -= OnCreditClicked;
            if (_creditCloseButton != null) _creditCloseButton.clicked -= OnCreditCloseClicked;
            if (_ruleButton != null) _ruleButton.clicked -= OnRuleClicked;
            if (_ruleCloseButton != null) _ruleCloseButton.clicked -= OnRuleCloseClicked;
            _singlePlayerButton = null;
            _onlineButton = null;
            _creditButton = null;
            _creditCloseButton = null;
            _ruleButton = null;
            _ruleCloseButton = null;
            _ruleBody = null;
            _ruleBodyBuilt = false;
            _creditModal = null;
            _ruleModal = null;
            _root = null;
        }

        private void OnDestroy()
        {
            _spriteLoader.Dispose();
        }
    }
}
