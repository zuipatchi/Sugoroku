using System;
using Common.MiniGame;
using UnityEngine;

namespace Main.Board
{
    /// <summary>
    /// 盤面上の 1 マスの定義。<see cref="BoardDefinition"/> が経路順に並べて保持する。
    /// 位置（方眼上の列・行）・イベント・見た目（色／アイコン）を持つ。
    /// 実行時は読み取り専用のプロパティで参照し、値の編集は盤面エディタが Set 系メソッドで行う。
    /// </summary>
    [Serializable]
    public sealed class BoardCellDefinition
    {
        /// <summary>透明色。色を「未設定」（＝USS の既定色を使う）とみなす番兵。</summary>
        public static readonly Color UnsetColor = new(0f, 0f, 0f, 0f);

        [SerializeField] private Vector2Int _grid;
        [SerializeField] private BoardCellEvent _event = BoardCellEvent.None;
        [SerializeField] private int _amount = 1;
        [SerializeField] private MiniGameId _miniGame = MiniGameId.Tap;
        [SerializeField] private Color _color = new(0f, 0f, 0f, 0f);

        public BoardCellDefinition()
        {
        }

        public BoardCellDefinition(Vector2Int grid)
        {
            _grid = grid;
        }

        /// <summary>方眼キャンバス上の位置（列・行）。</summary>
        public Vector2Int Grid => _grid;

        /// <summary>このマスに割り当てられたイベント。</summary>
        public BoardCellEvent Event => _event;

        /// <summary>イベントの数値パラメータ（進む／戻るマス数）。</summary>
        public int Amount => _amount;

        /// <summary>イベントが <see cref="BoardCellEvent.MiniGame"/> のとき起動するミニゲーム。</summary>
        public MiniGameId MiniGame => _miniGame;

        /// <summary>マスの塗り色。<see cref="HasCustomColor"/> が false のときは USS の既定色を使う。</summary>
        public Color Color => _color;

        /// <summary>塗り色が明示指定されているか（アルファ > 0）。</summary>
        public bool HasCustomColor => _color.a > 0f;

        // --- 以下は盤面エディタ専用の編集 API。実行時のゲームロジックからは呼ばない。---

        public void SetGrid(Vector2Int grid)
        {
            _grid = grid;
        }

        public void SetEvent(BoardCellEvent value)
        {
            _event = value;
        }

        public void SetAmount(int amount)
        {
            _amount = amount;
        }

        public void SetMiniGame(MiniGameId id)
        {
            _miniGame = id;
        }

        public void SetColor(Color color)
        {
            _color = color;
        }
    }
}
