using System.Linq;
using CaseFileOmission.Reports;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class CaseBoardStateTests
    {
        private static CaseBoardState NewBoard(params string[] ids)
        {
            var board = new CaseBoardState("c1", 1234);
            board.SyncMaterials(ids);
            return board;
        }

        [Test]
        public void Sync_CreatesOneItemPerMaterial_AndNeverDuplicates()
        {
            var board = NewBoard("a", "b", "a");
            board.SyncMaterials(new[] { "a", "b", "c" });

            Assert.AreEqual(1, board.Items.Count(i => i.ItemId == "a"));
            Assert.AreEqual(3, board.Items.Count(i => !i.BoardDecorationOnly));
            Assert.AreEqual(3, board.Items.Count(i => i.BoardDecorationOnly), "장식 항목은 처음 한 번만 만든다.");
        }

        [Test]
        public void InitialLayout_IsReproducibleFromSeed_AndReentryKeepsPositions()
        {
            var first = NewBoard("a", "b", "c", "d");
            var second = NewBoard("a", "b", "c", "d");
            foreach (var item in first.Items)
            {
                var twin = second.Get(item.ItemId);
                Assert.AreEqual(item.X, twin.X, 1e-6);
                Assert.AreEqual(item.Y, twin.Y, 1e-6);
            }

            first.Move("a", 0.2f, 0.3f);
            first.SyncMaterials(new[] { "a", "b", "c", "d" });
            Assert.AreEqual(0.2f, first.Get("a").X, 1e-6, "재진입해도 새 배치를 만들지 않는다.");
        }

        [Test]
        public void InitialLayout_StaysInsideBoard_AndAvoidsCentralMemo()
        {
            var board = NewBoard(Enumerable.Range(0, 12).Select(i => "m" + i).ToArray());
            foreach (var item in board.Items)
            {
                Assert.That(item.X, Is.InRange(CaseBoardState.EdgeMargin, 1f - CaseBoardState.EdgeMargin));
                Assert.That(item.Y, Is.InRange(CaseBoardState.EdgeMargin, 1f - CaseBoardState.EdgeMargin));
                bool inMemo = item.X > CaseBoardState.MemoMinX && item.X < CaseBoardState.MemoMaxX
                              && item.Y > CaseBoardState.MemoMinY && item.Y < CaseBoardState.MemoMaxY;
                Assert.IsFalse(inMemo, item.ItemId);
            }
        }

        [Test]
        public void Links_AreUniquePerPair_AndMemoVisibleOnlyWithLinks()
        {
            var board = NewBoard("a", "b", "c");
            Assert.IsFalse(board.IsMemoVisible);
            Assert.IsEmpty(board.BuildMemoLines(id => id));

            Assert.IsTrue(board.Connect("a", "b"));
            Assert.IsFalse(board.Connect("b", "a"), "한 쌍에는 연결선 하나만.");
            Assert.IsFalse(board.Connect("a", "a"));
            Assert.IsFalse(board.Connect("a", "decoration-0"), "장식 항목은 연결하지 않는다.");
            Assert.IsTrue(board.IsMemoVisible);
            CollectionAssert.AreEqual(new[] { "a — b" }, board.BuildMemoLines(id => id));

            Assert.IsTrue(board.Disconnect("b", "a"));
            Assert.IsFalse(board.IsMemoVisible);
        }

        [Test]
        public void Discard_HidesItemAndRemovesItsLinks_RestoreBringsSameItemBack()
        {
            var board = NewBoard("a", "b", "c");
            board.Connect("a", "b");
            board.Connect("b", "c");

            Assert.IsTrue(board.Discard("b"));
            Assert.IsFalse(board.Get("b").OnBoard);
            Assert.IsEmpty(board.Links, "폐기 항목의 연결선은 모두 지운다.");
            Assert.IsFalse(board.IsMemoVisible);

            Assert.IsTrue(board.Restore("b"));
            Assert.AreEqual(1, board.Items.Count(i => i.ItemId == "b"));
            Assert.IsTrue(board.Get("b").OnBoard);
        }
    }
}
