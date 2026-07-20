using System;
using System.Collections.Generic;
using Common.Character;

namespace MiniGame.RaceGame
{
    /// <summary>
    /// CPU の相手キャラ抽選。候補からプレイヤー以外を重複なしで選ぶ純ロジック。
    /// 乱数は <c>randomIndex</c>（上限 → 0 以上その未満のインデックス）として呼び出し側から注入する。
    /// </summary>
    public static class RaceOpponentPicker
    {
        /// <summary>
        /// <paramref name="candidates"/> からプレイヤー（<paramref name="playerId"/>）以外を重複なしで
        /// <paramref name="count"/> 体シャッフルして選ぶ。候補がプレイヤーしかいない場合は <paramref name="playerId"/> で
        /// 埋める。候補数が <paramref name="count"/> に満たない場合はシャッフル列を循環させて埋める。
        /// </summary>
        public static IReadOnlyList<CharacterId> PickMany(
            CharacterId playerId,
            IReadOnlyList<CharacterDefinition> candidates,
            int count,
            Func<int, int> randomIndex)
        {
            List<CharacterId> result = new(count > 0 ? count : 0);
            if (count <= 0)
            {
                return result;
            }

            List<CharacterId> pool = new();
            foreach (CharacterDefinition definition in candidates)
            {
                if (definition.Id != playerId)
                {
                    pool.Add(definition.Id);
                }
            }

            if (pool.Count == 0)
            {
                // 候補がプレイヤーしかいない異常時は playerId で埋める（呼び出し側の走者数を満たすため）。
                for (int i = 0; i < count; i++)
                {
                    result.Add(playerId);
                }
                return result;
            }

            // Fisher–Yates でシャッフルし、先頭から count 体。足りなければ循環して埋める。
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = randomIndex(i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            for (int i = 0; i < count; i++)
            {
                result.Add(pool[i % pool.Count]);
            }
            return result;
        }
    }
}
