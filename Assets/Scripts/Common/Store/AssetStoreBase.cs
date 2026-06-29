using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer.Unity;

namespace Common.Store
{
    public abstract class AssetStoreBase : IStartable
    {
        private readonly UniTaskCompletionSource _loadedTcs = new();
        public UniTask Loaded => _loadedTcs.Task;

        protected abstract string AssetCategory { get; }

        public void Start()
        {
            LoadAssetsAsync().Forget();
        }

        private async UniTask LoadAssetsAsync()
        {
            try
            {
                await LoadAssetsCore();
                _loadedTcs.TrySetResult();
            }
            catch (Exception e)
            {
                Debug.LogError($"{AssetCategory}アセットのロードに失敗: {e}");
                _loadedTcs.TrySetException(e);
            }
        }

        protected abstract UniTask LoadAssetsCore();
    }
}
