using System;
using System.Linq;
using CaseFileOmission.ContractContent;
using CaseFileOmission.Core;
using CaseFileOmission.Game;
using CaseFileOmission.Meeting;
using CaseFileOmission.NPC;
using CaseFileOmission.Reports;
using CaseFileOmission.Rooms;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    /// <summary>06·07·04 2026-11-11~13 확정 시간·일정·단서·보고서·의견 점수 규칙.</summary>
    public class InvestigationRulesTests
    {
        private static GameSession Start(int seed = 7)
        {
            var s = new GameSession(new GameSessionConfig { Random = new Random(seed) });
            s.StartNewGame();
            s.CompletePrologue();
            Assert.IsNotNull(s.OfferContract());
            Assert.IsTrue(s.AcceptContract().Success);
            return s;
        }

        [Test]
        public void Budget_Is32QuarterHourSlots_AndInvestigationStartsAtBlockStart()
        {
            var s = Start();
            Assert.AreEqual(32, s.State.RemainingTimeSlots);
            int minuteOfDay = s.WorldMinute % 1440;
            Assert.That(minuteOfDay, Is.EqualTo(6 * 60).Or.EqualTo(18 * 60));
            Assert.AreEqual(s.WorldMinute + 12 * 60, s.BlockEndMinute);
        }

        [Test]
        public void Move_CostsNothing_TalkCostsHalfHour_ObjectCostsOneHour()
        {
            var s = Start();
            int minute = s.WorldMinute;
            string next = s.Rooms.GetAdjacentRooms(s.PlayerRoom).First();
            Assert.IsTrue(s.MovePlayer(next).Success);
            Assert.AreEqual(32, s.State.RemainingTimeSlots, "이동은 0시간");
            Assert.AreEqual(minute, s.WorldMinute, "이동은 세계 시각도 진행하지 않는다");

            var obj = s.GetInvestigationObjects().First(o => o.RoomId == s.PlayerRoom);
            Assert.IsTrue(s.InvestigateObject(obj.ObjectId).Success);
            Assert.AreEqual(28, s.State.RemainingTimeSlots);
            Assert.AreEqual(minute + 60, s.WorldMinute);

            var npc = s.Npcs.GetByContract(s.CurrentContract.ContractId).FirstOrDefault(n => n.IsAlive && s.Occupancy.HasPosition(n.NpcId) && s.Occupancy.GetRoom(n.NpcId) == s.PlayerRoom);
            if (npc != null)
            {
                Assert.IsTrue(s.TalkTo(npc.NpcId).Success);
                Assert.AreEqual(26, s.State.RemainingTimeSlots, "대화 0.5시간");
            }
        }

        [Test]
        public void ActionLongerThanBudget_IsRejectedWithoutSpendingTime()
        {
            var s = Start();
            Assert.IsTrue(s.PassTime(30).Success);
            int minute = s.WorldMinute;
            var obj = s.GetInvestigationObjects().First(o => o.RoomId == s.PlayerRoom);
            Assert.AreEqual(GameActionFailure.NotEnoughTime, s.InvestigateObject(obj.ObjectId).Failure);
            Assert.AreEqual(2, s.State.RemainingTimeSlots);
            Assert.AreEqual(minute, s.WorldMinute);
        }

        [Test]
        public void EndingBlock_AutoAdvancesToBlockBoundary_AndNextBlockRestoresBudget()
        {
            var s = Start();
            int end = s.BlockEndMinute;
            Assert.IsTrue(s.EndInvestigationEarly().Success);
            Assert.AreEqual(end, s.WorldMinute, "조사 종료는 시간대 끝까지 자동 진행");
            Assert.AreEqual(GameActionFailure.TimeBlockEnded, s.MovePlayer(s.Rooms.GetAdjacentRooms(s.PlayerRoom).First()).Failure,
                "구간이 끝나면 추가 조사 입력을 막는다");

            Assert.IsTrue(s.AdvanceToNextTimeBlock().Success);
            Assert.AreEqual(32, s.State.RemainingTimeSlots);
            Assert.AreEqual(end, s.WorldMinute);
            Assert.AreEqual(end + 12 * 60, s.BlockEndMinute);
        }

        [Test]
        public void NpcsMoveDuringInvestigation_FollowingReproducibleSchedules()
        {
            var a = Start(11);
            var b = Start(11);
            var before = a.Npcs.GetByContract(a.CurrentContract.ContractId).Where(n => a.Occupancy.HasPosition(n.NpcId))
                .ToDictionary(n => n.NpcId, n => a.Occupancy.GetRoom(n.NpcId));
            a.PassTime(32);
            b.PassTime(32);

            bool anyMoved = before.Any(p => a.Occupancy.GetRoom(p.Key) != p.Value);
            Assert.IsTrue(anyMoved, "8시간 동안 누군가는 일정대로 움직인다");
            foreach (var id in before.Keys)
                Assert.AreEqual(a.Occupancy.GetRoom(id), b.Occupancy.GetRoom(id), "같은 시드 → 같은 일정·위치");
        }

        [Test]
        public void ScheduleGenerator_FillsWholeBlockInTenMinutePrecision()
        {
            var rooms = HouseLayoutFactory.CreateStandardHouse();
            var npc = new NpcState("n1", "c1", NpcActualRole.Witness, NpcPersonality.Timid) { Occupation = "가정부" };
            var schedule = new InvestigationScheduleGenerator(rooms).Generate(npc, HouseLayoutFactory.Kitchen, 360, 1080, 5, true);

            Assert.AreEqual(360, schedule.Entries.First().StartMinute);
            Assert.AreEqual(1080, schedule.Entries.Last().EndMinute);
            Assert.AreEqual(HouseLayoutFactory.Kitchen, schedule.Entries.First().RoomId);
            for (int i = 1; i < schedule.Entries.Count; i++)
            {
                Assert.AreEqual(schedule.Entries[i - 1].EndMinute, schedule.Entries[i].StartMinute, "빈틈 없음");
                Assert.AreEqual(0, schedule.Entries[i].StartMinute % 10, "10분 정밀도");
            }
        }

        [Test]
        public void GeneratedCase_HasNamedCast_ObjectPlacedClues_AndReportTargets()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                var s = Start(seed);
                var cast = s.Npcs.GetByContract(s.CurrentContract.ContractId).ToList();
                Assert.That(cast.Count, Is.InRange(2, 6), $"seed {seed}: 참여 NPC 2~6명");
                Assert.IsTrue(cast.All(n => !string.IsNullOrEmpty(n.DisplayName) && CaseCatalog.Occupations.Contains(n.Occupation)));

                foreach (var m in s.Materials.Where(m => m.Acquisition == MaterialAcquisition.InvestigateRoom))
                {
                    Assert.IsNotNull(m.ObjectId, $"seed {seed}: {m.MaterialId}는 조사 오브젝트에 놓인다");
                    Assert.AreEqual(m.RoomId, CaseCatalog.ObjectRoom(m.ObjectId));
                }
                Assert.IsTrue(s.Materials.All(m => !string.IsNullOrEmpty(m.Content)), $"seed {seed}: 모든 자료에 본문");
                Assert.IsTrue(s.Materials.Any(m => m.MaterialId.Contains("motive-document")), "동기 자료");
                Assert.That(CaseCatalog.MethodsFor(s.CurrentContract.PrimaryCrimeType), Contains.Item(s.CurrentContract.Truth.MethodId));

                var targets = s.GetReportTargets();
                Assert.GreaterOrEqual(targets.Count, 2);
                foreach (var t in targets)
                    Assert.IsTrue(t.EvidenceGroups.Count > 0, $"seed {seed}: 목표 '{t.Category}'에 증거 경로가 최소 1개");
            }
        }

        [Test]
        public void NarrativeJudge_FollowsFixedOrder_AndNeedsAcquiredEvidence()
        {
            var target = new ReportTarget { Index = 0, Category = "사건의 실체" };
            var crime = new ReportConcept { Label = "유형" };
            crime.Synonyms.Add("살해");
            var victim = new ReportConcept { Label = "피해자" };
            victim.Synonyms.Add("에드윈");
            target.Concepts.Add(crime);
            target.Concepts.Add(victim);
            target.ContradictionTerms.Add("자살");
            target.EvidenceGroups.Add(new System.Collections.Generic.List<string> { "ev1", "ev2" });

            var none = new string[0];
            Assert.AreEqual(ReportSlotStatus.NeedsClarification, NarrativeReportJudge.Judge(target, "음", none));
            Assert.AreEqual(ReportSlotStatus.Incorrect, NarrativeReportJudge.Judge(target, "에드윈은 자살했다", none));
            Assert.AreEqual(ReportSlotStatus.Partial, NarrativeReportJudge.Judge(target, "에드윈에게 무슨 일이", none));
            Assert.AreEqual(ReportSlotStatus.CorrectUnsupported, NarrativeReportJudge.Judge(target, "에드윈이 살해됐다", new[] { "ev1" }));
            Assert.AreEqual(ReportSlotStatus.Completed, NarrativeReportJudge.Judge(target, "에드윈이 살해됐다", new[] { "ev1", "ev2" }));
        }

        [Test]
        public void OpinionScore_UsesPersonalityWeights_SumsThenRoundsOnce()
        {
            var service = new OpinionScoreService();
            // 맹목적으로 믿는 자: 주장 3, w 1 → +2 주장 × 3 × (3/3) = +6
            service.ApplySimultaneous("a", NpcPersonality.BlindlyTrusting, new[] { new OpinionInput("x", 2, false) });
            Assert.AreEqual(56f, service.Get("a", "x"));

            // 무관심한 자: 증거 2, w 3 → +1×2×(1/3)=0.667 두 개 합산 1.333 → 1 (정보별 반올림이면 2가 된다)
            service.ApplySimultaneous("b", NpcPersonality.Indifferent, new[] { new OpinionInput("x", 1, true), new OpinionInput("x", 1, true) });
            Assert.AreEqual(51f, service.Get("b", "x"));

            Assert.IsTrue(service.VotesGuilty("b", "x"));
            Assert.IsFalse(service.VotesGuilty("c", "x"), "중립 50은 찬성이 아니다");
            service.ApplySimultaneous("x", NpcPersonality.Guilty, new[] { new OpinionInput("x", 3, true) });
            Assert.AreEqual(50f, service.Get("x", "x"), "자기 자신에 대한 의견은 바꾸지 않는다");
        }

        [Test]
        public void Persuasion_RequiresIdentifiedCulprit_AndOnlyOncePerContract()
        {
            var s = Start();
            var perp = s.CurrentContract.Truth.PerpetratorId;
            s.Occupancy.PlaceAt(perp, s.PlayerRoom);
            Assert.AreEqual(GameActionFailure.InvalidInput, s.Persuade(perp).Failure, "가해자 식별 전에는 시도할 수 없다");
            s.Campaign.CulpritIdentified = true;
            Assert.IsTrue(s.Persuade(perp).Success);
            Assert.AreEqual(GameActionFailure.InvalidInput, s.Persuade(perp).Failure, "재시도 없음");
        }

        private sealed class FakeAi : IAiContentProvider
        {
            public readonly System.Collections.Generic.Queue<string> Responses = new System.Collections.Generic.Queue<string>();
            public int Calls;

            public bool TryRequest(AiStage stage, string requestJson, out string responseJson)
            {
                Calls++;
                responseJson = Responses.Count > 0 ? Responses.Dequeue() : null;
                return responseJson != null;
            }
        }

        [Test]
        public void AiStage_RetriesInvalidResponses_UpToThreeCalls()
        {
            var ai = new FakeAi();
            ai.Responses.Enqueue("not json");
            ai.Responses.Enqueue("{\"result_status\":\"maybe\"}");
            ai.Responses.Enqueue("{\"result_status\":\"correct\"}");
            var target = new ReportTarget { Index = 0 };
            target.EvidenceGroups.Add(new System.Collections.Generic.List<string> { "ev1" });

            Assert.AreEqual(ReportSlotStatus.Completed, NarrativeReportJudge.JudgeWithAi(target, "답", new[] { "ev1" }, ai));
            Assert.AreEqual(3, ai.Calls);

            var failing = new FakeAi();
            var logger = new GameLogger();
            Assert.AreEqual(ReportSlotStatus.AnswerPending, NarrativeReportJudge.JudgeWithAi(target, "답", new[] { "ev1" }, failing, logger),
                "3회 모두 실패하면 정오답을 정하지 않고 판정 보류");
            Assert.AreEqual(AiStageRunner.MaxAttemptsPerStage, failing.Calls);
            Assert.IsTrue(logger.Entries.Any(e => e.ErrorCode == "AI_CONNECTION_FAILED"));

            var unsupported = new FakeAi();
            unsupported.Responses.Enqueue("{\"result_status\":\"correct\"}");
            Assert.AreEqual(ReportSlotStatus.CorrectUnsupported, NarrativeReportJudge.JudgeWithAi(target, "답", new string[0], unsupported),
                "AI가 내용을 맞다고 해도 증거 충분성은 시스템이 따로 검사한다");
        }

        [Test]
        public void ActualType_UsesOnlySupportedCombinations_AndPoliceSnapshotKeepsRepresentativeType()
        {
            var gen = new Contracts.ContractGenerator(Contracts.ContractGenerationConfig.CreateDefault(), new Random(5));
            foreach (Contracts.CrimeType rep in Enum.GetValues(typeof(Contracts.CrimeType)))
                for (int i = 0; i < 300; i++)
                    Assert.IsTrue(Contracts.ContractGenerator.IsSupportedCombination(rep, gen.PickActualCrimeType(rep)));

            bool sawDifferent = false;
            for (int seed = 1; seed <= 60; seed++)
            {
                // 메인 사건 2단계에서는 단서 생성 제약(살인 고정 등)이 붙으므로, 제약 없는 일반 의뢰(1단계)로 조합 분포를 본다.
                var s = new GameSession(new GameSessionConfig { Random = new Random(seed) });
                s.StartNewGame();
                Assert.IsNotNull(s.OfferContract());
                Assert.IsTrue(s.AcceptContract().Success);
                var c = s.CurrentContract;
                Assert.IsTrue(c.RepresentativeCrimeType.HasValue);
                Assert.IsNotEmpty(c.PoliceKnownFacts, "경찰 초기 정보");
                Assert.IsFalse(string.IsNullOrEmpty(c.PoliceClassificationBasis));
                StringAssert.StartsWith(RepPrefix(c.PoliceCrimeType), c.Truth.RepresentativeSubtypeId);
                if (c.PoliceCrimeType != c.PrimaryCrimeType)
                {
                    sawDifferent = true;
                    Assert.IsNotEmpty(c.PoliceUnknownFacts);
                }
            }
            Assert.IsTrue(sawDifferent, "60개 시드 중에는 대표≠실제 조합이 나온다");
        }

        private static string RepPrefix(Contracts.CrimeType t) => t switch
        {
            Contracts.CrimeType.Murder => "REP-SUB-MUR",
            Contracts.CrimeType.Missing => "REP-SUB-MIS",
            Contracts.CrimeType.AssaultInjury => "REP-SUB-ASS",
            Contracts.CrimeType.TheftRobbery => "REP-SUB-THE",
            _ => "REP-SUB-THR"
        };

        // 시신 이동 검증: 시신 자료의 방과 시신 위치가 같고, 사망 장소와 다르다.
        [Test]
        public void MurderSubtype03_PlacesBodyAwayFromDeathRoom()
        {
            // 프롤로그 뒤 의뢰는 메인 단서 제약(피해자 여러 명 등)이 붙어 MUR-03이 나올 수 없으므로, 제약 없는 일반 생성으로 본다.
            for (int seed = 1; seed <= 120; seed++)
            {
                var built = new ContractPipeline(Contracts.ContractGenerationConfig.CreateDefault(), new Random(seed)).Generate(() => $"c{seed}");
                var c = built.Contract;
                if (c.Truth.ActualSubtypeId != "ACT-SUB-MUR-03") continue;
                var body = built.Materials.First(m => m.MaterialId == ContractPipeline.BodyMaterialId(c.ContractId));
                Assert.AreNotEqual(body.AboutRoomId, body.RoomId, "사망 장소와 시신 발견 장소가 다르다");
                Assert.AreEqual(body.RoomId, built.Occupancy.GetRoom(c.Truth.VictimId));
                return;
            }
            Assert.Inconclusive("120개 시드 안에 MUR-03이 없었다");
        }

        [Test]
        public void SubCrime_IsAddedSometimes_WithTimelineEventTraceAndReportTarget()
        {
            int withSubCrime = 0, total = 0;
            for (int seed = 1; seed <= 150; seed++)
            {
                var s = Start(seed);
                var c = s.CurrentContract;
                total++;
                if (c.SubCrimeLinks.Count == 0) continue;
                withSubCrime++;

                var link = c.SubCrimeLinks.Single();
                Assert.IsTrue(c.Truth.TimelineEvents.Contains(link.EventId), $"seed {seed}: 부수 범죄가 시간순 경위에 반영된다");
                CollectionAssert.Contains(link.LinkedNpcIds, c.Truth.PerpetratorId, "연결 근거 인물");
                Assert.IsFalse(string.IsNullOrEmpty(link.LinkReason));
                var trace = s.Materials.SingleOrDefault(m => m.MaterialId.Contains("subcrime-trace"));
                Assert.IsNotNull(trace, $"seed {seed}: 부수 범죄 흔적 자료");
                Assert.AreEqual(link.EventId, trace.AboutEventId);
                Assert.IsTrue(s.GetReportTargets().Any(t => t.Category == "부수 범죄"), "부수 범죄 보고서 목표");
            }
            Assert.That(withSubCrime, Is.InRange(1, total / 2), "20% 판정 + 연결 조건이라 일부 의뢰에만 붙는다");
        }

        [Test]
        public void ExtendedCombinations_ProduceDepartureAndHiddenBodyCases()
        {
            bool sawDeparture = false, sawHidden = false;
            for (int seed = 1; seed <= 400 && !(sawDeparture && sawHidden); seed++)
            {
                var s = new GameSession(new GameSessionConfig { Random = new Random(seed) });
                s.StartNewGame();
                var offer = s.OfferContract();
                Assert.IsNotNull(offer, $"seed {seed}: 생성 실패");
                var c = offer.Contract;
                Assert.IsTrue(Contracts.ContractGenerator.IsSupportedCombination(c.PoliceCrimeType, c.PrimaryCrimeType));
                Assert.IsTrue(s.AcceptContract().Success);

                if (c.Truth.VictimDeparted)
                {
                    sawDeparture = true;
                    Assert.IsFalse(s.Occupancy.HasPosition(c.Truth.VictimId), $"seed {seed}: 스스로 떠난 피해자는 집에 없다");
                    Assert.IsTrue(s.Npcs.Get(c.Truth.VictimId).IsAlive, "자발적 이탈 피해자는 살아 있다");
                    Assert.IsTrue(s.Materials.Any(m => m.MaterialId.Contains("departure-trace")), "현관 물품 상태 자료");
                }
                if (c.Truth.BodyHidden)
                {
                    sawHidden = true;
                    Assert.AreEqual(Contracts.CrimeType.Murder, c.PrimaryCrimeType);
                    Assert.AreEqual("ACT-SUB-MUR-04", c.Truth.ActualSubtypeId);
                    Assert.IsFalse(s.Occupancy.HasPosition(c.Truth.VictimId), $"seed {seed}: 숨긴 시신은 판 위에 보이지 않는다");
                    var body = s.Materials.First(m => m.MaterialId == ContractPipeline.BodyMaterialId(c.ContractId));
                    Assert.IsNotNull(body.ObjectId, "숨긴 물건을 조사하면 찾는다");
                    Assert.IsTrue(c.PoliceUnknownFacts.Any(f => f.Contains("사망")), "경찰은 사망을 모른다");
                }
            }
            Assert.IsTrue(sawDeparture, "자발적 이탈 조합이 생성된다");
            Assert.IsTrue(sawHidden, "시신 은닉 조합이 생성된다");
        }

        [Test]
        public void AllFourteenConditionalCombinations_AreSupported()
        {
            var types = (Contracts.CrimeType[])Enum.GetValues(typeof(Contracts.CrimeType));
            int conditional = 0;
            foreach (var rep in types)
            foreach (var actual in types)
            {
                if (rep == actual) continue;
                bool impossible = actual == Contracts.CrimeType.TheftRobbery
                                  || (rep == Contracts.CrimeType.AssaultInjury && actual == Contracts.CrimeType.Blackmail)
                                  || (rep == Contracts.CrimeType.TheftRobbery && actual == Contracts.CrimeType.Blackmail);
                Assert.AreEqual(!impossible, Contracts.ContractGenerator.IsSupportedCombination(rep, actual), $"{rep}→{actual}");
                if (!impossible) conditional++;
            }
            Assert.AreEqual(14, conditional, "04 §5 조건부 조합 14개, §6 생성 불가 6개");
        }

        [Test]
        public void AssaultToMurder_DiesLaterInAnotherRoom_UnknownToPolice()
        {
            for (int seed = 1; seed <= 1500; seed++)
            {
                var s = new GameSession(new GameSessionConfig { Random = new Random(seed) });
                s.StartNewGame();
                var c = s.OfferContract()?.Contract;
                if (c == null || !c.Truth.DelayedDeath) continue;
                Assert.IsTrue(s.AcceptContract().Success);
                Assert.AreEqual(Contracts.CrimeType.AssaultInjury, c.PoliceCrimeType);
                Assert.AreEqual("ACT-SUB-MUR-02", c.Truth.ActualSubtypeId);
                var body = s.Materials.First(m => m.MaterialId == ContractPipeline.BodyMaterialId(c.ContractId));
                Assert.AreNotEqual(body.AboutRoomId, body.RoomId, "공격받은 방과 숨진 방이 다르다");
                Assert.AreEqual(body.RoomId, s.Occupancy.GetRoom(c.Truth.VictimId));
                Assert.IsFalse(s.Npcs.Get(c.Truth.VictimId).IsAlive);
                Assert.IsTrue(c.PoliceUnknownFacts.Any(f => f.Contains("숨졌")));
                return;
            }
            Assert.Fail("폭행→살인 조합이 생성되지 않았다");
        }

        [Test]
        public void TheftToMissing_ItemWentWithOwner_NoActualTheft()
        {
            for (int seed = 1; seed <= 1500; seed++)
            {
                var s = new GameSession(new GameSessionConfig { Random = new Random(seed) });
                s.StartNewGame();
                var c = s.OfferContract()?.Contract;
                if (c == null || c.PoliceCrimeType != Contracts.CrimeType.TheftRobbery || c.PrimaryCrimeType != Contracts.CrimeType.Missing) continue;
                Assert.IsTrue(s.AcceptContract().Success);
                Assert.IsTrue(s.Materials.Any(m => m.MaterialId.Contains("item-record")), "물건이 주인과 함께 사라졌다는 기록");
                Assert.IsFalse(c.SubCrimeLinks.Any(l => l.Type == Contracts.CrimeType.TheftRobbery), "타인의 실제 절도는 없어야 한다(부수 절도도 붙이지 않음)");
                Assert.IsFalse(s.Occupancy.HasPosition(c.Truth.VictimId), "소유자는 소재 미확인");
                return;
            }
            Assert.Fail("절도→실종 조합이 생성되지 않았다");
        }

        private static GameSession StartFixture()
        {
            var s = new GameSession(new GameSessionConfig { Random = new Random(TestFixtureCase001.Seed) });
            s.StartNewGame();
            s.CompletePrologue();
            s.OfferFixture(TestFixtureCase001.Build(), Police.PoliceRoster.PoliceC);
            Assert.IsTrue(s.AcceptContract().Success);
            return s;
        }

        private static void Discover(GameSession s, string id)
        {
            var m = s.Materials.First(x => x.MaterialId == id);
            if (m.Acquisition == MaterialAcquisition.TalkToNpc) s.Occupancy.PlaceAt(m.SourceNpcId, s.PlayerRoom);
            else s.Occupancy.PlaceAt(s.PlayerId, m.RoomId);
            var r = m.Acquisition == MaterialAcquisition.TalkToNpc ? s.TalkTo(m.SourceNpcId) : s.InvestigateObject(m.ObjectId);
            Assert.IsTrue(r.Success, id + " " + r.Failure);
            Assert.IsTrue(s.Clues.Exists(id), id);
        }

        [Test]
        public void Fixture001_StartsWithConfirmedValues()
        {
            var s = StartFixture();
            var c = s.CurrentContract;
            Assert.AreEqual(Contracts.CrimeType.AssaultInjury, c.PoliceCrimeType);
            Assert.AreEqual(Contracts.CrimeType.Missing, c.PrimaryCrimeType);
            Assert.AreEqual(Police.PoliceRoster.PoliceC, c.HandlerPoliceId);
            Assert.AreEqual(HouseLayoutFactory.EntranceHall, s.PlayerRoom);
            Assert.AreEqual(8 * 60, s.WorldMinute, "1984-12-03 08:00");
            Assert.AreEqual(32, s.State.RemainingTimeSlots);
            Assert.IsFalse(s.Occupancy.HasPosition(TestFixtureCase001.NpcP), "P는 장면에 배치하지 않는다");
            Assert.AreEqual(HouseLayoutFactory.Reception, s.Occupancy.GetRoom(TestFixtureCase001.NpcA));
            Assert.AreEqual(3, s.GetReportTargets().Count);
        }

        [Test]
        public void Fixture001_UnlockPaths_MatchExpectedTable()
        {
            var s = StartFixture();
            s.RefreshReportUnlocks();
            Assert.IsTrue(s.Report.GetOrAddSlot(0).Unlocked, "보고서 진입: 문단 01만");
            Assert.IsFalse(s.Report.GetOrAddSlot(1).Unlocked);
            Assert.IsFalse(s.Report.GetOrAddSlot(2).Unlocked);

            Discover(s, TestFixtureCase001.Ev(1));
            Discover(s, TestFixtureCase001.Ev(2));
            Assert.IsTrue(s.Report.GetOrAddSlot(1).Unlocked, "EV-01과 EV-02 확보 → 문단 02 공개");

            Assert.AreEqual(ReportSlotStatus.Completed, s.SubmitReportAnswer(0, "P는 스스로 집을 떠났고 지금 소재를 알 수 없다"));
            Assert.AreEqual(ReportSlotStatus.CorrectUnsupported, s.SubmitReportAnswer(1, "A에게 또 맞을까 두려워 떠났다"),
                "EV-03 또는 EV-01+EV-04가 없으면 근거 부족");
            Assert.IsFalse(s.Report.GetOrAddSlot(2).Unlocked, "문단 02 답이 근거 부족이면 문단 03 경로는 열리지 않는다");

            Discover(s, TestFixtureCase001.Ev(4));
            Discover(s, TestFixtureCase001.Ev(5));
            Assert.IsTrue(s.Report.GetOrAddSlot(2).Unlocked, "EV-04와 EV-05 확보 → 문단 03 공개");
            Assert.AreEqual(ReportSlotStatus.Completed, s.SubmitReportAnswer(1, "A에게 또 맞을까 두려워 떠났다"));
            Assert.AreEqual(ReportSlotStatus.Incorrect, s.SubmitReportAnswer(2, "누군가 P를 납치했다"));
            Assert.AreEqual(ReportSlotStatus.Completed, s.SubmitReportAnswer(2, "실제 사건은 실종이고, 앞서 있었던 폭행 때문에 떠난 것이다"));
        }

        [Test]
        public void MeetingTurnOrder_NpcsFirst_ThenPlayerAndPoliceGroup()
        {
            var s = Start();
            s.EndInvestigationEarly();
            Assert.IsTrue(s.StartMeeting().Success);
            var order = s.MeetingFlow.ParticipantIds.ToList();
            int firstNonNpc = order.FindIndex(id => !s.Npcs.Exists(id));
            Assert.IsTrue(order.Skip(firstNonNpc).All(id => !s.Npcs.Exists(id)), "일반 NPC가 먼저, 그 뒤 플레이어·경찰 그룹");
            CollectionAssert.AreEqual(order.Take(firstNonNpc).OrderBy(x => x, StringComparer.Ordinal).ToList(), order.Take(firstNonNpc).ToList());
            Assert.AreEqual(s.PlayerId, s.MeetingFlow.CurrentTurnId, "NPC 차례가 지나 플레이어 차례에서 시작한다");
        }

        [Test]
        public void HandlerQueue_DoesNotRepeatUntilExhausted()
        {
            var s = new GameSession(new GameSessionConfig { Random = new Random(3) });
            s.StartNewGame();
            var handlers = new System.Collections.Generic.List<string>();
            for (int i = 0; i < 5; i++)
            {
                s.OfferContract();
                Assert.IsTrue(s.AcceptContract().Success);
                handlers.Add(s.HandlerPoliceId);
                s.AbandonContract();
            }
            Assert.AreEqual(5, handlers.Distinct().Count(), "대기열을 다 쓰기 전에는 중복 배정하지 않는다");
        }
    }
}
