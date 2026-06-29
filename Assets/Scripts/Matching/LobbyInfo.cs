namespace Matching
{
    public readonly struct LobbyInfo
    {
        public string LobbyId { get; }
        public string Name { get; }
        public int PlayerCount { get; }
        public int MaxPlayers { get; }

        public LobbyInfo(string lobbyId, string name, int playerCount, int maxPlayers)
        {
            LobbyId = lobbyId;
            Name = name;
            PlayerCount = playerCount;
            MaxPlayers = maxPlayers;
        }
    }
}
