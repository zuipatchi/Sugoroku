using Common.Character;
using NUnit.Framework;

namespace Tests.EditMode
{
    /// <summary>
    /// キャラカタログのメタデータ整合性を検証する。特に陣地マス占拠の旗演出で使う
    /// <see cref="CharacterDefinition.FlagAddress"/> が全キャラで用意されていることを保証する。
    /// </summary>
    public sealed class CharacterCatalogTests
    {
        [Test]
        public void AllCharacters_HaveFlagAddress()
        {
            foreach (CharacterDefinition definition in CharacterCatalog.All)
            {
                Assert.IsFalse(
                    string.IsNullOrEmpty(definition.FlagAddress),
                    $"{definition.Id} の FlagAddress が未設定です");
            }
        }
    }
}
