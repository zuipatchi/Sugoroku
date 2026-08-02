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
        public void MiniGameLandingはゲームの内容をそろえる種が往復する()
        {
            // 種がずれると被っちゃやーよの提示カードが食い違うので、負値も含めてそのまま運べること。
            string json = GameActionCodec.Encode(GameAction.MiniGameLanding(1, -123456));

            Assert.IsTrue(GameActionCodec.TryDecode(json, out GameAction decoded));
            Assert.AreEqual(GameActionType.MiniGameLanding, decoded.Type);
            Assert.AreEqual(1, decoded.Seat);
            Assert.AreEqual(-123456, decoded.MiniGameSeed);
        }

        [Test]
        public void MiniGameScoreは生の結果値が往復する()
        {
            // 結果値はゲームごとに意味が違う（連打数・ゴールタイム・選んだカード index）。
            string json = GameActionCodec.Encode(GameAction.MiniGameScore(2, 4821));

            Assert.IsTrue(GameActionCodec.TryDecode(json, out GameAction decoded));
            Assert.AreEqual(GameActionType.MiniGameScore, decoded.Type);
            Assert.AreEqual(2, decoded.Seat);
            Assert.AreEqual(4821, decoded.MiniGameValue);
        }

        [Test]
        public void Busyは待機理由と解除の両方が往復する()
        {
            // 他プレイヤーの操作待ち表示のお知らせ。None は「待機表示の解除」を意味する。
            string busy = GameActionCodec.Encode(GameAction.Busy(2, BusyReason.MiniGame));
            string cleared = GameActionCodec.Encode(GameAction.Busy(2, BusyReason.None));

            Assert.IsTrue(GameActionCodec.TryDecode(busy, out GameAction busyAction));
            Assert.AreEqual(GameActionType.Busy, busyAction.Type);
            Assert.AreEqual(2, busyAction.Seat);
            Assert.AreEqual((int)BusyReason.MiniGame, busyAction.BusyReasonId);

            Assert.IsTrue(GameActionCodec.TryDecode(cleared, out GameAction clearedAction));
            Assert.AreEqual((int)BusyReason.None, clearedAction.BusyReasonId);
        }

        [Test]
        public void 通し番号は往復する()
        {
            // 復帰したクライアントが「どこまで受け取ったか」を判断する唯一の手がかりなので、
            // 採番したぶんがそのまま相手へ届くこと。
            string json = GameActionCodec.Encode(GameAction.Spin(1, 3).WithSeq(42));

            Assert.IsTrue(GameActionCodec.TryDecode(json, out GameAction decoded));
            Assert.AreEqual(42, decoded.Seq);
            Assert.AreEqual(3, decoded.Sector);
        }

        [Test]
        public void 採番前のアクションはNoSeqのまま往復する()
        {
            // 発行された時点ではまだホストが番号を振っていない（一人用モードは最後まで振られない）。
            string json = GameActionCodec.Encode(GameAction.SpinStart(0));

            Assert.IsTrue(GameActionCodec.TryDecode(json, out GameAction decoded));
            Assert.AreEqual(GameAction.NoSeq, decoded.Seq);
        }

        [Test]
        public void WithSeqは番号だけを差し替える()
        {
            GameAction original = GameAction.ItemUse(2, 5, 11, 22);
            GameAction numbered = original.WithSeq(7);

            Assert.AreEqual(7, numbered.Seq);
            Assert.AreEqual(GameAction.NoSeq, original.Seq, "元のアクションは書き換わらないこと");
            Assert.AreEqual(original.Type, numbered.Type);
            Assert.AreEqual(original.Seat, numbered.Seat);
            Assert.AreEqual(5, numbered.UsedItemId);
            Assert.AreEqual(11, numbered.EffectArgAt(0));
            Assert.AreEqual(22, numbered.EffectArgAt(1));
        }

        [Test]
        public void 一時停止と復帰と取りこぼし申告が往復する()
        {
            string pause = GameActionCodec.Encode(GameAction.Pause(2, 60));
            string resume = GameActionCodec.Encode(GameAction.Resume(2));
            string resync = GameActionCodec.Encode(GameAction.Resync(2, 17));

            Assert.IsTrue(GameActionCodec.TryDecode(pause, out GameAction pauseAction));
            Assert.AreEqual(GameActionType.Pause, pauseAction.Type);
            Assert.AreEqual(2, pauseAction.Seat);
            Assert.AreEqual(60, pauseAction.GraceSeconds);

            Assert.IsTrue(GameActionCodec.TryDecode(resume, out GameAction resumeAction));
            Assert.AreEqual(GameActionType.Resume, resumeAction.Type);
            Assert.AreEqual(2, resumeAction.Seat);

            Assert.IsTrue(GameActionCodec.TryDecode(resync, out GameAction resyncAction));
            Assert.AreEqual(GameActionType.Resync, resyncAction.Type);
            Assert.AreEqual(17, resyncAction.LastSeq);
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
