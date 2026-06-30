using System.Collections.Generic;

namespace Common.Character
{
    /// <summary>
    /// キャラクター 1 種類分のメタデータ。
    /// </summary>
    public sealed class CharacterDefinition
    {
        public CharacterDefinition(CharacterId id, string displayName, string iconAddress, string portraitAddress)
        {
            Id = id;
            DisplayName = displayName;
            IconAddress = iconAddress;
            PortraitAddress = portraitAddress;
        }

        public CharacterId Id { get; }

        /// <summary>選択画面に出す表示名。</summary>
        public string DisplayName { get; }

        /// <summary>カード（クリック用）に出すアイコン画像の Addressable アドレス。未配置ならプレースホルダ表示にフォールバックする。</summary>
        public string IconAddress { get; }

        /// <summary>選択時に表示する大きい立ち絵の Addressable アドレス。未配置ならプレースホルダ表示にフォールバックする。</summary>
        public string PortraitAddress { get; }
    }

    /// <summary>
    /// 選択可能なキャラクター一覧（表示順）。UI 非依存の純粋データ。
    /// アイコン・立ち絵は各 Addressable アドレスにアセットを割り当てて用意する。
    /// </summary>
    public static class CharacterCatalog
    {
        public static readonly IReadOnlyList<CharacterDefinition> All = new[]
        {
            new CharacterDefinition(CharacterId.Character1, "キャラ 1", "Character/Character1/Icon", "Character/Character1/Portrait"),
            new CharacterDefinition(CharacterId.Character2, "キャラ 2", "Character/Character2/Icon", "Character/Character2/Portrait"),
            new CharacterDefinition(CharacterId.Character3, "キャラ 3", "Character/Character3/Icon", "Character/Character3/Portrait"),
            new CharacterDefinition(CharacterId.Character4, "キャラ 4", "Character/Character4/Icon", "Character/Character4/Portrait"),
        };

        /// <summary>既定（先頭）のキャラクター。</summary>
        public static CharacterId Default => All[0].Id;

        public static CharacterDefinition Find(CharacterId id)
        {
            foreach (CharacterDefinition definition in All)
            {
                if (definition.Id == id)
                {
                    return definition;
                }
            }
            return All[0];
        }

        /// <summary>表示順での位置（プレースホルダ色などに使う）。見つからなければ 0。</summary>
        public static int IndexOf(CharacterId id)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i].Id == id)
                {
                    return i;
                }
            }
            return 0;
        }
    }
}
