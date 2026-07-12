using System;
using System.Collections.Generic;
using Common.GameSession;
using Main.Card;
using Main.Turn;
using NUnit.Framework;
using R3;

namespace Tests.EditMode
{
    public class CardModelTests
    {
        private static CardModel TwoPlayerCards()
        {
            GameSessionModel session = new();
            session.SetSinglePlayer();
            return new CardModel(new GameParticipants(session));
        }

        [Test]
        public void 初期状態は全プレイヤーの手札が空()
        {
            using CardModel cards = TwoPlayerCards();
            Assert.AreEqual(2, cards.PlayerCount);
            Assert.AreEqual(0, cards.Cards(0).Count);
            Assert.AreEqual(0, cards.Cards(1).Count);
        }

        [Test]
        public void Addで手札に取得順で貯まる()
        {
            using CardModel cards = TwoPlayerCards();
            cards.Add(0, CardId.StealTerritory);
            cards.Add(0, CardId.StealTerritory);
            Assert.AreEqual(2, cards.Cards(0).Count);
            Assert.AreEqual(CardId.StealTerritory, cards.Cards(0)[0]);
        }

        [Test]
        public void Addはプレイヤーごとに独立している()
        {
            using CardModel cards = TwoPlayerCards();
            cards.Add(1, CardId.StealTerritory);
            Assert.AreEqual(1, cards.Cards(1).Count);
            Assert.AreEqual(0, cards.Cards(0).Count);
        }

        [Test]
        public void Addで取得通知が飛ぶ()
        {
            using CardModel cards = TwoPlayerCards();
            List<CardGain> received = new();
            using IDisposable _ = cards.Gained.Subscribe(received.Add);

            cards.Add(1, CardId.StealTerritory);

            Assert.AreEqual(1, received.Count);
            Assert.AreEqual(1, received[0].Player);
            Assert.AreEqual(CardId.StealTerritory, received[0].Card);
        }
    }
}
