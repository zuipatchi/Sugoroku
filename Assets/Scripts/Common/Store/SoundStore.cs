using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Common.Store
{
    public class SoundStore : AssetStoreBase
    {
        private readonly string _mainBgmAddressable = "Sound/BGM/CatInPalmBeach";
        private readonly string _titleBGMAddressable = "Sound/BGM/Title";
        private readonly string _cancel1SEAddressable = "Sound/SE/Cancel1";
        private readonly string _enter1SEAddressable = "Sound/SE/Enter1";
        private readonly string _enter2SEAddressable = "Sound/SE/Enter2";
        private readonly string _enter3SEAddressable = "Sound/SE/Enter3";
        private readonly string _decisionSEAddressable = "Sound/SE/Decision";
        private readonly string _rouletteSEAddressable = "Sound/SE/Roulette";
        private readonly string _cheerSEAddressable = "Sound/SE/Cheer";

        public AudioClip MainBGM => _mainBGM;
        public AudioClip TitleBGM => _titleBGM;
        public AudioClip Cancel1SE => _cancel1SE;
        public AudioClip Enter1SE => _enter1SE;
        public AudioClip Enter2SE => _enter2SE;
        public AudioClip Enter3SE => _enter3SE;
        public AudioClip DecisionSE => _decisionSE;

        /// <summary>ルーレット回転中、セクター境界を通過するたびに鳴らすティック SE。</summary>
        public AudioClip RouletteSE => _rouletteSE;

        /// <summary>タイトルで動画と同時に鳴らす歓声 SE。鳴り終わってからタイトル BGM（光晴イズム）へ移る。</summary>
        public AudioClip CheerSE => _cheerSE;

        private AudioClip _mainBGM = null;
        private AudioClip _titleBGM = null;
        private AudioClip _cancel1SE = null;
        private AudioClip _enter1SE = null;
        private AudioClip _enter2SE = null;
        private AudioClip _enter3SE = null;
        private AudioClip _decisionSE = null;
        private AudioClip _rouletteSE = null;
        private AudioClip _cheerSE = null;

        protected override string AssetCategory => "サウンド";

        protected override async UniTask LoadAssetsCore()
        {
            _mainBGM = await Addressables.LoadAssetAsync<AudioClip>(_mainBgmAddressable).ToUniTask();
            _titleBGM = await Addressables.LoadAssetAsync<AudioClip>(_titleBGMAddressable).ToUniTask();
            _cancel1SE = await Addressables.LoadAssetAsync<AudioClip>(_cancel1SEAddressable).ToUniTask();
            _enter1SE = await Addressables.LoadAssetAsync<AudioClip>(_enter1SEAddressable).ToUniTask();
            _enter2SE = await Addressables.LoadAssetAsync<AudioClip>(_enter2SEAddressable).ToUniTask();
            _enter3SE = await Addressables.LoadAssetAsync<AudioClip>(_enter3SEAddressable).ToUniTask();
            _decisionSE = await Addressables.LoadAssetAsync<AudioClip>(_decisionSEAddressable).ToUniTask();
            _rouletteSE = await Addressables.LoadAssetAsync<AudioClip>(_rouletteSEAddressable).ToUniTask();
            _cheerSE = await Addressables.LoadAssetAsync<AudioClip>(_cheerSEAddressable).ToUniTask();
        }
    }
}
