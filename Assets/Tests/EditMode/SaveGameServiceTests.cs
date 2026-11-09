using System.Linq;
using CaseFileOmission.Core;
using CaseFileOmission.Endings;
using CaseFileOmission.MainCase;
using CaseFileOmission.Organization;
using CaseFileOmission.Police;
using CaseFileOmission.SaveLoad;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class SaveGameServiceTests
    {
        private const string PlayerId = "player";

        private sealed class Fixture
        {
            public GameStateController GameState = new GameStateController();
            public OrganizationState Organization = new OrganizationState();
            public PoliceSuspicionService PoliceSuspicion = new PoliceSuspicionService();
            public PoliceLifeStateService PoliceLifeState = new PoliceLifeStateService();
            public MainCaseProgressTracker MainCaseProgress = new MainCaseProgressTracker();
            public EndingExecutor EndingExecutor =
                new EndingExecutor(new GameEndEvaluator(new PoliceSuspicionService(), new OrganizationState()));
        }

        [Test]
        public void Capture_ThenRestore_RoundTripsCoreState()
        {
            var source = new Fixture();
            source.GameState.BeginContract("c1");
            source.GameState.AdvanceTimeBlock(); // -> 오후

            var data = SaveGameService.Capture(
                source.GameState, source.Organization, source.PoliceSuspicion,
                source.PoliceLifeState, source.MainCaseProgress, source.EndingExecutor, PlayerId);

            var target = new Fixture();
            SaveGameService.Restore(data, target.GameState, target.Organization, target.PoliceSuspicion,
                target.PoliceLifeState, target.MainCaseProgress, target.EndingExecutor, PlayerId);

            Assert.AreEqual("c1", target.GameState.CurrentContractId);
            Assert.AreEqual(GamePhase.Investigation, target.GameState.CurrentPhase);
            Assert.AreEqual(TimeBlock.Afternoon, target.GameState.CurrentTimeBlock);
            Assert.AreEqual(1, target.GameState.CurrentDay);
        }

        [Test]
        public void Capture_ThenRestore_RoundTripsOrganizationTrust()
        {
            var source = new Fixture();
            source.Organization.ApplyConditionAchieved();
            source.Organization.ApplyConditionAchieved();

            var data = SaveGameService.Capture(
                source.GameState, source.Organization, source.PoliceSuspicion,
                source.PoliceLifeState, source.MainCaseProgress, source.EndingExecutor, PlayerId);

            var target = new Fixture();
            SaveGameService.Restore(data, target.GameState, target.Organization, target.PoliceSuspicion,
                target.PoliceLifeState, target.MainCaseProgress, target.EndingExecutor, PlayerId);

            Assert.AreEqual(source.Organization.Trust, target.Organization.Trust);
        }

        [Test]
        public void Capture_ThenRestore_RoundTripsPoliceSuspicionAndLifeState()
        {
            var source = new Fixture();
            source.PoliceSuspicion.ApplyBatch(PoliceRoster.PoliceA, PlayerId, new[] { 42f });
            source.PoliceLifeState.MarkDeceased(PoliceRoster.PoliceB);

            var data = SaveGameService.Capture(
                source.GameState, source.Organization, source.PoliceSuspicion,
                source.PoliceLifeState, source.MainCaseProgress, source.EndingExecutor, PlayerId);

            var target = new Fixture();
            SaveGameService.Restore(data, target.GameState, target.Organization, target.PoliceSuspicion,
                target.PoliceLifeState, target.MainCaseProgress, target.EndingExecutor, PlayerId);

            Assert.AreEqual(42f, target.PoliceSuspicion.GetSuspicion(PoliceRoster.PoliceA, PlayerId));
            Assert.IsFalse(target.PoliceLifeState.IsAlive(PoliceRoster.PoliceB));
            Assert.IsTrue(target.PoliceLifeState.IsAlive(PoliceRoster.PoliceA));
        }

        [Test]
        public void Capture_ThenRestore_RoundTripsMainCaseProgress()
        {
            var source = new Fixture();
            source.MainCaseProgress.AcquireClue(MainCaseClueNames.NieceDiary);
            source.MainCaseProgress.AcquireClue(MainCaseClueNames.NieceDeathReport);
            source.MainCaseProgress.AcquireClue(MainCaseClueNames.FamilyNotificationRecord);
            source.MainCaseProgress.TryAdvanceStage();

            var data = SaveGameService.Capture(
                source.GameState, source.Organization, source.PoliceSuspicion,
                source.PoliceLifeState, source.MainCaseProgress, source.EndingExecutor, PlayerId);

            var target = new Fixture();
            SaveGameService.Restore(data, target.GameState, target.Organization, target.PoliceSuspicion,
                target.PoliceLifeState, target.MainCaseProgress, target.EndingExecutor, PlayerId);

            Assert.AreEqual(MainCaseTruthStage.SystemicTruth, target.MainCaseProgress.CurrentStage);
            Assert.IsTrue(target.MainCaseProgress.HasAcquired(MainCaseClueNames.NieceDiary));
        }

        [Test]
        public void Capture_ThenRestore_RoundTripsExecutedEnding_AndDoesNotReExecute()
        {
            var source = new Fixture();
            source.EndingExecutor.TryExecute(GameEndingType.PhoneCallRejected);

            var data = SaveGameService.Capture(
                source.GameState, source.Organization, source.PoliceSuspicion,
                source.PoliceLifeState, source.MainCaseProgress, source.EndingExecutor, PlayerId);

            var target = new Fixture();
            SaveGameService.Restore(data, target.GameState, target.Organization, target.PoliceSuspicion,
                target.PoliceLifeState, target.MainCaseProgress, target.EndingExecutor, PlayerId);

            Assert.AreEqual(GameEndingType.PhoneCallRejected, target.EndingExecutor.ExecutedEnding);

            // 불러온 뒤 다른 엔딩을 실행하려 해도(예: 체포) 이미 실행된 엔딩을 덮어쓰지 않는다.
            bool overwritten = target.EndingExecutor.TryExecute(GameEndingType.Arrest);
            Assert.IsFalse(overwritten);
            Assert.AreEqual(GameEndingType.PhoneCallRejected, target.EndingExecutor.ExecutedEnding);
        }

        [Test]
        public void ToJson_ThenFromJson_PreservesData()
        {
            var source = new Fixture();
            source.GameState.BeginContract("c1");
            source.Organization.ApplyConditionAchieved();

            var data = SaveGameService.Capture(
                source.GameState, source.Organization, source.PoliceSuspicion,
                source.PoliceLifeState, source.MainCaseProgress, source.EndingExecutor, PlayerId);

            string json = SaveGameService.ToJson(data);
            var restored = SaveGameService.FromJson(json);

            Assert.AreEqual(data.ContractId, restored.ContractId);
            Assert.AreEqual(data.OrganizationTrust, restored.OrganizationTrust);
        }

        [Test]
        public void Restore_UnknownPoliceId_IsSkippedSafely_AndLogged()
        {
            var data = new SaveGameData
            {
                ContractId = "c1",
                Phase = GamePhase.Investigation,
                TimeBlock = TimeBlock.Morning,
                CurrentDay = 1,
                DeceasedPoliceIds = new[] { "police-unknown" },
                PoliceSuspicions = System.Array.Empty<PoliceSuspicionEntry>(),
                AcquiredMainCaseClueNames = System.Array.Empty<string>(),
                MainCaseStage = MainCaseTruthStage.PersonalTruth,
                ExecutedEnding = GameEndingType.None
            };

            var target = new Fixture();
            var logger = new GameLogger();

            Assert.DoesNotThrow(() => SaveGameService.Restore(
                data, target.GameState, target.Organization, target.PoliceSuspicion,
                target.PoliceLifeState, target.MainCaseProgress, target.EndingExecutor, PlayerId, logger));

            Assert.IsFalse(PoliceRoster.AllPoliceIds.Contains("police-unknown"), "애초에 유효한 경찰 ID가 아님을 전제로 한다.");
            foreach (var validId in PoliceRoster.AllPoliceIds)
                Assert.IsTrue(target.PoliceLifeState.IsAlive(validId), "알 수 없는 ID 하나 때문에 다른 경찰 상태가 오염되면 안 된다.");

            Assert.AreEqual(1, logger.Entries.Count);
            Assert.AreEqual("SAVE_UNKNOWN_POLICE_ID", logger.Entries[0].ErrorCode);
        }
    }
}
