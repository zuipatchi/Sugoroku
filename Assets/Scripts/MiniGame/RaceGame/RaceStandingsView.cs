using System.Collections.Generic;
using Common.MiniGame;
using UnityEngine.UIElements;

namespace MiniGame.RaceGame
{
    /// <summary>
    /// 結果パネルの順位表（順位・キャラ名・タイムの行＋見出し＋注記）を組み立てて更新するビュー。
    /// 走者と同じ並びで行を作っておき、表示のたびに順位順へ入れ直す。並べ替えのルールは純粋関数
    /// <see cref="RaceRanking"/> が持ち、ここは見せ方だけを担う（<see cref="RaceGamePlay"/> が所有）。
    /// </summary>
    public sealed class RaceStandingsView
    {
        private readonly Label _headline;
        private readonly VisualElement _list;
        private readonly Label _note;

        // 走者ごとの行（index＝走者 index・0＝自分）。並べ替えは _list へ入れ直して行う。
        private readonly List<VisualElement> _rows = new();
        private readonly List<Label> _rankLabels = new();
        private readonly List<Label> _timeLabels = new();

        public RaceStandingsView(Label headline, VisualElement list, Label note)
        {
            _headline = headline;
            _list = list;
            _note = note;
        }

        /// <summary>行をすべて捨てる（走者を組み直すとき）。</summary>
        public void Clear()
        {
            _list.Clear();
            _rows.Clear();
            _rankLabels.Clear();
            _timeLabels.Clear();
        }

        /// <summary>走者 1 人ぶんの行を足す（走者と同じ順で呼ぶ）。中身は <see cref="Refresh"/> で埋める。</summary>
        public void AddRunner(string characterName, bool isPlayer)
        {
            VisualElement row = new() { pickingMode = PickingMode.Ignore };
            row.AddToClassList("race-standing");
            if (isPlayer)
            {
                row.AddToClassList("race-standing--you");
            }

            Label rank = new() { pickingMode = PickingMode.Ignore };
            rank.AddToClassList("race-standing__rank");
            row.Add(rank);

            Label name = new(characterName) { pickingMode = PickingMode.Ignore };
            name.AddToClassList("race-standing__name");
            row.Add(name);

            Label time = new() { pickingMode = PickingMode.Ignore };
            time.AddToClassList("race-standing__time");
            row.Add(time);

            _rows.Add(row);
            _rankLabels.Add(rank);
            _timeLabels.Add(time);
        }

        /// <summary>
        /// <paramref name="entries"/> を順位順に並べ替えて表示する。<paramref name="provisional"/> が true の
        /// ときは**暫定順位**（まだ走っている相手がいる）として注記を添える。
        /// </summary>
        public void Refresh(IReadOnlyList<RaceEntry> entries, bool provisional)
        {
            IReadOnlyList<RaceStanding> standings = RaceRanking.Order(entries);

            _list.Clear();
            int myRank = 1;
            foreach (RaceStanding standing in standings)
            {
                int runner = standing.Entry.Runner;
                if (runner < 0 || runner >= _rows.Count)
                {
                    continue;
                }

                _rankLabels[runner].text = $"{standing.Rank}位";
                _timeLabels[runner].text = TimeTextOf(standing.Entry, provisional);
                _list.Add(_rows[runner]);
                if (runner == 0)
                {
                    myRank = standing.Rank;
                }
            }

            _headline.text = !provisional && myRank == 1 ? "1位！" : $"{myRank}位 / {_rows.Count}人";
            _note.text = provisional ? "他のプレイヤーが走行中…（暫定）" : string.Empty;
            _note.style.display = provisional ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // タイム欄。ゴール済みならタイム（分からなければ「ゴール」＝一人用モードの CPU は先着決着で
        // タイムを持たない）、まだ走っていれば暫定かどうかで文言を変える。
        private static string TimeTextOf(RaceEntry entry, bool provisional)
        {
            if (!entry.Finished)
            {
                return provisional ? "走行中" : "未ゴール";
            }
            return entry.Millis != MiniGameRanking.NotFinished
                ? $"{entry.Millis / 1000f:0.00}秒"
                : "ゴール";
        }
    }
}
