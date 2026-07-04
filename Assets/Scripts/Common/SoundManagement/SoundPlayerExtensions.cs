using UnityEngine;

namespace Common.SoundManagement
{
    /// <summary>
    /// <see cref="SoundPlayer"/> の拡張メソッド。
    /// </summary>
    public static class SoundPlayerExtensions
    {
        /// <summary>
        /// null ガード付きの SE 再生。プレイヤー（injection 前）・クリップ（未設定）の
        /// どちらかが null なら何もしない。演出コードから安心して呼べるようにする。
        /// </summary>
        public static void PlaySafe(this SoundPlayer player, AudioClip clip)
        {
            if (player != null && clip != null)
            {
                player.PlaySE(clip);
            }
        }
    }
}
