using System;
using System.Collections.Generic;
using R3;

namespace Matching
{
    public class MatchingModel
    {
        public ReactiveProperty<MatchingState> State { get; } = new(MatchingState.Idle);
        public ReactiveProperty<IReadOnlyList<LobbyInfo>> Rooms { get; } = new(Array.Empty<LobbyInfo>());

        /// <summary>作成したルームの現在の参加人数（自分含む）。相手待ち中の「◯/◯人」表示に使う。</summary>
        public ReactiveProperty<int> WaitingCurrent { get; } = new(0);

        /// <summary>作成したルームの定員（ホストが選んだ人数）。相手待ち中の「◯/◯人」表示に使う。</summary>
        public ReactiveProperty<int> WaitingMax { get; } = new(0);
    }
}
