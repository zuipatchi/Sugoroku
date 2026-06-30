using System;
using R3;

namespace MiniGame.TapGame
{
    /// <summary>
    /// タップ連打ミニゲームの状態。<see cref="TapGamePhase.Playing"/> 中のみタップを数える。
    /// 時間の進行は Presenter（UniTask）が駆動し、ここはフェーズと数値だけを保持する純粋ロジック。
    /// </summary>
    public sealed class TapGameModel : IDisposable
    {
        private readonly ReactiveProperty<TapGamePhase> _phase = new(TapGamePhase.Ready);
        private readonly ReactiveProperty<int> _tapCount = new(0);
        private readonly ReactiveProperty<float> _remainingSeconds = new(0f);

        /// <summary>現在のフェーズ。</summary>
        public ReadOnlyReactiveProperty<TapGamePhase> Phase => _phase;

        /// <summary>現在のタップ数。</summary>
        public ReadOnlyReactiveProperty<int> TapCount => _tapCount;

        /// <summary>計測の残り秒数。</summary>
        public ReadOnlyReactiveProperty<float> RemainingSeconds => _remainingSeconds;

        /// <summary>カウントダウンを開始する。</summary>
        public void BeginCountdown()
        {
            _phase.Value = TapGamePhase.Countdown;
        }

        /// <summary>計測を開始する。タップ数を 0 に戻し、残り時間をセットする。</summary>
        public void StartPlaying(float durationSeconds)
        {
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

        /// <summary>タップを 1 回数える。計測中のみ有効。</summary>
        public void Tap()
        {
            if (_phase.Value != TapGamePhase.Playing)
            {
                return;
            }
            _tapCount.Value++;
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
    }
}
