using System;
using System.Collections.Generic;
using System.Threading;
using Common.Character;
using Common.SceneManagement;
using Common.SoundManagement;
using Common.Store;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
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
        private readonly List<AsyncOperationHandle<Sprite>> _handles = new();
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
            UpdateSelection();

            _confirmButton.clicked += OnConfirmClicked;
            _backButton.clicked += OnBackClicked;
        }

        private async UniTask BuildCardsAsync(CancellationToken ct)
        {
            _grid.Clear();
            _cards.Clear();
            _portraits.Clear();

            IReadOnlyList<CharacterDefinition> all = CharacterCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                CharacterDefinition definition = all[i];

                // クリック用のカード絵と、選択時に表示する立ち絵を両方ロードする。
                Sprite icon = await TryLoadAsync(definition.CardAddress, ct);
                _portraits[definition.Id] = await TryLoadAsync(definition.PortraitAddress, ct);

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
                    iconView.style.backgroundColor = PlaceholderColor(i, all.Count);
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

        private async UniTask<Sprite> TryLoadAsync(string address, CancellationToken ct)
        {
            AsyncOperationHandle<Sprite> handle = default;
            try
            {
                handle = Addressables.LoadAssetAsync<Sprite>(address);
                Sprite sprite = await handle.ToUniTask(cancellationToken: ct);
                _handles.Add(handle);
                return sprite;
            }
            catch (OperationCanceledException)
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
                throw;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"キャラ画像 '{address}' のロードに失敗。プレースホルダ表示にします: {e.Message}");
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
                return null;
            }
        }

        private static Color PlaceholderColor(int index, int count)
        {
            float hue = (count <= 0) ? 0f : (float)index / count;
            return Color.HSVToRGB(hue, 0.45f, 0.65f);
        }

        private void OnCardClicked(CharacterId id)
        {
            if (_transiting)
            {
                return;
            }
            _selected = id;
            _soundPlayer.PlaySE(_soundStore.Enter3SE);
            UpdateSelection();
        }

        // 選択中のカードを強調し、立ち絵プレビューを差し替える。
        private void UpdateSelection()
        {
            foreach (KeyValuePair<CharacterId, VisualElement> pair in _cards)
            {
                pair.Value.EnableInClassList("character-card--selected", pair.Key == _selected);
            }

            if (_portraits.TryGetValue(_selected, out Sprite portrait) && portrait != null)
            {
                _portraitView.style.backgroundImage = new StyleBackground(portrait);
                // 透過部分は暗いベース色を見せる（プレースホルダ色を残さない）。
                _portraitView.style.backgroundColor = new StyleColor(new Color(22f / 255f, 22f / 255f, 35f / 255f));
            }
            else
            {
                // 立ち絵未配置時はプレースホルダ（色面）。
                _portraitView.style.backgroundImage = StyleKeyword.None;
                _portraitView.style.backgroundColor = PlaceholderColor(CharacterCatalog.IndexOf(_selected), CharacterCatalog.All.Count);
            }
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
            _sceneTransitioner.Transit(Scenes.Main).Forget();
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
            foreach (AsyncOperationHandle<Sprite> handle in _handles)
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
            }
            _handles.Clear();
        }
    }
}
