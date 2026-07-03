using System.Collections.Generic;

namespace Common.MiniGame
{
    /// <summary>
    /// ミニゲーム 1 種類分のメタデータ。
    /// </summary>
    public sealed class MiniGameDefinition
    {
        public MiniGameDefinition(MiniGameId id, string displayName, string uxmlAddress)
        {
            Id = id;
            DisplayName = displayName;
            UxmlAddress = uxmlAddress;
        }

        public MiniGameId Id { get; }

        /// <summary>テストシーン等に出す表示名。</summary>
        public string DisplayName { get; }

        /// <summary>中身の UI（UXML）の Addressable アドレス。<see cref="MiniGameHostPresenter"/> がロードに使う。</summary>
        public string UxmlAddress { get; }
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
            new MiniGameDefinition(MiniGameId.Tap, "タップ連打", "MiniGame/TapGame"),
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
    }
}
