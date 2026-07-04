using DG.Tweening;
using UnityEngine;
using UnityEngine.UIElements;

namespace Main.Roulette
{
    /// <summary>
    /// ルーレットの DOTween 演出。針のバウンス・当たり時の円盤パルス・出目ラベルのポップを担う。
    /// UI 要素はコンストラクタで受け取り、Tween の生存管理（Kill）もここで行う。
    /// </summary>
    public sealed class RouletteEffects
    {
        private readonly VisualElement _wheel;
        private readonly Label _pointer;
        private readonly Label _resultLabel;

        private Tween _pointerBounceTween;
        private Tween _wheelPulseTween;
        private Tween _resultTween;

        public RouletteEffects(VisualElement wheel, Label pointer, Label resultLabel)
        {
            _wheel = wheel;
            _pointer = pointer;
            _resultLabel = resultLabel;
        }

        /// <summary>針が「カチッ」と弾む演出（縦方向にスカッシュして戻す）。</summary>
        public void BouncePointer()
        {
            if (_pointer == null)
            {
                return;
            }
            _pointerBounceTween?.Kill();
            float squash = 1.45f;
            _pointer.style.scale = new Scale(new Vector3(1f, squash, 1f));
            _pointerBounceTween = DOTween.To(
                    () => squash,
                    s =>
                    {
                        squash = s;
                        _pointer.style.scale = new Scale(new Vector3(1f, s, 1f));
                    },
                    1f,
                    0.12f)
                .SetEase(Ease.OutBack);
        }

        /// <summary>当たりの瞬間に円盤をひと突き拡大して祝う。</summary>
        public void PulseWheelOnWin()
        {
            if (_wheel == null)
            {
                return;
            }
            _wheelPulseTween?.Kill();
            float pulse = 1.06f;
            _wheel.style.scale = new Scale(new Vector3(pulse, pulse, 1f));
            _wheelPulseTween = DOTween.To(
                    () => pulse,
                    p =>
                    {
                        pulse = p;
                        _wheel.style.scale = new Scale(new Vector3(p, p, 1f));
                    },
                    1f,
                    0.35f)
                .SetEase(Ease.OutBack);
        }

        /// <summary>パルス途中で再回転されても拡大が残らないよう、円盤のスケールを等倍へ戻す。</summary>
        public void ResetWheelPulse()
        {
            _wheelPulseTween?.Kill();
            _wheelPulseTween = null;
            if (_wheel != null)
            {
                _wheel.style.scale = new Scale(Vector3.one);
            }
        }

        /// <summary>出目ラベルを小さい状態から弾ませて表示する。</summary>
        public void PlayResultLabelPop()
        {
            if (_resultLabel == null)
            {
                return;
            }
            _resultTween?.Kill();
            float s = 0.5f;
            _resultLabel.style.scale = new Scale(new Vector3(s, s, 1f));
            _resultTween = DOTween.To(
                    () => s,
                    v =>
                    {
                        s = v;
                        _resultLabel.style.scale = new Scale(new Vector3(v, v, 1f));
                    },
                    1f,
                    0.4f)
                .SetEase(Ease.OutBack);
        }

        /// <summary>すべての Tween を停止する。Presenter の OnDisable から呼ぶ。</summary>
        public void KillAll()
        {
            _pointerBounceTween?.Kill();
            _pointerBounceTween = null;
            _wheelPulseTween?.Kill();
            _wheelPulseTween = null;
            _resultTween?.Kill();
            _resultTween = null;
        }
    }
}
