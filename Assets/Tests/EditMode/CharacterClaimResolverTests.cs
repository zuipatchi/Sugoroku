using System;
using System.Collections.Generic;
using Common.Character;
using NUnit.Framework;
using OnlineCharacterSelect.Sync;

namespace Tests.EditMode
{
    public class CharacterClaimResolverTests
    {
        private static PlayerChoice Choice(string id, CharacterId? desired, bool ready = false)
        {
            return new PlayerChoice(id, desired, ready);
        }

        private static Dictionary<CharacterId, string> NoLocks()
        {
            return new Dictionary<CharacterId, string>();
        }

        [Test]
        public void ユニークな選択なら全員がそれぞれロックされる()
        {
            List<PlayerChoice> choices = new()
            {
                Choice("a", CharacterId.Character1),
                Choice("b", CharacterId.Character2),
                Choice("c", CharacterId.Character3),
            };

            IReadOnlyDictionary<CharacterId, string> locks = CharacterClaimResolver.ResolveLocks(NoLocks(), choices);

            Assert.AreEqual("a", locks[CharacterId.Character1]);
            Assert.AreEqual("b", locks[CharacterId.Character2]);
            Assert.AreEqual("c", locks[CharacterId.Character3]);
            Assert.AreEqual(3, locks.Count);
        }

        [Test]
        public void 先にロックした人が同キャラ競合で勝ち後発はロックされない()
        {
            // 前ティックで a が Character1 をロック済み。b が同じキャラを希望してくる。
            Dictionary<CharacterId, string> prev = new() { { CharacterId.Character1, "a" } };
            List<PlayerChoice> choices = new()
            {
                Choice("a", CharacterId.Character1),
                Choice("b", CharacterId.Character1),
            };

            IReadOnlyDictionary<CharacterId, string> locks = CharacterClaimResolver.ResolveLocks(prev, choices);

            Assert.AreEqual("a", locks[CharacterId.Character1], "先着の a が保持する");
            Assert.AreEqual(1, locks.Count, "後発 b はロックされない");
        }

        [Test]
        public void 同ティックの同時希望はPlayerId昇順で決定的に決まる()
        {
            List<PlayerChoice> choices = new()
            {
                Choice("zoe", CharacterId.Character5),
                Choice("amy", CharacterId.Character5),
            };

            IReadOnlyDictionary<CharacterId, string> locks = CharacterClaimResolver.ResolveLocks(NoLocks(), choices);

            Assert.AreEqual("amy", locks[CharacterId.Character5], "PlayerId 昇順で amy が勝つ");
            Assert.AreEqual(1, locks.Count);
        }

        [Test]
        public void 所有者が別キャラへ変えると旧ロックが解放され待っていた人がロックできる()
        {
            Dictionary<CharacterId, string> prev = new() { { CharacterId.Character1, "a" } };
            List<PlayerChoice> choices = new()
            {
                Choice("a", CharacterId.Character2), // a が Character1 → Character2 へ変更
                Choice("b", CharacterId.Character1), // b は Character1 を希望し続けている
            };

            IReadOnlyDictionary<CharacterId, string> locks = CharacterClaimResolver.ResolveLocks(prev, choices);

            Assert.AreEqual("a", locks[CharacterId.Character2], "a は新しいキャラをロック");
            Assert.AreEqual("b", locks[CharacterId.Character1], "解放された Character1 を b がロック");
        }

        [Test]
        public void 離脱した所有者のロックは解放される()
        {
            Dictionary<CharacterId, string> prev = new() { { CharacterId.Character1, "a" } };
            // a はもう choices にいない（離脱）。
            List<PlayerChoice> choices = new()
            {
                Choice("b", CharacterId.Character2),
            };

            IReadOnlyDictionary<CharacterId, string> locks = CharacterClaimResolver.ResolveLocks(prev, choices);

            Assert.IsFalse(locks.ContainsKey(CharacterId.Character1), "離脱した a のロックは消える");
            Assert.AreEqual("b", locks[CharacterId.Character2]);
        }

        [Test]
        public void 未選択のプレイヤーはロックを持たない()
        {
            List<PlayerChoice> choices = new()
            {
                Choice("a", CharacterId.Character1),
                Choice("b", null),
            };

            IReadOnlyDictionary<CharacterId, string> locks = CharacterClaimResolver.ResolveLocks(NoLocks(), choices);

            Assert.AreEqual(1, locks.Count);
            Assert.AreEqual("a", locks[CharacterId.Character1]);
        }

        [Test]
        public void AllSettledは全員readyでユニークで人数ちょうどのときだけtrue()
        {
            List<PlayerChoice> choices = new()
            {
                Choice("a", CharacterId.Character1, ready: true),
                Choice("b", CharacterId.Character2, ready: true),
            };
            Dictionary<CharacterId, string> locks = new()
            {
                { CharacterId.Character1, "a" },
                { CharacterId.Character2, "b" },
            };

            Assert.IsTrue(CharacterClaimResolver.AllSettled(choices, locks, 2));
        }

        [Test]
        public void AllSettledは1人でも未readyならfalse()
        {
            List<PlayerChoice> choices = new()
            {
                Choice("a", CharacterId.Character1, ready: true),
                Choice("b", CharacterId.Character2, ready: false),
            };
            Dictionary<CharacterId, string> locks = new()
            {
                { CharacterId.Character1, "a" },
                { CharacterId.Character2, "b" },
            };

            Assert.IsFalse(CharacterClaimResolver.AllSettled(choices, locks, 2));
        }

        [Test]
        public void AllSettledは希望キャラを自分でロックできていなければfalse()
        {
            // b の希望 Character1 は a にロックされている（被り）。
            List<PlayerChoice> choices = new()
            {
                Choice("a", CharacterId.Character1, ready: true),
                Choice("b", CharacterId.Character1, ready: true),
            };
            Dictionary<CharacterId, string> locks = new()
            {
                { CharacterId.Character1, "a" },
            };

            Assert.IsFalse(CharacterClaimResolver.AllSettled(choices, locks, 2));
        }

        [Test]
        public void AllSettledは人数が足りなければfalse()
        {
            List<PlayerChoice> choices = new()
            {
                Choice("a", CharacterId.Character1, ready: true),
            };
            Dictionary<CharacterId, string> locks = new()
            {
                { CharacterId.Character1, "a" },
            };

            Assert.IsFalse(CharacterClaimResolver.AllSettled(choices, locks, 2), "定員 2 に対し 1 人");
        }

        [Test]
        public void BuildRosterは参加時刻の昇順で席順に並べる()
        {
            DateTime t0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            List<(string, DateTime, CharacterId)> players = new()
            {
                ("late", t0.AddSeconds(30), CharacterId.Character3),
                ("host", t0, CharacterId.Character1),
                ("mid", t0.AddSeconds(10), CharacterId.Character2),
            };

            IReadOnlyList<RosterSeat> roster = CharacterClaimResolver.BuildRoster(players);

            Assert.AreEqual("host", roster[0].PlayerId, "最初の参加が seat 0");
            Assert.AreEqual(CharacterId.Character1, roster[0].Character);
            Assert.AreEqual("mid", roster[1].PlayerId);
            Assert.AreEqual("late", roster[2].PlayerId);
        }

        [Test]
        public void BuildRosterは同時刻ならPlayerId昇順で決定的に並ぶ()
        {
            DateTime t = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            List<(string, DateTime, CharacterId)> players = new()
            {
                ("b", t, CharacterId.Character2),
                ("a", t, CharacterId.Character1),
            };

            IReadOnlyList<RosterSeat> roster = CharacterClaimResolver.BuildRoster(players);

            Assert.AreEqual("a", roster[0].PlayerId);
            Assert.AreEqual("b", roster[1].PlayerId);
        }
    }
}
