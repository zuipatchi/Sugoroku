using System;
using System.Collections.Generic;
using System.Threading;
using Common.Character;
using Cysharp.Threading.Tasks;

namespace Common.MiniGame
{
    /// <summary>
    /// ミニゲームの起動側（<see cref="MiniGameLauncher"/>）とミニゲームシーンのホストを仲介する Model。
    /// 起動側が <see cref="Begin"/> で遊ぶゲームを設定し、ホストが <see cref="Report"/> で結果を返す。
    /// </summary>
    public sealed class MiniGameSessionModel
    {
        private static readonly IReadOnlyList<CharacterId> EmptyCharacters = new CharacterId[0];

        private UniTaskCompletionSource<MiniGameResult> _resultSource;

        /// <summary>現在プレイ中のミニゲーム。ホストはこれを見て中身を切り替える。</summary>
        public MiniGameId CurrentGame { get; private set; }

        /// <summary>
        /// 現在のミニゲームの参加者数（人間＋CPU）。人数を使うゲーム（被っちゃやーよ・2Dレース）が参照する。
        /// MiniGame シーンは別スコープで <c>GameParticipants</c> を直接注入できないため、起動側が
        /// <see cref="Begin"/> でここへ渡す。
        /// </summary>
        public int PlayerCount { get; private set; }

        /// <summary>
        /// 各参加者（index 0＝プレイヤー、1〜＝CPU）に割り当てたキャラ。ミニゲームは走者・カードの表示や
        /// 名前ラベルにこれを使う（YOU/CPU の代わり）。本番は起動側が実参加者のキャラ、MiniGameTest は
        /// ランダムなキャラを渡す。空のときは各ゲームが従来の解決（選択キャラ／YOU・CPU）へフォールバックする。
        /// </summary>
        public IReadOnlyList<CharacterId> Characters { get; private set; } = EmptyCharacters;

        /// <summary>起動側が呼ぶ。遊ぶゲーム・参加者数・参加者キャラを設定し、結果待ちを初期化する。</summary>
        public void Begin(MiniGameId game, int playerCount, IReadOnlyList<CharacterId> characters)
        {
            CurrentGame = game;
            PlayerCount = playerCount;
            Characters = characters ?? EmptyCharacters;
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
