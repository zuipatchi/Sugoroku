using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Common.Store
{
    /// <summary>
    /// Addressables のスプライトを読み込み、ハンドルを保持して <see cref="Dispose"/> でまとめて解放する共通ローダー。
    /// ロード失敗（未配置等）は警告ログを出して null を返し、呼び出し元の色面フォールバックに任せる。
    /// キャンセル（シーン破棄）はハンドルを解放して <see cref="OperationCanceledException"/> を伝播する。
    /// </summary>
    public sealed class AddressableSpriteLoader : IDisposable
    {
        private readonly List<AsyncOperationHandle<Sprite>> _handles = new();

        /// <summary>
        /// スプライト 1 枚を読み込む。成功したらハンドルを保持して Sprite を、失敗したら null を返す。
        /// <paramref name="assetLabel"/> は失敗時の警告ログに使う表示名（「カード画像」「走行スプライト」等）。
        /// </summary>
        public async UniTask<Sprite> TryLoadAsync(string address, string assetLabel, CancellationToken ct)
        {
            AsyncOperationHandle<Sprite> handle = default;
            try
            {
                handle = Addressables.LoadAssetAsync<Sprite>(address);
                Sprite sprite = await handle.ToUniTask(cancellationToken: ct);
                _handles.Add(handle);
                return sprite;
            }
            catch (OperationCanceledException)
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
                throw;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{assetLabel} '{address}' のロードに失敗。プレースホルダ表示にします: {e.Message}");
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
                return null;
            }
        }

        /// <summary>保持している全ハンドルを解放する。</summary>
        public void Dispose()
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
