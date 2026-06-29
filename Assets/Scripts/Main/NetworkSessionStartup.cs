using System;
using System.Threading;
using Common.GameSession;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using VContainer.Unity;

namespace Main
{
    public class NetworkSessionStartup : IAsyncStartable, IDisposable
    {
        private readonly GameSessionModel _gameSessionModel;
        private readonly NetworkModel _networkModel;

        public NetworkSessionStartup(GameSessionModel gameSessionModel, NetworkModel networkModel)
        {
            _gameSessionModel = gameSessionModel;
            _networkModel = networkModel;
        }

        public async UniTask StartAsync(CancellationToken ct)
        {
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

            _networkModel.State.Value = NetworkState.Connected;
        }

        public void Dispose()
        {
            NetworkManager.Singleton?.Shutdown();
        }
    }
}
