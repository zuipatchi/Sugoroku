using System.Collections;
using Common.Option;
using Common.SoundManagement;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    public class SoundPlayerTests
    {
        private OptionModel _optionModel;
        private SoundPlayer _soundPlayer;
        private GameObject _go;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _optionModel = new OptionModel();
            _optionModel.Start();

            _go = new GameObject("SoundPlayer");
            _soundPlayer = _go.AddComponent<SoundPlayer>();
            _soundPlayer.Construct(_optionModel);

            yield return null; // SoundPlayer.Start() を実行させる
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(_go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BGM音量変更がAudioSourceのvolumeに反映される()
        {
            _optionModel.SetBGMVolume(0.8f);
            yield return null;

            // loop == true の AudioSource が BGM
            AudioSource[] sources = _go.GetComponents<AudioSource>();
            AudioSource bgmSource = System.Array.Find(sources, s => s.loop);
            Assert.IsNotNull(bgmSource, "BGM AudioSource が見つかりません");
            Assert.AreEqual(0.4f, bgmSource.volume, 0.001f);
        }

        [UnityTest]
        public IEnumerator SE音量変更がAudioSourceのvolumeに反映される()
        {
            _optionModel.SetSEVolume(0.6f);
            yield return null;

            // loop == false の AudioSource が SE
            AudioSource[] sources = _go.GetComponents<AudioSource>();
            AudioSource seSource = System.Array.Find(sources, s => !s.loop);
            Assert.IsNotNull(seSource, "SE AudioSource が見つかりません");
            Assert.AreEqual(0.3f, seSource.volume, 0.001f);
        }

        [UnityTest]
        public IEnumerator PlayBGMが再生を開始する()
        {
            AudioClip clip = AudioClip.Create("TestBGM", 44100, 1, 44100, false);
            _soundPlayer.PlayBGM(clip);
            yield return null;

            AudioSource[] sources = _go.GetComponents<AudioSource>();
            AudioSource bgmSource = System.Array.Find(sources, s => s.loop);
            Assert.IsNotNull(bgmSource, "BGM AudioSource が見つかりません");
            Assert.IsTrue(bgmSource.isPlaying, "BGMが再生されていません");
        }
    }
}
