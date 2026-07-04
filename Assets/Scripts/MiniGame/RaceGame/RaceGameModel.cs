using System;
using R3;
using UnityEngine;

namespace MiniGame.RaceGame
{
    /// <summary>
    /// タイミングメーター式 2D レースの状態。プレイヤーと CPU が進捗 0（右端スタート）→ 1（左端ゴール）を
    /// 競う。全走者はベース速度でゆっくり進み、プレイヤーはメーターを止めた判定（Great/Good）で前進を上乗せする。
    /// CPU はプレイヤーと同じベース速度で進み、ランダムな間隔で Great/Good/Miss を抽選して前進する。
    /// 時間の進行は Presenter が <see cref="Tick"/>／<see cref="ApplyTap"/> で駆動し、ここはフェーズ・進捗・
    /// 勝敗だけを持つ純粋ロジック。CPU の抽選は <see cref="System.Random"/> で決定的。
    /// </summary>
    public sealed class RaceGameModel : IDisposable
    {
        private readonly ReactiveProperty<RaceGamePhase> _phase = new(RaceGamePhase.Ready);

        private RaceGameConfig _config = RaceGameConfig.Default;
        private System.Random _random;
        private float _playerProgress;
        private float _cpuProgress;
        private float _cpuTapTimer;

        /// <summary>現在のフェーズ。</summary>
        public ReadOnlyReactiveProperty<RaceGamePhase> Phase => _phase;

        /// <summary>プレイヤーの進捗（0=スタート／1=ゴール）。</summary>
        public float PlayerProgress => _playerProgress;

        /// <summary>CPU の進捗（0=スタート／1=ゴール）。</summary>
        public float CpuProgress => _cpuProgress;

        /// <summary>勝者。まだ決着していなければ null。</summary>
        public RaceRunner? Winner { get; private set; }

        /// <summary>プレイヤーが勝ったか。決着前は false。</summary>
        public bool IsPlayerWin => Winner == RaceRunner.Player;

        /// <summary>レースを準備する。進捗を 0 に戻しフェーズを <see cref="RaceGamePhase.Ready"/> にする。</summary>
        public void Setup(int seed)
        {
            Setup(RaceGameConfig.Default, seed);
        }

        /// <summary><see cref="Setup(int)"/> のパラメータ指定版。</summary>
        public void Setup(RaceGameConfig config, int seed)
        {
            _config = config;
            _random = new System.Random(seed);
            _playerProgress = 0f;
            _cpuProgress = 0f;
            _cpuTapTimer = NextCpuInterval();
            Winner = null;
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
        /// メーターを止める。判定に応じてプレイヤーを前進させ、ゴールしたら決着させる。判定を返す。
        /// レース中以外は何もせず <see cref="MeterJudgement.Miss"/> を返す。
        /// </summary>
        public MeterJudgement ApplyTap(float meterValue)
        {
            if (_phase.Value != RaceGamePhase.Racing)
            {
                return MeterJudgement.Miss;
            }

            MeterJudgement judgement = Judge(meterValue);
            _playerProgress += BoostFor(judgement);
            ResolveFinish();
            return judgement;
        }

        /// <summary>
        /// 時間を <paramref name="deltaSeconds"/> 進める。プレイヤーと CPU を同じベース速度で前進させ、CPU は
        /// ランダムな間隔で Great/Good/Miss を抽選して（プレイヤーのタップと同じ量で）前進する。
        /// ゴールしたら決着させる。レース中以外は何もしない。
        /// </summary>
        public void Tick(float deltaSeconds)
        {
            if (_phase.Value != RaceGamePhase.Racing || deltaSeconds <= 0f)
            {
                return;
            }

            _playerProgress += _config.BaseSpeed * deltaSeconds;
            _cpuProgress += _config.BaseSpeed * deltaSeconds;

            _cpuTapTimer -= deltaSeconds;
            while (_cpuTapTimer <= 0f)
            {
                _cpuProgress += BoostFor(NextCpuJudgement());
                _cpuTapTimer += NextCpuInterval();
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

        // どちらかがゴール（進捗 >= 1）したら進捗をクランプし勝者を確定する。
        // 同時到達はより先行している側（同値ならプレイヤー）を勝ちとする。
        private void ResolveFinish()
        {
            if (Winner.HasValue)
            {
                return;
            }

            bool playerDone = _playerProgress >= 1f;
            bool cpuDone = _cpuProgress >= 1f;
            if (!playerDone && !cpuDone)
            {
                return;
            }

            RaceRunner winner;
            if (playerDone && cpuDone)
            {
                winner = _playerProgress >= _cpuProgress ? RaceRunner.Player : RaceRunner.Cpu;
            }
            else
            {
                winner = playerDone ? RaceRunner.Player : RaceRunner.Cpu;
            }

            _playerProgress = Mathf.Clamp01(_playerProgress);
            _cpuProgress = Mathf.Clamp01(_cpuProgress);
            Winner = winner;
            _phase.Value = RaceGamePhase.Finished;
        }
    }
}
