using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Common.SceneManagement
{
    // ビルド起動時: Common がエントリポイントの場合に Title をロードする
    // Editor では Title から起動するため sceneCount > 1 になり何もしない
    public class BootLoader : MonoBehaviour
    {
        private async void Start()
        {
            if (SceneManager.sceneCount > 1)
            {
                return;
            }

            await SceneManager.LoadSceneAsync((int)Scenes.Title, LoadSceneMode.Additive)
                .WithCancellation(this.GetCancellationTokenOnDestroy());

            Scene titleScene = SceneManager.GetSceneByBuildIndex((int)Scenes.Title);
            titleScene.BuildLifetimeScopes();

            SceneManager.SetActiveScene(titleScene);
        }
    }
}
