using System;
using R3;
using UnityEngine;

namespace MiniGame.TapGame
{
    /// <summary>
    /// タップ連打ミニゲームの状態。参加者はプレイヤー（index 0）＋ CPU（1〜N-1）で、<see cref="TapGamePhase.Playing"/>
    /// 中だけ数える。プレイヤーは <see cref="Tap"/> で 1 回ずつ、各 CPU は <see cref="Tick"/> で自動連打する
    /// （速度は <see cref="TapGameConfig"/>・<see cref="System.Random"/> で決定的）。時間の進行は Presenter が駆動し、
    /// ここはフェーズと各参加者の連打数を保持する純粋ロジック。
    /// </summary>
    public sealed class TapGameModel : IDisposable
    {
        // 参加者は最低 1 人（プレイヤーのみ）。Setup を呼ばない従来のソロ利用でも動くようにする。
        private const int MinParticipantCount = 1;

        private readonly ReactiveProperty<TapGamePhase> _phase = new(TapGamePhase.Ready);
        private readonly ReactiveProperty<int> _tapCount = new(0);
        private readonly ReactiveProperty<float> _remainingSeconds = new(0f);

        private TapGameConfig _config = TapGameConfig.Default;
        private System.Random _random;
        // 参加者ごとの連打数（index 0＝プレイヤー、1〜＝CPU）。プレイヤーぶんは _tapCount にも反映する。
        private int[] _tapCounts = new int[MinParticipantCount];
        // 各 CPU の次タップまでの残り秒と基準タップ間隔（秒）。プレイヤー枠 index 0 は未使用。
        private float[] _cpuTapTimer = new float[MinParticipantCount];
        private float[] _cpuInterval = new float[MinParticipantCount];
        private int _participantCount = MinParticipantCount;

        /// <summary>現在のフェーズ。</summary>
        public ReadOnlyReactiveProperty<TapGamePhase> Phase => _phase;

        /// <summary>プレイヤー（index 0）の連打数。大きい数字表示・結果に使う。</summary>
        public ReadOnlyReactiveProperty<int> TapCount => _tapCount;

        /// <summary>計測の残り秒数。</summary>
        public ReadOnlyReactiveProperty<float> RemainingSeconds => _remainingSeconds;

        /// <summary>参加者数（プレイヤー＋CPU）。</summary>
        public int ParticipantCount => _participantCount;

        /// <summary>参加者 <paramref name="participant"/>（0＝プレイヤー）の連打数。範囲外は 0。</summary>
        public int TapCountOf(int participant)
        {
            return participant >= 0 && participant < _participantCount ? _tapCounts[participant] : 0;
        }

        /// <summary>プレイヤー（index 0）が 1 位か（他の誰よりも連打数が多い＝同数なら勝ち）。</summary>
        public bool IsPlayerWin
        {
            get
            {
                for (int p = 1; p < _participantCount; p++)
                {
                    if (_tapCounts[p] > _tapCounts[0])
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        /// <summary>ソロ（プレイヤーのみ）で準備する。従来の呼び出し互換。</summary>
        public void Setup(int seed)
        {
            Setup(TapGameConfig.Default, MinParticipantCount, seed);
        }

        /// <summary>参加者数の既定コンフィグ版。</summary>
        public void Setup(int playerCount, int seed)
        {
            Setup(TapGameConfig.Default, playerCount, seed);
        }

        /// <summary>
        /// <paramref name="playerCount"/> 人（プレイヤー 1 ＋ CPU <c>playerCount-1</c> 体）で準備する。
        /// 各 CPU の基準タップ間隔を <paramref name="config"/> の速度範囲から抽選し、フェーズを Ready に戻す。
        /// </summary>
        public void Setup(TapGameConfig config, int playerCount, int seed)
        {
            _config = config;
            _random = new System.Random(seed);
            _participantCount = Mathf.Max(MinParticipantCount, playerCount);
            _tapCounts = new int[_participantCount];
            _cpuTapTimer = new float[_participantCount];
            _cpuInterval = new float[_participantCount];
            for (int p = 1; p < _participantCount; p++)
            {
                float rate = Mathf.Lerp(config.CpuTapsPerSecondMin, config.CpuTapsPerSecondMax, (float)_random.NextDouble());
                _cpuInterval[p] = 1f / Mathf.Max(0.01f, rate);
                _cpuTapTimer[p] = NextCpuInterval(p);
            }
            _tapCount.Value = 0;
            _remainingSeconds.Value = 0f;
            _phase.Value = TapGamePhase.Ready;
        }

        /// <summary>カウントダウンを開始する。</summary>
        public void BeginCountdown()
        {
            _phase.Value = TapGamePhase.Countdown;
        }

        /// <summary>計測を開始する。全参加者の連打数を 0 に戻し、CPU の抽選タイマーを引き直して残り時間をセットする。</summary>
        public void StartPlaying(float durationSeconds)
        {
            for (int p = 0; p < _participantCount; p++)
            {
                _tapCounts[p] = 0;
            }
            for (int p = 1; p < _participantCount; p++)
            {
                _cpuTapTimer[p] = NextCpuInterval(p);
            }
            _tapCount.Value = 0;
            _remainingSeconds.Value = durationSeconds < 0f ? 0f : durationSeconds;
            _phase.Value = TapGamePhase.Playing;
        }

        /// <summary>残り時間を更新する（計測中のみ反映、負値は 0 に丸める）。</summary>
        public void UpdateRemaining(float seconds)
        {
            if (_phase.Value != TapGamePhase.Playing)
            {
                return;
            }
            _remainingSeconds.Value = seconds < 0f ? 0f : seconds;
        }

        /// <summary>プレイヤー（index 0）のタップを 1 回数える。計測中のみ有効。</summary>
        public void Tap()
        {
            if (_phase.Value != TapGamePhase.Playing)
            {
                return;
            }
            _tapCounts[0]++;
            _tapCount.Value = _tapCounts[0];
        }

        /// <summary>時間を <paramref name="deltaSeconds"/> 進めて各 CPU を自動連打させる。計測中のみ。</summary>
        public void Tick(float deltaSeconds)
        {
            if (_phase.Value != TapGamePhase.Playing || deltaSeconds <= 0f)
            {
                return;
            }
            for (int p = 1; p < _participantCount; p++)
            {
                _cpuTapTimer[p] -= deltaSeconds;
                while (_cpuTapTimer[p] <= 0f)
                {
                    _tapCounts[p]++;
                    _cpuTapTimer[p] += NextCpuInterval(p);
                }
            }
        }

        /// <summary>計測を終了する。</summary>
        public void Finish()
        {
            _remainingSeconds.Value = 0f;
            _phase.Value = TapGamePhase.Finished;
        }

        public void Dispose()
        {
            _phase.Dispose();
            _tapCount.Dispose();
            _remainingSeconds.Dispose();
        }

        // ゆらぎを掛けた CPU の次タップまでの秒（無限ループ防止に下限を設ける）。
        private float NextCpuInterval(int participant)
        {
            float jitter = 1f + _config.CpuIntervalJitter * (float)(_random.NextDouble() * 2.0 - 1.0);
            return Mathf.Max(0.01f, _cpuInterval[participant] * jitter);
        }
    }
}
