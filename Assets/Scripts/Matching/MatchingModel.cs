using System;
using System.Collections.Generic;
using R3;

namespace Matching
{
    public class MatchingModel
    {
        public ReactiveProperty<MatchingState> State { get; } = new(MatchingState.Idle);
        public ReactiveProperty<IReadOnlyList<LobbyInfo>> Rooms { get; } = new(Array.Empty<LobbyInfo>());
    }
}
