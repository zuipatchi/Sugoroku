using System.Collections.Generic;
using Common.Character;

namespace Common.GameSession
{
    /// <summary>
    /// オンラインのキャラ選択ロビーで確定した「席順→キャラ」の割り当てと、
    /// 自分の席 index をシーンをまたいで保持する Common シングルトン。
    /// <see cref="CharacterSessionModel"/> のオンライン多人数版で、Main 側が席ごとに正しい
    /// （被らない）キャラを描画し、自分の席を「あなた」として扱うために使う。
    /// 一人用モードでは設定されない（<see cref="HasRoster"/> が false のまま）。
    /// </summary>
    public sealed class OnlineRosterSessionModel
    {
        private CharacterId[] _seats;

        /// <summary>自分の席 index（0 始まり）。ロースター未設定なら 0。</summary>
        public int MySeat { get; private set; }

        /// <summary>ロビーで席割り当てが確定しているか。</summary>
        public bool HasRoster => _seats != null;

        /// <summary>席数（＝参加者数）。未設定なら 0。</summary>
        public int Count => _seats?.Length ?? 0;

        /// <summary>席 <paramref name="seat"/> に割り当てられたキャラ。</summary>
        public CharacterId CharacterAt(int seat)
        {
            return _seats[seat];
        }

        /// <summary>ロビー確定時に、席順のキャラ一覧と自分の席 index を保存する。</summary>
        public void SetRoster(IReadOnlyList<CharacterId> seats, int mySeat)
        {
            CharacterId[] copy = new CharacterId[seats.Count];
            for (int i = 0; i < seats.Count; i++)
            {
                copy[i] = seats[i];
            }
            _seats = copy;
            MySeat = mySeat;
        }

        /// <summary>ロースターを破棄する（ホームへ戻る等でセッションを離れたとき）。</summary>
        public void Clear()
        {
            _seats = null;
            MySeat = 0;
        }
    }
}
