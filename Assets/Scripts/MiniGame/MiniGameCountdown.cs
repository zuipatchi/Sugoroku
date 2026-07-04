using System.Threading;
using Common.SoundManagement;
using Common.Store;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace MiniGame
{
    /// <summary>
    /// ミニゲーム共通のカウントダウン演出。「3→2→1」を SE 付きで刻み、
    /// 「スタート！」を一瞬見せてから戻る。ラベルの表示/非表示や Model のフェーズ遷移は呼び出し元が担う。
    /// </summary>
    public static class MiniGameCountdown
    {
        /// <summary>カウントダウン 1 刻みの表示時間（ms）。</summary>
        public const int CountdownStepMs = 700;
        /// <summary>「スタート！」の表示時間（ms）。</summary>
        public const int StartFlashMs = 500;

        /// <summary>「3→2→1→スタート！」を <paramref name="label"/> に流す。</summary>
        public static async UniTask RunAsync(Label label, SoundStore soundStore, SoundPlayer soundPlayer, CancellationToken ct)
        {
            for (int n = 3; n >= 1; n--)
            {
                label.text = n.ToString();
                soundPlayer.PlaySafe(soundStore?.Enter1SE);
                await UniTask.Delay(CountdownStepMs, cancellationToken: ct);
            }

            label.text = "スタート！";
            soundPlayer.PlaySafe(soundStore?.Enter2SE);
            await UniTask.Delay(StartFlashMs, cancellationToken: ct);
        }
    }
}
