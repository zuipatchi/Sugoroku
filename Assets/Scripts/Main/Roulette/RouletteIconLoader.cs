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
        /// <summary>
        /// 画像に差し替えたスピンボタンへ付ける USS クラス。円形の見た目になるため、
        /// Presenter はこのクラスの有無で当たり判定を円に切り替える。
        /// </summary>
        public const string SpinButtonImageClass = "roulette-spin--image";

        private readonly AddressableSpriteLoader _spriteLoader = new();

        /// <summary>
        /// 各セクターのアイコンにキャラ画像を貼る。<paramref name="sectorCharacters"/> はセクターごとに
        /// 割り当てた参加者のキャラ（<paramref name="icons"/> と同じ並び）。未配置・失敗はプレースホルダ色のまま残す。
        /// </summary>
        public async UniTaskVoid LoadCharacterIconsAsync(
            IReadOnlyList<VisualElement> icons, IReadOnlyList<CharacterId> sectorCharacters, CancellationToken ct)
        {
            int count = Mathf.Min(icons.Count, sectorCharacters.Count);
            for (int i = 0; i < count; i++)
            {
                CharacterDefinition definition = CharacterCatalog.Find(sectorCharacters[i]);
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

        /// <summary>
        /// スピンボタンの画像（<paramref name="address"/>）を読み込んでボタンに背景として貼り、文字を消す。
        /// 未配置・失敗のときは文字のまま残す。
        /// </summary>
        public async UniTaskVoid LoadSpinButtonAsync(Button button, string address, CancellationToken ct)
        {
            Sprite sprite = await _spriteLoader.TryLoadAsync(address, "ルーレットのスピンボタン画像", ct);
            if (sprite == null || button == null)
            {
                return;
            }
            button.style.backgroundImage = new StyleBackground(sprite);
            button.text = string.Empty;
            button.AddToClassList(SpinButtonImageClass);
        }

        /// <summary>保持している全ハンドルを解放する。Presenter の OnDestroy から呼ぶ。</summary>
        public void ReleaseAll()
        {
            _spriteLoader.Dispose();
        }
    }
}
