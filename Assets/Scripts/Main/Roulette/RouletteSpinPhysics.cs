using UnityEngine;

namespace Main.Roulette
{
    /// <summary>
    /// ルーレット円盤の角速度シミュレーション。長押し中の加速と、離した後の ease-out 減速・停止判定を担う。
    /// deltaTime を引数で受け取る純ロジッククラスで、UI やシーンには依存しない（UnityEngine 依存は Mathf のみ）。
    /// </summary>
    public sealed class RouletteSpinPhysics
    {
        private readonly float _minSpinSpeed;
        private readonly float _maxSpinSpeed;
        private readonly float _spinAcceleration;

        private float _angularVelocity;
        private bool _isHolding;
        // 離した瞬間の速度と、停止までの経過・目標時間。ease-out 減速に使う。
        private float _decelStartVelocity;
        private float _decelElapsed;
        private float _stopDuration;

        public RouletteSpinPhysics(float minSpinSpeed, float maxSpinSpeed, float spinAcceleration)
        {
            _minSpinSpeed = minSpinSpeed;
            _maxSpinSpeed = maxSpinSpeed;
            _spinAcceleration = spinAcceleration;
        }

        /// <summary>累積回転角（度）。円盤の rotate と出目算出に使う。</summary>
        public float CurrentRotation { get; private set; }

        /// <summary>押下中かどうか。</summary>
        public bool IsHolding => _isHolding;

        /// <summary>離した後に減速し切って速度が尽きたか（出目確定のタイミング）。</summary>
        public bool HasStopped => !_isHolding && _angularVelocity <= 0f;

        /// <summary>押下開始。初速 <see cref="_minSpinSpeed"/> で回り始める。</summary>
        public void BeginHold()
        {
            _isHolding = true;
            _angularVelocity = _minSpinSpeed;
        }

        /// <summary>
        /// 押下解除。離した瞬間の速度に関わらず <paramref name="stopDuration"/> 秒かけて
        /// ease-out で減速して止める。これによりすぐ離しても長押しから離しても止まり方の印象が揃う。
        /// </summary>
        public void Release(float stopDuration)
        {
            if (!_isHolding)
            {
                return;
            }
            _isHolding = false;

            _decelStartVelocity = _angularVelocity;
            _decelElapsed = 0f;
            _stopDuration = stopDuration;
        }

        /// <summary>回転を即座に打ち切る（無効化・停止確定時のリセット用。角度は保持する）。</summary>
        public void Halt()
        {
            _isHolding = false;
            _angularVelocity = 0f;
        }

        /// <summary>
        /// 1 フレームぶんのシミュレーションを進める。角度が進んだ（回転を表示へ反映すべき）とき true を返す。
        /// </summary>
        public bool Tick(float deltaTime)
        {
            if (_isHolding)
            {
                // 押下中は加速。
                _angularVelocity = Mathf.MoveTowards(_angularVelocity, _maxSpinSpeed, _spinAcceleration * deltaTime);
            }
            else
            {
                // 離したら、離した瞬間の速度から _stopDuration 秒かけて ease-out（終盤ほど緩やか）で 0 まで落とす。
                // 停止までの時間は速度に依存しないため、すぐ離しても長押しから離しても止まり方の印象が揃う。
                _decelElapsed += deltaTime;
                float u = _stopDuration > 0f ? Mathf.Clamp01(_decelElapsed / _stopDuration) : 1f;
                _angularVelocity = _decelStartVelocity * (1f - u) * (1f - u);
                if (u >= 1f)
                {
                    _angularVelocity = 0f;
                }
            }

            if (_angularVelocity > 0f)
            {
                CurrentRotation += _angularVelocity * deltaTime;
                return true;
            }
            return false;
        }
    }
}
