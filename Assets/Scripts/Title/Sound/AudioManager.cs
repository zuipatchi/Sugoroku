using System;
using Common.SoundManagement;
using Common.Store;
using VContainer;
using VContainer.Unity;
using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;

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

                // タイトル動画の再生開始と同時に歓声（Cheer）を鳴らし、鳴り終わってから
                // タイトル BGM（光晴イズム）へ移る。Cheer が無ければそのまま BGM を流す。
                AudioClip cheer = _soundStore.CheerSE;
                if (cheer != null)
                {
                    _soundPlayer.PlaySE(cheer);
                    await UniTask.Delay(TimeSpan.FromSeconds(cheer.length), cancellationToken: cancellation);
                }

                _soundPlayer.PlayBGM(_soundStore.TitleBGM);
            }
            catch (OperationCanceledException) { }
        }
    }
}
