using System;
using System.Collections.Generic;
using System.Threading;
using Common.Transition;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;

namespace Common.SceneManagement
{
    public enum Scenes
    {
        Common = 0,
        Title = 1,
        Matching = 2,
        Main = 3,
        Home = 4,
        MiniGame = 5
    }
    /// <summary>
    /// アクティブシーンを変更するクラス
    /// </summary>
    public sealed class SceneTransitioner : MonoBehaviour
    {
        private readonly SemaphoreSlim _gate = new(1, 1);

        // 「今走ってる遷移」を止めるためのCTS
        private CancellationTokenSource _runningCts;
        private TransitionPresenter _transitionPresenter;

        [Inject]
        public void Construct(TransitionPresenter transitionPresenter)
        {
            _transitionPresenter = transitionPresenter;
        }

        // アクティブシーンを next に変更する
        // 複数同時に実行された場合は一番最後の処理のみ実行される
        public async UniTask Transit(Scenes next)
        {
            // コモンシーンはアクティブにしない
            if (next == Scenes.Common) return;

            // 実行中の Transit はキャンセル
            _runningCts?.Cancel();
            _runningCts?.Dispose();
            _runningCts = new CancellationTokenSource();

            await _gate.WaitAsync();
            try
            {
                // Destroyされるまたは、キャンセルされたら止まるトークンを作成する
                using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                    _runningCts.Token,
                    this.GetCancellationTokenOnDestroy()
                );
                CancellationToken ct = linked.Token;

                Scene activeScene = SceneManager.GetActiveScene();

                // 同じシーンに遷移できなくする
                if (activeScene.buildIndex == (int)next) return;

                await _transitionPresenter.CoverAsync();

                // nextScene が未ロードならロード
                Scene nextScene = SceneManager.GetSceneByBuildIndex((int)next);
                if (!nextScene.IsValid() || !nextScene.isLoaded)
                {
                    await SceneManager.LoadSceneAsync((int)next, LoadSceneMode.Additive)
                        .WithCancellation(ct);

                    nextScene = SceneManager.GetSceneByBuildIndex((int)next);
                }

                nextScene.BuildLifetimeScopes();

                // nextScene をメイン(Active)にする
                SceneManager.SetActiveScene(nextScene);

                // Common とターゲット以外を全てアンロード（MPM では複数シーンが混在するため）
                List<Scene> toUnload = new();
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene s = SceneManager.GetSceneAt(i);
                    if (s.buildIndex != (int)Scenes.Common && s.buildIndex != nextScene.buildIndex && s.isLoaded)
                    {
                        toUnload.Add(s);
                    }
                }
                foreach (Scene s in toUnload)
                {
                    await SceneManager.UnloadSceneAsync(s).WithCancellation(ct);
                }

                // 新シーンが非同期初期化を持つ場合は完了を待ってからフェードインする。
                // ISceneReady を実装したコンポーネントを全て待機（無ければ素通り）。
                await nextScene.WaitSceneReadyAsync(ct);

                await _transitionPresenter.RevealAsync();

            }
            catch (OperationCanceledException)
            {
                // 連打やDestroyでキャンセルされたのは正常系なので何もしない
            }
            finally
            {
                _gate.Release();
            }
        }

        private void OnDestroy()
        {
            _runningCts?.Cancel();
            _runningCts?.Dispose();
        }
    }
}
