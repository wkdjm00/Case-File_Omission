using CaseFileOmission.Evidence;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class ClueRepositoryTests
    {
        [Test]
        public void Discover_NewClue_IsStoredAndHeldByPlayer()
        {
            var repo = new ClueRepository();
            var def = new ClueDefinition("clue-1", "contract-1") { DisplayName = "숟가락" };

            var record = repo.Discover(def);

            Assert.IsTrue(repo.Exists("clue-1"));
            Assert.AreEqual(ClueWorldState.HeldByPlayer, record.WorldState);
        }

        [Test]
        public void Discover_DuplicateClueId_ReturnsExistingRecord()
        {
            // 통합 구현 명세서 §4-3 확정(2026-11-09): 동일 단서를 다시 조사하면 예외가 아니라
            // 기존 기록을 반환한다. 이전 정책(예외 발생)은 이 테스트로 대체되었다.
            var repo = new ClueRepository();
            var first = repo.Discover(new ClueDefinition("clue-1", "contract-1") { DisplayName = "원본" });

            var second = repo.Discover(new ClueDefinition("clue-1", "contract-1") { DisplayName = "다른 이름으로 재조사" });

            Assert.AreSame(first, second, "동일 clueId 재발견은 같은 레코드 인스턴스를 반환해야 한다.");
            Assert.AreEqual("원본", repo.GetDefinition("clue-1").DisplayName, "최초 발견 시점의 정의가 유지되어야 한다.");
        }

        [Test]
        public void Drop_ThenPickUp_TogglesWorldStateButKeepsRecord()
        {
            var repo = new ClueRepository();
            repo.Discover(new ClueDefinition("clue-1", "contract-1"));

            repo.Drop("clue-1");
            Assert.AreEqual(ClueWorldState.DroppedInWorld, repo.GetRecord("clue-1").WorldState);

            repo.PickUpByPlayer("clue-1");
            Assert.AreEqual(ClueWorldState.HeldByPlayer, repo.GetRecord("clue-1").WorldState);

            // 버렸다 다시 주워도 보고서 기록(정의) 자체는 계속 존재한다.
            Assert.IsTrue(repo.Exists("clue-1"));
        }

        [Test]
        public void MarkHeldByNpc_ChangesWorldStateOnly_RecordStaysInReport()
        {
            var repo = new ClueRepository();
            repo.Discover(new ClueDefinition("clue-1", "contract-1"));
            repo.Drop("clue-1");

            repo.GetRecord("clue-1").MarkHeldByNpc();

            Assert.AreEqual(ClueWorldState.HeldByNpc, repo.GetRecord("clue-1").WorldState);
            Assert.IsTrue(repo.Exists("clue-1"), "NPC가 주워 가도 보고서 기록은 남는다(06 확정).");
            CollectionAssert.DoesNotContain(repo.GetHeldByPlayerClueIds("contract-1"), "clue-1");
        }

        [Test]
        public void GetHeldByPlayerClueIds_ExcludesDroppedClues()
        {
            var repo = new ClueRepository();
            repo.Discover(new ClueDefinition("clue-1", "contract-1"));
            repo.Discover(new ClueDefinition("clue-2", "contract-1"));
            repo.Drop("clue-2");

            var held = repo.GetHeldByPlayerClueIds("contract-1");

            Assert.AreEqual(1, held.Count);
            Assert.AreEqual("clue-1", held[0]);
        }

        [Test]
        public void GetAllClueIds_FiltersByContract()
        {
            var repo = new ClueRepository();
            repo.Discover(new ClueDefinition("clue-1", "contract-1"));
            repo.Discover(new ClueDefinition("clue-2", "contract-2"));

            var forContract1 = repo.GetAllClueIds("contract-1");

            Assert.AreEqual(1, forContract1.Count);
            Assert.AreEqual("clue-1", forContract1[0]);
        }

        [Test]
        public void ConfirmedByPolice_DoesNotChangeOwnership()
        {
            var repo = new ClueRepository();
            var record = repo.Discover(new ClueDefinition("clue-1", "contract-1"));

            record.MarkConfirmedByPolice("police-A");

            Assert.IsTrue(record.ConfirmedByPoliceIds.Contains("police-A"));
            Assert.AreEqual(ClueWorldState.HeldByPlayer, record.WorldState, "경찰이 확인해도 소유권(물리적 보유 상태)은 플레이어에게 남아야 한다.");
        }
    }
}
