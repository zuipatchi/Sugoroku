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

        /// <summary>
        /// 対戦相手をこのクライアントでシミュレートするか。一人用モードは true で、各ゲームが CPU を
        /// 自動連打・自走・自動選択させる。**オンライン対戦では false** で、相手は実プレイヤーなので
        /// シミュレートせず、勝敗は各自の結果値（<see cref="MiniGameResult.Value"/>）を持ち寄って決める。
        /// </summary>
        public bool SimulateOpponents { get; private set; } = true;

        /// <summary>
        /// ゲームの内容をランダムに組み立てるときの種。**オンラインでは全クライアントで同じ値**にして、
        /// 提示されるものが食い違わないようにする（被っちゃやーよのカード構成など）。
        /// 0 のときは各ゲームがその場で乱数を引く（一人用・MiniGameTest）。
        /// </summary>
        public int Seed { get; private set; }

        /// <summary>
        /// プレイ中の途中経過（連打数など）を配り合う経路。オンライン対戦で起動側が差し込む。
        /// null のとき（一人用・MiniGameTest）は相手をシミュレートしているので不要。
        /// </summary>
        public MiniGameProgressChannel Progress { get; private set; }

        /// <summary>起動側が呼ぶ。遊ぶゲーム・参加者数・参加者キャラを設定し、結果待ちを初期化する。</summary>
        public void Begin(
            MiniGameId game,
            int playerCount,
            IReadOnlyList<CharacterId> characters,
            bool simulateOpponents = true,
            int seed = 0,
            MiniGameProgressChannel progress = null)
        {
            CurrentGame = game;
            PlayerCount = playerCount;
            Characters = characters ?? EmptyCharacters;
            SimulateOpponents = simulateOpponents;
            Seed = seed;
            Progress = progress;
            _resultSource = new UniTaskCompletionSource<MiniGameResult>();
        }

        /// <summary>
        /// ホストが呼ぶ。勝敗（<paramref name="score"/>）と生の結果値（<paramref name="value"/>）を
        /// 確定して結果待ちを完了させる。値を省略した場合は勝敗をそのまま結果値として扱う。
        /// </summary>
        public void Report(int score, int value)
        {
            _resultSource?.TrySetResult(new MiniGameResult(CurrentGame, score, value));
        }

        /// <summary>ホストが呼ぶ。勝敗だけを返す互換版（結果値は勝敗と同じ）。</summary>
        public void Report(int score)
        {
            Report(score, score);
        }

        /// <summary>
        /// ゲームの組み立てに使う種。オンラインで共有された種（<see cref="Seed"/>）があればそれを、
        /// 無ければその場で引いた乱数を返す。各ゲームの準備はこれを通して種を取る。
        /// </summary>
        public int ResolveSeed()
        {
            return Seed != 0 ? Seed : UnityEngine.Random.Range(int.MinValue, int.MaxValue);
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
