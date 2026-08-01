using System;
using R3;
using UnityEngine;

namespace MiniGame.RaceGame
{
    /// <summary>
    /// タイミングメーター式 2D レースの状態。走者はプレイヤー（走者 index 0）＋ CPU（1〜N-1）で、全員が
    /// 進捗 0（右端スタート）→ 1（左端ゴール）を競う。全走者はベース速度でゆっくり進み、プレイヤーはメーターを
    /// 止めた判定（Great/Good）で前進を上乗せする。各 CPU は独立したタイマーでランダムに Great/Good/Miss を
    /// 抽選して前進する。時間の進行は Presenter が <see cref="Tick"/>／<see cref="ApplyTap"/> で駆動し、ここは
    /// フェーズ・進捗・勝敗だけを持つ純粋ロジック。CPU の抽選は <see cref="System.Random"/> で決定的。
    /// </summary>
    public sealed class RaceGameModel : IDisposable
    {
        // レースは最低 2 走者（プレイヤー＋CPU1体）で成立させる。
        private const int MinRunnerCount = 2;

        private readonly ReactiveProperty<RaceGamePhase> _phase = new(RaceGamePhase.Ready);

        private RaceGameConfig _config = RaceGameConfig.Default;
        // CPU をこのクライアントで自走させるか（オンライン対戦では相手が実プレイヤーなので false）。
        private bool _simulateOpponents = true;
        private System.Random _random;
        // 走者ごとの進捗（index 0 = プレイヤー、1〜 = CPU）。
        private float[] _progress = new float[MinRunnerCount];
        // 走者ごとの次抽選までの残り秒（プレイヤー枠 index 0 は未使用）。
        private float[] _cpuTapTimer = new float[MinRunnerCount];
        private int _runnerCount = MinRunnerCount;

        /// <summary>現在のフェーズ。</summary>
        public ReadOnlyReactiveProperty<RaceGamePhase> Phase => _phase;

        /// <summary>走者の総数（プレイヤー＋CPU）。</summary>
        public int RunnerCount => _runnerCount;

        /// <summary>走者 <paramref name="runner"/>（0=プレイヤー）の進捗（0=スタート／1=ゴール）。範囲外は 0。</summary>
        public float Progress(int runner)
        {
            return runner >= 0 && runner < _runnerCount ? _progress[runner] : 0f;
        }

        /// <summary>プレイヤー（走者 index 0）の進捗。</summary>
        public float PlayerProgress => Progress(0);

        /// <summary>先頭 CPU（走者 index 1）の進捗。2 人レースの相手にあたる。</summary>
        public float CpuProgress => Progress(1);

        /// <summary>勝者の走者 index（0=プレイヤー）。まだ決着していなければ null。</summary>
        public int? WinnerIndex { get; private set; }

        /// <summary>プレイヤー（走者 index 0）が勝ったか。決着前は false。</summary>
        public bool IsPlayerWin => WinnerIndex == 0;

        /// <summary>レースを準備する（2 走者＝既定コンフィグ）。進捗を 0 に戻しフェーズを <see cref="RaceGamePhase.Ready"/> に。</summary>
        public void Setup(int seed)
        {
            Setup(RaceGameConfig.Default, seed);
        }

        /// <summary><see cref="Setup(int)"/> のコンフィグ指定版（2 走者）。</summary>
        public void Setup(RaceGameConfig config, int seed)
        {
            Setup(config, MinRunnerCount, seed);
        }

        /// <summary>
        /// <paramref name="playerCount"/> 人（プレイヤー 1 ＋ CPU <c>playerCount-1</c> 体）でレースを準備する。
        /// 進捗を 0 に戻し、各 CPU の抽選タイマーを初期化してフェーズを <see cref="RaceGamePhase.Ready"/> にする。
        /// </summary>
        public void Setup(RaceGameConfig config, int playerCount, int seed)
        {
            Setup(config, playerCount, seed, true);
        }

        /// <summary>
        /// <paramref name="simulateOpponents"/> が false のときは CPU を自走させない
        /// （オンライン対戦では相手が実プレイヤーで、ゴールタイムは各自から持ち寄るため）。
        /// </summary>
        public void Setup(RaceGameConfig config, int playerCount, int seed, bool simulateOpponents)
        {
            _config = config;
            _simulateOpponents = simulateOpponents;
            _random = new System.Random(seed);
            _runnerCount = Mathf.Max(MinRunnerCount, playerCount);
            _progress = new float[_runnerCount];
            _cpuTapTimer = new float[_runnerCount];
            for (int runner = 1; runner < _runnerCount; runner++)
            {
                _cpuTapTimer[runner] = NextCpuInterval();
            }
            WinnerIndex = null;
            _phase.Value = RaceGamePhase.Ready;
        }

        /// <summary>カウントダウンを開始する（Ready からのみ）。</summary>
        public void BeginCountdown()
        {
            if (_phase.Value == RaceGamePhase.Ready)
            {
                _phase.Value = RaceGamePhase.Countdown;
            }
        }

        /// <summary>レースを開始する（Countdown からのみ）。</summary>
        public void StartRacing()
        {
            if (_phase.Value == RaceGamePhase.Countdown)
            {
                _phase.Value = RaceGamePhase.Racing;
            }
        }

        /// <summary>メーター値（0〜1）を中央 0.5 からの距離で判定する。</summary>
        public MeterJudgement Judge(float meterValue)
        {
            float distance = Mathf.Abs(meterValue - 0.5f);
            if (distance <= _config.GreatHalfWidth)
            {
                return MeterJudgement.Great;
            }
            if (distance <= _config.GoodHalfWidth)
            {
                return MeterJudgement.Good;
            }
            return MeterJudgement.Miss;
        }

        /// <summary>
        /// メーターを止める。判定に応じてプレイヤー（走者 index 0）を前進させ、ゴールしたら決着させる。判定を返す。
        /// レース中以外は何もせず <see cref="MeterJudgement.Miss"/> を返す。
        /// </summary>
        public MeterJudgement ApplyTap(float meterValue)
        {
            if (_phase.Value != RaceGamePhase.Racing)
            {
                return MeterJudgement.Miss;
            }

            MeterJudgement judgement = Judge(meterValue);
            _progress[0] += BoostFor(judgement);
            ResolveFinish();
            return judgement;
        }

        /// <summary>
        /// 時間を <paramref name="deltaSeconds"/> 進める。全走者を同じベース速度で前進させ、各 CPU は独立した
        /// タイマーでランダムに Great/Good/Miss を抽選して（プレイヤーのタップと同じ量で）前進する。
        /// ゴールしたら決着させる。レース中以外は何もしない。
        /// </summary>
        public void Tick(float deltaSeconds)
        {
            if (_phase.Value != RaceGamePhase.Racing || deltaSeconds <= 0f)
            {
                return;
            }

            // オンライン対戦では自分以外の走者は動かさない（進捗は持ち寄らず、ゴールタイムだけで順位を決める）。
            int advanceCount = _simulateOpponents ? _runnerCount : 1;
            for (int runner = 0; runner < advanceCount; runner++)
            {
                _progress[runner] += _config.BaseSpeed * deltaSeconds;
            }

            for (int runner = 1; runner < advanceCount; runner++)
            {
                _cpuTapTimer[runner] -= deltaSeconds;
                while (_cpuTapTimer[runner] <= 0f)
                {
                    _progress[runner] += BoostFor(NextCpuJudgement());
                    _cpuTapTimer[runner] += NextCpuInterval();
                }
            }

            ResolveFinish();
        }

        // 判定に応じた前進量。プレイヤーのタップと CPU の抽選で共用する。
        private float BoostFor(MeterJudgement judgement)
        {
            return judgement switch
            {
                MeterJudgement.Great => _config.GreatBoost,
                MeterJudgement.Good => _config.GoodBoost,
                _ => 0f
            };
        }

        // CPU の 1 回の抽選。Great は低確率、次いで Good、残りは Miss。
        private MeterJudgement NextCpuJudgement()
        {
            double roll = _random.NextDouble();
            if (roll < _config.CpuGreatChance)
            {
                return MeterJudgement.Great;
            }
            if (roll < _config.CpuGreatChance + _config.CpuGoodChance)
            {
                return MeterJudgement.Good;
            }
            return MeterJudgement.Miss;
        }

        // 次の CPU 抽選までの秒数（無限ループ防止に下限を設ける）。
        private float NextCpuInterval()
        {
            float min = _config.CpuTapIntervalMin;
            float max = Mathf.Max(_config.CpuTapIntervalMax, min);
            float interval = min + (float)_random.NextDouble() * (max - min);
            return Mathf.Max(0.01f, interval);
        }

        public void Dispose()
        {
            _phase.Dispose();
        }

        // いずれかの走者がゴール（進捗 >= 1）したら進捗をクランプし勝者を確定する。
        // 複数が同時到達したらより先行している走者を勝ちとし、同値なら若い index（プレイヤー優先）を勝ちにする。
        private void ResolveFinish()
        {
            if (WinnerIndex.HasValue)
            {
                return;
            }

            int winner = -1;
            float winnerProgress = 0f;
            for (int runner = 0; runner < _runnerCount; runner++)
            {
                if (_progress[runner] < 1f)
                {
                    continue;
                }
                // 同値なら先に見つかった若い index を勝ちに残す（> で厳密比較）。
                if (winner < 0 || _progress[runner] > winnerProgress)
                {
                    winner = runner;
                    winnerProgress = _progress[runner];
                }
            }

            if (winner < 0)
            {
                return;
            }

            for (int runner = 0; runner < _runnerCount; runner++)
            {
                _progress[runner] = Mathf.Clamp01(_progress[runner]);
            }
            WinnerIndex = winner;
            _phase.Value = RaceGamePhase.Finished;
        }
    }
}
