using System;
using System.Collections.Generic;
using R3;

namespace Main.Board
{
    /// <summary>
    /// 陣地マス（<see cref="BoardCellEvent.Territory"/>）の占拠状態を保持する Model。
    /// マスに止まったプレイヤーがそのマスを占拠し（相手の陣地でも上書きで奪える）、
    /// 盤面の陣地マス総数の過半数（<see cref="RequiredToWin"/>）を占拠したプレイヤーが勝つ。
    /// 盤面データ（陣地マスの index 一覧）は DI に無いため <see cref="BoardPresenter"/> が
    /// <see cref="Initialize"/> で渡す。占拠の反映（マスの色替え）は Presenter が <see cref="Owner"/> を購読して行う。
    /// </summary>
    public sealed class TerritoryModel : IDisposable
    {
        // 陣地マスの盤面 index → 所有者（-1 = 未占拠）。生成順は Initialize の渡し順。
        private readonly Dictionary<int, ReactiveProperty<int>> _owners = new();

        /// <summary>盤面上の陣地マス総数。</summary>
        public int Total => _owners.Count;

        /// <summary>勝利に必要な占拠数（陣地マス総数の過半数）。陣地マスが無い盤面では到達不能（int.MaxValue）。</summary>
        public int RequiredToWin => Total > 0 ? Total / 2 + 1 : int.MaxValue;

        /// <summary>
        /// 陣地マスの盤面 index 一覧を受け取り、全マスを未占拠（-1）で初期化する。
        /// 盤面構築時に <see cref="BoardPresenter"/> が 1 度だけ呼ぶ。
        /// </summary>
        public void Initialize(IReadOnlyList<int> territoryCells)
        {
            foreach (ReactiveProperty<int> owner in _owners.Values)
            {
                owner.Dispose();
            }
            _owners.Clear();

            if (territoryCells == null)
            {
                return;
            }
            for (int i = 0; i < territoryCells.Count; i++)
            {
                int cell = territoryCells[i];
                if (!_owners.ContainsKey(cell))
                {
                    _owners.Add(cell, new ReactiveProperty<int>(-1));
                }
            }
        }

        /// <summary>マス <paramref name="cellIndex"/> が陣地マスか。</summary>
        public bool IsTerritory(int cellIndex)
        {
            return _owners.ContainsKey(cellIndex);
        }

        /// <summary>陣地マス <paramref name="cellIndex"/> の所有者（-1 = 未占拠）。陣地マスでなければ null。</summary>
        public ReadOnlyReactiveProperty<int> Owner(int cellIndex)
        {
            return _owners.TryGetValue(cellIndex, out ReactiveProperty<int> owner) ? owner : null;
        }

        /// <summary>プレイヤー <paramref name="player"/> が陣地マス <paramref name="cellIndex"/> を占拠する（上書き）。陣地マスでなければ何もしない。</summary>
        public void Claim(int player, int cellIndex)
        {
            if (_owners.TryGetValue(cellIndex, out ReactiveProperty<int> owner))
            {
                owner.Value = player;
            }
        }

        /// <summary>
        /// プレイヤー <paramref name="player"/> が所有していない陣地マスの盤面 index 一覧
        /// （未占拠＋他プレイヤー占拠）。陣地獲得アイテムで「自分以外の陣地マス」を選ばせるのに使う。
        /// </summary>
        public IReadOnlyList<int> CellsNotOwnedBy(int player)
        {
            List<int> cells = new();
            foreach (KeyValuePair<int, ReactiveProperty<int>> entry in _owners)
            {
                if (entry.Value.Value != player)
                {
                    cells.Add(entry.Key);
                }
            }
            return cells;
        }

        /// <summary>プレイヤー <paramref name="player"/> が占拠している陣地マス数。</summary>
        public int CountOwnedBy(int player)
        {
            int count = 0;
            foreach (ReactiveProperty<int> owner in _owners.Values)
            {
                if (owner.Value == player)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>プレイヤー <paramref name="player"/> が過半数を占拠している（＝勝利条件を満たす）か。</summary>
        public bool HasMajority(int player)
        {
            return Total > 0 && CountOwnedBy(player) >= RequiredToWin;
        }

        public void Dispose()
        {
            foreach (ReactiveProperty<int> owner in _owners.Values)
            {
                owner.Dispose();
            }
            _owners.Clear();
        }
    }
}
