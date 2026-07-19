using System;
using System.Threading;
using Common.MiniGame;
using Common.SceneManagement;
using Cysharp.Threading.Tasks;
using MiniGame.OverlapGame;
using MiniGame.RaceGame;
using MiniGame.TapGame;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UIElements;
using VContainer;

namespace MiniGame
{
    /// <summary>
    /// ミニゲームシーンのホスト。<see cref="MiniGameSessionModel.CurrentGame"/> に応じた UXML を
    /// Addressables でロードして表示し、対応するゲーム進行クラス（<see cref="TapGamePlay"/> /
    /// <see cref="RaceGamePlay"/>）へ委譲して、結果を <see cref="MiniGameSessionModel.Report"/> で返す。
    /// 新しいゲームはここに分岐を足し、対応する UXML を Addressables に登録する。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MiniGameHostPresenter : MonoBehaviour, ISceneReady
    {
        private MiniGameSessionModel _session;
        private TapGamePlay _tap;
        private RaceGamePlay _race;
        private OverlapGamePlay _overlap;

        private UIDocument _uiDocument;

        private CancellationToken _destroyCt;

        [Inject]
        public void Construct(
            MiniGameSessionModel session,
            TapGamePlay tap,
            RaceGamePlay race,
            OverlapGamePlay overlap)
        {
            _session = session;
            _tap = tap;
            _race = race;
            _overlap = overlap;
        }

        private void Awake()
        {
            _uiDocument = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            // 破棄前に最低 1 回参照しておく（patterns.md #2）。
            _destroyCt = destroyCancellationToken;
        }

        // TapGamePlay / RaceGamePlay は DI（Scoped）が所有・破棄するためここでは触らない。

        // SceneTransitioner / MiniGameLauncher がフェードイン前に待つ。
        // UI 構築（Addressables ロード）が終わってから画面を見せる。
        public async UniTask ReadyAsync(CancellationToken ct)
        {
            VisualElement root = _uiDocument.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("MiniGame の rootVisualElement が見つかりませんでした。");
                return;
            }

            string address = AddressFor(_session.CurrentGame);
            VisualTreeAsset tree = await Addressables.LoadAssetAsync<VisualTreeAsset>(address)
                .ToUniTask(cancellationToken: ct);

            root.Clear();
            tree.CloneTree(root);

            // CurrentGame でゲームごとの進行クラスへ委譲する。
            // フェードイン後にプレイ入力の待機・進行が始まるよう、RunAsync は別タスクで走らせる。
            if (_session.CurrentGame == MiniGameId.Race)
            {
                await _race.BuildAsync(root, ct);
                ReportAsync(_race.RunAsync, _destroyCt).Forget();
                return;
            }

            if (_session.CurrentGame == MiniGameId.Overlap)
            {
                await _overlap.BuildAsync(root, ct);
                ReportAsync(_overlap.RunAsync, _destroyCt).Forget();
                return;
            }

            await _tap.BuildAsync(root, ct);
            ReportAsync(_tap.RunAsync, _destroyCt).Forget();
        }

        private async UniTaskVoid ReportAsync(Func<CancellationToken, UniTask<int>> runAsync, CancellationToken ct)
        {
            try
            {
                int score = await runAsync(ct);
                // 結果を起動側（Main）へ返す。これを受けて MiniGameLauncher がシーンをアンロードする。
                _session.Report(score);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static string AddressFor(MiniGameId game)
        {
            return MiniGameCatalog.Find(game).UxmlAddress;
        }
    }
}
