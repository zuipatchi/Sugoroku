using System;
using R3;

namespace Main.Roulette
{
    /// <summary>
    /// ルーレットの状態と出目を保持する Model。
    /// 出目は「円盤が自然に止まった位置のセクター」で決まるため、ここでは状態遷移のみを担い、
    /// 回転演出・出目の算出（停止角度 → セクター）は Presenter が担当する。
    /// </summary>
    public sealed class RouletteModel : IDisposable
    {
        private readonly ReactiveProperty<RouletteState> _state = new(RouletteState.Idle);
        private readonly ReactiveProperty<int> _result = new(0);

        /// <summary>現在の状態。</summary>
        public ReadOnlyReactiveProperty<RouletteState> State => _state;

        /// <summary>最後に確定した出目（移動マス数）。未確定時は 0。</summary>
        public ReadOnlyReactiveProperty<int> Result => _result;

        /// <summary>
        /// 回転を開始する。状態を <see cref="RouletteState.Spinning"/> にする。
        /// 出目は停止時に <see cref="CompleteSpin"/> で確定する。
        /// </summary>
        public void BeginSpin()
        {
            _state.Value = RouletteState.Spinning;
        }

        /// <summary>
        /// 回転演出の完了時に呼び、出目を確定して状態を <see cref="RouletteState.Stopped"/> にする。
        /// </summary>
        public void CompleteSpin(int value)
        {
            _result.Value = value;
            _state.Value = RouletteState.Stopped;
        }

        /// <summary>
        /// 次の手番のために状態を <see cref="RouletteState.Idle"/> へ戻す（出目の値は保持する）。
        /// 前手番の Stopped を「今回の停止」と誤検知しないよう、手番開始時に呼ぶ。
        /// </summary>
        public void Reset()
        {
            _state.Value = RouletteState.Idle;
        }

        public void Dispose()
        {
            _state.Dispose();
            _result.Dispose();
        }
    }
}
