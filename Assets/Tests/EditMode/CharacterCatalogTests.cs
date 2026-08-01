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

        [Test]
        public void 席順ごとの初期キャラは表示順に対応する()
        {
            Assert.AreEqual(CharacterId.Character1, CharacterCatalog.DefaultFor(0), "1P はのらどっく");
            Assert.AreEqual(CharacterId.Character2, CharacterCatalog.DefaultFor(1), "2P はザニザニマン");
            Assert.AreEqual(CharacterId.Character3, CharacterCatalog.DefaultFor(2), "3P は D.O.M");
            Assert.AreEqual(CharacterId.Character4, CharacterCatalog.DefaultFor(3), "4P はアリマ");
        }

        [Test]
        public void 範囲外の席はカタログ内へクランプする()
        {
            Assert.AreEqual(CharacterCatalog.All[0].Id, CharacterCatalog.DefaultFor(-1));
            Assert.AreEqual(CharacterCatalog.All[CharacterCatalog.All.Count - 1].Id, CharacterCatalog.DefaultFor(CharacterCatalog.All.Count));
        }
    }
}
