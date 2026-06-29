using System;
using R3;

namespace Main.Roulette
{
    /// <summary>
    /// ルーレットの状態と出目を保持する Model。
    /// 出目の決定はここで行い（ローカル乱数）、回転演出は Presenter が担当する。
    /// </summary>
    public sealed class RouletteModel : IDisposable
    {
        private readonly ReactiveProperty<RouletteState> _state = new(RouletteState.Idle);
        private readonly ReactiveProperty<int> _result = new(0);
        private readonly Random _random = new();

        /// <summary>現在の状態。</summary>
        public ReadOnlyReactiveProperty<RouletteState> State => _state;

        /// <summary>最後に確定した出目（移動マス数）。未確定時は 0。</summary>
        public ReadOnlyReactiveProperty<int> Result => _result;

        /// <summary>
        /// 出目を決めて回転を開始する。状態を <see cref="RouletteState.Spinning"/> にし、決定した出目（1〜<paramref name="count"/>）を返す。
        /// </summary>
        public int BeginSpin(int count)
        {
            int value = _random.Next(1, count + 1);
            _state.Value = RouletteState.Spinning;
            return value;
        }

        /// <summary>
        /// 回転演出の完了時に呼び、出目を確定して状態を <see cref="RouletteState.Stopped"/> にする。
        /// </summary>
        public void CompleteSpin(int value)
        {
            _result.Value = value;
            _state.Value = RouletteState.Stopped;
        }

        public void Dispose()
        {
            _state.Dispose();
            _result.Dispose();
        }
    }
}
