using System;
using System.Threading;
using Common.MiniGame;
using Common.SoundManagement;
using Common.Store;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace MiniGame
{
    /// <summary>
    /// ミニゲーム単体の動作確認用シーン（<c>MiniGameTest</c>）のホスト。
    /// <see cref="MiniGameCatalog"/> のミニゲームをボタンで一覧し、押すと <see cref="MiniGameLauncher"/> で
    /// 起動して結果スコアを表示する。本番フロー（Title→Home→…）には出さず、エディタでこのシーンを
    /// 直接開いて Play する前提。新しいミニゲームは MiniGameCatalog に 1 行足せば自動でボタンが並ぶ。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MiniGameTestPresenter : MonoBehaviour
    {
        private MiniGameLauncher _launcher;
        private SoundStore _soundStore;
        private SoundPlayer _soundPlayer;

        private UIDocument _uiDocument;
        private VisualElement _list;
        private Label _resultLabel;
        private CancellationToken _destroyCt;
        private bool _busy;

        [Inject]
        public void Construct(
            MiniGameLauncher launcher,
            SoundStore soundStore,
            SoundPlayer soundPlayer)
        {
            _launcher = launcher;
            _soundStore = soundStore;
            _soundPlayer = soundPlayer;
        }

        private void Awake()
        {
            _uiDocument = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            // 破棄前に最低 1 回参照しておく（patterns.md #2）。
            _destroyCt = destroyCancellationToken;

            VisualElement root = _uiDocument.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("MiniGameTest の rootVisualElement が見つかりませんでした。");
                return;
            }

            _list = root.Q<VisualElement>("GameList");
            _resultLabel = root.Q<Label>("ResultLabel");
            if (_list == null || _resultLabel == null)
            {
                Debug.LogError("MiniGameTest の UI 要素が見つかりませんでした。");
                return;
            }

            BuildButtons();
        }

        // カタログの各ミニゲームを 1 ボタンずつ生成する。増えたら自動で並ぶ。
        private void BuildButtons()
        {
            _list.Clear();
            foreach (MiniGameDefinition definition in MiniGameCatalog.All)
            {
                MiniGameId id = definition.Id;
                Button button = new() { text = definition.DisplayName };
                button.AddToClassList("test-game-button");
                button.clicked += () => PlayAsync(id).Forget();
                _list.Add(button);
            }
        }

        private async UniTaskVoid PlayAsync(MiniGameId id)
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            try
            {
                PlaySe(_soundStore?.Enter1SE);

                MiniGameResult result = await _launcher.PlayAsync(id, _destroyCt);

                if (_resultLabel != null)
                {
                    _resultLabel.text = $"{MiniGameCatalog.Find(id).DisplayName}：スコア {result.Score}";
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _busy = false;
            }
        }

        private void PlaySe(AudioClip clip)
        {
            if (_soundPlayer != null && clip != null)
            {
                _soundPlayer.PlaySE(clip);
            }
        }
    }
}
