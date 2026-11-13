using System;
using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.ContractContent;
using CaseFileOmission.Contracts;
using CaseFileOmission.NPC;
using CaseFileOmission.Rooms;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class ContractTimelineTests
    {
        internal const string ContractId = "c1";
        internal const string Perp = "c1-perp";
        internal const string Victim = "c1-victim";
        internal const string Witness = "c1-witness";
        internal const string RelatedA = "c1-related-a";
        internal const string RelatedB = "c1-related-b";

        internal static (ContractInstance contract, NpcRegistry registry, RoomGraph rooms) BuildCase(
            CrimeType crime, CoverUpMethod coverUp = CoverUpMethod.None, bool includeWitness = true, bool includeRelated = true)
        {
            var contract = new ContractInstance(ContractId) { PrimaryCrimeType = crime };
            contract.Truth.PerpetratorId = Perp;
            contract.Truth.VictimId = Victim;
            contract.Truth.CoverUp = coverUp;

            var registry = new NpcRegistry();
            registry.Add(new NpcState(Perp, ContractId, NpcActualRole.Perpetrator, NpcPersonality.Greedy));
            registry.Add(new NpcState(Victim, ContractId, NpcActualRole.Victim, NpcPersonality.Timid));
            if (includeWitness)
                registry.Add(new NpcState(Witness, ContractId, NpcActualRole.Witness, NpcPersonality.Suspicious));
            if (includeRelated)
            {
                registry.Add(new NpcState(RelatedA, ContractId, NpcActualRole.CaseRelatedA, NpcPersonality.Secretive));
                registry.Add(new NpcState(RelatedB, ContractId, NpcActualRole.CaseRelatedB, NpcPersonality.Indifferent));
            }

            return (contract, registry, HouseLayoutFactory.CreateStandardHouse());
        }

        internal static ContractTimeline GenerateValid(int seed, CrimeType crime, CoverUpMethod coverUp = CoverUpMethod.None)
        {
            var (contract, registry, rooms) = BuildCase(crime, coverUp);
            var result = new ContractTimelineGenerator(new Random(seed)).Generate(contract, registry, rooms);
            Assert.IsTrue(result.Success, $"seed {seed} {crime}: {string.Join(",", result.Errors)}");
            return result.Timeline;
        }

        internal static int FindSeed(CrimeType crime, Func<ContractTimeline, bool> predicate)
        {
            for (int seed = 0; seed < 200; seed++)
            {
                if (predicate(GenerateValid(seed, crime)))
                    return seed;
            }
            Assert.Fail("조건에 맞는 시드를 찾지 못했다.");
            return -1;
        }

        [Test]
        public void AllCrimeTypes_ManySeeds_ProduceValidTimelines()
        {
            foreach (CrimeType crime in Enum.GetValues(typeof(CrimeType)))
            {
                for (int seed = 0; seed < 30; seed++)
                {
                    var (contract, registry, rooms) = BuildCase(crime, seed % 3 == 0 ? CoverUpMethod.AccidentalDeathDisguise : CoverUpMethod.None);
                    var result = new ContractTimelineGenerator(new Random(seed)).Generate(contract, registry, rooms);

                    Assert.IsTrue(result.Success, $"seed {seed} {crime}: {string.Join(",", result.Errors)}");
                    Assert.IsTrue(new ContractTimelineValidator().Validate(result.Timeline, contract, registry, rooms).IsValid);
                    Assert.LessOrEqual(result.AttemptsUsed, 6, "요소 재시도는 최대 5회(최초 1회 + 5회).");
                }
            }
        }

        [Test]
        public void Murder_VictimAndPerpetratorCoPresent_WitnessActuallyWitnesses_VictimHasNoLaterSchedule()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var t = GenerateValid(seed, CrimeType.Murder);
                var core = t.CoreIncident;

                Assert.AreEqual(t.SceneRoomId, t.GetRoomAt(Perp, core.StartMinute));
                Assert.AreEqual(t.SceneRoomId, t.GetRoomAt(Victim, core.StartMinute));
                Assert.AreEqual(t.SceneRoomId, t.GetRoomAt(Witness, core.StartMinute));
                Assert.IsNull(t.GetRoomAt(Victim, core.EndMinute), "사망한 피해자는 사건 이후 일정이 없다.");
                Assert.IsTrue(t.HasGrant(Witness, core.InfoId));
            }
        }

        [Test]
        public void Theft_DoesNotForceVictimMeeting_AndVariesAcrossSeeds()
        {
            int withVictim = 0, withoutVictim = 0;
            for (int seed = 0; seed < 40; seed++)
            {
                var core = GenerateValid(seed, CrimeType.TheftRobbery).CoreIncident;
                if (core.ParticipantIds.Contains(Victim)) withVictim++;
                else withoutVictim++;
            }

            Assert.Greater(withVictim, 0);
            Assert.Greater(withoutVictim, 0);
        }

        [Test]
        public void Timelines_AreDiverseAcrossSeeds()
        {
            var timelines = Enumerable.Range(0, 40).Select(seed => GenerateValid(seed, CrimeType.Murder)).ToList();

            Assert.GreaterOrEqual(timelines.Select(t => t.SceneRoomId).Distinct().Count(), 3, "현장 방이 다양해야 한다.");
            Assert.GreaterOrEqual(timelines.Select(t => t.CoreIncident.StartMinute).Distinct().Count(), 3, "사건 시각이 다양해야 한다.");
            Assert.GreaterOrEqual(timelines.Select(t => t.Discovery.ParticipantIds[0]).Distinct().Count(), 2, "발견자가 다양해야 한다.");

            string Signature(ContractTimeline t) => string.Join("|", t.Schedules.OrderBy(p => p.Key)
                .SelectMany(p => p.Value.Entries.Select(e => $"{p.Key}:{e.StartMinute}-{e.EndMinute}@{e.RoomId}")));
            Assert.GreaterOrEqual(timelines.Select(Signature).Distinct().Count(), 30, "인물 일정 조합이 거의 매번 달라야 한다.");
        }

        [Test]
        public void Schedules_MoveOnlyBetweenAdjacentRooms_WithoutGaps()
        {
            var rooms = HouseLayoutFactory.CreateStandardHouse();
            for (int seed = 0; seed < 20; seed++)
            {
                var t = GenerateValid(seed, CrimeType.AssaultInjury);
                foreach (var schedule in t.Schedules.Values)
                {
                    var entries = schedule.Entries;
                    Assert.AreEqual(0, entries[0].StartMinute);
                    for (int i = 1; i < entries.Count; i++)
                    {
                        Assert.AreEqual(entries[i - 1].EndMinute, entries[i].StartMinute);
                        Assert.IsTrue(rooms.AreConnected(entries[i - 1].RoomId, entries[i].RoomId));
                    }
                }
            }
        }

        [Test]
        public void WitnessGrants_OnlyForPeoplePresentAtEventTimeAndPlace()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var t = GenerateValid(seed, CrimeType.Blackmail);
                foreach (var e in t.Events)
                {
                    var present = new HashSet<string>(t.GetPresentAt(e.RoomId, e.StartMinute));
                    var knowers = t.Knowledge.Where(k => k.SourceEventId == e.EventId).Select(k => k.NpcId).ToList();
                    CollectionAssert.AreEquivalent(present, knowers, "그 시각 그 방에 있던 사람만, 빠짐없이 안다.");
                }
            }
        }

        [Test]
        public void Discovery_IsByNonPerpetrator_AndNobodyElseEntersSceneBeforeIt()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var t = GenerateValid(seed, CrimeType.Murder);
                var discovery = t.Discovery;
                Assert.AreNotEqual(Perp, discovery.ParticipantIds[0]);
                Assert.GreaterOrEqual(discovery.StartMinute, t.CoreIncident.EndMinute);

                for (int minute = t.CoreIncident.EndMinute; minute < discovery.StartMinute; minute += t.SlotMinutes)
                {
                    var others = t.GetPresentAt(t.SceneRoomId, minute).Where(id => id != Perp);
                    CollectionAssert.IsEmpty(others, $"seed {seed}: 발견 전에 현장에 들어간 사람이 없어야 한다.");
                }
            }
        }

        [Test]
        public void CoverUp_IsAfterCoreIncident_AtScene_BeforeDiscovery()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var t = GenerateValid(seed, CrimeType.Murder, CoverUpMethod.SuicideDisguise);
                var cover = t.Events.Single(e => e.Kind == TimelineEventKind.CoverUp);

                Assert.AreEqual(t.CoreIncident.EndMinute, cover.StartMinute);
                Assert.AreEqual(t.SceneRoomId, cover.RoomId);
                CollectionAssert.Contains(cover.CauseEventIds, t.CoreIncident.EventId);
                Assert.GreaterOrEqual(t.Discovery.StartMinute, cover.EndMinute);
                CollectionAssert.Contains(t.Discovery.CauseEventIds, cover.EventId);
            }
        }

        [Test]
        public void Generation_DoesNotChangeTruth()
        {
            var (contract, registry, rooms) = BuildCase(CrimeType.Missing);
            new ContractTimelineGenerator(new Random(3)).Generate(contract, registry, rooms);

            Assert.AreEqual(Perp, contract.Truth.PerpetratorId);
            Assert.AreEqual(Victim, contract.Truth.VictimId);
            Assert.AreEqual(CrimeType.Missing, contract.PrimaryCrimeType);
            Assert.AreEqual(CoverUpMethod.None, contract.Truth.CoverUp);
        }

        [Test]
        public void InvalidTruth_IsRejected()
        {
            var (contract, registry, rooms) = BuildCase(CrimeType.Murder);
            contract.Truth.VictimId = Perp;
            var result = new ContractTimelineGenerator(new Random(1)).Generate(contract, registry, rooms);
            CollectionAssert.Contains(result.Errors, ContractValidationErrorCode.CONTRACT_INVALID);

            contract.Truth.VictimId = "nobody";
            result = new ContractTimelineGenerator(new Random(1)).Generate(contract, registry, rooms);
            CollectionAssert.Contains(result.Errors, ContractValidationErrorCode.CONTRACT_INVALID);
        }

        [Test]
        public void Unsatisfiable_RetriesFiveTimesThenFails()
        {
            // 살인 피해자는 사망, 가해자는 발견자가 될 수 없으므로 발견할 사람이 없다.
            var (contract, registry, rooms) = BuildCase(CrimeType.Murder, includeWitness: false, includeRelated: false);
            var result = new ContractTimelineGenerator(new Random(1)).Generate(contract, registry, rooms);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(6, result.AttemptsUsed);
            CollectionAssert.Contains(result.Errors, ContractValidationErrorCode.CONTRADICTION);
            Assert.AreEqual(0, contract.Truth.TimelineEvents.Count, "실패한 경위는 반영하지 않는다.");
        }

        [Test]
        public void Validator_DetectsTamperedTimelines()
        {
            var (contract, registry, rooms) = BuildCase(CrimeType.Murder);
            var validator = new ContractTimelineValidator();

            int seed = FindSeed(CrimeType.Murder, x => x.GetRoomAt(RelatedA, x.CoreIncident.StartMinute) != x.SceneRoomId);
            var t = GenerateValid(seed, CrimeType.Murder);
            var outsider = RelatedA;
            t.Knowledge.Add(new KnowledgeGrant
            {
                NpcId = outsider, InfoId = t.CoreIncident.InfoId, Path = InfoAcquisitionPath.DirectWitness,
                Minute = t.CoreIncident.StartMinute, SourceEventId = t.CoreIncident.EventId
            });
            CollectionAssert.Contains(validator.Validate(t, contract, registry, rooms).Errors, ContractValidationErrorCode.KNOWLEDGE_LEAK);

            t = GenerateValid(5, CrimeType.Murder);
            t.Schedules[Victim].TryAdd(t.DayEndMinute - 1, t.DayEndMinute, t.SceneRoomId);
            var errors = validator.Validate(t, contract, registry, rooms).Errors;
            CollectionAssert.Contains(errors, ContractValidationErrorCode.CONTRADICTION, "사망한 피해자가 사건 이후 일정을 가지면 안 된다.");

            t = GenerateValid(5, CrimeType.Murder);
            var jumpy = new NpcSchedule(RelatedA);
            jumpy.TryAdd(0, 10, HouseLayoutFactory.Reception);
            jumpy.TryAdd(10, t.DayEndMinute, HouseLayoutFactory.Bedroom);
            t.Schedules[RelatedA] = jumpy;
            CollectionAssert.Contains(validator.Validate(t, contract, registry, rooms).Errors, ContractValidationErrorCode.TIMELINE_CONFLICT,
                "인접하지 않은 방으로 한 번에 이동할 수 없다.");

            t = GenerateValid(5, CrimeType.Murder);
            var away = new NpcSchedule(Witness);
            string notScene = rooms.AllRoomIds.First(r => r != t.SceneRoomId);
            away.TryAdd(0, t.DayEndMinute, notScene);
            t.Schedules[Witness] = away;
            CollectionAssert.Contains(validator.Validate(t, contract, registry, rooms).Errors, ContractValidationErrorCode.CONTRADICTION,
                "목격자 역할 인물은 실제로 목격해야 한다.");
        }

        [Test]
        public void Apply_SeedsGroundedKnowledge_PlacesPeople_AndIsIdempotentForTimelineIds()
        {
            int seed = FindSeed(CrimeType.Murder, x => x.GetRoomAt(RelatedA, x.CoreIncident.StartMinute) != x.SceneRoomId
                                                       && x.Discovery.ParticipantIds[0] != RelatedA);
            var (contract, registry, rooms) = BuildCase(CrimeType.Murder);
            var t = new ContractTimelineGenerator(new Random(seed)).Generate(contract, registry, rooms).Timeline;
            var occupancy = new RoomOccupancyTracker(rooms);

            ContractTimelineApplier.Apply(t, contract, registry, occupancy);
            ContractTimelineApplier.Apply(t, contract, registry, occupancy);

            Assert.AreEqual(t.Events.Count, contract.Truth.TimelineEvents.Count);
            Assert.IsTrue(new ContractValidator().Validate(contract).Errors.All(e => e != ContractValidationErrorCode.TIMELINE_CONFLICT));
            Assert.IsFalse(registry.Get(Victim).IsAlive);
            Assert.AreEqual(t.SceneRoomId, occupancy.GetRoom(Victim), "시신은 현장에 남는다.");
            Assert.AreEqual(t.GetRoomAt(Perp, Math.Min(t.InvestigationStartMinute, t.DayEndMinute - 1)), occupancy.GetRoom(Perp));
            Assert.IsTrue(registry.Get(Witness).Knows(t.CoreIncident.InfoId));

            var validator = new ContractTimelineValidator();
            Assert.IsTrue(validator.ValidateNpcKnowledge(t, registry).IsValid);

            var outsider = registry.Get(RelatedA);
            Assert.IsFalse(outsider.Knows(t.CoreIncident.InfoId));
            outsider.LearnInfo(t.CoreIncident.InfoId, NpcJudgement.Withhold);
            CollectionAssert.Contains(validator.ValidateNpcKnowledge(t, registry).Errors, ContractValidationErrorCode.KNOWLEDGE_LEAK);
            Assert.IsTrue(validator.ValidateNpcKnowledge(t, registry, (npc, info) => npc == outsider.NpcId).IsValid,
                "정보 전달 기록이 있으면 획득 경로가 있는 지식이다.");
        }
    }
}
