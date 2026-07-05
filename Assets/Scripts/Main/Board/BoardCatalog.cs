using System.Collections.Generic;
using UnityEngine;

namespace Main.Board
{
    /// <summary>
    /// 選択可能なマップ（<see cref="BoardDefinition"/>）の一覧を持つカタログ。
    /// <see cref="Common.Character.CharacterCatalog"/> のマップ版。BoardDefinition は
    /// ScriptableObject 資産なので静的クラスからは参照できず、カタログ自身も ScriptableObject にして
    /// 資産参照のリストを保持する。マップの識別子には資産名（<see cref="Object.name"/>）を使う
    /// （<see cref="Common.Board.BoardSessionModel"/> が保持する識別子と対応させる）。
    /// </summary>
    [CreateAssetMenu(menuName = "Sugoroku/Board Catalog", fileName = "BoardCatalog")]
    public sealed class BoardCatalog : ScriptableObject
    {
        [SerializeField] private List<BoardDefinition> _boards = new();

        /// <summary>表示順のマップ一覧（読み取り専用）。</summary>
        public IReadOnlyList<BoardDefinition> All => _boards;

        /// <summary>登録マップが 1 枚も無いか。</summary>
        public bool IsEmpty => _boards.Count == 0;

        /// <summary>既定（先頭）のマップ。未登録なら null。</summary>
        public BoardDefinition Default => _boards.Count > 0 ? _boards[0] : null;

        /// <summary>
        /// 識別子（マップ資産名）に一致するマップを返す。見つからなければ既定（先頭）にフォールバックする。
        /// </summary>
        public BoardDefinition Find(string id)
        {
            if (!string.IsNullOrEmpty(id))
            {
                for (int i = 0; i < _boards.Count; i++)
                {
                    if (_boards[i] != null && _boards[i].name == id)
                    {
                        return _boards[i];
                    }
                }
            }
            return Default;
        }
    }
}
