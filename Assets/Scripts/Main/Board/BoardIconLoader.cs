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
        /// 各マスの画像を経路順に読み込み、成功するたびに <paramref name="onLoaded"/>(マス index, Sprite) を呼ぶ。
        /// アドレスはマスのイベント種別ごとに盤面が持つ画像（<see cref="BoardDefinition.EventIconAddress"/>）で解決する。
        /// ただしスタート＝ゴール（index 0）は固定の <see cref="BoardDefinition.StartCellIconAddress"/> を優先して使う。
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
                    // スタート＝ゴール（index 0）は専用画像。それ以外はイベント種別ごとの画像で解決する。
                    BoardCellEvent cellEvent = definition.Cell(i).Event;
                    string address = i == 0
                        ? BoardDefinition.StartCellIconAddress
                        : definition.EventIconAddress(cellEvent);
                    // アイテムマスは盤面が画像を明示設定していなければ既定の画像を使う（スタートと同じ扱い）。
                    if (string.IsNullOrEmpty(address) && cellEvent == BoardCellEvent.Item)
                    {
                        address = BoardDefinition.ItemCellIconAddress;
                    }
                    if (string.IsNullOrEmpty(address))
                    {
                        continue;
                    }
                    Sprite sprite = await _spriteLoader.TryLoadAsync(address, "盤面画像", ct);
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
        /// 全マス共通の枠画像を 1 枚読み込んで返す。未配置・キャンセル時は null（枠なしにフォールバック）。
        /// </summary>
        public async UniTask<Sprite> LoadFrameAsync(string address, CancellationToken ct)
        {
            try
            {
                return await _spriteLoader.TryLoadAsync(address, "盤面枠画像", ct);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        /// <summary>
        /// アイテム絵を 1 枚読み込んで返す。未配置・キャンセル時は null（プレースホルダ文字にフォールバック）。
        /// </summary>
        public async UniTask<Sprite> LoadItemAsync(string address, CancellationToken ct)
        {
            try
            {
                return await _spriteLoader.TryLoadAsync(address, "アイテム画像", ct);
            }
            catch (OperationCanceledException)
            {
                return null;
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
