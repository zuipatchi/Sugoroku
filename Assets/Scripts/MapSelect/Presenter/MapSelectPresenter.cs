using System.Collections.Generic;
using System.Threading;
using Common.Board;
using Common.GameSession;
using Common.SceneManagement;
using Common.SoundManagement;
using Common.Store;
using Cysharp.Threading.Tasks;
using Main.Board;
using MapSelect.View;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace MapSelect.Presenter
{
    /// <summary>
    /// マップ選択シーンの UI。<see cref="BoardCatalog"/> のマップを盤面サムネイル付きのカードで一覧表示し、
    /// 選ぶと大プレビューに拡大表示する。「決定」で <see cref="BoardSessionModel"/> に保存して Main へ、
    /// 「戻る」で CharacterSelect へ遷移する。キャラ選択（CharacterSelect）の盤面版。
    /// サムネイルは画像を使わず <see cref="BoardSchematicView"/> が Painter2D で描くため、非同期ロードは無い。
    /// UI 構築は DI 注入（Construct）後に走る <see cref="ISceneReady.ReadyAsync"/> で行う（OnEnable は注入前）。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MapSelectPresenter : MonoBehaviour, ISceneReady
    {
        // 選択できるマップ一覧。MapSelect シーンでこのカタログ資産を割り当てる（Main の BoardPresenter と同じ資産）。
        [SerializeField] private BoardCatalog _catalog;

        private SceneTransitioner _sceneTransitioner;
        private BoardSessionModel _boardSession;
        private PlayerCountSessionModel _playerCountSession;
        private SoundStore _soundStore;
        private SoundPlayer _soundPlayer;

        private UIDocument _uiDocument;
        private VisualElement _root;
        private VisualElement _grid;
        private VisualElement _preview;
        private Label _title;
        private Label _cellCount;
        private VisualElement _stats;
        private Button _confirmButton;
        private Button _backButton;
        private Button _playerCountMinus;
        private Button _playerCountPlus;
        private Label _playerCountValue;

        private readonly Dictionary<string, VisualElement> _cards = new();
        private string _selectedId = string.Empty;
        private BoardDefinition _previewBoard;
        private int _playerCount;
        private bool _transiting;

        [Inject]
        public void Construct(
            SceneTransitioner sceneTransitioner,
            BoardSessionModel boardSession,
            PlayerCountSessionModel playerCountSession,
            SoundStore soundStore,
            SoundPlayer soundPlayer)
        {
            _sceneTransitioner = sceneTransitioner;
            _boardSession = boardSession;
            _playerCountSession = playerCountSession;
            _soundStore = soundStore;
            _soundPlayer = soundPlayer;
        }

        private void Awake()
        {
            _uiDocument = GetComponent<UIDocument>();
        }

        // フェードイン前に、注入済みの状態を使ってカード・プレビューを組む。
        public UniTask ReadyAsync(CancellationToken ct)
        {
            _root = _uiDocument.rootVisualElement;
            if (_root == null)
            {
                Debug.LogError("MapSelect の rootVisualElement が見つかりませんでした。");
                return UniTask.CompletedTask;
            }

            _grid = _root.Q<VisualElement>("MapGrid");
            _preview = _root.Q<VisualElement>("MapPreview");
            _title = _root.Q<Label>("MapTitle");
            _cellCount = _root.Q<Label>("MapCellCount");
            _stats = _root.Q<VisualElement>("MapStats");
            _confirmButton = _root.Q<Button>("ConfirmButton");
            _backButton = _root.Q<Button>("BackButton");
            _playerCountMinus = _root.Q<Button>("PlayerCountMinus");
            _playerCountPlus = _root.Q<Button>("PlayerCountPlus");
            _playerCountValue = _root.Q<Label>("PlayerCountValue");
            if (_grid == null || _preview == null || _title == null || _cellCount == null || _stats == null
                || _confirmButton == null || _backButton == null
                || _playerCountMinus == null || _playerCountPlus == null || _playerCountValue == null)
            {
                Debug.LogError("MapSelect の UI 要素が見つかりませんでした。");
                return UniTask.CompletedTask;
            }

            // 大プレビューは選択マップ（_previewBoard）を毎回読み直して描く。
            _preview.generateVisualContent += ctx => BoardSchematicView.Draw(ctx, _previewBoard);
            _preview.RegisterCallback<GeometryChangedEvent>(_ => _preview.MarkDirtyRepaint());

            BuildCards();
            UpdateSelection();

            // プレイヤー人数：前回の選択（既定 2）から始め、−／＋ で範囲内を増減する。
            _playerCount = _playerCountSession != null ? _playerCountSession.Count : PlayerCountSessionModel.Min;
            UpdatePlayerCount();

            _playerCountMinus.clicked += OnPlayerCountMinusClicked;
            _playerCountPlus.clicked += OnPlayerCountPlusClicked;
            _confirmButton.clicked += OnConfirmClicked;
            _backButton.clicked += OnBackClicked;
            return UniTask.CompletedTask;
        }

        private void OnPlayerCountMinusClicked()
        {
            ChangePlayerCount(-1);
        }

        private void OnPlayerCountPlusClicked()
        {
            ChangePlayerCount(1);
        }

        // 人数を増減して表示・ボタンの有効状態を更新する（SE つき）。範囲端では何もしない。
        private void ChangePlayerCount(int delta)
        {
            if (_transiting)
            {
                return;
            }
            int next = Mathf.Clamp(_playerCount + delta, PlayerCountSessionModel.Min, PlayerCountSessionModel.Max);
            if (next == _playerCount)
            {
                return;
            }
            _playerCount = next;
            _soundPlayer.PlaySE(_soundStore.Enter3SE);
            UpdatePlayerCount();
        }

        // 現在の人数を数値ラベルに反映し、下限/上限で −／＋ を無効化する。
        private void UpdatePlayerCount()
        {
            _playerCountValue.text = _playerCount.ToString();
            _playerCountMinus.SetEnabled(_playerCount > PlayerCountSessionModel.Min);
            _playerCountPlus.SetEnabled(_playerCount < PlayerCountSessionModel.Max);
        }

        private void BuildCards()
        {
            _grid.Clear();
            _cards.Clear();

            if (_catalog == null || _catalog.IsEmpty)
            {
                Debug.LogError("BoardCatalog が未割り当て、またはマップが 1 枚も登録されていません。");
                _confirmButton.SetEnabled(false);
                return;
            }

            // 既定の選択：前回の選択が残っていればそれ、無ければカタログ先頭。
            BoardDefinition initial = _boardSession != null && _boardSession.HasSelection
                ? _catalog.Find(_boardSession.SelectedId)
                : _catalog.Default;
            _selectedId = initial != null ? initial.name : string.Empty;
            _previewBoard = initial;

            IReadOnlyList<BoardDefinition> all = _catalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                BoardDefinition board = all[i];
                if (board == null)
                {
                    continue;
                }

                Button card = new();
                card.AddToClassList("map-card");

                VisualElement thumb = new() { pickingMode = PickingMode.Ignore };
                thumb.AddToClassList("map-thumb");
                thumb.generateVisualContent += ctx => BoardSchematicView.Draw(ctx, board);
                thumb.RegisterCallback<GeometryChangedEvent>(_ => thumb.MarkDirtyRepaint());
                card.Add(thumb);

                Label name = new() { text = DisplayNameOf(board) };
                name.AddToClassList("map-name");
                card.Add(name);

                string id = board.name;
                card.clicked += () => OnCardClicked(id, board);

                _grid.Add(card);
                _cards[id] = card;
            }
        }

        private static string DisplayNameOf(BoardDefinition board)
        {
            return string.IsNullOrEmpty(board.DisplayName) ? board.name : board.DisplayName;
        }

        private void OnCardClicked(string id, BoardDefinition board)
        {
            if (_transiting)
            {
                return;
            }
            _selectedId = id;
            _previewBoard = board;
            _soundPlayer.PlaySE(_soundStore.Enter3SE);
            UpdateSelection();
        }

        // 選択中のカードを強調し、大プレビュー・タイトルを更新する。
        private void UpdateSelection()
        {
            foreach (KeyValuePair<string, VisualElement> pair in _cards)
            {
                pair.Value.EnableInClassList("map-card--selected", pair.Key == _selectedId);
            }

            _title.text = _previewBoard != null ? DisplayNameOf(_previewBoard) : string.Empty;
            _preview.MarkDirtyRepaint();
            UpdateStats();
        }

        // 選択中マップのイベント構成（総マス数＋イベント別の色チップ）を組み直す。
        // どんなマップか（陣地・アイテム・お金…がどれだけあるか）をひと目で分かるようにする。
        private void UpdateStats()
        {
            _stats.Clear();
            if (_previewBoard == null)
            {
                _cellCount.text = string.Empty;
                return;
            }

            _cellCount.text = $"全 {_previewBoard.CellCount} マス";
            foreach ((BoardCellEvent cellEvent, int count) in BoardEventTally.Summarize(_previewBoard))
            {
                _stats.Add(BuildStatChip(cellEvent, count));
            }
        }

        // 1 イベント分のチップ（色ドット＋「ラベル ×N」）。ドット色・ラベルはサムネイルの塗り分けと共通。
        private static VisualElement BuildStatChip(BoardCellEvent cellEvent, int count)
        {
            VisualElement chip = new() { pickingMode = PickingMode.Ignore };
            chip.AddToClassList("ms-stat");

            VisualElement dot = new() { pickingMode = PickingMode.Ignore };
            dot.AddToClassList("ms-stat__dot");
            dot.style.backgroundColor = BoardEventColors.Of(cellEvent);
            chip.Add(dot);

            Label label = new($"{BoardEventLabel.Of(cellEvent)} ×{count}") { pickingMode = PickingMode.Ignore };
            label.AddToClassList("ms-stat__label");
            chip.Add(label);

            return chip;
        }

        private void OnConfirmClicked()
        {
            if (_transiting || string.IsNullOrEmpty(_selectedId))
            {
                return;
            }
            _transiting = true;
            _boardSession.Select(_selectedId);
            _playerCountSession?.Select(_playerCount);
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
            _sceneTransitioner.Transit(Scenes.CharacterSelect).Forget();
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
            if (_playerCountMinus != null)
            {
                _playerCountMinus.clicked -= OnPlayerCountMinusClicked;
            }
            if (_playerCountPlus != null)
            {
                _playerCountPlus.clicked -= OnPlayerCountPlusClicked;
            }
        }
    }
}
