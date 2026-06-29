using System;
using Common.SoundManagement;
using Common.Store;
using VContainer;
using VContainer.Unity;
using Cysharp.Threading.Tasks;
using System.Threading;

namespace Title.Sound
{

    public class AudioManager : IAsyncStartable
    {
        private SoundPlayer _soundPlayer;
        private SoundStore _soundStore;

        [Inject]
        public AudioManager(SoundPlayer soundPlayer, SoundStore soundStore)
        {
            _soundPlayer = soundPlayer;
            _soundStore = soundStore;
        }

        public async UniTask StartAsync(CancellationToken cancellation = default)
        {
            try
            {
                await _soundStore.Loaded.AttachExternalCancellation(cancellation);
                _soundPlayer.PlayBGM(_soundStore.TitleBGM);
            }
            catch (OperationCanceledException) { }
        }
    }
}
