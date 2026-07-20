using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Common.MiniGame
{
    /// <summary>
    /// ミニゲームの起動側（<see cref="MiniGameLauncher"/>）とミニゲームシーンのホストを仲介する Model。
    /// 起動側が <see cref="Begin"/> で遊ぶゲームを設定し、ホストが <see cref="Report"/> で結果を返す。
    /// </summary>
    public sealed class MiniGameSessionModel
    {
        private UniTaskCompletionSource<MiniGameResult> _resultSource;

        /// <summary>現在プレイ中のミニゲーム。ホストはこれを見て中身を切り替える。</summary>
        public MiniGameId CurrentGame { get; private set; }

        /// <summary>
        /// 現在のミニゲームの参加者数（人間＋CPU）。人数を使うゲーム（被っちゃやーよ）が参照する。
        /// MiniGame シーンは別スコープで <c>GameParticipants</c> を直接注入できないため、起動側が
        /// <see cref="Begin"/> でここへ渡す。
        /// </summary>
        public int PlayerCount { get; private set; }

        /// <summary>起動側が呼ぶ。遊ぶゲームと参加者数を設定し、結果待ちを初期化する。</summary>
        public void Begin(MiniGameId game, int playerCount)
        {
            CurrentGame = game;
            PlayerCount = playerCount;
            _resultSource = new UniTaskCompletionSource<MiniGameResult>();
        }

        /// <summary>ホストが呼ぶ。スコアを確定して結果待ちを完了させる。</summary>
        public void Report(int score)
        {
            _resultSource?.TrySetResult(new MiniGameResult(CurrentGame, score));
        }

        /// <summary>起動側が呼ぶ。ホストの <see cref="Report"/> を待って結果を返す。</summary>
        public UniTask<MiniGameResult> WaitResultAsync(CancellationToken ct)
        {
            if (_resultSource == null)
            {
                throw new InvalidOperationException("Begin が呼ばれていません。");
            }
            return _resultSource.Task.AttachExternalCancellation(ct);
        }
    }
}
