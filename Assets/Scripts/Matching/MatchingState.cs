namespace Matching
{
    public enum MatchingState
    {
        Idle,
        Authenticating,
        BrowsingRooms,
        CreatingRoom,
        JoiningRoom,
        WaitingForPlayer,
        WaitingInCreatedRoom,
        Starting,
        Ready,
        TimedOut,
        Error
    }

    public static class MatchingStateExtensions
    {
        public static bool IsLoading(this MatchingState state)
        {
            return state is MatchingState.Authenticating
                or MatchingState.CreatingRoom
                or MatchingState.JoiningRoom
                or MatchingState.Starting;
        }

        public static bool IsWaiting(this MatchingState state)
        {
            return state is MatchingState.WaitingForPlayer
                or MatchingState.WaitingInCreatedRoom
                or MatchingState.TimedOut;
        }
    }
}
