using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace MiniGame.RaceGame
{
    /// <summary>
    /// 走者スプライトの Addressables ロードとハンドル解放。ロードしたハンドルを内部に保持し、
    /// <see cref="ReleaseAll"/> でまとめて解放する。<see cref="RaceGamePlay"/> が生成して使う。
    /// </summary>
    public sealed class RaceSpriteLoader
    {
        private readonly List<AsyncOperationHandle<Sprite>> _handles = new();

        /// <summary>
        /// 指定アドレスのスプライトをロードして返す。ハンドルは成否に関わらず保持され、
        /// <see cref="ReleaseAll"/> で解放される。ロード失敗時は例外を投げる。
        /// </summary>
        public async UniTask<Sprite> LoadAsync(string address, CancellationToken ct)
        {
            AsyncOperationHandle<Sprite> handle = Addressables.LoadAssetAsync<Sprite>(address);
            _handles.Add(handle);
            return await handle.ToUniTask(cancellationToken: ct);
        }

        /// <summary>保持している全ハンドルを解放する。</summary>
        public void ReleaseAll()
        {
            foreach (AsyncOperationHandle<Sprite> handle in _handles)
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
            }
            _handles.Clear();
        }
    }
}
