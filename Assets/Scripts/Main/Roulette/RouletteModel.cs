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
        private readonly ReactiveProperty<int> _advancingPlayer = new(-1);
        private readonly Subject<SpinDecision> _decided = new();

        /// <summary>現在の状態。</summary>
        public ReadOnlyReactiveProperty<RouletteState> State => _state;

        /// <summary>
        /// このクライアントで回した円盤の停止位置が確定した（＝押下を離して減速に入った）通知。
        /// 円盤が止まるのを待たずに発火するので、オンラインでは結果を回り終わる前に配れる。
        /// 他プレイヤーの結果を再生するだけの受信側では発火しない。
        /// </summary>
        public Observable<SpinDecision> Decided => _decided;

        /// <summary>最後に確定した出目（移動マス数）。未確定時は 0。</summary>
        public ReadOnlyReactiveProperty<int> Result => _result;

        /// <summary>
        /// 最後に確定した「進む参加者」の index（＝止まったセクターのキャラ）。未確定時は -1。
        /// 「止まったキャラが進む」方式のため、手番プレイヤーとは別に進む人がここで決まる。
        /// </summary>
        public ReadOnlyReactiveProperty<int> AdvancingPlayer => _advancingPlayer;

        /// <summary>
        /// 回転を開始する。状態を <see cref="RouletteState.Spinning"/> にする。
        /// 出目は停止時に <see cref="CompleteSpin"/> で確定する。
        /// </summary>
        public void BeginSpin()
        {
            _state.Value = RouletteState.Spinning;
        }

        /// <summary>
        /// 減速に入った時点で確定した停止位置を通知する（<see cref="Decided"/>）。
        /// 状態はまだ <see cref="RouletteState.Spinning"/> のままで、実際の停止は <see cref="CompleteSpin"/>。
        /// </summary>
        public void DecideSpin(SpinDecision decision)
        {
            _decided.OnNext(decision);
        }

        /// <summary>
        /// 回転演出の完了時に呼び、出目（進むマス数）と進む参加者を確定して状態を
        /// <see cref="RouletteState.Stopped"/> にする。
        /// </summary>
        public void CompleteSpin(int steps, int advancingPlayer)
        {
            _result.Value = steps;
            _advancingPlayer.Value = advancingPlayer;
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
            _advancingPlayer.Dispose();
            _decided.Dispose();
        }
    }
}
