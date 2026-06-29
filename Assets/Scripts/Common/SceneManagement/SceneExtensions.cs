using System;
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
    }
}
