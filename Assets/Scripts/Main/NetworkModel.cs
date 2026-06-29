using R3;

namespace Main
{
    public class NetworkModel
    {
        public ReactiveProperty<NetworkState> State { get; } = new(NetworkState.Connecting);
    }
}
