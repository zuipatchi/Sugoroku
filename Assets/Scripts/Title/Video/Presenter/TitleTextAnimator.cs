using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Title.Video
{
    /// <summary>
    /// タイトル文言（TitleText）の生成と表示演出を担当する。
    /// 3 行の文言を 1 文字ずつのラベルに分解して生成し、文字ごとに transition-delay を
    /// ずらすことで「上から順番に降ってくる」演出を作る（アニメーション本体は USS の
    /// .title-char / .title-char--visible のトランジション）。
    /// <see cref="TitleVideoPresenter"/> から生成され、動画の再生終了で <see cref="Show"/>、
    /// ループ再生の再開で <see cref="Hide"/> が呼ばれる。
    /// </summary>
    public sealed class TitleTextAnimator
    {
        // タイトル文言（3 行）。1 文字ずつ上から降らせる。
        private static readonly string[] TitleLines = { "ドラゴン", "ファミリー", "すごろく" };
        // 文字ごとの登場ディレイ（秒）。降ってくる順番の間隔。
        private const float CharStaggerSeconds = 0.09f;

        private readonly VisualElement _titleText;
        private readonly List<VisualElement> _titleChars = new();

        public TitleTextAnimator(VisualElement titleText)
        {
            _titleText = titleText;
        }

        // 3 行ぶんの行コンテナと 1 文字ずつのラベルを生成する。初期は隠れた状態（USS の .title-char）。
        // 文字ごとに transition-delay をずらして、降ってくる順番の間隔を作る。
        public void Build()
        {
            if (_titleText == null)
            {
                return;
            }

            _titleText.Clear();
            _titleChars.Clear();

            int globalIndex = 0;
            foreach (string line in TitleLines)
            {
                VisualElement row = new() { pickingMode = PickingMode.Ignore };
                row.AddToClassList("title-line");

                foreach (char character in line)
                {
                    Label charLabel = new() { text = character.ToString(), pickingMode = PickingMode.Ignore };
                    charLabel.AddToClassList("title-char");
                    charLabel.style.transitionDelay = new List<TimeValue>
                    {
                        new TimeValue(globalIndex * CharStaggerSeconds, TimeUnit.Second),
                    };
                    row.Add(charLabel);
                    _titleChars.Add(charLabel);
                    globalIndex++;
                }

                _titleText.Add(row);
            }
        }

        // 全文字に visible クラスを付与。各文字は自分の transition-delay ぶん遅れて降りてくる。
        public void Show()
        {
            foreach (VisualElement charLabel in _titleChars)
            {
                charLabel.EnableInClassList("title-char--visible", true);
            }
        }

        // 全文字から visible クラスを外す。ループ再生の再開時に、次の再生終了までタイトル文言を隠す。
        public void Hide()
        {
            foreach (VisualElement charLabel in _titleChars)
            {
                charLabel.EnableInClassList("title-char--visible", false);
            }
        }
    }
}
