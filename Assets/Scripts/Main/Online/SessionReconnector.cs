using System;
using System.Threading;
using Common.GameSession;
using Cysharp.Threading.Tasks;

namespace Main.Online
{
    /// <summary>
    /// 対戦中に接続が切れたクライアントを、同じセッションへ入り直させる手順。
    /// 「入り直す」こと自体は <see cref="GameSessionModel.ReconnectAsync"/>（UGS SDK）が
    /// Lobby の再参加と Relay / NGO の張り直しまで面倒を見るので、ここが持つのは
    /// **いつまで・どの間隔で試すか**と、**送受信できる状態まで待ち切ること**だけ。
    ///
    /// 猶予（<see cref="GraceSeconds"/>）を過ぎても戻れなければ諦める。ホスト側も同じ猶予で
    /// 待っているので、諦めるころには相手の画面でも対戦が終了している。
    ///
    /// 対象は「アプリは生きたまま通信だけ切れた」ケース（モバイルのバックグラウンド化・Wi-Fi の瞬断・
    /// Relay の不調）。アプリを落とした場合はローカルの盤面が消えるので復帰できない。
    /// ホストが切れた場合は Relay の割り当てごと消えるため、ここでの再接続もたいてい失敗する
    /// （その場合は猶予切れと同じ扱いで対戦を終える）。
    /// </summary>
    public sealed class SessionReconnector
    {
        /// <summary>復帰を待つ猶予（秒）。この時間を過ぎたら対戦を終了する。</summary>
        public const int GraceSeconds = 60;

        // 再試行の間隔。失敗するたびに倍にして上限で頭打ちにする（復旧直後は素早く、駄目なら控えめに）。
        private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(8);

        private readonly GameSessionModel _gameSession;

        public SessionReconnector(GameSessionModel gameSession)
        {
            _gameSession = gameSession;
        }

        /// <summary>
        /// 猶予いっぱいまで再接続を試みる。送受信できる状態まで戻れたら true、
        /// 猶予切れ・ルーム消滅なら false。<paramref name="ct"/> のキャンセル（シーン破棄）は伝播する。
        /// </summary>
        public async UniTask<bool> RunAsync(CancellationToken ct)
        {
            using CancellationTokenSource graceCts = new(TimeSpan.FromSeconds(GraceSeconds));
            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(ct, graceCts.Token);

            TimeSpan delay = FirstRetryDelay;
            try
            {
                while (true)
                {
                    linked.Token.ThrowIfCancellationRequested();

                    if (await TryOnceAsync(linked.Token))
                    {
                        return true;
                    }

                    await UniTask.Delay(delay, cancellationToken: linked.Token);
                    TimeSpan doubled = delay + delay;
                    delay = doubled > MaxRetryDelay ? MaxRetryDelay : doubled;
                }
            }
            catch (OperationCanceledException)
            {
                // シーン破棄によるキャンセルは呼び出し元（＝進行の打ち切り）へ伝える。
                ct.ThrowIfCancellationRequested();
                // 猶予切れ。復帰できなかったことだけ返す。
                return false;
            }
        }

        // 1 回ぶんの試行。セッションへ入り直し、実際にメッセージを送受信できるところまで確認する。
        private async UniTask<bool> TryOnceAsync(CancellationToken ct)
        {
            if (!await _gameSession.ReconnectAsync())
            {
                return false;
            }

            // UGS の await 後は非メインスレッドになり得る。NetworkManager を触る前に戻す。
            await UniTask.SwitchToMainThread(ct);

            // セッションには入れても NGO が整うとは限らない。整わないまま猶予が切れたら
            // ここのキャンセルで抜けて、RunAsync が「復帰できなかった」として扱う。
            await NgoReadiness.WaitUntilReadyAsync(_gameSession.IsHost, ct);
            return true;
        }
    }
}
