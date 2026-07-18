using System.Collections.Generic;
using Main.Item;
using MiniGame.OverlapGame;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class OverlapGameModelTests
    {
        private OverlapGameModel _model;

        [SetUp]
        public void SetUp()
        {
            _model = new OverlapGameModel();
        }

        [TearDown]
        public void TearDown()
        {
            _model.Dispose();
        }

        // Countdown → Choosing まで進めて選択を受け付けられる状態にする。
        private static void BeginChoosing(OverlapGameModel model)
        {
            model.BeginCountdown();
            model.BeginChoosing();
        }

        [Test]
        public void Setupで提示枚数は人数分かつ重複なしでReadyになる()
        {
            _model.Setup(playerCount: 2, seed: 1);

            Assert.AreEqual(OverlapGamePhase.Ready, _model.Phase.CurrentValue);
            Assert.AreEqual(2, _model.PlayerCount);
            Assert.AreEqual(2, _model.OfferedItems.Count);
            Assert.AreEqual(-1, _model.PlayerChoiceIndex);
            CollectionAssert.AllItemsAreUnique(_model.OfferedItems);
        }

        [Test]
        public void 提示枚数はアイテムカタログ総数を超えない()
        {
            // 参加者数がカタログ総数より多くても、重複なしで配れる上限（＝カタログ総数）に丸める。
            int catalogCount = ItemCatalog.All.Count;
            _model.Setup(playerCount: catalogCount + 3, seed: 1);

            Assert.AreEqual(catalogCount, _model.OfferedItems.Count);
            CollectionAssert.AllItemsAreUnique(_model.OfferedItems);
        }

        [Test]
        public void CPUの選択は人数マイナス1でありいずれも提示範囲内()
        {
            _model.Setup(playerCount: 3, seed: 7);

            Assert.AreEqual(2, _model.CpuChoiceIndices.Count);
            foreach (int index in _model.CpuChoiceIndices)
            {
                Assert.GreaterOrEqual(index, 0);
                Assert.Less(index, _model.OfferedItems.Count);
            }
        }

        [Test]
        public void ChooseはChoosing中のみ有効でRevealedへ進む()
        {
            _model.Setup(playerCount: 2, seed: 1);

            // Ready 中は無効
            Assert.IsFalse(_model.Choose(0));
            Assert.AreEqual(-1, _model.PlayerChoiceIndex);

            BeginChoosing(_model);

            Assert.IsTrue(_model.Choose(1));
            Assert.AreEqual(1, _model.PlayerChoiceIndex);
            Assert.AreEqual(OverlapGamePhase.Revealed, _model.Phase.CurrentValue);

            // 一度確定したら再選択は無効
            Assert.IsFalse(_model.Choose(0));
            Assert.AreEqual(1, _model.PlayerChoiceIndex);
        }

        [Test]
        public void 範囲外の選択は無効()
        {
            _model.Setup(playerCount: 2, seed: 1);
            BeginChoosing(_model);

            Assert.IsFalse(_model.Choose(-1));
            Assert.IsFalse(_model.Choose(_model.OfferedItems.Count));
            Assert.AreEqual(-1, _model.PlayerChoiceIndex);
            Assert.AreEqual(OverlapGamePhase.Choosing, _model.Phase.CurrentValue);
        }

        [Test]
        public void CPUと被らない選択なら勝ち()
        {
            _model.Setup(playerCount: 2, seed: 1);
            int safeIndex = FindIndex(_model, chosenByCpu: false);
            BeginChoosing(_model);

            Assert.IsTrue(_model.Choose(safeIndex));
            Assert.IsTrue(_model.IsPlayerWin);
        }

        [Test]
        public void CPUと被る選択なら負け()
        {
            _model.Setup(playerCount: 2, seed: 1);
            int overlappedIndex = FindIndex(_model, chosenByCpu: true);
            BeginChoosing(_model);

            Assert.IsTrue(_model.Choose(overlappedIndex));
            Assert.IsFalse(_model.IsPlayerWin);
        }

        [Test]
        public void 未選択なら勝ちにならない()
        {
            _model.Setup(playerCount: 2, seed: 1);
            Assert.IsFalse(_model.IsPlayerWin);
        }

        [Test]
        public void TimeOutはChoosing中のみ有効で無効票のままRevealedへ進む()
        {
            _model.Setup(playerCount: 2, seed: 1);

            // Ready 中の TimeOut は無効
            _model.TimeOut();
            Assert.AreEqual(OverlapGamePhase.Ready, _model.Phase.CurrentValue);

            BeginChoosing(_model);
            _model.TimeOut();

            Assert.AreEqual(OverlapGamePhase.Revealed, _model.Phase.CurrentValue);
            Assert.AreEqual(-1, _model.PlayerChoiceIndex);
            Assert.IsTrue(_model.IsPlayerVoteInvalid);
            Assert.IsFalse(_model.IsPlayerWin);
        }

        [Test]
        public void TimeOut後の選択は無効()
        {
            _model.Setup(playerCount: 2, seed: 1);
            BeginChoosing(_model);
            _model.TimeOut();

            Assert.IsFalse(_model.Choose(0));
            Assert.AreEqual(-1, _model.PlayerChoiceIndex);
        }

        [Test]
        public void 選択済みなら無効票ではない()
        {
            _model.Setup(playerCount: 2, seed: 1);
            BeginChoosing(_model);
            _model.Choose(0);

            Assert.IsFalse(_model.IsPlayerVoteInvalid);
        }

        [Test]
        public void FinishはRevealedからのみFinishedへ進む()
        {
            _model.Setup(playerCount: 2, seed: 1);

            // Revealed 前の Finish は無効
            _model.Finish();
            Assert.AreEqual(OverlapGamePhase.Ready, _model.Phase.CurrentValue);

            BeginChoosing(_model);
            _model.Choose(0);
            _model.Finish();
            Assert.AreEqual(OverlapGamePhase.Finished, _model.Phase.CurrentValue);
        }

        [Test]
        public void 同一シードなら提示アイテムとCPU選択が再現する()
        {
            OverlapGameModel a = new();
            OverlapGameModel b = new();
            try
            {
                a.Setup(playerCount: 3, seed: 2024);
                b.Setup(playerCount: 3, seed: 2024);

                CollectionAssert.AreEqual(a.OfferedItems, b.OfferedItems);
                CollectionAssert.AreEqual(a.CpuChoiceIndices, b.CpuChoiceIndices);
            }
            finally
            {
                a.Dispose();
                b.Dispose();
            }
        }

        // CPU に選ばれている／いない提示 index を 1 つ返す（2 枚提示＋CPU1 なら必ず両方存在する）。
        private static int FindIndex(OverlapGameModel model, bool chosenByCpu)
        {
            IReadOnlyList<ItemId> offered = model.OfferedItems;
            for (int i = 0; i < offered.Count; i++)
            {
                if (model.IsChosenByCpu(i) == chosenByCpu)
                {
                    return i;
                }
            }
            Assert.Fail(chosenByCpu ? "CPU が選んだ index が見つからない" : "CPU に選ばれていない index が見つからない");
            return -1;
        }
    }
}
