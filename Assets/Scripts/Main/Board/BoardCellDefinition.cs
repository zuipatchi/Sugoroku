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

        /// <summary>
        /// かつてイベントの数値パラメータ（進む／戻るマス数・お金の増減額）だった値。**現在は誰も読まない**。
        /// 進む／戻るマス数は <see cref="MoveCellRule"/>、お金の増減額は <see cref="Money.MoneyCellRule"/> が
        /// 着地のたびにランダムで決めるようになり、マスごとに設定する値ではなくなった
        /// （盤面エディタからも入力欄と編集 API を外してある）。
        /// 保存済みアセットに書かれている値を捨てないよう、フィールドと読み取りだけ残してある。
        /// </summary>
        public int Amount => _amount;

        /// <summary>
        /// かつて「このマスで起動するミニゲーム」だった値。**現在は誰も読まない**。
        /// 遊ぶゲームは <see cref="MiniGameCatalog.RandomGame"/> が着地のたびに抽選するようになり、
        /// マスごとに設定する値ではなくなった（盤面エディタからも入力欄と編集 API を外してある）。
        /// マスの絵も全マス共通の <see cref="BoardEventArtCatalog.MiniGameAddress"/> になった。
        /// <see cref="Amount"/> と同じく、保存済みアセットに書かれている値を捨てないようフィールドだけ残してある。
        /// </summary>
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

        public void SetColor(Color color)
        {
            _color = color;
        }
    }
}
