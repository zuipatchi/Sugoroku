using System.Collections.Generic;
using Common.Character;
using MiniGame.RaceGame;
using NUnit.Framework;

namespace Tests.EditMode
{
    /// <summary>
    /// 2D レースの CPU キャラ抽選 <see cref="RaceOpponentPicker.PickMany"/> の検証。
    /// プレイヤーを除外し、候補数の範囲では重複なしで人数ぶん配れることを保証する。
    /// </summary>
    public class RaceOpponentPickerTests
    {
        // 決定的にするため randomIndex は常に 0 を返す（Fisher–Yates が固定順で回る）。
        private static int FixedZero(int upperExclusive) => 0;

        [Test]
        public void PickManyはプレイヤーを含まず要求数ぶん返す()
        {
            CharacterId player = CharacterCatalog.All[0].Id;

            IReadOnlyList<CharacterId> picked = RaceOpponentPicker.PickMany(
                player, CharacterCatalog.All, 3, FixedZero);

            Assert.AreEqual(3, picked.Count);
            CollectionAssert.DoesNotContain(picked, player);
        }

        [Test]
        public void PickManyは候補数以内なら重複しない()
        {
            CharacterId player = CharacterCatalog.All[0].Id;
            int distinctCandidates = CharacterCatalog.All.Count - 1; // プレイヤーを除いた候補数

            IReadOnlyList<CharacterId> picked = RaceOpponentPicker.PickMany(
                player, CharacterCatalog.All, distinctCandidates, FixedZero);

            Assert.AreEqual(distinctCandidates, picked.Count);
            CollectionAssert.AllItemsAreUnique(picked);
        }

        [Test]
        public void PickManyは候補を超える要求では循環して埋める()
        {
            CharacterId player = CharacterCatalog.All[0].Id;
            int over = CharacterCatalog.All.Count + 5;

            IReadOnlyList<CharacterId> picked = RaceOpponentPicker.PickMany(
                player, CharacterCatalog.All, over, FixedZero);

            Assert.AreEqual(over, picked.Count);
            CollectionAssert.DoesNotContain(picked, player);
        }

        [Test]
        public void PickManyは0以下の要求で空を返す()
        {
            CharacterId player = CharacterCatalog.All[0].Id;

            IReadOnlyList<CharacterId> picked = RaceOpponentPicker.PickMany(
                player, CharacterCatalog.All, 0, FixedZero);

            Assert.AreEqual(0, picked.Count);
        }
    }
}
