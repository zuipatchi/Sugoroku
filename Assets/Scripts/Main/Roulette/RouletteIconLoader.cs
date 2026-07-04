using System.Collections.Generic;
using System.Threading;
using Common.Character;
using Common.Store;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Main.Roulette
{
    /// <summary>
    /// ルーレット各セクターのキャラアイコン画像のロードを担う。ロードの実体とハンドル管理は
    /// <see cref="AddressableSpriteLoader"/> に委ね、成功したアイコン要素へ背景画像を貼り、
    /// 未配置・失敗はプレースホルダ色のまま残す。
    /// </summary>
    public sealed class RouletteIconLoader
    {
        private readonly AddressableSpriteLoader _spriteLoader = new();

        /// <summary>各セクターのアイコンにキャラ画像を貼る。未配置・失敗はプレースホルダ色のまま残す。</summary>
        public async UniTaskVoid LoadCharacterIconsAsync(IReadOnlyList<VisualElement> icons, CancellationToken ct)
        {
            for (int i = 0; i < icons.Count; i++)
            {
                CharacterId id = RouletteMath.CharacterForSector(i);
                CharacterDefinition definition = CharacterCatalog.Find(id);
                // コインには盤面コマと同じ丸バッジ画像（PieceIconAddress）を使う。
                Sprite icon = await _spriteLoader.TryLoadAsync(definition.PieceIconAddress, "ルーレットのキャラ画像", ct);
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
            _spriteLoader.Dispose();
        }
    }
}
