using System.Collections.Generic;

namespace Common.MiniGame
{
    /// <summary>
    /// ミニゲーム 1 種類分のメタデータ。
    /// </summary>
    public sealed class MiniGameDefinition
    {
        public MiniGameDefinition(
            MiniGameId id, string displayName, string uxmlAddress, string imageAddress, string description = null)
        {
            Id = id;
            DisplayName = displayName;
            UxmlAddress = uxmlAddress;
            ImageAddress = imageAddress ?? string.Empty;
            Description = description ?? string.Empty;
        }

        public MiniGameId Id { get; }

        /// <summary>テストシーン等に出す表示名。</summary>
        public string DisplayName { get; }

        /// <summary>
        /// 遊び方の 1 行説明（Home のルール説明に出す単一の情報源）。
        /// ゲームごとの細かい数値（制限時間など）は各ゲームの Config が持つので、ここには書かない。
        /// </summary>
        public string Description { get; }

        /// <summary>中身の UI（UXML）の Addressable アドレス。<see cref="MiniGameHostPresenter"/> がロードに使う。</summary>
        public string UxmlAddress { get; }

        /// <summary>
        /// ミニゲーム選択カードに出すサムネイル画像の Addressable アドレス。
        /// 未配置ならプレースホルダ（表示名テキスト）にフォールバックする。
        /// </summary>
        public string ImageAddress { get; }
    }

    /// <summary>
    /// 選択可能なミニゲーム一覧（表示順）。UI 非依存の純粋データ。
    /// 新しいミニゲームはここに 1 行足し、対応する UXML を Addressables（<see cref="MiniGameDefinition.UxmlAddress"/>）に登録する。
    /// テストシーン（<c>MiniGameTest</c>）はこの一覧をボタン化する。
    /// </summary>
    public static class MiniGameCatalog
    {
        public static readonly IReadOnlyList<MiniGameDefinition> All = new[]
        {
            new MiniGameDefinition(
                MiniGameId.Tap, "タップ連打", "MiniGame/TapGame", "Image/MiniGame/Renda",
                "制限時間のあいだサンドバッグを連打し、叩いた回数の多い人が勝ち。"),
            new MiniGameDefinition(
                MiniGameId.Race, "2Dレース", "MiniGame/RaceGame", "Image/MiniGame/Lace",
                "往復するメーターをタップして走者を前へ進め、ゴールした人が勝ち。"),
            new MiniGameDefinition(
                MiniGameId.Overlap, "被っちゃやーよ", "MiniGame/OverlapGame", "Image/MiniGame/kaburi",
                "並んだアイテムから 1 枚を選び、ほかの誰とも被らなければゲットできる。"),
        };

        public static MiniGameDefinition Find(MiniGameId id)
        {
            foreach (MiniGameDefinition definition in All)
            {
                if (definition.Id == id)
                {
                    return definition;
                }
            }
            return All[0];
        }

        /// <summary>
        /// 一覧からランダムに 1 つ選ぶ。**ミニゲームマスに止まったときに遊ぶゲームを決める**のに使う
        /// （マスごとの設定ではなく着地のたびの抽選）。乱数源は呼び出し側が渡す（テストで seed 固定できる）。
        /// <paramref name="rng"/> が null なら先頭を返して決定的（<c>MoneyCellRule</c> などと同じ規約）。
        ///
        /// **決めた結果はオンラインで配る必要がある**（盤面データから導けなくなったため
        /// ＝<c>GameAction.MiniGameLanding</c>）。
        /// </summary>
        public static MiniGameId RandomGame(System.Random rng)
        {
            return rng == null ? All[0].Id : All[rng.Next(All.Count)].Id;
        }
    }
}
