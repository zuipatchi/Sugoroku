namespace Matching
{
    public readonly struct LobbyInfo
    {
        public string LobbyId { get; }
        public string Name { get; }
        public int PlayerCount { get; }
        public int MaxPlayers { get; }
        // ホストがルーム作成時に選んだマップの識別子（資産名）。未設定なら空文字。
        public string BoardId { get; }

        public LobbyInfo(string lobbyId, string name, int playerCount, int maxPlayers, string boardId = "")
        {
            LobbyId = lobbyId;
            Name = name;
            PlayerCount = playerCount;
            MaxPlayers = maxPlayers;
            BoardId = boardId ?? string.Empty;
        }
    }
}
