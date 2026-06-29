using System;
using R3;
using UnityEngine;
using VContainer.Unity;

namespace Common.Option
{
    public class OptionModel: IStartable
    {
        private readonly string _bgmVolumeKey = "bgmVolume";
        private readonly string _seVolumeKey = "seVolume";

        private readonly ReactiveProperty<float> _bgmVolume = new();
        public ReadOnlyReactiveProperty<float> BGMVolume => _bgmVolume;

        private readonly ReactiveProperty<float> _seVolume = new();
        public ReadOnlyReactiveProperty<float> SEVolume => _seVolume;

        // セーブデータから読み込み
        public void Start()
        {
            float bgmVolume = PlayerPrefs.GetFloat(_bgmVolumeKey, 0.5f);
            _bgmVolume.Value = bgmVolume;

            float seVolume = PlayerPrefs.GetFloat(_seVolumeKey, 0.5f);
            _seVolume.Value = seVolume;
        }

        public void SetBGMVolume(float value)
        {
            _bgmVolume.Value = Math.Clamp(value, 0, 1);
            PlayerPrefs.SetFloat(_bgmVolumeKey, _bgmVolume.Value);
        }

        public void SetSEVolume(float value)
        {
            _seVolume.Value = Math.Clamp(value, 0, 1);
            PlayerPrefs.SetFloat(_seVolumeKey, _seVolume.Value);
        }
    }
}
