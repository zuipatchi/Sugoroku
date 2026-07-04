using System;
using System.Collections.Generic;
using Common.Character;

namespace MiniGame.RaceGame
{
    /// <summary>
    /// CPU の相手キャラ抽選。候補からプレイヤー以外を 1 体選ぶ純ロジック。
    /// 乱数は <c>randomIndex</c>（候補数 → 0 以上候補数未満のインデックス）として呼び出し側から注入する。
    /// </summary>
    public static class RaceOpponentPicker
    {
        /// <summary>
        /// <paramref name="candidates"/> からプレイヤー（<paramref name="playerId"/>）以外を 1 体選ぶ。
        /// プレイヤー以外の候補がいなければ <paramref name="playerId"/> を返す。
        /// </summary>
        public static CharacterId Pick(
            CharacterId playerId,
            IReadOnlyList<CharacterDefinition> candidates,
            Func<int, int> randomIndex)
        {
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
                return playerId;
            }
            return pool[randomIndex(pool.Count)];
        }
    }
}
