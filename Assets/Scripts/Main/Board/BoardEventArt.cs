using System;
using UnityEngine;

namespace Main.Board
{
    /// <summary>
    /// イベント種別 1 つに割り当てる画像アドレスの対応。<see cref="BoardDefinition"/> が
    /// 盤面ごとに一覧で保持し、盤面エディタで編集する。該当イベントのマスすべてに同じ画像を貼る。
    /// </summary>
    [Serializable]
    public sealed class BoardEventArt
    {
        [SerializeField] private BoardCellEvent _event;
        [SerializeField] private string _address = string.Empty;

        public BoardEventArt()
        {
        }

        public BoardEventArt(BoardCellEvent cellEvent, string address)
        {
            _event = cellEvent;
            _address = address ?? string.Empty;
        }

        /// <summary>対応するイベント種別。</summary>
        public BoardCellEvent Event => _event;

        /// <summary>このイベントのマスに貼る画像の Addressables アドレス。</summary>
        public string Address => _address;

        public void SetAddress(string address)
        {
            _address = address ?? string.Empty;
        }
    }
}
