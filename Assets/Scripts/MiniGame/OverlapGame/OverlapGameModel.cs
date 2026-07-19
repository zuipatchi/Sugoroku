using System;
using System.Collections.Generic;
using Main.Item;
using R3;
using UnityEngine;

namespace MiniGame.OverlapGame
{
    /// <summary>
    /// 「被っちゃやーよ」の状態。参加者数ぶんの重複なしアイテムを提示し、各参加者が 1 つずつ選ぶ。
    /// プレイヤー（index 0）の選んだアイテムが他の誰とも被っていなければ獲得＝勝ち。
    /// CPU（参加者のうちプレイヤー以外）の選択は <see cref="Setup"/> 時点で <see cref="System.Random"/> により
    /// 確定させるため決定的。時間・入力の駆動は Presenter が行い、ここはフェーズと選択・勝敗だけを持つ純粋ロジック。
    /// </summary>
    public sealed class OverlapGameModel : IDisposable
    {
        private readonly ReactiveProperty<OverlapGamePhase> _phase = new(OverlapGamePhase.Ready);

        private readonly List<ItemId> _offered = new();
        private readonly List<int> _cpuChoices = new();

        private int _playerCount;
        private int _playerChoiceIndex = -1;

        /// <summary>現在のフェーズ。</summary>
        public ReadOnlyReactiveProperty<OverlapGamePhase> Phase => _phase;

        /// <summary>参加者数（提示アイテム枚数の元。実際の提示枚数は <see cref="OfferedItems"/> の数）。</summary>
        public int PlayerCount => _playerCount;

        /// <summary>提示中のアイテム（重複なし）。</summary>
        public IReadOnlyList<ItemId> OfferedItems => _offered;

        /// <summary>プレイヤー（人間）が選んだ提示アイテムの index。未選択なら -1。</summary>
        public int PlayerChoiceIndex => _playerChoiceIndex;

        /// <summary>各 CPU が選んだ提示アイテムの index（<see cref="Setup"/> で確定）。</summary>
        public IReadOnlyList<int> CpuChoiceIndices => _cpuChoices;

        /// <summary>プレイヤーが勝ったか（選択済みかつ他の誰とも被っていない）。未選択（無効票）・被りは false。</summary>
        public bool IsPlayerWin => _playerChoiceIndex >= 0 && !_cpuChoices.Contains(_playerChoiceIndex);

        /// <summary>プレイヤーの票が無効か（制限時間内に選べず未選択のままオープンされた）。</summary>
        public bool IsPlayerVoteInvalid => _playerChoiceIndex < 0
            && (_phase.Value == OverlapGamePhase.Revealed || _phase.Value == OverlapGamePhase.Finished);

        /// <summary>
        /// 提示アイテムと CPU の選択を用意する。提示枚数は <paramref name="playerCount"/> と
        /// アイテムカタログ総数の小さい方（重複なしで配れる上限）にクランプする。
        /// </summary>
        public void Setup(int playerCount, int seed)
        {
            System.Random random = new(seed);
            _playerCount = Mathf.Max(1, playerCount);

            int catalogCount = ItemCatalog.All.Count;
            int offeredCount = Mathf.Clamp(_playerCount, 1, Mathf.Max(1, catalogCount));

            BuildOfferedItems(offeredCount, random);

            // CPU（プレイヤー以外の参加者）ぶんの選択を先に確定する。
            // 各 CPU は提示アイテムから一様ランダムに選ぶ（互いに・プレイヤーと被りうる）。
            _cpuChoices.Clear();
            int cpuCount = _playerCount - 1;
            if (_offered.Count > 0)
            {
                for (int i = 0; i < cpuCount; i++)
                {
                    _cpuChoices.Add(random.Next(_offered.Count));
                }
            }

            _playerChoiceIndex = -1;
            _phase.Value = OverlapGamePhase.Ready;
        }

        /// <summary>カウントダウンを開始する（Ready からのみ）。</summary>
        public void BeginCountdown()
        {
            if (_phase.Value == OverlapGamePhase.Ready)
            {
                _phase.Value = OverlapGamePhase.Countdown;
            }
        }

        /// <summary>選択受付を開始する（Countdown からのみ）。</summary>
        public void BeginChoosing()
        {
            if (_phase.Value == OverlapGamePhase.Countdown)
            {
                _phase.Value = OverlapGamePhase.Choosing;
            }
        }

        /// <summary>
        /// プレイヤーの選択を確定してオープンする。選択中かつ提示範囲内の index のときだけ有効。
        /// 成功したら <see cref="OverlapGamePhase.Revealed"/> へ進み true を返す。
        /// </summary>
        public bool Choose(int offeredIndex)
        {
            if (_phase.Value != OverlapGamePhase.Choosing)
            {
                return false;
            }
            if (offeredIndex < 0 || offeredIndex >= _offered.Count)
            {
                return false;
            }

            _playerChoiceIndex = offeredIndex;
            _phase.Value = OverlapGamePhase.Revealed;
            return true;
        }

        /// <summary>
        /// 制限時間切れで無効票にする（Choosing からのみ）。プレイヤーの選択は未確定（-1）のまま
        /// <see cref="OverlapGamePhase.Revealed"/> へ進む。無効票は獲得できない（<see cref="IsPlayerWin"/> は false）。
        /// </summary>
        public void TimeOut()
        {
            if (_phase.Value == OverlapGamePhase.Choosing)
            {
                _phase.Value = OverlapGamePhase.Revealed;
            }
        }

        /// <summary>結果を確定する（Revealed からのみ）。</summary>
        public void Finish()
        {
            if (_phase.Value == OverlapGamePhase.Revealed)
            {
                _phase.Value = OverlapGamePhase.Finished;
            }
        }

        /// <summary>提示アイテム <paramref name="offeredIndex"/> をいずれかの CPU が選んでいるか（被り表示用）。</summary>
        public bool IsChosenByCpu(int offeredIndex)
        {
            return _cpuChoices.Contains(offeredIndex);
        }

        public void Dispose()
        {
            _phase.Dispose();
        }

        // カタログをシャッフルして先頭から offeredCount 枚を重複なしで採る。
        private void BuildOfferedItems(int offeredCount, System.Random random)
        {
            _offered.Clear();
            IReadOnlyList<ItemDefinition> all = ItemCatalog.All;
            if (all.Count == 0)
            {
                return;
            }

            List<ItemId> pool = new(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                pool.Add(all[i].Id);
            }

            // Fisher-Yates で先頭 offeredCount 枚だけ確定させる。
            int take = Mathf.Min(offeredCount, pool.Count);
            for (int i = 0; i < take; i++)
            {
                int j = i + random.Next(pool.Count - i);
                (pool[i], pool[j]) = (pool[j], pool[i]);
                _offered.Add(pool[i]);
            }
        }
    }
}
