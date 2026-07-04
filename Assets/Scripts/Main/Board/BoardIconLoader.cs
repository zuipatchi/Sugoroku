using System;
using System.Threading;
using Common.Store;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Main.Board
{
    /// <summary>
    /// 盤面のマス画像・コマのキャラアイコンのロードを担う。ロードの実体とハンドル管理は
    /// <see cref="AddressableSpriteLoader"/> に委ね、ロード失敗（未配置等）は
    /// 呼び出し元の色面フォールバックに任せる。キャンセル（シーン破棄）は静かに打ち切る。
    /// </summary>
    public sealed class BoardIconLoader : IDisposable
    {
        private readonly AddressableSpriteLoader _spriteLoader = new();

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
                    Sprite sprite = await _spriteLoader.TryLoadAsync(cell.IconAddress, "盤面画像", ct);
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
                    Sprite sprite = await _spriteLoader.TryLoadAsync(addressOf(player), "盤面画像", ct);
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

        public void Dispose()
        {
            _spriteLoader.Dispose();
        }
    }
}
