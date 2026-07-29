using System;
using System.Threading;
using Common.GameSession;
using Cysharp.Threading.Tasks;
using Main.Online;
using Unity.Netcode;
using VContainer.Unity;

namespace Main
{
    public class NetworkSessionStartup : IAsyncStartable, IDisposable
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
            // 一人用モードでは NGO を起動せず、即接続済み扱いにする。
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

            if (isHost)
            {
                nm.StartHost();
            }
            else
            {
                nm.StartClient();
            }

            // networking.md issue 4: JoinSession 直後は CustomMessagingManager が null のケースがある
            while (nm.CustomMessagingManager == null
                   || (isHost ? !nm.IsListening : !nm.IsConnectedClient))
            {
                await UniTask.NextFrame(cancellationToken: ct);
            }

            // 進行の開始（Connected 通知）より前にハンドラを永続登録しておく。
            // これで最初のアクションから取りこぼさない（networking.md issue 8）。
            _sync.OnConnected();

            _networkModel.State.Value = NetworkState.Connected;
        }

        public void Dispose()
        {
            NetworkManager.Singleton?.Shutdown();
        }
    }
}
