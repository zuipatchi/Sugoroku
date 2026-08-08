using System.Collections.Generic;
using UnityEngine.UIElements;

namespace MiniGame
{
    /// <summary>順位表の 1 行ぶんの表示内容。<see cref="MiniGameStandingsView.Refresh"/> へ順位順で渡す。</summary>
    public readonly struct StandingLine
    {
        public StandingLine(int participant, string rankText, string valueText)
        {
            Participant = participant;
            RankText = rankText;
            ValueText = valueText;
        }

        /// <summary>参加者 index（0＝自分）。<see cref="MiniGameStandingsView.AddParticipant"/> を呼んだ順と対応する。</summary>
        public int Participant { get; }

        /// <summary>左の列（「1位」「獲得」など）。</summary>
        public string RankText { get; }

        /// <summary>右の列（タイム・連打数・選んだアイテムなど）。</summary>
        public string ValueText { get; }
    }

    /// <summary>
    /// ミニゲームの結果パネルに出す「全参加者ぶんの順位表」。参加者と同じ並びで行を作っておき、
    /// 表示のたびに渡された順（＝順位順）へ入れ直す。順位の決め方と文言はゲームごとに違うので
    /// 呼び出し側が決め、ここは見せ方だけを担う（タップ連打・2Dレース・被っちゃやーよで共用）。
    /// USS クラスは接頭辞（例 <c>race-standing</c>）から
    /// <c>&lt;prefix&gt;</c> / <c>--you</c> / <c>__rank</c> / <c>__name</c> / <c>__value</c> を組み立てるので、
    /// 使う側のゲームの USS に同じクラスを定義しておく。
    /// </summary>
    public sealed class MiniGameStandingsView
    {
        private readonly VisualElement _list;
        private readonly string _classPrefix;

        // 参加者ごとの行（index＝参加者・0＝自分）。並べ替えは _list へ入れ直して行う。
        private readonly List<VisualElement> _rows = new();
        private readonly List<Label> _rankLabels = new();
        private readonly List<Label> _valueLabels = new();

        public MiniGameStandingsView(VisualElement list, string classPrefix)
        {
            _list = list;
            _classPrefix = classPrefix;
        }

        /// <summary>並べてある参加者の人数。</summary>
        public int Count => _rows.Count;

        /// <summary>行をすべて捨てる（参加者を組み直すとき）。</summary>
        public void Clear()
        {
            _list.Clear();
            _rows.Clear();
            _rankLabels.Clear();
            _valueLabels.Clear();
        }

        /// <summary>参加者 1 人ぶんの行を足す（参加者と同じ順で呼ぶ）。中身は <see cref="Refresh"/> で埋める。</summary>
        public void AddParticipant(string characterName, bool isPlayer)
        {
            VisualElement row = new() { pickingMode = PickingMode.Ignore };
            row.AddToClassList(_classPrefix);
            if (isPlayer)
            {
                row.AddToClassList($"{_classPrefix}--you");
            }

            Label rank = new() { pickingMode = PickingMode.Ignore };
            rank.AddToClassList($"{_classPrefix}__rank");
            row.Add(rank);

            Label name = new(characterName) { pickingMode = PickingMode.Ignore };
            name.AddToClassList($"{_classPrefix}__name");
            row.Add(name);

            Label value = new() { pickingMode = PickingMode.Ignore };
            value.AddToClassList($"{_classPrefix}__value");
            row.Add(value);

            _rows.Add(row);
            _rankLabels.Add(rank);
            _valueLabels.Add(value);
        }

        /// <summary><paramref name="lines"/> の並び順そのままに行を入れ直し、順位と値の文言を反映する。</summary>
        public void Refresh(IReadOnlyList<StandingLine> lines)
        {
            _list.Clear();
            if (lines == null)
            {
                return;
            }

            foreach (StandingLine line in lines)
            {
                int participant = line.Participant;
                if (participant < 0 || participant >= _rows.Count)
                {
                    continue;
                }

                _rankLabels[participant].text = line.RankText;
                _valueLabels[participant].text = line.ValueText;
                _list.Add(_rows[participant]);
            }
        }
    }
}
