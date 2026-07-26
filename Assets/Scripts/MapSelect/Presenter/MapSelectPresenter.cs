using System.Threading;
using Common.Board;
using Common.GameSession;
using Common.SceneManagement;
using Common.SoundManagement;
using Common.Store;
using Cysharp.Threading.Tasks;
using Main.Board;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace MapSelect.Presenter
{
    /// <summary>
    /// マップ選択シーンの UI。<see cref="BoardCatalog"/> のマップを盤面サムネイル付きのカードで一覧表示し、
    /// 選ぶと大プレビューに拡大表示する。「決定」で <see cref="BoardSessionModel"/> に保存して Main へ、
    /// 「戻る」で CharacterSelect へ遷移する。キャラ選択（CharacterSelect）の盤面版。
    /// カードの並べ替え・大プレビュー・イベント内訳・選択状態は共通の <see cref="MapPickerView"/> に委譲する
    /// （オンラインのルーム作成マップ選択と共用）。サムネイルは画像を使わず Painter2D 描画で非同期ロードは無い。
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

        private MapPickerView _picker;
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

            // カード一覧・大プレビュー・イベント内訳・選択状態は共通コントローラに委譲する。
            _picker = new MapPickerView(_grid, _preview, _title, _cellCount, _stats);
            _picker.Selected += () => _soundPlayer.PlaySE(_soundStore.Enter3SE);
            _picker.Build(_catalog, _boardSession != null && _boardSession.HasSelection ? _boardSession.SelectedId : null);
            if (!_picker.HasSelection)
            {
                Debug.LogError("BoardCatalog が未割り当て、またはマップが 1 枚も登録されていません。");
                _confirmButton.SetEnabled(false);
            }

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

        private void OnConfirmClicked()
        {
            if (_transiting || _picker == null || !_picker.HasSelection)
            {
                return;
            }
            _transiting = true;
            _boardSession.Select(_picker.SelectedId);
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
