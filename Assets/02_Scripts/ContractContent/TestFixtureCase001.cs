using System.Collections.Generic;
using CaseFileOmission.Contracts;
using CaseFileOmission.Core;
using CaseFileOmission.Evidence;
using CaseFileOmission.NPC;
using CaseFileOmission.Reports;
using CaseFileOmission.Rooms;

namespace CaseFileOmission.ContractContent
{
    /// <summary>
    /// 노션 「TEST_TEMP — 인계용 고정 사건 fixture」 A절 TEST_TEMP_CASE_001(보고서 단계별 생성 검증용 샘플 01 기반).
    /// 대표 폭행·상해(REP-SUB-ASS-01) → 실제 실종(ACT-SUB-MIS-01, 자발적 이탈). 모든 값은 TEST_TEMP이며 Editor/Development 빌드 전용,
    /// 정식 세계관 사건이 아니다. 출시 빌드에서 fallback으로 쓰지 않는다.
    /// 원문의 테스트 시각은 07:00 사건 → 08:00 인계라 시간표 0분 = 07:00, 조사 시작 세계 시각 = 1984-12-03 08:00.
    /// 원문 값: seed 1984111301, 배정 경찰 POLICE_C, METHOD-01·TOOL-00, CONCEAL-00, 조직 조건 없음, 플레이어 시작 ROOM_ENTRANCE_HALL.
    /// 문단 02 해금은 「무작위」 2026-11-12 보완(EV-01∧EV-02 / EV-03 / 문단 01 완료)의 세 경로를 쓴다.
    /// </summary>
    public static class TestFixtureCase001
    {
        public const string FixtureId = "TEST_TEMP_CASE_001";
        public const int Seed = 1984111301;
        public const string ContractId = "TEST_TEMP_CASE_001";

        public const string NpcP = "NPC_TEST_P";
        public const string NpcA = "NPC_TEST_A";
        public const string NpcW = "NPC_TEST_W";

        public static string Ev(int n) => $"{ContractId}-EV-0{n}";

        public static GeneratedContract Build()
        {
            var c = new ContractInstance(ContractId)
            {
                FixtureId = FixtureId,
                PrimaryCrimeType = CrimeType.Missing,
                RepresentativeCrimeType = CrimeType.AssaultInjury,
                LocationCategory = LocationCategory.Indoor,
                Location = StubLocation.House,
                StartTimeBlock = TimeBlock.Morning,
                IncidentDate = "1984-12-03",
                IncidentClockMinute = 7 * 60,
                HandoverDayOffset = 0,
                InvestigationStartMinuteOverride = 8 * 60,
                PlayerStartRoomId = HouseLayoutFactory.EntranceHall
            };
            var truth = c.Truth;
            truth.PerpetratorId = NpcA;     // 선행 폭행의 가해자(실종 자체에는 가해자 없음 — 자발적 이탈)
            truth.VictimId = NpcP;
            truth.MethodId = "METHOD-01";
            truth.Method = CaseCatalog.Name(CaseCatalog.Methods, "METHOD-01");
            truth.ToolId = "TOOL-00";
            truth.Tool = null;
            truth.MotiveId = null;          // 자발적 이탈이면 가해자·동기를 만들지 않는다(실종 핵심 기준)
            truth.PrimaryMotive = null;
            truth.SecondaryMotive = null;
            truth.RelationshipId = null;
            truth.CoverUp = CoverUpMethod.None;
            truth.RepresentativeSubtypeId = "REP-SUB-ASS-01";
            truth.ActualSubtypeId = "ACT-SUB-MIS-01";
            c.OrgCondition = new OrganizationCondition();

            var npcs = new NpcRegistry();
            npcs.Add(new NpcState(NpcP, ContractId, NpcActualRole.Victim, NpcPersonality.Timid) { DisplayName = "P(피해자·TEST)", Occupation = "사무원" });
            npcs.Add(new NpcState(NpcA, ContractId, NpcActualRole.Perpetrator, NpcPersonality.Suspicious) { DisplayName = "A(TEST)", Occupation = "상인" });
            npcs.Add(new NpcState(NpcW, ContractId, NpcActualRole.Witness, NpcPersonality.Indifferent) { DisplayName = "W(TEST)", Occupation = "가정부" });

            var rooms = HouseLayoutFactory.CreateStandardHouse();
            var occupancy = new RoomOccupancyTracker(rooms);

            // 시간순 실제 진실(원문 표): 07:00 폭행(침실) → 07:20 신고·경찰 부상 확인 → 07:45 P가 현관 홀로 스스로 떠남(W 목격) → 08:00 인계.
            var t = new ContractTimeline
            {
                ContractId = ContractId,
                CrimeType = CrimeType.Missing,
                SlotMinutes = 10,
                SlotCount = 12,
                SceneRoomId = HouseLayoutFactory.Bedroom,
                PerpetratorId = NpcA,
                VictimId = NpcP,
                VictimLeavesStoryAfterIncident = true
            };
            var assault = Event(t, TimelineEventKind.CoreIncident, 0, 10, HouseLayoutFactory.Bedroom, NpcA, NpcP);
            var report = Event(t, TimelineEventKind.DiscoveryOrReport, 20, 30, HouseLayoutFactory.Bedroom, NpcP);
            report.CauseEventIds.Add(assault.EventId);
            var departure = Event(t, TimelineEventKind.Aftermath, 40, 50, HouseLayoutFactory.EntranceHall, NpcP);
            departure.CauseEventIds.Add(assault.EventId);
            foreach (var e in t.Events) truth.TimelineEvents.Add(e.EventId);

            AddSchedule(t, NpcA, HouseLayoutFactory.Bedroom, 0, 20, HouseLayoutFactory.Reception, 120);
            AddSchedule(t, NpcW, HouseLayoutFactory.Reception, 0, 40, HouseLayoutFactory.EntranceHall, 50, HouseLayoutFactory.Reception, 120);
            AddSchedule(t, NpcP, HouseLayoutFactory.Bedroom, 0, 40, HouseLayoutFactory.EntranceHall, 50);

            // 지식(원문 인물 표)
            Grant(t, NpcA, assault.InfoId, InfoAcquisitionPath.OwnAction, 0, assault.EventId);
            Grant(t, NpcP, assault.InfoId, InfoAcquisitionPath.OwnAction, 0, assault.EventId);
            Grant(t, NpcW, departure.InfoId, InfoAcquisitionPath.DirectWitness, 40, departure.EventId);
            Grant(t, NpcW, LetterInfo, InfoAcquisitionPath.Conversation, 60, null);
            foreach (var g in t.Knowledge)
            {
                var npc = npcs.Get(g.NpcId);
                npc.LearnInfo(g.InfoId, NpcJudgement.Believe, ContractTimelineApplier.RecordFor(g));
                if (g.Path == InfoAcquisitionPath.DirectWitness && g.SourceEventId != null) npc.MarkWitnessed(g.SourceEventId);
            }

            // 공간(원문 표): A·W·POLICE_C는 응접실, P는 장면에 배치하지 않음(실종). 플레이어는 현관 홀에서 시작(수락 시 배치).
            occupancy.PlaceAt(NpcA, HouseLayoutFactory.Reception);
            occupancy.PlaceAt(NpcW, HouseLayoutFactory.Reception);

            var built = new GeneratedContract { Contract = c, Npcs = npcs, Rooms = rooms, Occupancy = occupancy, Timeline = t };

            // 증거 EV-01~05(원문 표)
            built.Materials.Add(new EvidenceMaterial
            {
                MaterialId = Ev(1), ContractId = ContractId, Format = ClueFormat.Testimony, SourceNpcId = NpcW,
                Acquisition = MaterialAcquisition.TalkToNpc, AboutEventId = departure.EventId, AboutMinute = 40, AboutRoomId = HouseLayoutFactory.EntranceHall,
                StatedInfoIds = { departure.InfoId },
                Title = "W(TEST)의 목격 진술", Content = "W(TEST): \"07:40쯤 P가 혼자 현관으로 나가는 걸 봤어요. 아무하고도 같이 있지 않았어요.\""
            });
            built.Materials.Add(new EvidenceMaterial
            {
                MaterialId = Ev(2), ContractId = ContractId, Format = ClueFormat.PhysicalObject, RoomId = HouseLayoutFactory.EntranceHall,
                ObjectId = CaseCatalog.ObjectId(HouseLayoutFactory.EntranceHall, "신발"), Acquisition = MaterialAcquisition.InvestigateRoom,
                AboutEventId = departure.EventId, AboutMinute = 40, AboutRoomId = HouseLayoutFactory.EntranceHall, StatedInfoIds = { departure.InfoId },
                Title = "현관의 출입·개인 물품 상태", Content = "현관 옷걸이에서 P의 외투가, 신발장에서 P의 신발이 함께 사라졌다. 스스로 외출할 때처럼 챙겨 간 흔적이다."
            });
            built.Materials.Add(new EvidenceMaterial
            {
                MaterialId = Ev(3), ContractId = ContractId, Format = ClueFormat.Document, RoomId = HouseLayoutFactory.Bedroom,
                ObjectId = CaseCatalog.ObjectId(HouseLayoutFactory.Bedroom, "일기장"), Acquisition = MaterialAcquisition.InvestigateRoom,
                SubjectNpcId = NpcA, StatedInfoIds = { FearInfo },
                Title = "P의 자필 메모", Content = "P의 자필 메모: 'A를 다시 마주치는 게 두렵다. 당분간 나를 찾지 말아 줘.'"
            });
            built.Materials.Add(new EvidenceMaterial
            {
                MaterialId = Ev(4), ContractId = ContractId, Format = ClueFormat.Document, SourceNpcId = NpcW,
                Acquisition = MaterialAcquisition.TalkToNpc, StatedInfoIds = { LetterInfo }, RequiresClueId = Ev(1),
                Title = "P가 W에게 보낸 종이 서신", Content = "P가 떠난 뒤 W에게 보낸 서신: '나는 무사해. 스스로 떠났고, 어디 있는지는 알리지 않을게.' (W가 보관 — 확인·검증한 서신)"
            });
            built.Materials.Add(new EvidenceMaterial
            {
                MaterialId = Ev(5), ContractId = ContractId, Format = ClueFormat.Document, RoomId = HouseLayoutFactory.Study,
                ObjectId = CaseCatalog.ObjectId(HouseLayoutFactory.Study, "서류 더미"), Acquisition = MaterialAcquisition.InvestigateRoom,
                StatedInfoIds = { FollowUpInfo },
                Title = "경찰 후속 연락·소재 확인 기록", Content = "경찰 후속 기록(08:30): P에게 여러 차례 연락하고 소재를 확인했으나 현재 위치를 확인하지 못했다."
            });

            // 경찰 초기 정보(원문: 폭행·상해 신고와 부상은 확인, 이탈 이유와 현재 위치는 모름)
            c.PoliceKnownFacts.Add("1984-12-03 07:20 P가 폭행을 신고했고 경찰이 부상과 진술을 확인했다.");
            c.PoliceKnownFacts.Add("인계 시점(08:00)에 P의 후속 출석·연락이 확인되지 않는다.");
            c.PoliceUnknownFacts.Add("P가 거주지를 떠난 이유");
            c.PoliceUnknownFacts.Add("P의 현재 위치");
            c.PoliceClassificationBasis = "폭행 신고와 부상 확인을 중심으로 폭행·상해로 분류했다(실종으로 재분류하지 않음).";
            c.ExternalState.ReportedSummary = "P의 폭행 신고";
            c.ExternalState.InitialExplanation = "폭행·상해 사건";
            return built;
        }

        public const string FearInfo = ContractId + "-fact-fear";
        public const string LetterInfo = ContractId + "-fact-letter";
        public const string FollowUpInfo = ContractId + "-fact-followup";

        /// <summary>원문 「보고서 정답 및 해금 기대값」 3문단. 정답 개념 동의어는 결정적 검증용 TEST_TEMP 키워드다.</summary>
        public static List<ReportTarget> BuildTargets()
        {
            var list = new List<ReportTarget>();

            var p1 = Target(list, "사건의 실체", "폭행 신고 이후 P에게 어떤 일이 있었는가?",
                "경찰 인계 기록에 따르면 P는 폭행 피해를 신고했고 부상 사실도 확인되었다. 이후 P는 후속 확인 시점에 나타나지 않았으며, 인계 당시 현재 소재도 확인되지 않았다. 경찰은 기존 폭행·상해 신고를 중심으로 사건을 분류해 인계했다. P가 마지막으로 확인된 시점과 그 이후의 경위를 추가로 확인해야 한다.");
            p1.Concepts.Add(Concept("스스로 떠남", "스스로", "혼자", "자발", "제발로", "직접떠", "자진"));
            p1.Concepts.Add(Concept("소재 미확인", "소재", "행방", "위치", "어디", "알수없", "모른"));
            p1.ContradictionTerms.AddRange(new[] { "납치", "살해", "죽었", "시신", "감금" });
            p1.EvidenceGroups.Add(new List<string> { Ev(1), Ev(2) });
            p1.EvidenceGroups.Add(new List<string> { Ev(4) });
            p1.FollowUpHint = "P가 떠난 이유를 보여 줄 개인 기록(침실)을 확인하라.";

            var p2 = Target(list, "인물 정보", "P가 거주지를 떠난 이유는 무엇인가?",
                "추가로 확보된 자료를 통해 P의 마지막 확인 시점과 거주지 이탈 경위를 좁힐 수 있게 되었다. 다만 거주지를 떠났다는 사실만으로 P가 이후 연락을 끊은 이유까지 확정할 수는 없다.");
            p2.Concepts.Add(Concept("두려움", "두려", "무서", "겁", "피하", "피해"));
            p2.Concepts.Add(Concept("A", "A"));
            p2.EvidenceGroups.Add(new List<string> { Ev(3) });
            p2.EvidenceGroups.Add(new List<string> { Ev(1), Ev(4) });
            p2.UnlockGroups.Add(new List<string> { Ev(1), Ev(2) });
            p2.UnlockGroups.Add(new List<string> { Ev(3) });
            p2.FollowUpHint = "경찰의 후속 소재 확인 기록(서재)과 W가 받은 서신을 대조하라.";

            var p3 = Target(list, "사건의 실체", "이 의뢰의 실제 핵심 사건 유형은 무엇이며, 선행 폭행과 어떤 관계인가?",
                "인계 당시 경찰이 기록한 대표 사건 유형은 폭행·상해다. 최초 신고와 부상 확인은 실제로 발생한 사건을 뒷받침하지만, 이후 P의 행방이 확인되지 않는 상태가 이어지고 있다. 선행 폭행과 현재의 행방불명 상태를 함께 설명할 수 있는 실제 사건 유형을 판단해야 한다.");
            p3.Concepts.Add(Concept("실종", "실종"));
            p3.Concepts.Add(Concept("선행 폭행", "폭행", "맞", "때려", "때린"));
            p3.ContradictionTerms.AddRange(new[] { "납치", "살해", "시신", "감금" });
            p3.EvidenceGroups.Add(new List<string> { Ev(1), Ev(4), Ev(5) });
            p3.EvidenceGroups.Add(new List<string> { Ev(3), Ev(4), Ev(5) });
            p3.UnlockGroups.Add(new List<string> { Ev(4), Ev(5) });
            p3.FollowUpHint = "보고서 목표를 모두 정리했다. 회의에서 결론을 정하라.";
            return list;
        }

        private static ReportTarget Target(List<ReportTarget> list, string category, string question, string common)
        {
            var t = new ReportTarget { Index = list.Count, Category = category, Question = question, CommonText = common };
            list.Add(t);
            return t;
        }

        private static ReportConcept Concept(string label, params string[] words)
        {
            var c = new ReportConcept { Label = label };
            c.Synonyms.AddRange(words);
            return c;
        }

        private static TimelineEvent Event(ContractTimeline t, TimelineEventKind kind, int start, int end, string room, params string[] participants)
        {
            string id = $"{t.ContractId}-evt-{t.Events.Count + 1}-{kind}";
            var e = new TimelineEvent { EventId = id, Kind = kind, StartMinute = start, EndMinute = end, RoomId = room, InfoId = id };
            e.ParticipantIds.AddRange(participants);
            t.Events.Add(e);
            return e;
        }

        /// <summary>(방, 시작, 끝) 반복 — 첫 방은 0분부터. 인자: room0, start0, end0(=room1 시작), room1, end1, ...</summary>
        private static void AddSchedule(ContractTimeline t, string npcId, string room, int start, int end, params object[] rest)
        {
            var s = new NpcSchedule(npcId);
            s.TryAdd(start, end, room);
            int from = end;
            for (int i = 0; i + 1 < rest.Length; i += 2)
            {
                int to = (int)rest[i + 1];
                s.TryAdd(from, to, (string)rest[i]);
                from = to;
            }
            t.Schedules[npcId] = s;
        }

        private static void Grant(ContractTimeline t, string npc, string info, InfoAcquisitionPath path, int minute, string source) =>
            t.Knowledge.Add(new KnowledgeGrant { NpcId = npc, InfoId = info, Path = path, Minute = minute, SourceEventId = source });
    }
}
