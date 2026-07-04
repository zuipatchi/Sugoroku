using System;
using System.Collections.Generic;
using System.Threading;
using Common.Character;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UIElements;

namespace Main.Roulette
{
    /// <summary>
    /// ルーレット各セクターのキャラアイコン画像の Addressables ロードとハンドル解放を担う。
    /// ロードに成功したアイコン要素へ背景画像を貼り、未配置・失敗はプレースホルダ色のまま残す。
    /// </summary>
    public sealed class RouletteIconLoader
    {
        private readonly List<AsyncOperationHandle<Sprite>> _iconHandles = new();

        /// <summary>各セクターのアイコンにキャラ画像を貼る。未配置・失敗はプレースホルダ色のまま残す。</summary>
        public async UniTaskVoid LoadCharacterIconsAsync(IReadOnlyList<VisualElement> icons, CancellationToken ct)
        {
            for (int i = 0; i < icons.Count; i++)
            {
                CharacterId id = RouletteMath.CharacterForSector(i);
                CharacterDefinition definition = CharacterCatalog.Find(id);
                // コインには盤面コマと同じ丸バッジ画像（PieceIconAddress）を使う。
                Sprite icon = await TryLoadIconAsync(definition.PieceIconAddress, ct);
                if (icon != null)
                {
                    icons[i].style.backgroundImage = new StyleBackground(icon);
                    // 画像を貼れたらプレースホルダ色は消す（透過部分に色が透けないように）。
                    icons[i].style.backgroundColor = new StyleColor(Color.clear);
                }
            }
        }

        /// <summary>保持している全ハンドルを解放する。Presenter の OnDestroy から呼ぶ。</summary>
        public void ReleaseAll()
        {
            foreach (AsyncOperationHandle<Sprite> handle in _iconHandles)
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
            }
            _iconHandles.Clear();
        }

        private async UniTask<Sprite> TryLoadIconAsync(string address, CancellationToken ct)
        {
            AsyncOperationHandle<Sprite> handle = default;
            try
            {
                handle = Addressables.LoadAssetAsync<Sprite>(address);
                Sprite sprite = await handle.ToUniTask(cancellationToken: ct);
                _iconHandles.Add(handle);
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
                Debug.LogWarning($"ルーレットのキャラ画像 '{address}' のロードに失敗。プレースホルダ表示にします: {e.Message}");
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
                return null;
            }
        }
    }
}
