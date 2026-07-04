namespace MiniGame.RaceGame
{
    /// <summary>
    /// 往復メーターのスイープ物理。0〜1 を一定速度で往復し、端に達したら向きを反転する。
    /// マーカー UI への反映は <see cref="RaceGamePlay"/> 側が <see cref="Value"/> を読んで行う。
    /// </summary>
    public sealed class RaceMeter
    {
        private readonly float _sweepSpeed;
        private float _direction = 1f;

        public RaceMeter(float sweepSpeed)
        {
            _sweepSpeed = sweepSpeed;
        }

        /// <summary>現在のメーター値（0〜1）。</summary>
        public float Value { get; private set; }

        /// <summary>メーターを左端（0）・往路（正方向）に戻す。</summary>
        public void Reset()
        {
            Value = 0f;
            _direction = 1f;
        }

        /// <summary>時間を <paramref name="deltaSeconds"/> 進めてメーターを往復させる。</summary>
        public void Advance(float deltaSeconds)
        {
            float value = Value + _direction * _sweepSpeed * deltaSeconds;
            if (value >= 1f)
            {
                value = 1f;
                _direction = -1f;
            }
            else if (value <= 0f)
            {
                value = 0f;
                _direction = 1f;
            }
            Value = value;
        }
    }
}
