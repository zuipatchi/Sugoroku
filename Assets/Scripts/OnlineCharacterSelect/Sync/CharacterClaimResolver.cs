using System;
using System.Collections.Generic;
using Common.Character;

namespace OnlineCharacterSelect.Sync
{
    /// <summary>
    /// ロビーでの 1 プレイヤーの選択状態（希望キャラ・決定済みか）。
    /// </summary>
    public readonly struct PlayerChoice
    {
        public PlayerChoice(string playerId, CharacterId? desired, bool ready)
        {
            PlayerId = playerId;
            Desired = desired;
            Ready = ready;
        }

        /// <summary>UGS のプレイヤー識別子。</summary>
        public string PlayerId { get; }

        /// <summary>希望キャラ。未選択なら null。</summary>
        public CharacterId? Desired { get; }

        /// <summary>「決定」済みか。</summary>
        public bool Ready { get; }
    }

    /// <summary>
    /// オンラインのキャラ選択ロビーで「誰がどのキャラをロックしたか」を決める純粋ロジック。
    /// ホスト（審判）が毎ティック <see cref="ResolveLocks"/> でロック表を更新し、全員へ共有する。
    /// 先着ロック（sticky）：一度ロックしたキャラは、本人が別キャラへ変える／離脱するまで保持され、
    /// 後から同じキャラを希望した人はロックできない（＝被らない）。純粋関数なので単体テスト可能。
    /// </summary>
    public static class CharacterClaimResolver
    {
        /// <summary>
        /// 先着ロックを更新する。<paramref name="previousLocks"/>（前ティックのロック：キャラ→所有者Id）を土台に、
        /// 現在の希望（<paramref name="choices"/>）から新しいロック表を返す。
        /// 手順: (1) 所有者がまだ在席し同じキャラを希望し続けているロックだけ引き継ぐ、
        /// (2) 空いているキャラを、それを希望していてまだ何もロックしていないプレイヤーへ配る
        /// （同ティックの同時希望は PlayerId 昇順で決定的にタイブレーク）。
        /// </summary>
        public static IReadOnlyDictionary<CharacterId, string> ResolveLocks(
            IReadOnlyDictionary<CharacterId, string> previousLocks,
            IReadOnlyList<PlayerChoice> choices)
        {
            Dictionary<CharacterId, string> locks = new();

            // 在席プレイヤーと、その希望キャラの索引を作る。
            Dictionary<string, CharacterId?> desiredByPlayer = new();
            foreach (PlayerChoice choice in choices)
            {
                desiredByPlayer[choice.PlayerId] = choice.Desired;
            }

            // (1) 引き継ぎ：所有者が在席し、同じキャラを希望し続けているロックだけ残す。
            foreach (KeyValuePair<CharacterId, string> pair in previousLocks)
            {
                CharacterId character = pair.Key;
                string owner = pair.Value;
                if (desiredByPlayer.TryGetValue(owner, out CharacterId? desired)
                    && desired.HasValue && desired.Value == character)
                {
                    locks[character] = owner;
                }
            }

            HashSet<string> alreadyOwns = new(locks.Values);

            // (2) 空きキャラの割り当て。PlayerId 昇順で決定的に処理する。
            List<PlayerChoice> ordered = new(choices);
            ordered.Sort((a, b) => string.CompareOrdinal(a.PlayerId, b.PlayerId));
            foreach (PlayerChoice choice in ordered)
            {
                if (!choice.Desired.HasValue)
                {
                    continue;
                }
                CharacterId character = choice.Desired.Value;
                if (locks.ContainsKey(character))
                {
                    continue; // 既に誰かにロックされている（引き継ぎ or 同ティックの先着）。
                }
                if (alreadyOwns.Contains(choice.PlayerId))
                {
                    continue; // 1 人 1 キャラ。念のため多重ロックを防ぐ。
                }
                locks[character] = choice.PlayerId;
                alreadyOwns.Add(choice.PlayerId);
            }

            return locks;
        }

        /// <summary>
        /// 全員がゲーム開始できる状態か。人数がそろい、全員が「決定」済みで、
        /// 各自が希望キャラを自分でロックできている（＝誰とも被っていない）ときだけ true。
        /// </summary>
        public static bool AllSettled(
            IReadOnlyList<PlayerChoice> choices,
            IReadOnlyDictionary<CharacterId, string> locks,
            int expectedCount)
        {
            if (choices.Count != expectedCount)
            {
                return false;
            }
            foreach (PlayerChoice choice in choices)
            {
                if (!choice.Ready || !choice.Desired.HasValue)
                {
                    return false;
                }
                if (!locks.TryGetValue(choice.Desired.Value, out string owner) || owner != choice.PlayerId)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 席順ロースターを作る。参加順（<c>joined</c> 昇順・同時刻は PlayerId 昇順）で
        /// seat 0,1,2… に並べる。ホスト（最初の参加）が seat 0 になる。全クライアントで同じ並びになる。
        /// </summary>
        public static IReadOnlyList<RosterSeat> BuildRoster(
            IReadOnlyList<(string playerId, DateTime joined, CharacterId character)> players)
        {
            List<(string playerId, DateTime joined, CharacterId character)> sorted = new(players);
            sorted.Sort((a, b) =>
            {
                int byJoined = a.joined.CompareTo(b.joined);
                return byJoined != 0 ? byJoined : string.CompareOrdinal(a.playerId, b.playerId);
            });

            List<RosterSeat> roster = new(sorted.Count);
            foreach ((string playerId, DateTime _, CharacterId character) in sorted)
            {
                roster.Add(new RosterSeat(playerId, character));
            }
            return roster;
        }
    }

    /// <summary>ロースターの 1 席（プレイヤーId とそのキャラ）。</summary>
    public readonly struct RosterSeat
    {
        public RosterSeat(string playerId, CharacterId character)
        {
            PlayerId = playerId;
            Character = character;
        }

        public string PlayerId { get; }
        public CharacterId Character { get; }
    }
}
