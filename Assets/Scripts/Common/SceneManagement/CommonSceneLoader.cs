using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Common.SceneManagement
{
    // 共通シーンをロードするクラス
    public class CommonSceneLoader : MonoBehaviour
    {
        private static bool _loaded = false;

        private async void Awake()
        {
            // 2重起動させない
            if (_loaded) return;
            _loaded = true;

            Scene commonScene = SceneManager.GetSceneByBuildIndex(0);

            // 共通シーンが存在しなければAdditiveでロード
            if (!commonScene.IsValid())
            {
                CancellationToken token = this.GetCancellationTokenOnDestroy();
                await SceneManager.LoadSceneAsync(0, LoadSceneMode.Additive).WithCancellation(token);
                // Common シーンの LifetimeScope.Awake() が完了するまで1フレーム待つ
                await UniTask.NextFrame(token);
            }

            SceneManager.GetActiveScene().BuildLifetimeScopes();
        }
    }
}
