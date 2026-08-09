using System.Collections.Generic;
using Common.MiniGame;
using UnityEngine.UIElements;

namespace MiniGame.RaceGame
{
    /// <summary>
    /// 結果パネルの順位表（順位・キャラ名・タイムの行＋見出し＋注記）を組み立てて更新するビュー。
    /// 行の生成と並べ替えは共通の <see cref="MiniGameStandingsView"/>（USS 接頭辞 <c>race-standing</c>）に任せ、
    /// ここはレース固有の並べ替え（純粋関数 <see cref="RaceRanking"/>）とタイムの文言だけを担う
    /// （<see cref="RaceGamePlay"/> が所有）。
    /// </summary>
    public sealed class RaceStandingsView
    {
        private const string ClassPrefix = "race-standing";

        private readonly Label _headline;
        private readonly Label _note;
        private readonly MiniGameStandingsView _standings;

        public RaceStandingsView(Label headline, VisualElement list, Label note)
        {
            _headline = headline;
            _note = note;
            _standings = new MiniGameStandingsView(list, ClassPrefix);
        }

        /// <summary>行をすべて捨てる（走者を組み直すとき）。</summary>
        public void Clear()
        {
            _standings.Clear();
        }

        /// <summary>走者 1 人ぶんの行を足す（走者と同じ順で呼ぶ）。中身は <see cref="Refresh"/> で埋める。</summary>
        public void AddRunner(string characterName, bool isPlayer)
        {
            _standings.AddParticipant(characterName, isPlayer);
        }

        /// <summary>
        /// <paramref name="entries"/> を順位順に並べ替えて表示し、**走者ごとの 1 始まりの順位**
        /// （index＝走者）を返す（一人用モードの順位別の賞金に使う）。
        /// <paramref name="provisional"/> が true のときは**暫定順位**（まだ走っている相手がいる）として
        /// 注記を添える。
        /// </summary>
        public int[] Refresh(IReadOnlyList<RaceEntry> entries, bool provisional)
        {
            IReadOnlyList<RaceStanding> ordered = RaceRanking.Order(entries);

            List<StandingLine> lines = new(ordered.Count);
            int[] ranks = new int[entries?.Count ?? 0];
            int myRank = 1;
            foreach (RaceStanding standing in ordered)
            {
                lines.Add(new StandingLine(
                    standing.Entry.Runner,
                    $"{standing.Rank}位",
                    TimeTextOf(standing.Entry, provisional)));
                if (standing.Entry.Runner >= 0 && standing.Entry.Runner < ranks.Length)
                {
                    ranks[standing.Entry.Runner] = standing.Rank;
                }
                if (standing.Entry.Runner == 0)
                {
                    myRank = standing.Rank;
                }
            }
            _standings.Refresh(lines);

            _headline.text = !provisional && myRank == 1 ? "1位！" : $"{myRank}位 / {_standings.Count}人";
            _note.text = provisional ? "他のプレイヤーが走行中…（暫定）" : string.Empty;
            _note.style.display = provisional ? DisplayStyle.Flex : DisplayStyle.None;
            return ranks;
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
