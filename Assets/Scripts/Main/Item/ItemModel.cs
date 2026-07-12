using System;
using System.Collections.Generic;
using Main.Turn;
using R3;

namespace Main.Item
{
    /// <summary>
    /// プレイヤー 1 人がアイテムを取得したことを表す通知。
    /// </summary>
    public readonly struct ItemGain
    {
        public ItemGain(int player, ItemId item)
        {
            Player = player;
            Item = item;
        }

        /// <summary>取得したプレイヤー index。</summary>
        public int Player { get; }

        /// <summary>取得したアイテム。</summary>
        public ItemId Item { get; }
    }

    /// <summary>
    /// 各プレイヤーが取得したアイテム（手札）を保持する Model。アイテム取得マス
    /// （<see cref="Board.BoardCellEvent.Item"/>）に止まると <see cref="Add"/> で 1 つ加わる。
    /// UI は人間プレイヤーの手札を <see cref="Gained"/> の購読で受け取り、右下に表示する。
    /// アイテムの「使用（効果発動）」は将来対応する。<see cref="Money.MoneyModel"/> と同じく参加者ごとに持つ。
    /// </summary>
    public sealed class ItemModel : IDisposable
    {
        private readonly List<ItemId>[] _hands;
        private readonly Subject<ItemGain> _gained = new();

        public ItemModel(GameParticipants participants)
        {
            int count = participants.Count;
            _hands = new List<ItemId>[count];
            for (int i = 0; i < count; i++)
            {
                _hands[i] = new List<ItemId>();
            }
        }

        /// <summary>手札を持つプレイヤーの数。</summary>
        public int PlayerCount => _hands.Length;

        /// <summary>アイテム取得の通知（取得したプレイヤーとアイテム）。</summary>
        public Observable<ItemGain> Gained => _gained;

        /// <summary>プレイヤー <paramref name="player"/> の手札（取得順）。</summary>
        public IReadOnlyList<ItemId> Items(int player) => _hands[player];

        /// <summary>プレイヤー <paramref name="player"/> にアイテム <paramref name="item"/> を 1 つ加え、取得を通知する。</summary>
        public void Add(int player, ItemId item)
        {
            _hands[player].Add(item);
            _gained.OnNext(new ItemGain(player, item));
        }

        public void Dispose()
        {
            _gained.Dispose();
        }
    }
}
