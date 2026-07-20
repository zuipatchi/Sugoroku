using System.Collections.Generic;
using System.Threading;
using Common.Character;
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
        // 参加者数を指定しない呼び出し（本番の盤面ミニゲーム）の既定人数＝一人用の [Human, Cpu]。
        private const int DefaultPlayerCount = 2;

        private readonly TransitionPresenter _transition;
        private readonly MiniGameSessionModel _session;
        private bool _running;

        public MiniGameLauncher(TransitionPresenter transition, MiniGameSessionModel session)
        {
            _transition = transition;
            _session = session;
        }

        /// <summary>
        /// <paramref name="game"/> を <paramref name="playerCount"/> 人でプレイして結果を返す。多重起動はガードし、
        /// 実行中の呼び出しは <c>default</c> を返す。人数を省略した場合は本番の既定（<see cref="DefaultPlayerCount"/>）で回す。
        /// <paramref name="characters"/> に参加者（index 0＝プレイヤー）のキャラを渡すと、走者・カードの表示や
        /// 名前ラベルにそのキャラを使う（省略時は各ゲームが従来の解決＝選択キャラ／YOU・CPU にフォールバック）。
        /// </summary>
        public async UniTask<MiniGameResult> PlayAsync(
            MiniGameId game,
            CancellationToken ct,
            int playerCount = DefaultPlayerCount,
            IReadOnlyList<CharacterId> characters = null)
        {
            if (_running)
            {
                return default;
            }
            _running = true;
            try
            {
                _session.Begin(game, playerCount, characters);

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
