using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Netcode;

namespace Main
{
    /// <summary>
    /// NGO の接続が「メッセージを送受信できる状態」になるまで待つ共通処理。
    ///
    /// 接続の確立そのものは UGS セッションが Relay の割り当てと一緒に済ませる
    /// （<c>SessionOptions.WithRelayNetwork()</c>）ので、ここでは**起動せずに待つだけ**。
    /// シーンに入った直後（<see cref="NetworkSessionStartup"/>）と、切断から復帰した直後
    /// （<see cref="Online.SessionReconnector"/>）の両方で同じ条件を使う。
    /// </summary>
    public static class NgoReadiness
    {
        /// <summary>
        /// 送受信できるようになるまで待つ。ホストとゲストで確認する項目が違う点に注意
        /// （ホストは待ち受け開始＝<c>IsListening</c>、ゲストは接続完了＝<c>IsConnectedClient</c>）。
        /// </summary>
        public static async UniTask WaitUntilReadyAsync(bool isHost, CancellationToken ct)
        {
            while (NetworkManager.Singleton == null)
            {
                await UniTask.NextFrame(cancellationToken: ct);
            }

            NetworkManager nm = NetworkManager.Singleton;
            while (nm.CustomMessagingManager == null
                   || (isHost ? !nm.IsListening : !nm.IsConnectedClient))
            {
                await UniTask.NextFrame(cancellationToken: ct);
            }
        }
    }
}
