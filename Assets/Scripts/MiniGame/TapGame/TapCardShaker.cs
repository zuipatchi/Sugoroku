using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UIElements;

namespace MiniGame.TapGame
{
    /// <summary>
    /// タップ連打のキャラカード 1 枚ぶんの「弾み」演出。タップのたびに
    /// 「がたがた」（減衰する小刻みな振動）と「パンチ」（ぷにっと拡大→戻る）を合わせて再生する。
    /// 参加者ごとに 1 つ持ち（自分は自分のタップで、相手は届いた連打数の増加で叩く）、
    /// <see cref="TapGamePlay"/> が所有・破棄する。
    /// </summary>
    public sealed class TapCardShaker : IDisposable
    {
        // 1 回の弾みの長さ（秒）。位相 1→0 をこの時間で流す。
        private const float ShakeSeconds = 0.4f;
        // 揺れ幅（px）の抽選範囲。毎回わずかに変えて機械的な繰り返しに見せない。
        private const float MinAmplitude = 9f;
        private const float MaxAmplitude = 13f;
        // パンチ（拡大）の最大量。位相と一緒に 0 へ収束する。
        private const float PunchScale = 0.16f;

        private readonly VisualElement _card;

        private Tween _tween;
        private float _phase;

        public TapCardShaker(VisualElement card)
        {
            _card = card;
        }

        /// <summary>1 回ぶん弾ませる（再生中に呼ばれたら弾み直す）。</summary>
        public void Shake()
        {
            if (_card == null)
            {
                return;
            }

            _tween?.Kill();

            float amplitude = UnityEngine.Random.Range(MinAmplitude, MaxAmplitude);
            float sign = UnityEngine.Random.value < 0.5f ? -1f : 1f;

            // スタイル値を直接ゲッターにせずローカル（フィールド）を仲介させる（patterns.md 3）。
            _phase = 1f;
            Apply(1f, amplitude, sign);

            _tween = DOTween.To(
                    () => _phase,
                    p =>
                    {
                        _phase = p;
                        Apply(p, amplitude, sign);
                    },
                    0f,
                    ShakeSeconds)
                .SetEase(Ease.Linear);
        }

        // 位相 phase（1→0）から、減衰する小刻み振動（がたがた）と減衰する拡大（パンチ）を適用する。
        private void Apply(float phase, float amplitude, float sign)
        {
            if (_card == null)
            {
                return;
            }

            float offsetX = sign * amplitude * phase * Mathf.Sin(phase * 42f);
            float offsetY = amplitude * 0.6f * phase * Mathf.Cos(phase * 38f);
            _card.style.translate = new Translate(
                new Length(offsetX, LengthUnit.Pixel),
                new Length(offsetY, LengthUnit.Pixel));

            float punch = 1f + (PunchScale * phase);
            _card.style.scale = new Scale(new Vector3(punch, punch, 1f));
        }

        public void Dispose()
        {
            _tween?.Kill();
            _tween = null;
        }
    }
}
