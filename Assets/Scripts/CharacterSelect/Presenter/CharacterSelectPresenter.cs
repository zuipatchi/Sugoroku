using System.Collections.Generic;
using System.Threading;
using Common.Character;
using Common.SceneManagement;
using Common.SoundManagement;
using Common.Store;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace CharacterSelect.Presenter
{
    /// <summary>
    /// キャラクター選択シーンの UI。カタログのキャラを「アイコン」のカードで一覧表示し、
    /// 選ぶと「立ち絵」を大きいプレビューに表示する。「けってい」で
    /// <see cref="CharacterSessionModel"/> に保存して Main へ遷移する。
    /// 画像は Addressables から読み、未配置のものはプレースホルダ（色面）で表示する。
    /// 表示前に画像のロードを終えるため <see cref="ISceneReady"/> を実装する。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class CharacterSelectPresenter : MonoBehaviour, ISceneReady
    {
        private SceneTransitioner _sceneTransitioner;
        private CharacterSessionModel _characterSession;
        private SoundStore _soundStore;
        private SoundPlayer _soundPlayer;

        private UIDocument _uiDocument;
        private VisualElement _root;
        private VisualElement _grid;
        private VisualElement _portraitView;
        private Button _confirmButton;
        private Button _backButton;

        private readonly Dictionary<CharacterId, VisualElement> _cards = new();
        private readonly Dictionary<CharacterId, Sprite> _portraits = new();
        private readonly AddressableSpriteLoader _spriteLoader = new();
        private CharacterId _selected;
        private bool _transiting;

        [Inject]
        public void Construct(
            SceneTransitioner sceneTransitioner,
            CharacterSessionModel characterSession,
            SoundStore soundStore,
            SoundPlayer soundPlayer)
        {
            _sceneTransitioner = sceneTransitioner;
            _characterSession = characterSession;
            _soundStore = soundStore;
            _soundPlayer = soundPlayer;
        }

        private void Awake()
        {
            _uiDocument = GetComponent<UIDocument>();
        }

        // フェードイン前に画像を読み終えてからカード・プレビューを組む。
        public async UniTask ReadyAsync(CancellationToken ct)
        {
            _root = _uiDocument.rootVisualElement;
            if (_root == null)
            {
                Debug.LogError("CharacterSelect の rootVisualElement が見つかりませんでした。");
                return;
            }

            _grid = _root.Q<VisualElement>("CharacterGrid");
            _portraitView = _root.Q<VisualElement>("PortraitView");
            _confirmButton = _root.Q<Button>("ConfirmButton");
            _backButton = _root.Q<Button>("BackButton");
            if (_grid == null || _portraitView == null || _confirmButton == null || _backButton == null)
            {
                Debug.LogError("CharacterSelect の UI 要素が見つかりませんでした。");
                return;
            }

            _selected = _characterSession.Selected;
            await BuildCardsAsync(ct);
            // フェードイン前に待つのは初期選択キャラの立ち絵 1 枚だけ。残りは選択時に遅延ロードする。
            await UpdateSelectionAsync(ct);

            _confirmButton.clicked += OnConfirmClicked;
            _backButton.clicked += OnBackClicked;
        }

        private async UniTask BuildCardsAsync(CancellationToken ct)
        {
            _grid.Clear();
            _cards.Clear();
            _portraits.Clear();

            IReadOnlyList<CharacterDefinition> all = CharacterCatalog.All;

            // クリック用のカード絵は全キャラぶんを並列ロードする（1 枚ずつ await で待たない）。
            // 立ち絵（Portrait）は選択時に表示するだけなので、ここでは読まず遅延ロードにする。
            List<UniTask<Sprite>> iconTasks = new(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                iconTasks.Add(_spriteLoader.TryLoadAsync(all[i].CardAddress, "キャラ画像", ct));
            }
            Sprite[] icons = await UniTask.WhenAll(iconTasks);

            for (int i = 0; i < all.Count; i++)
            {
                CharacterDefinition definition = all[i];
                Sprite icon = icons[i];

                Button card = new();
                card.AddToClassList("character-card");

                VisualElement iconView = new();
                iconView.AddToClassList("character-icon");
                if (icon != null)
                {
                    iconView.style.backgroundImage = new StyleBackground(icon);
                }
                else
                {
                    iconView.style.backgroundColor = CharacterPalette.PlaceholderColor(i, all.Count);
                }
                card.Add(iconView);

                Label name = new() { text = definition.DisplayName };
                name.AddToClassList("character-name");
                card.Add(name);

                CharacterId id = definition.Id;
                card.clicked += () => OnCardClicked(id);

                _grid.Add(card);
                _cards[id] = card;
            }
        }

        private void OnCardClicked(CharacterId id)
        {
            if (_transiting)
            {
                return;
            }
            _selected = id;
            _soundPlayer.PlaySE(_soundStore.Enter3SE);
            UpdateSelectionAsync(destroyCancellationToken).Forget();
        }

        // 選択中のカードを強調し、立ち絵プレビューを差し替える。
        // 立ち絵は未ロードなら遅延ロードする（ロード中に選択が変わったら適用しない）。
        private async UniTask UpdateSelectionAsync(CancellationToken ct)
        {
            CharacterId selected = _selected;
            foreach (KeyValuePair<CharacterId, VisualElement> pair in _cards)
            {
                pair.Value.EnableInClassList("character-card--selected", pair.Key == selected);
            }

            Sprite portrait = await GetPortraitAsync(selected, ct);
            // await で待っている間に別のキャラが選ばれていたら、この結果は破棄する。
            if (_portraitView == null || _selected != selected)
            {
                return;
            }

            if (portrait != null)
            {
                _portraitView.style.backgroundImage = new StyleBackground(portrait);
                // 透過部分は暗いベース色を見せる（プレースホルダ色を残さない）。
                _portraitView.style.backgroundColor = new StyleColor(new Color(22f / 255f, 22f / 255f, 35f / 255f));
            }
            else
            {
                // 立ち絵未配置時はプレースホルダ（色面）。
                _portraitView.style.backgroundImage = StyleKeyword.None;
                _portraitView.style.backgroundColor = CharacterPalette.PlaceholderColorFor(selected);
            }
        }

        // 立ち絵を取得する。一度読んだものはキャッシュ（null 含む）から返す。
        private async UniTask<Sprite> GetPortraitAsync(CharacterId id, CancellationToken ct)
        {
            if (_portraits.TryGetValue(id, out Sprite cached))
            {
                return cached;
            }

            CharacterDefinition definition = CharacterCatalog.Find(id);
            Sprite portrait = await _spriteLoader.TryLoadAsync(definition.PortraitAddress, "キャラ画像", ct);
            _portraits[id] = portrait;
            return portrait;
        }

        private void OnConfirmClicked()
        {
            if (_transiting)
            {
                return;
            }
            _transiting = true;
            _characterSession.Select(_selected);
            _soundPlayer.PlaySE(_soundStore.Enter1SE);
            // キャラ決定後はマップ選択へ進む（マップ決定で Main へ遷移する）。
            _sceneTransitioner.Transit(Scenes.MapSelect).Forget();
        }

        private void OnBackClicked()
        {
            if (_transiting)
            {
                return;
            }
            _transiting = true;
            _soundPlayer.PlaySE(_soundStore.Cancel1SE);
            _sceneTransitioner.Transit(Scenes.Home).Forget();
        }

        private void OnDisable()
        {
            if (_confirmButton != null)
            {
                _confirmButton.clicked -= OnConfirmClicked;
            }
            if (_backButton != null)
            {
                _backButton.clicked -= OnBackClicked;
            }
        }

        private void OnDestroy()
        {
            _spriteLoader.Dispose();
        }
    }
}
