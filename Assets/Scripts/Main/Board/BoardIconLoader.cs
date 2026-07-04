using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Main.Board
{
    /// <summary>
    /// 盤面のマス画像・コマのキャラアイコンを Addressables から読み込み、ハンドルを保持して
    /// <see cref="Dispose"/> でまとめて解放する。ロード失敗（未配置等）は null を返して
    /// 呼び出し元の色面フォールバックに任せる。キャンセル（シーン破棄）は静かに打ち切る。
    /// </summary>
    public sealed class BoardIconLoader : IDisposable
    {
        private readonly List<AsyncOperationHandle<Sprite>> _iconHandles = new();

        /// <summary>
        /// アイコンアドレスを持つマスの画像を経路順に読み込み、成功するたびに
        /// <paramref name="onLoaded"/>(マス index, Sprite) を呼ぶ。
        /// </summary>
        public async UniTask LoadCellIconsAsync(
            BoardDefinition definition,
            Action<int, Sprite> onLoaded,
            CancellationToken ct)
        {
            try
            {
                for (int i = 0; i < definition.CellCount; i++)
                {
                    BoardCellDefinition cell = definition.Cell(i);
                    if (!cell.HasIcon)
                    {
                        continue;
                    }
                    Sprite sprite = await TryLoadSpriteAsync(cell.IconAddress, ct);
                    if (sprite == null)
                    {
                        continue;
                    }
                    onLoaded(i, sprite);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        /// <summary>
        /// 各プレイヤーのコマ用アイコンを読み込み、成功するたびに
        /// <paramref name="onLoaded"/>(プレイヤー index, Sprite) を呼ぶ。
        /// アドレスは <paramref name="addressOf"/>(プレイヤー index) で解決する。
        /// </summary>
        public async UniTask LoadPieceIconsAsync(
            int pieceCount,
            Func<int, string> addressOf,
            Action<int, Sprite> onLoaded,
            CancellationToken ct)
        {
            try
            {
                for (int player = 0; player < pieceCount; player++)
                {
                    Sprite sprite = await TryLoadSpriteAsync(addressOf(player), ct);
                    if (sprite == null)
                    {
                        continue; // 未配置のキャラは従来の色コマにフォールバック
                    }
                    onLoaded(player, sprite);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        /// <summary>
        /// スプライト 1 枚を読み込む。成功したらハンドルを保持して Sprite を、失敗したら null を返す。
        /// キャンセル時はハンドルを解放して <see cref="OperationCanceledException"/> を伝播する。
        /// </summary>
        public async UniTask<Sprite> TryLoadSpriteAsync(string address, CancellationToken ct)
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
                Debug.LogWarning($"盤面画像 '{address}' のロードに失敗。色表示にフォールバックします: {e.Message}");
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
                return null;
            }
        }

        public void Dispose()
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
    }
}
