using Main.Online;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class GameActionCodecTests
    {
        [Test]
        public void Spinは往復しても種別と席とセクターと減速時間が保たれる()
        {
            string json = GameActionCodec.Encode(GameAction.Spin(2, 7, 2800));

            Assert.IsTrue(GameActionCodec.TryDecode(json, out GameAction decoded));
            Assert.AreEqual(GameActionType.Spin, decoded.Type);
            Assert.AreEqual(2, decoded.Seat);
            Assert.AreEqual(7, decoded.Sector);
            Assert.AreEqual(2800, decoded.StopMillis);
        }

        [Test]
        public void SpinStartは引数なしで往復する()
        {
            // 回し始めの合図。受信側はこれで自分の円盤も回し始める（結果待ちで画面が止まらないように）。
            string json = GameActionCodec.Encode(GameAction.SpinStart(1));

            Assert.IsTrue(GameActionCodec.TryDecode(json, out GameAction decoded));
            Assert.AreEqual(GameActionType.SpinStart, decoded.Type);
            Assert.AreEqual(1, decoded.Seat);
            Assert.AreEqual(0, decoded.ArgCount);
        }

        [Test]
        public void MoneyLandingは負の増減額も往復する()
        {
            string json = GameActionCodec.Encode(GameAction.MoneyLanding(1, -300));

            Assert.IsTrue(GameActionCodec.TryDecode(json, out GameAction decoded));
            Assert.AreEqual(GameActionType.MoneyLanding, decoded.Type);
            Assert.AreEqual(1, decoded.Seat);
            Assert.AreEqual(-300, decoded.MoneyDelta);
        }

        [Test]
        public void ShopResultは買わなかったことも往復する()
        {
            string bought = GameActionCodec.Encode(GameAction.ShopResult(0, 3));
            string skipped = GameActionCodec.Encode(GameAction.ShopResult(0, -1));

            Assert.IsTrue(GameActionCodec.TryDecode(bought, out GameAction boughtAction));
            Assert.AreEqual(3, boughtAction.ShopItemId);

            Assert.IsTrue(GameActionCodec.TryDecode(skipped, out GameAction skippedAction));
            Assert.AreEqual(-1, skippedAction.ShopItemId);
        }

        [Test]
        public void ItemUseは効果パラメータの並び順を保つ()
        {
            // お金よこどりの「席ごとの奪取額」は並び順が席 index そのものなので、順序が崩れると誤送金になる。
            string json = GameActionCodec.Encode(GameAction.ItemUse(1, 5, 0, 120, 0, 340));

            Assert.IsTrue(GameActionCodec.TryDecode(json, out GameAction decoded));
            Assert.AreEqual(GameActionType.ItemUse, decoded.Type);
            Assert.AreEqual(1, decoded.Seat);
            Assert.AreEqual(5, decoded.UsedItemId);
            Assert.AreEqual(4, decoded.EffectArgCount);
            Assert.AreEqual(0, decoded.EffectArgAt(0));
            Assert.AreEqual(120, decoded.EffectArgAt(1));
            Assert.AreEqual(0, decoded.EffectArgAt(2));
            Assert.AreEqual(340, decoded.EffectArgAt(3));
        }

        [Test]
        public void 効果パラメータの無いItemUseも往復する()
        {
            string json = GameActionCodec.Encode(GameAction.ItemUse(3, 9));

            Assert.IsTrue(GameActionCodec.TryDecode(json, out GameAction decoded));
            Assert.AreEqual(9, decoded.UsedItemId);
            Assert.AreEqual(0, decoded.EffectArgCount);
        }

        [Test]
        public void Leaveも往復する()
        {
            string json = GameActionCodec.Encode(GameAction.Leave(-1));

            Assert.IsTrue(GameActionCodec.TryDecode(json, out GameAction decoded));
            Assert.AreEqual(GameActionType.Leave, decoded.Type);
            Assert.AreEqual(-1, decoded.Seat);
        }

        [Test]
        public void 壊れたJSONや未知の種別はデコードに失敗する()
        {
            Assert.IsFalse(GameActionCodec.TryDecode(null, out _));
            Assert.IsFalse(GameActionCodec.TryDecode(string.Empty, out _));
            Assert.IsFalse(GameActionCodec.TryDecode("{", out _));
            // 将来のバージョンが増やした種別が届いても落ちずに無視できること。
            Assert.IsFalse(GameActionCodec.TryDecode("{\"type\":99,\"seat\":0,\"args\":[]}", out _));
        }

        [Test]
        public void 範囲外のパラメータ参照は既定値を返す()
        {
            GameAction action = GameAction.Spin(0, 4);

            Assert.AreEqual(0, action.ArgAt(5));
            Assert.AreEqual(-1, action.ArgAt(5, -1));
            Assert.AreEqual(0, action.EffectArgAt(3));
        }
    }
}
