using Common.Option;
using R3;
using UnityEngine;
using VContainer;

namespace Common.SoundManagement
{
    /// <summary>
    /// AudioClip を再生するクラス
    /// OptionModel の volume で音量を管理している
    /// </summary>
    public class SoundPlayer : MonoBehaviour
    {
        private AudioSource _bgmAudioSource;
        private AudioSource _seAudioSource;
        private AudioSource _seLoopAudioSource;
        private OptionModel _optionModel;
        private readonly CompositeDisposable _disposables = new();

        [Inject]
        public void Construct(OptionModel optionModel)
        {
            _optionModel = optionModel;
        }

        private void Start()
        {
            _bgmAudioSource = gameObject.AddComponent<AudioSource>();
            _bgmAudioSource.loop = true;

            _optionModel.BGMVolume
                .Subscribe(v => _bgmAudioSource.volume = v / 2)
                .AddTo(_disposables);

            _seAudioSource = gameObject.AddComponent<AudioSource>();
            _seAudioSource.playOnAwake = false;
            _seAudioSource.loop = false;

            _seLoopAudioSource = gameObject.AddComponent<AudioSource>();
            _seLoopAudioSource.playOnAwake = false;
            _seLoopAudioSource.loop = true;

            _optionModel.SEVolume
                .Subscribe(v =>
                {
                    _seAudioSource.volume = v / 2;
                    _seLoopAudioSource.volume = v / 2;
                })
                .AddTo(_disposables);
        }

        private void OnDestroy()
        {
            _disposables.Dispose();
        }

        public void PlayBGM(AudioClip clip)
        {
            _bgmAudioSource.clip = clip;
            _bgmAudioSource.Play();
        }

        public void PlaySE(AudioClip clip)
        {
            _seAudioSource.PlayOneShot(clip);
        }

        /// <summary>
        /// ループ SE の再生を開始する（コマ移動中の走行音など）。<see cref="StopSELoop"/> で止める。
        /// 同じクリップが既に鳴っている場合は鳴らし直さない。
        /// </summary>
        public void PlaySELoop(AudioClip clip)
        {
            if (clip == null)
            {
                return;
            }

            if (_seLoopAudioSource.isPlaying && _seLoopAudioSource.clip == clip)
            {
                return;
            }

            _seLoopAudioSource.clip = clip;
            _seLoopAudioSource.Play();
        }

        /// <summary>ループ SE を停止する。</summary>
        public void StopSELoop()
        {
            _seLoopAudioSource.Stop();
            _seLoopAudioSource.clip = null;
        }
    }
}
