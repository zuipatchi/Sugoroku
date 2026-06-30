using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace Common.SceneManagement
{
    internal static class SceneExtensions
    {
        internal static void BuildLifetimeScopes(this Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (LifetimeScope scope in root.GetComponentsInChildren<LifetimeScope>(true))
                {
                    if (scope.Container != null) continue;
                    ResolveParentReference(scope);
                    scope.Build();
                }
            }
        }

        // MPM 対応: FindAnyObjectByType は他プレイヤーのシーンのスコープを誤検出するため全シーンを直接走査する
        private static void ResolveParentReference(LifetimeScope scope)
        {
            if (scope.parentReference.Object != null) return;
            if (scope.parentReference.Type == null) return;

            Type parentType = scope.parentReference.Type;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                foreach (GameObject root in s.GetRootGameObjects())
                {
                    LifetimeScope candidate = root.GetComponentInChildren(parentType, true) as LifetimeScope;
                    if (candidate != null && candidate.Container != null)
                    {
                        scope.parentReference.Object = candidate;
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// <paramref name="scene"/> 内の <see cref="ISceneReady"/> を実装した全コンポーネントの
        /// 準備完了（<see cref="ISceneReady.ReadyAsync"/>）を並行して待つ。実装が無ければ素通りする。
        /// 表示前に完了させたい非同期初期化（Addressables ロード等）をフェードイン前に終わらせるために使う。
        /// </summary>
        internal static async UniTask WaitSceneReadyAsync(this Scene scene, CancellationToken ct)
        {
            List<UniTask> readyTasks = new();
            foreach (GameObject rootGo in scene.GetRootGameObjects())
            {
                foreach (ISceneReady sceneReady in rootGo.GetComponentsInChildren<ISceneReady>(true))
                {
                    readyTasks.Add(WaitReadySafelyAsync(sceneReady, ct));
                }
            }
            if (readyTasks.Count > 0)
            {
                await UniTask.WhenAll(readyTasks);
            }
        }

        // ReadyAsync 内の例外で暗幕が残り続けないよう、キャンセル以外は握りつぶしてフェードインを継続する。
        // キャンセルは正常系（連打・Destroy）なので呼び出し元の catch に委ねるため再送出する。
        private static async UniTask WaitReadySafelyAsync(ISceneReady sceneReady, CancellationToken ct)
        {
            try
            {
                await sceneReady.ReadyAsync(ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
