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
        // 停止角を指定して減速する（ReleaseTo）ときの開始角・目標角。オンラインで他プレイヤーの
        // 停止セクターを再現するために使う。目標角モードの間は _hasTarget が true。
        private float _startRotation;
        private float _targetRotation;
        private bool _hasTarget;

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
        public bool HasStopped => !_isHolding && !_hasTarget && _angularVelocity <= 0f;

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
            _hasTarget = false;
        }

        /// <summary>
        /// 押下解除。<paramref name="stopDuration"/> 秒かけて ease-out で減速し、
        /// **ちょうど <paramref name="targetRotation"/> 度で止める**。
        /// オンラインで他プレイヤーのスピン結果（停止セクター）を同じ演出で再現するために使う。
        /// <paramref name="targetRotation"/> は現在角以上（前方＝時計回り）であること。
        /// </summary>
        public void ReleaseTo(float targetRotation, float stopDuration)
        {
            if (!_isHolding)
            {
                return;
            }
            _isHolding = false;

            _decelStartVelocity = _angularVelocity;
            _decelElapsed = 0f;
            _stopDuration = stopDuration;
            _startRotation = CurrentRotation;
            _targetRotation = targetRotation < CurrentRotation ? CurrentRotation : targetRotation;
            _hasTarget = true;
        }

        /// <summary>回転を即座に打ち切る（無効化・停止確定時のリセット用。角度は保持する）。</summary>
        public void Halt()
        {
            _isHolding = false;
            _angularVelocity = 0f;
            _hasTarget = false;
        }

        /// <summary>
        /// 1 フレームぶんのシミュレーションを進める。角度が進んだ（回転を表示へ反映すべき）とき true を返す。
        /// </summary>
        public bool Tick(float deltaTime)
        {
            if (_hasTarget)
            {
                return TickToTarget(deltaTime);
            }

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

        /// <summary>
        /// 目標角モード（<see cref="ReleaseTo"/>）の 1 フレーム。<see cref="Release"/> と同じ効きの
        /// ease-out（速度が (1-u)^2 で落ちる＝距離は 1-(1-u)^3）で開始角から目標角へ寄せ、
        /// 経過が停止時間に達したら目標角ちょうどで止める。
        /// </summary>
        private bool TickToTarget(float deltaTime)
        {
            _decelElapsed += deltaTime;
            float u = _stopDuration > 0f ? Mathf.Clamp01(_decelElapsed / _stopDuration) : 1f;
            float previous = CurrentRotation;

            if (u >= 1f)
            {
                CurrentRotation = _targetRotation;
                _angularVelocity = 0f;
                _hasTarget = false;
                return true;
            }

            float remaining = 1f - u;
            CurrentRotation = Mathf.LerpUnclamped(
                _startRotation, _targetRotation, 1f - remaining * remaining * remaining);
            _angularVelocity = deltaTime > 0f ? (CurrentRotation - previous) / deltaTime : 0f;
            return true;
        }
    }
}
