using System;
using System.Collections.Generic;
using Main.Turn;
using R3;

namespace Main.Card
{
    /// <summary>
    /// プレイヤー 1 人がカードを取得したことを表す通知。
    /// </summary>
    public readonly struct CardGain
    {
        public CardGain(int player, CardId card)
        {
            Player = player;
            Card = card;
        }

        /// <summary>取得したプレイヤー index。</summary>
        public int Player { get; }

        /// <summary>取得したカード。</summary>
        public CardId Card { get; }
    }

    /// <summary>
    /// 各プレイヤーが取得したカード（手札）を保持する Model。カード取得マス
    /// （<see cref="Board.BoardCellEvent.Card"/>）に止まると <see cref="Add"/> で 1 枚加わる。
    /// UI は人間プレイヤーの手札を <see cref="Gained"/> の購読で受け取り、右下に表示する。
    /// カードの「使用（効果発動）」は将来対応する。<see cref="Money.MoneyModel"/> と同じく参加者ごとに持つ。
    /// </summary>
    public sealed class CardModel : IDisposable
    {
        private readonly List<CardId>[] _hands;
        private readonly Subject<CardGain> _gained = new();

        public CardModel(GameParticipants participants)
        {
            int count = participants.Count;
            _hands = new List<CardId>[count];
            for (int i = 0; i < count; i++)
            {
                _hands[i] = new List<CardId>();
            }
        }

        /// <summary>手札を持つプレイヤーの数。</summary>
        public int PlayerCount => _hands.Length;

        /// <summary>カード取得の通知（取得したプレイヤーとカード）。</summary>
        public Observable<CardGain> Gained => _gained;

        /// <summary>プレイヤー <paramref name="player"/> の手札（取得順）。</summary>
        public IReadOnlyList<CardId> Cards(int player) => _hands[player];

        /// <summary>プレイヤー <paramref name="player"/> にカード <paramref name="card"/> を 1 枚加え、取得を通知する。</summary>
        public void Add(int player, CardId card)
        {
            _hands[player].Add(card);
            _gained.OnNext(new CardGain(player, card));
        }

        public void Dispose()
        {
            _gained.Dispose();
        }
    }
}
