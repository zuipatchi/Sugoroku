using System;
using System.Collections.Generic;
using Matching;
using NUnit.Framework;
using Unity.Services.Multiplayer;

namespace Tests.EditMode
{
    public class MatchingServiceTests
    {
        private sealed class FakeSessionInfo : ISessionInfo
        {
            public FakeSessionInfo(string id, string name, int maxPlayers, int availableSlots)
            {
                Id = id;
                Name = name;
                MaxPlayers = maxPlayers;
                AvailableSlots = availableSlots;
            }

            public string Name { get; }
            public string Id { get; }
            public int MaxPlayers { get; }
            public int AvailableSlots { get; }
            public string Upid => string.Empty;
            public string HostId => string.Empty;
            public bool IsLocked => false;
            public bool HasPassword => false;
            public DateTime LastUpdated => default;
            public DateTime Created => default;
            public IReadOnlyDictionary<string, SessionProperty> Properties => null;
        }

        [Test]
        public void 空きスロットありのセッションはLobbyInfoに変換される()
        {
            List<ISessionInfo> sessions = new() { new FakeSessionInfo("id-1", "Room1", 4, 1) };

            IReadOnlyList<LobbyInfo> rooms = MatchingService.MapSessionsToRooms(sessions);

            Assert.AreEqual(1, rooms.Count);
            Assert.AreEqual("id-1", rooms[0].LobbyId);
            Assert.AreEqual("Room1", rooms[0].Name);
            Assert.AreEqual(4, rooms[0].MaxPlayers);
        }

        [Test]
        public void プレイヤー数はMaxPlayersから空きスロットを引いた値になる()
        {
            // 定員 4・空き 1 → 3人が参加中
            List<ISessionInfo> sessions = new() { new FakeSessionInfo("id-1", "Room1", 4, 1) };

            IReadOnlyList<LobbyInfo> rooms = MatchingService.MapSessionsToRooms(sessions);

            Assert.AreEqual(3, rooms[0].PlayerCount);
        }

        [Test]
        public void 満室のセッションは除外される()
        {
            List<ISessionInfo> sessions = new() { new FakeSessionInfo("full", "FullRoom", 4, 0) };

            IReadOnlyList<LobbyInfo> rooms = MatchingService.MapSessionsToRooms(sessions);

            Assert.AreEqual(0, rooms.Count);
        }

        [Test]
        public void 満室と空きありが混在する場合は空きありのみ残る()
        {
            List<ISessionInfo> sessions = new()
            {
                new FakeSessionInfo("full", "FullRoom", 4, 0),
                new FakeSessionInfo("open", "OpenRoom", 4, 2),
            };

            IReadOnlyList<LobbyInfo> rooms = MatchingService.MapSessionsToRooms(sessions);

            Assert.AreEqual(1, rooms.Count);
            Assert.AreEqual("open", rooms[0].LobbyId);
            Assert.AreEqual(2, rooms[0].PlayerCount);
        }

        [Test]
        public void 空のセッション一覧は空のリストになる()
        {
            List<ISessionInfo> sessions = new();

            IReadOnlyList<LobbyInfo> rooms = MatchingService.MapSessionsToRooms(sessions);

            Assert.AreEqual(0, rooms.Count);
        }
    }
}
