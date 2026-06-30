using System.Threading;
using Common.SceneManagement;
using Common.Transition;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace Common.MiniGame
{
    /// <summary>
    /// Main を残したままミニゲームシーンを Additive で重ね、終了後にそのシーンだけアンロードする。
    /// <see cref="SceneTransitioner.Transit"/> は Common 以外を全アンロードして Main（盤面・NGO 接続）を
    /// 破棄してしまうため使わず、専用のロード経路をとる。
    /// </summary>
    public sealed class MiniGameLauncher
    {
        private readonly TransitionPresenter _transition;
        private readonly MiniGameSessionModel _session;
        private bool _running;

        public MiniGameLauncher(TransitionPresenter transition, MiniGameSessionModel session)
        {
            _transition = transition;
            _session = session;
        }

        /// <summary>
        /// <paramref name="game"/> をプレイして結果を返す。多重起動はガードし、実行中の呼び出しは
        /// <c>default</c> を返す。
        /// </summary>
        public async UniTask<MiniGameResult> PlayAsync(MiniGameId game, CancellationToken ct)
        {
            if (_running)
            {
                return default;
            }
            _running = true;
            try
            {
                _session.Begin(game);

                await _transition.CoverAsync();

                Scene scene = SceneManager.GetSceneByBuildIndex((int)Scenes.MiniGame);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    await SceneManager.LoadSceneAsync((int)Scenes.MiniGame, LoadSceneMode.Additive)
                        .WithCancellation(ct);
                    scene = SceneManager.GetSceneByBuildIndex((int)Scenes.MiniGame);
                }

                // Main はアクティブのまま。ミニゲームシーンの LifetimeScope をビルドして準備完了を待つ。
                scene.BuildLifetimeScopes();
                await scene.WaitSceneReadyAsync(ct);

                await _transition.RevealAsync();

                MiniGameResult result = await _session.WaitResultAsync(ct);

                await _transition.CoverAsync();
                if (scene.IsValid() && scene.isLoaded)
                {
                    await SceneManager.UnloadSceneAsync(scene).WithCancellation(ct);
                }
                await _transition.RevealAsync();

                return result;
            }
            finally
            {
                _running = false;
            }
        }
    }
}
