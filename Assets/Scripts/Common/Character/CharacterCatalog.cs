using System.Collections.Generic;

namespace Common.Character
{
    /// <summary>
    /// キャラクター 1 種類分のメタデータ。
    /// </summary>
    public sealed class CharacterDefinition
    {
        public CharacterDefinition(CharacterId id, string displayName, string cardAddress, string pieceIconAddress, string portraitAddress)
        {
            Id = id;
            DisplayName = displayName;
            CardAddress = cardAddress;
            PieceIconAddress = pieceIconAddress;
            PortraitAddress = portraitAddress;
        }

        public CharacterId Id { get; }

        /// <summary>選択画面に出す表示名。</summary>
        public string DisplayName { get; }

        /// <summary>選択画面のカード（クリック用）に出すキャラ絵の Addressable アドレス。未配置ならプレースホルダ表示にフォールバックする。</summary>
        public string CardAddress { get; }

        /// <summary>盤面のコマに使う丸アイコン（バッジ）の Addressable アドレス。未配置ならプレースホルダ（色コマ）にフォールバックする。</summary>
        public string PieceIconAddress { get; }

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
            new CharacterDefinition(CharacterId.Character1, "のらどっく", "Character/Character1/Card", "Character/Character1/Icon", "Character/Character1/Portrait"),
            new CharacterDefinition(CharacterId.Character2, "ザニザニマン", "Character/Character2/Card", "Character/Character2/Icon", "Character/Character2/Portrait"),
            new CharacterDefinition(CharacterId.Character3, "D.O.M", "Character/Character3/Card", "Character/Character3/Icon", "Character/Character3/Portrait"),
            new CharacterDefinition(CharacterId.Character4, "アリマ", "Character/Character4/Card", "Character/Character4/Icon", "Character/Character4/Portrait"),
            new CharacterDefinition(CharacterId.Character5, "モナカ", "Character/Character5/Card", "Character/Character5/Icon", "Character/Character5/Portrait"),
            new CharacterDefinition(CharacterId.Character6, "ずいさん", "Character/Character6/Card", "Character/Character6/Icon", "Character/Character6/Portrait"),
            new CharacterDefinition(CharacterId.Character7, "釈迦パッチ", "Character/Character7/Card", "Character/Character7/Icon", "Character/Character7/Portrait"),
            new CharacterDefinition(CharacterId.Character8, "タロー", "Character/Character8/Card", "Character/Character8/Icon", "Character/Character8/Portrait"),
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
