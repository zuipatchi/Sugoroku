using System.Threading;
using Common.GameSession;
using Cysharp.Threading.Tasks;
using Main.Online;
using Unity.Netcode;
using VContainer.Unity;

namespace Main
{
    /// <summary>
    /// Main シーンでネットワークの準備が整うのを待ち、整ったら <see cref="NetworkModel"/> を
    /// <see cref="NetworkState.Connected"/> にして進行（<see cref="Turn.GameFlowController"/>）を始めさせる。
    ///
    /// NGO の起動・停止は行わない。オンラインでは UGS のセッションが Relay の割り当てと一緒に
    /// <c>StartHost</c> / <c>StartClient</c> を済ませており（`SessionOptions.WithRelayNetwork()`）、
    /// 停止も <c>ISession.LeaveAsync</c> が担う。ここで起動・停止するとその管理下の接続を壊す。
    /// 一人用モードは NGO を使わないので即 <see cref="NetworkState.Connected"/> にする。
    /// </summary>
    public class NetworkSessionStartup : IAsyncStartable
    {
        private readonly GameSessionModel _gameSessionModel;
        private readonly NetworkModel _networkModel;
        private readonly OnlineGameSync _sync;

        public NetworkSessionStartup(
            GameSessionModel gameSessionModel, NetworkModel networkModel, OnlineGameSync sync)
        {
            _gameSessionModel = gameSessionModel;
            _networkModel = networkModel;
            _sync = sync;
        }

        public async UniTask StartAsync(CancellationToken ct)
        {
            // 一人用モードでは NGO を使わないので、即接続済み扱いにする。
            if (_gameSessionModel.Mode == GameMode.SinglePlayer)
            {
                _networkModel.State.Value = NetworkState.Connected;
                return;
            }

            while (NetworkManager.Singleton == null)
            {
                await UniTask.NextFrame(cancellationToken: ct);
            }

            NetworkManager nm = NetworkManager.Singleton;
            bool isHost = _gameSessionModel.IsHost;

            // 接続は Matching シーンでのセッション作成/参加時に確立済みだが、Main へ来た時点で
            // 完全に整っているとは限らない（networking.md 4）。ホストは IsListening、
            // クライアントは IsConnectedClient で確認する（条件が異なることに注意）。
            while (nm.CustomMessagingManager == null
                   || (isHost ? !nm.IsListening : !nm.IsConnectedClient))
            {
                await UniTask.NextFrame(cancellationToken: ct);
            }

            // 進行の開始（Connected 通知）より前にハンドラを永続登録しておく。
            // これで最初のアクションから取りこぼさない（networking.md 8）。
            _sync.OnConnected();

            _networkModel.State.Value = NetworkState.Connected;
        }
    }
}
