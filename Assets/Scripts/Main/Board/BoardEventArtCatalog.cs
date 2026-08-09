using Common.MiniGame;

namespace Main.Board
{
    /// <summary>
    /// 盤面マスの画像アドレス（Addressables）を全マップ共通で解決する静的カタログ。
    /// マスの見た目はマップに依らず共通なので、盤面データ（<see cref="BoardDefinition"/>）ごとに持たせず、
    /// ここ 1 か所を唯一のソースにする。解決の入口は <see cref="AddressFor"/>。
    /// 画像アドレスは <c>Board/&lt;イベント名&gt;</c> 規約で、対応するスプライトを Addressables に登録して用意する
    /// （通常マス＝<see cref="BoardCellEvent.None"/> だけは素材名のままの <see cref="NoneAddress"/>）。
    /// 画像を持たないイベントは空文字を返し、呼び出し側は記号表示にフォールバックする。
    /// <see cref="CharacterCatalog"/> / <see cref="Item.ItemCatalog"/> と同じ静的カタログ方式。
    /// </summary>
    public static class BoardEventArtCatalog
    {
        /// <summary>
        /// スタート＝ゴール（経路 index 0）のマスに使う固定画像アドレス。イベント種別に依らない特別扱いで、
        /// <see cref="Address"/> より優先して使う（解決は <see cref="AddressFor"/>）。
        /// </summary>
        public const string StartAddress = "Board/Start";

        /// <summary>
        /// 通常マス（<see cref="BoardCellEvent.None"/>＝何も起きないマス）に貼る画像アドレス。
        /// 他のイベントは <c>Board/&lt;イベント名&gt;</c> 規約だが、これだけは素材名のままの例外。
        /// </summary>
        public const string NoneAddress = "Image/Board/Glass";

        /// <summary>
        /// ミニゲームマス（<see cref="BoardCellEvent.MiniGame"/>）に貼る画像アドレス。
        /// **遊ぶゲームは着地のたびにランダムで決まる**ので、どのゲームを指すこともできない＝
        /// 全マス共通の「ミニゲーム」の絵を貼る（ゲーム別のサムネイルは着地の告知で見せる）。
        /// 他のイベントの <c>Board/&lt;イベント名&gt;</c> 規約ではなく、ミニゲームの絵と同じ
        /// <c>Image/MiniGame/</c> 配下に置く。
        /// </summary>
        public const string MiniGameAddress = "Image/MiniGame/Minigame";

        /// <summary>
        /// マス <paramref name="cell"/>（経路 index <paramref name="index"/>）に貼る画像のアドレス。
        /// スタート＝ゴール（index 0）だけは固定の <see cref="StartAddress"/>、
        /// それ以外はイベント種別ごとの共通画像（<see cref="Address"/>）。
        /// </summary>
        public static string AddressFor(BoardCellDefinition cell, int index)
        {
            if (index == 0)
            {
                return StartAddress;
            }
            return cell == null ? string.Empty : Address(cell.Event);
        }

        /// <summary>
        /// イベント種別 <paramref name="cellEvent"/> のマスに貼る共通画像の Addressables アドレス。
        /// 画像を用意していないイベントは空文字（記号表示にフォールバック）。
        /// </summary>
        public static string Address(BoardCellEvent cellEvent)
        {
            switch (cellEvent)
            {
                case BoardCellEvent.Forward:
                    return "Board/Forward";
                case BoardCellEvent.Back:
                    return "Board/Back";
                case BoardCellEvent.MoneyUp:
                    return "Board/MoneyUp";
                case BoardCellEvent.MoneyDown:
                    return "Board/MoneyDown";
                case BoardCellEvent.Territory:
                    return "Board/Territory";
                case BoardCellEvent.Item:
                    return "Board/Item";
                case BoardCellEvent.MiniGame:
                    return MiniGameAddress;
                case BoardCellEvent.None:
                    return NoneAddress;
                default:
                    return string.Empty;
            }
        }
    }
}
