using System;
using System.Collections.Generic;
using Common.GameSession;
using Main.Item;
using Main.Turn;
using NUnit.Framework;
using R3;

namespace Tests.EditMode
{
    public class ItemModelTests
    {
        private static ItemModel TwoPlayerItems()
        {
            GameSessionModel session = new();
            session.SetSinglePlayer();
            return new ItemModel(new GameParticipants(session));
        }

        [Test]
        public void 初期状態は全プレイヤーの手札が空()
        {
            using ItemModel items = TwoPlayerItems();
            Assert.AreEqual(2, items.PlayerCount);
            Assert.AreEqual(0, items.Items(0).Count);
            Assert.AreEqual(0, items.Items(1).Count);
        }

        [Test]
        public void Addで手札に取得順で貯まる()
        {
            using ItemModel items = TwoPlayerItems();
            items.Add(0, ItemId.StealTerritory);
            items.Add(0, ItemId.StealTerritory);
            Assert.AreEqual(2, items.Items(0).Count);
            Assert.AreEqual(ItemId.StealTerritory, items.Items(0)[0]);
        }

        [Test]
        public void Addはプレイヤーごとに独立している()
        {
            using ItemModel items = TwoPlayerItems();
            items.Add(1, ItemId.StealTerritory);
            Assert.AreEqual(1, items.Items(1).Count);
            Assert.AreEqual(0, items.Items(0).Count);
        }

        [Test]
        public void Addで取得通知が飛ぶ()
        {
            using ItemModel items = TwoPlayerItems();
            List<ItemGain> received = new();
            using IDisposable _ = items.Gained.Subscribe(received.Add);

            items.Add(1, ItemId.StealTerritory);

            Assert.AreEqual(1, received.Count);
            Assert.AreEqual(1, received[0].Player);
            Assert.AreEqual(ItemId.StealTerritory, received[0].Item);
        }

        [Test]
        public void Useで手札から1つ減る()
        {
            using ItemModel items = TwoPlayerItems();
            items.Add(0, ItemId.StealTerritory);
            items.Add(0, ItemId.StealTerritory);

            bool used = items.Use(0, ItemId.StealTerritory);

            Assert.IsTrue(used);
            Assert.AreEqual(1, items.Items(0).Count);
        }

        [Test]
        public void Useで使用通知が飛ぶ()
        {
            using ItemModel items = TwoPlayerItems();
            items.Add(1, ItemId.StealMoney);
            List<ItemUse> received = new();
            using IDisposable _ = items.Used.Subscribe(received.Add);

            items.Use(1, ItemId.StealMoney);

            Assert.AreEqual(1, received.Count);
            Assert.AreEqual(1, received[0].Player);
            Assert.AreEqual(ItemId.StealMoney, received[0].Item);
        }

        [Test]
        public void 持っていないアイテムのUseは失敗して通知も飛ばない()
        {
            using ItemModel items = TwoPlayerItems();
            items.Add(0, ItemId.StealTerritory);
            List<ItemUse> received = new();
            using IDisposable _ = items.Used.Subscribe(received.Add);

            bool used = items.Use(0, ItemId.MiniGame);

            Assert.IsFalse(used);
            Assert.AreEqual(0, received.Count);
            Assert.AreEqual(1, items.Items(0).Count);
        }

        [Test]
        public void Useは他プレイヤーの手札に影響しない()
        {
            using ItemModel items = TwoPlayerItems();
            items.Add(0, ItemId.StealTerritory);
            items.Add(1, ItemId.StealTerritory);

            items.Use(0, ItemId.StealTerritory);

            Assert.AreEqual(0, items.Items(0).Count);
            Assert.AreEqual(1, items.Items(1).Count);
        }
    }
}
