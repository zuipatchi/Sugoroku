using System.Collections.Generic;
using System.Threading;
using Common.Character;
using Common.GameSession;
using Common.SceneManagement;
using Common.SoundManagement;
using Common.Store;
using Cysharp.Threading.Tasks;
using OnlineCharacterSelect.Sync;
using R3;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace OnlineCharacterSelect.Presenter
{
    /// <summary>
    /// オンラインのキャラ選択ロビー UI。カタログのキャラをカードで一覧し、タップで選択・「決定」で確定する。
    /// 他プレイヤーにロックされたキャラはグレーアウトして選べない（被り防止）。全員が決定して割り当てが
    /// 確定すると Main へ遷移する。状態同期は <see cref="CharacterLobbySync"/> が UGS セッションプロパティで行う。
    /// 表示前に画像ロードを終えるため <see cref="ISceneReady"/> を実装する。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class OnlineCharacterSelectPresenter : MonoBehaviour, ISceneReady
    {
        private SceneTransitioner _sceneTransitioner;
        private CharacterLobbySync _sync;
        private GameSessionModel _gameSession;
        private OnlineRosterSessionModel _roster;
        private SoundStore _soundStore;
        private SoundPlayer _soundPlayer;

        private UIDocument _uiDocument;
        private VisualElement _root;
        private VisualElement _grid;
        private VisualElement _portraitView;
        private Label _statusLabel;
        private Button _confirmButton;
        private Button _leaveButton;

        private readonly Dictionary<CharacterId, Button> _cards = new();
        private readonly Dictionary<CharacterId, Label> _lockBadges = new();
        // 立ち絵は選択時に遅延ロードしてキャッシュする（null 含む）。
        private readonly Dictionary<CharacterId, Sprite> _portraits = new();
        private readonly AddressableSpriteLoader _spriteLoader = new();
        private readonly CompositeDisposable _disposables = new();
        private bool _transiting;

        [Inject]
        public void Construct(
            SceneTransitioner sceneTransitioner,
            CharacterLobbySync sync,
            GameSessionModel gameSession,
            OnlineRosterSessionModel roster,
            SoundStore soundStore,
            SoundPlayer soundPlayer)
        {
            _sceneTransitioner = sceneTransitioner;
            _sync = sync;
            _gameSession = gameSession;
            _roster = roster;
            _soundStore = soundStore;
            _soundPlayer = soundPlayer;
        }

        private void Awake()
        {
            _uiDocument = GetComponent<UIDocument>();
        }

        public async UniTask ReadyAsync(CancellationToken ct)
        {
            _root = _uiDocument.rootVisualElement;
            if (_root == null)
            {
                Debug.LogError("OnlineCharacterSelect の rootVisualElement が見つかりませんでした。");
                return;
            }

            _grid = _root.Q<VisualElement>("CharacterGrid");
            _portraitView = _root.Q<VisualElement>("PortraitView");
            _statusLabel = _root.Q<Label>("StatusLabel");
            _confirmButton = _root.Q<Button>("ConfirmButton");
            _leaveButton = _root.Q<Button>("LeaveButton");
            if (_grid == null || _portraitView == null || _statusLabel == null || _confirmButton == null || _leaveButton == null)
            {
                Debug.LogError("OnlineCharacterSelect の UI 要素が見つかりませんでした。");
                return;
            }

            await BuildCardsAsync(ct);

            _sync.Initialize();
            _confirmButton.clicked += OnConfirmClicked;
            _leaveButton.clicked += OnLeaveClicked;

            // ロック表の変化でカードの選択可否・見た目・状態表示を更新する。
            _sync.Locks
                .Subscribe(_ =>
                {
                    RefreshCards();
                    UpdateStatus();
                })
                .AddTo(_disposables);

            // 割り当て確定で Main へ遷移する。
            _sync.Started
                .Where(started => started)
                .Subscribe(_ => GoToMain())
                .AddTo(_disposables);

            RefreshCards();
            UpdateStatus();

            _sync.RunLoopAsync(destroyCancellationToken).Forget();
        }

        private async UniTask BuildCardsAsync(CancellationToken ct)
        {
            _grid.Clear();
            _cards.Clear();
            _lockBadges.Clear();

            IReadOnlyList<CharacterDefinition> all = CharacterCatalog.All;

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
                card.AddToClassList("ocs-card");

                VisualElement iconView = new();
                iconView.AddToClassList("ocs-card__icon");
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
                name.AddToClassList("ocs-card__name");
                card.Add(name);

                Label lockBadge = new() { text = "取得済み" };
                lockBadge.AddToClassList("ocs-card__lock");
                lockBadge.style.display = DisplayStyle.None;
                card.Add(lockBadge);

                CharacterId id = definition.Id;
                card.clicked += () => OnCardClicked(id);

                _grid.Add(card);
                _cards[id] = card;
                _lockBadges[id] = lockBadge;
            }
        }

        private void OnCardClicked(CharacterId id)
        {
            if (_transiting || _sync.IsLockedByOther(id))
            {
                return;
            }
            _soundPlayer.PlaySE(_soundStore.Enter3SE);
            _sync.Select(id);
            RefreshCards();
            UpdateStatus();
            UpdatePortraitAsync(id, destroyCancellationToken).Forget();
        }

        // 選択中キャラの立ち絵を全画面背景に表示する（オフラインのキャラ選択と同じ）。
        // 立ち絵は未ロードなら遅延ロードする（ロード中に選択が変わったら適用しない）。
        private async UniTask UpdatePortraitAsync(CharacterId id, CancellationToken ct)
        {
            Sprite portrait = await GetPortraitAsync(id, ct);
            // await で待っている間に別のキャラが選ばれていたら、この結果は破棄する。
            if (_portraitView == null || _sync.CurrentSelection != id)
            {
                return;
            }

            if (portrait != null)
            {
                _portraitView.style.backgroundImage = new StyleBackground(portrait);
                _portraitView.style.backgroundColor = new StyleColor(new Color(22f / 255f, 22f / 255f, 35f / 255f));
            }
            else
            {
                // 立ち絵未配置時はプレースホルダ（色面）。
                _portraitView.style.backgroundImage = StyleKeyword.None;
                _portraitView.style.backgroundColor = CharacterPalette.PlaceholderColorFor(id);
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
            if (_transiting || _sync.CurrentSelection == null || _sync.IsLockedByOther(_sync.CurrentSelection.Value))
            {
                return;
            }
            _soundPlayer.PlaySE(_soundStore.Enter1SE);
            _sync.Confirm();
            // 往復を待たず自分の画面だけ即座に緑（確定）へ切り替える。
            RefreshCards();
            UpdateStatus();
        }

        private void OnLeaveClicked()
        {
            if (_transiting)
            {
                return;
            }
            _transiting = true;
            _soundPlayer.PlaySE(_soundStore.Cancel1SE);
            _roster.Clear();
            LeaveAndReturnAsync().Forget();
        }

        private async UniTask LeaveAndReturnAsync()
        {
            await _gameSession.LeaveCurrentSessionAsync();
            await _sceneTransitioner.Transit(Scenes.Title);
        }

        // ロック状態に合わせて各カードの見た目・選択可否を更新する。
        private void RefreshCards()
        {
            CharacterId? selection = _sync.CurrentSelection;

            // 自分の選択が他人にロックされていたら選択解除する。
            if (selection.HasValue && _sync.IsLockedByOther(selection.Value))
            {
                selection = null;
            }

            foreach (KeyValuePair<CharacterId, Button> pair in _cards)
            {
                CharacterId id = pair.Key;
                Button card = pair.Value;
                bool lockedByOther = _sync.IsLockedByOther(id);
                bool isSelected = selection.HasValue && selection.Value == id;
                // 緑（自分のもの）は「決定」を押したときだけ。ホストのロック集計には連動させない
                // （連動させると決定前でも時間経過で緑になってしまうため）。押した瞬間に即時＝楽観表示で、
                // 競合で負けたら次の集計で lockedByOther になり自動で外れる。
                bool isMine = _sync.IsReady && isSelected;

                card.EnableInClassList("ocs-card--locked", lockedByOther);
                card.EnableInClassList("ocs-card--selected", isSelected && !isMine);
                card.EnableInClassList("ocs-card--mine", isMine);
                card.SetEnabled(!lockedByOther);
                _lockBadges[id].style.display = lockedByOther ? DisplayStyle.Flex : DisplayStyle.None;
            }

            bool canConfirm = selection.HasValue && !_sync.IsLockedByOther(selection.Value);
            _confirmButton.SetEnabled(canConfirm && !_sync.IsReady);
        }

        private void UpdateStatus()
        {
            // 「決定」済みで自分の選択が他人に取られていなければ、集計往復を待たず確定表示にする。
            bool ownsOptimistic = _sync.IsReady
                                  && _sync.CurrentSelection.HasValue
                                  && !_sync.IsLockedByOther(_sync.CurrentSelection.Value);
            if (ownsOptimistic)
            {
                _statusLabel.text = "決定しました。他のプレイヤーを待っています...";
            }
            else if (_sync.CurrentSelection.HasValue && _sync.IsLockedByOther(_sync.CurrentSelection.Value))
            {
                _statusLabel.text = "他のプレイヤーに取られました。別のキャラクターを選んでください";
            }
            else if (_sync.CurrentSelection.HasValue)
            {
                _statusLabel.text = "「決定」を押して確定します";
            }
            else
            {
                _statusLabel.text = "使うキャラクターを選んでください";
            }
        }

        private void GoToMain()
        {
            if (_transiting)
            {
                return;
            }
            _transiting = true;
            _soundPlayer.PlaySE(_soundStore.DecisionSE);
            _sceneTransitioner.Transit(Scenes.Main).Forget();
        }

        private void OnDisable()
        {
            if (_confirmButton != null)
            {
                _confirmButton.clicked -= OnConfirmClicked;
            }
            if (_leaveButton != null)
            {
                _leaveButton.clicked -= OnLeaveClicked;
            }
        }

        private void OnDestroy()
        {
            _disposables.Dispose();
            _spriteLoader.Dispose();
        }
    }
}
