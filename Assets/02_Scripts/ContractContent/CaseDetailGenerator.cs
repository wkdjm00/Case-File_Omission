using System;
using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Contracts;
using CaseFileOmission.Evidence;
using CaseFileOmission.NPC;
using CaseFileOmission.Rooms;

namespace CaseFileOmission.ContractContent
{
    /// <summary>조사 오브젝트 하나(방 안의 물건). 단서가 없을 수도 있다.</summary>
    public sealed class InvestigationObject
    {
        public string ObjectId;
        public string RoomId;
        public string Name;
    }

    /// <summary>
    /// 04 확정 고정 선택지로 진실 세부를 정하고, 시간표·기본 자료를 바탕으로 조사 오브젝트별 단서·문서·증언을 만든다.
    /// 원문은 문서·증언 문장을 AI(CASE_MATERIALS)가 만들게 했다. AI 연동 전에는 같은 데이터 조건을 지키는 결정적 템플릿으로 만든다(TEST_TEMP 문장).
    /// 지키는 확정 규칙: 각 필수 사실에 증거 경로 최소 1개, 사실 증언은 출처 NPC가 아는 정보만, 단서는 방별 조사 오브젝트 후보 안에만,
    /// 단서 없는 조사 대상 허용, 현장에 없는 단서 자동 지급 금지, 거짓 자료 한도(기존 생성기가 담당).
    /// </summary>
    public static class CaseDetailGenerator
    {
        // 04 확정 경찰 선행 수사 기간(일): 살인 3~5, 실종 2~5, 폭행·상해 1~3, 절도·도난 1~3, 협박 1~4.
        private static (int Min, int Max) HandoverDays(CrimeType crime) => crime switch
        {
            CrimeType.Murder => (3, 5),
            CrimeType.Missing => (2, 5),
            CrimeType.AssaultInjury => (1, 3),
            CrimeType.TheftRobbery => (1, 3),
            _ => (1, 4)
        };

        /// <summary>
        /// 진실 세부를 고정 선택지에서 고른다. 동기는 양립 후보 중 균등(2026-11-13). 세부 종류는 유효 후보 중 균등.
        /// 사건 날짜는 1984-10-01~12-31 균등. 조사 시작 시간대는 의뢰 데이터로 정한다(아침·저녁 중 생성 시 결정).
        /// </summary>
        public static void AssignTruthDetails(ContractInstance contract, Random random, ContractConstraints constraints = null)
        {
            var truth = contract.Truth;
            var crime = contract.PrimaryCrimeType;

            // 폭행 뒤 스스로 떠난 실종은 핵심 행위가 선행 폭행이다(실종 자체에는 범행 방법이 없다 — 자발적 이탈).
            var methods = CaseCatalog.MethodsFor(truth.VictimDeparted && crime == CrimeType.Missing ? CrimeType.AssaultInjury : crime);
            // 메인 사건 단서 제약(예: 칼 TOOL-01)이 있으면 그 도구를 쓸 수 있는 방법만 고른다. 후보가 없으면 제약 검증에서 버려진다.
            var allowed = constraints?.FilterMethods(methods) ?? methods;
            if (allowed.Count > 0) methods = allowed;
            truth.MethodId = methods[random.Next(methods.Count)];
            var tools = CaseCatalog.ToolsFor(truth.MethodId);
            truth.ToolId = constraints?.ToolId != null && tools.Contains(constraints.ToolId) ? constraints.ToolId : tools[random.Next(tools.Count)];
            truth.MotiveId = CaseCatalog.Motives[random.Next(CaseCatalog.Motives.Length)].Id;
            truth.RelationshipId = CaseCatalog.Relationships[random.Next(CaseCatalog.Relationships.Length - 1)].Id; // REL-07은 동기 설명 관계에서 제외

            truth.Method = CaseCatalog.Name(CaseCatalog.Methods, truth.MethodId);
            truth.Tool = truth.ToolId == "TOOL-00" ? null : CaseCatalog.Name(CaseCatalog.Tools, truth.ToolId);
            truth.PrimaryMotive = CaseCatalog.Name(CaseCatalog.Motives, truth.MotiveId);
            truth.RelationshipType = CaseCatalog.Name(CaseCatalog.Relationships, truth.RelationshipId);

            var rep = CaseCatalog.RepresentativeSubtypes[contract.PoliceCrimeType];
            truth.RepresentativeSubtypeId = rep[random.Next(rep.Length)].Id;
            var act = CaseCatalog.ActualSubtypes[crime];
            truth.ActualSubtypeId = act[random.Next(act.Length)].Id;

            var date = new DateTime(1984, 10, 1).AddDays(random.Next(92));
            contract.IncidentDate = date.ToString("yyyy-MM-dd");
            contract.IncidentClockMinute = 10 * random.Next(8 * 6, 20 * 6); // 08:00~19:50 사이에서 경위 시작(10분 정밀도)
            var (min, max) = HandoverDays(crime);
            contract.HandoverDayOffset = random.Next(min, max + 1);
            // 시작 시간대는 의뢰 데이터(생성 설정)가 정한 값을 그대로 쓴다(06: 아침 강제·무작위 강제 아님).
        }

        public static string Clock(ContractInstance contract, int timelineMinute)
        {
            int m = contract.IncidentClockMinute + timelineMinute;
            return $"{(m / 60) % 24:00}:{m % 60:00}";
        }

        private static string Name(NpcRegistry npcs, string id) => id != null && npcs.Exists(id) ? npcs.Get(id).DisplayName ?? id : "누군가";

        /// <summary>대표 피해자와 함께 숨진 추가 피해자 이름("A, B, C").</summary>
        private static string AllVictimNames(ContractInstance c, NpcRegistry npcs) =>
            string.Join(", ", new[] { c.Truth.VictimId }.Concat(c.Truth.AdditionalVictimIds).Select(id => Name(npcs, id)));

        private static string Room(string roomId) => HouseLayoutFactory.GetDisplayName(roomId);

        /// <summary>
        /// 기존 자료에 조사 오브젝트·제목·본문을 붙이고, 범행 도구·동기 문서·피해자 쪽 기록·은폐 흔적·관계 진술을 추가한다.
        /// 추가 증언의 정보는 출처 NPC의 지식(사전 상황 — 대화로 알게 된 관계)으로 시간표에 함께 기록한다.
        /// </summary>
        public static void Enrich(GeneratedContract built, Random random)
        {
            var c = built.Contract;
            var t = built.Timeline;
            var npcs = built.Npcs;
            var core = t.CoreIncident;
            if (core == null) return;

            string perp = Name(npcs, c.Truth.PerpetratorId);
            string victim = Name(npcs, c.Truth.VictimId);
            string time = Clock(c, core.StartMinute);
            var used = new HashSet<string>();

            string PickObject(string room, bool document)
            {
                var pool = CaseCatalog.RoomObjects[room].Where(o => CaseCatalog.DocumentObjects.Contains(o) == document).ToList();
                if (pool.Count == 0) pool = CaseCatalog.RoomObjects[room].ToList();
                var free = pool.Where(o => !used.Contains(CaseCatalog.ObjectId(room, o))).ToList();
                var name = (free.Count > 0 ? free : pool)[random.Next((free.Count > 0 ? free : pool).Count)];
                string id = CaseCatalog.ObjectId(room, name);
                used.Add(id);
                return id;
            }

            foreach (var m in built.Materials)
            {
                string source = m.SourceNpcId != null ? Name(npcs, m.SourceNpcId) : null;
                string aboutRoom = m.AboutRoomId != null ? Room(m.AboutRoomId) : null;
                string aboutTime = Clock(c, m.AboutMinute);
                if (m.MaterialId == ContractPipeline.BodyMaterialId(c.ContractId))
                {
                    m.ObjectId = c.Truth.BodyHidden ? PickObject(m.RoomId, document: false) : CaseCatalog.ObjectId(m.RoomId, "시신");
                    m.Title = c.Truth.BodyHidden ? $"숨겨진 {victim}의 시신" : $"{victim}의 시신";
                    m.Content = $"{victim}의 시신이 {Room(m.RoomId)}에 있다. {BodyNote(c.Truth.MethodId)}"
                                + (c.Truth.ActualSubtypeId == "ACT-SUB-MUR-02" ? " 공격받은 뒤 한동안 살아 있었던 듯 피가 번진 자리가 넓다." : "")
                                + (m.RoomId == m.AboutRoomId ? ""
                                    : c.Truth.DelayedDeath ? $" {Room(m.AboutRoomId)}에서부터 핏방울이 이어져 있다 — 다친 채로 이 방까지 걸어와 숨진 듯하다."
                                    : $" 바닥에 끌린 자국이 {Room(m.AboutRoomId)} 쪽에서 이어진다.");
                }
                else if (m.MaterialId.Contains("scene-trace"))
                {
                    m.ObjectId = PickObject(m.RoomId, document: false);
                    m.Title = $"{CaseCatalog.ObjectName(m.ObjectId)} 주변의 흔적";
                    m.Content = $"{CaseCatalog.ObjectName(m.ObjectId)} 주변에 {SceneNote(c.PrimaryCrimeType)} {time}경 이 방({Room(m.RoomId)})에서 일이 벌어진 것으로 보인다.";
                }
                else if (m.MaterialId.Contains("witness-testimony"))
                {
                    m.Title = $"{source}의 목격 진술";
                    m.Content = $"{source}: \"{aboutTime}쯤 {aboutRoom}에서 {perp}이(가) {ActionPhrase(c.PrimaryCrimeType, victim)} 걸 봤어요.\"";
                }
                else if (m.MaterialId.Contains("presence-testimony") || m.MaterialId.Contains("false-alibi"))
                {
                    m.Title = $"{source}의 알리바이 진술";
                    m.Content = $"{source}: \"{aboutTime}쯤이면… 저는 {aboutRoom}에 있었어요.\"";
                }
            }

            int n = built.Materials.Count;
            string NextId(string kind) => $"{c.ContractId}-mat-{++n}-{kind}";

            // 범행 도구: 가해자가 사건 직후 머문 방(없으면 현장)의 물건 속.
            if (c.Truth.ToolId != null && c.Truth.ToolId != "TOOL-00")
            {
                string room = t.GetRoomAt(c.Truth.PerpetratorId, core.EndMinute) ?? t.SceneRoomId;
                string obj = PickObject(room, document: false);
                built.Materials.Add(new EvidenceMaterial
                {
                    MaterialId = NextId("tool-evidence"),
                    ContractId = c.ContractId,
                    Format = ClueFormat.PhysicalObject,
                    RoomId = room,
                    ObjectId = obj,
                    Acquisition = MaterialAcquisition.InvestigateRoom,
                    AboutEventId = core.EventId,
                    AboutMinute = core.StartMinute,
                    AboutRoomId = core.RoomId,
                    SubjectNpcId = c.Truth.PerpetratorId,
                    StatedInfoIds = { core.InfoId },
                    Title = $"숨겨진 {c.Truth.Tool}",
                    Content = $"{CaseCatalog.ObjectName(obj)} 속에서 {c.Truth.Tool}이(가) 나왔다. '{c.Truth.Method}'에 쓰인 것으로 보이며, {perp}의 손수건에 싸여 있다."
                });
            }

            // 동기·관계 문서: 서재·침실·응접실 중 문서 오브젝트에.
            {
                var rooms = new[] { HouseLayoutFactory.Study, HouseLayoutFactory.Bedroom, HouseLayoutFactory.Reception };
                string room = rooms[random.Next(rooms.Length)];
                string obj = PickObject(room, document: true);
                built.Materials.Add(new EvidenceMaterial
                {
                    MaterialId = NextId("motive-document"),
                    ContractId = c.ContractId,
                    Format = ClueFormat.Document,
                    RoomId = room,
                    ObjectId = obj,
                    Acquisition = MaterialAcquisition.InvestigateRoom,
                    SubjectNpcId = c.Truth.PerpetratorId,
                    StatedInfoIds = { MotiveInfoId(c.ContractId) },
                    Title = $"{CaseCatalog.ObjectName(obj)} — {perp}와(과) {victim}",
                    Content = $"{perp}이(가) {victim}에게 보낸 글. 두 사람은 {c.Truth.RelationshipType} 관계이며, {MotiveNote(c.Truth.MotiveId)} 내용이 적혀 있다."
                });
            }

            // 자발적 이탈: 현관의 개인 물품 상태(EV-02 유형)와 이탈을 본 인물의 진술(EV-01 유형).
            var departure = t.VictimDepartureMinute >= 0 && c.Truth.VictimDeparted ? t.Events.FirstOrDefault(e => e.Kind == TimelineEventKind.Aftermath && e.StartMinute == t.VictimDepartureMinute && e.ParticipantIds.Contains(c.Truth.VictimId)) : null;
            if (departure != null)
            {
                string hallObj = PickObject(HouseLayoutFactory.EntranceHall, document: false);
                built.Materials.Add(new EvidenceMaterial
                {
                    MaterialId = NextId("departure-trace"), ContractId = c.ContractId, Format = ClueFormat.PhysicalObject,
                    RoomId = HouseLayoutFactory.EntranceHall, ObjectId = hallObj, Acquisition = MaterialAcquisition.InvestigateRoom,
                    AboutEventId = departure.EventId, AboutMinute = departure.StartMinute, AboutRoomId = departure.RoomId, StatedInfoIds = { departure.InfoId },
                    Title = "현관의 출입·개인 물품 상태",
                    Content = $"{victim}의 외투와 신발이 함께 사라졌다. 스스로 챙겨 나간 흔적이다."
                });
                foreach (var seen in t.Knowledge.Where(k => k.InfoId == departure.InfoId && k.Path == InfoAcquisitionPath.DirectWitness && k.NpcId != c.Truth.VictimId).Select(k => k.NpcId).Distinct().ToList())
                {
                    built.Materials.Add(new EvidenceMaterial
                    {
                        MaterialId = NextId("departure-testimony"), ContractId = c.ContractId, Format = ClueFormat.Testimony, SourceNpcId = seen,
                        Acquisition = MaterialAcquisition.TalkToNpc, AboutEventId = departure.EventId, AboutMinute = departure.StartMinute, AboutRoomId = departure.RoomId,
                        StatedInfoIds = { departure.InfoId },
                        Title = $"{Name(npcs, seen)}의 목격 진술",
                        Content = $"{Name(npcs, seen)}: \"{Clock(c, departure.StartMinute)}쯤 {victim}이(가) 혼자 현관으로 나가는 걸 봤어요.\""
                    });
                }
            }

            // 절도→실종: 사라진 물건은 타인이 훔친 것이 아니라 소유자와 함께 사라졌다(원문 성립 조건).
            if (c.PoliceCrimeType == CrimeType.TheftRobbery && c.PrimaryCrimeType == CrimeType.Missing)
            {
                string ledger = PickObject(HouseLayoutFactory.Study, document: true);
                built.Materials.Add(new EvidenceMaterial
                {
                    MaterialId = NextId("item-record"), ContractId = c.ContractId, Format = ClueFormat.Document,
                    RoomId = HouseLayoutFactory.Study, ObjectId = ledger, Acquisition = MaterialAcquisition.InvestigateRoom,
                    StatedInfoIds = { CrimeInfoId(c.ContractId) },
                    Title = "보관 물품 반출 기록",
                    Content = $"도난 신고된 물건의 반출 기록. '{victim} 본인이 직접 꺼내 감'이라고 적혀 있고, 그 뒤로 {victim}의 서명이 없다."
                });
            }

            // 피해자 쪽 기록: 사건 유형별로 피해 사실을 뒷받침한다.
            {
                string room = c.PrimaryCrimeType == CrimeType.TheftRobbery ? HouseLayoutFactory.Study : HouseLayoutFactory.Bedroom;
                string obj = PickObject(room, document: true);
                var (title, content) = VictimDocument(c, victim, perp, time);
                built.Materials.Add(new EvidenceMaterial
                {
                    MaterialId = NextId("victim-document"),
                    ContractId = c.ContractId,
                    Format = ClueFormat.Document,
                    RoomId = room,
                    ObjectId = obj,
                    Acquisition = MaterialAcquisition.InvestigateRoom,
                    SubjectNpcId = c.Truth.PerpetratorId,
                    StatedInfoIds = { CrimeInfoId(c.ContractId) },
                    Title = title,
                    Content = content
                });
            }

            // 은폐 흔적: 은폐가 있으면 현장에.
            if (c.Truth.CoverUp != CoverUpMethod.None)
            {
                string obj = PickObject(t.SceneRoomId, document: false);
                built.Materials.Add(new EvidenceMaterial
                {
                    MaterialId = NextId("coverup-trace"),
                    ContractId = c.ContractId,
                    Format = ClueFormat.PhysicalObject,
                    RoomId = t.SceneRoomId,
                    ObjectId = obj,
                    Acquisition = MaterialAcquisition.InvestigateRoom,
                    StatedInfoIds = { CoverUpInfoId(c.ContractId) },
                    Title = "부자연스러운 흔적",
                    Content = $"{CaseCatalog.ObjectName(obj)} 쪽에 {CoverUpNote(c.Truth.CoverUp)}"
                });
            }

            // 부수 범죄 흔적(E12)
            SubCrimeGenerator.AddTrace(built, PickObject, NextId, perp);

            // 사건 관계자의 관계 진술: 피해자와 가해자의 관계·갈등(사전 상황)을 안다.
            foreach (var related in npcs.GetByContract(c.ContractId)
                         .Where(x => x.ActualRole == NpcActualRole.CaseRelatedA || x.ActualRole == NpcActualRole.CaseRelatedB)
                         .OrderBy(x => x.NpcId, StringComparer.Ordinal))
            {
                string info = MotiveInfoId(c.ContractId);
                t.Knowledge.Add(new KnowledgeGrant { NpcId = related.NpcId, InfoId = info, Path = InfoAcquisitionPath.Conversation, Minute = 0 });
                built.Materials.Add(new EvidenceMaterial
                {
                    MaterialId = NextId("relation-testimony"),
                    ContractId = c.ContractId,
                    Format = ClueFormat.Testimony,
                    SourceNpcId = related.NpcId,
                    Acquisition = MaterialAcquisition.TalkToNpc,
                    SubjectNpcId = c.Truth.PerpetratorId,
                    StatedInfoIds = { info },
                    Title = $"{related.DisplayName}의 관계 진술",
                    Content = $"{related.DisplayName}: \"{victim}하고 {perp}요? {c.Truth.RelationshipType} 사이죠. 요즘 {MotiveShort(c.Truth.MotiveId)} 문제로 자주 부딪혔어요.\""
                });
            }
        }

        /// <summary>
        /// 시간표가 정해진 뒤 실제 세부 종류의 성립 기준을 맞춘다(04 중복 판정 규칙).
        /// - 살인 ACT-SUB-MUR-03: 시신을 가해자가 사건 직후 머문 방(현장과 같으면 이웃 방)으로 옮긴다 — 사망 장소 ≠ 발견 장소.
        /// - 폭행 ACT-SUB-ASS-03: 피해자가 사건 직후 다른 방으로 이동했을 때만. 아니면 ASS-01(즉시 확인)로 둔다.
        /// 시신 자료의 방을 바꾸고, 시신 위치는 <see cref="PlaceBody"/>가 시간표 적용 뒤에 맞춘다.
        /// </summary>
        public static void ApplySubtypeToTimeline(GeneratedContract built)
        {
            var c = built.Contract;
            var t = built.Timeline;
            var core = t.CoreIncident;
            if (core == null) return;

            if (c.PrimaryCrimeType == CrimeType.AssaultInjury)
            {
                string after = t.GetRoomAt(c.Truth.VictimId, core.EndMinute);
                bool moved = after != null && after != t.SceneRoomId;
                if (moved) c.Truth.ActualSubtypeId = "ACT-SUB-ASS-03";
                else if (c.Truth.ActualSubtypeId == "ACT-SUB-ASS-03") c.Truth.ActualSubtypeId = "ACT-SUB-ASS-01";
            }

            if (c.Truth.BodyHidden) c.Truth.ActualSubtypeId = "ACT-SUB-MUR-04";
            else if (c.Truth.DelayedDeath) c.Truth.ActualSubtypeId = "ACT-SUB-MUR-02";
            else if (c.Truth.VictimDeparted && c.PrimaryCrimeType == CrimeType.Missing) c.Truth.ActualSubtypeId = "ACT-SUB-MIS-01";

            if (c.PrimaryCrimeType == CrimeType.Murder && (c.Truth.ActualSubtypeId == "ACT-SUB-MUR-03" || c.Truth.BodyHidden || c.Truth.DelayedDeath))
            {
                var body = built.Materials.FirstOrDefault(m => m.MaterialId == ContractPipeline.BodyMaterialId(c.ContractId));
                string room = t.GetRoomAt(c.Truth.PerpetratorId, core.EndMinute);
                if (c.Truth.DelayedDeath)
                    room = t.Events.FirstOrDefault(e => e.Kind == TimelineEventKind.Aftermath && e.StartMinute == t.VictimDepartureMinute && e.ParticipantIds.Contains(c.Truth.VictimId))?.RoomId ?? room;
                if (room == null || room == t.SceneRoomId)
                    room = built.Rooms.GetAdjacentRooms(t.SceneRoomId).OrderBy(r => r, StringComparer.Ordinal).First();
                if (body != null) body.RoomId = room;
            }
        }

        /// <summary>시간표 적용(사망 피해자를 현장에 둠) 뒤, 시신 자료가 놓인 방으로 시신 위치를 맞춘다.</summary>
        public static void PlaceBody(GeneratedContract built)
        {
            var c = built.Contract;
            var body = built.Materials.FirstOrDefault(m => m.MaterialId == ContractPipeline.BodyMaterialId(c.ContractId));
            // 시신 은닉: 인계 시점까지 발견되지 않았으므로 판 위에 놓지 않는다(숨긴 물건을 조사해야 찾는다).
            // 스스로 떠난 피해자도 마찬가지 — 조사는 경찰 선행 수사 뒤 인계 시점에 시작하므로 이미 집에 없다.
            if (c.Truth.BodyHidden || c.Truth.VictimDeparted)
                built.Occupancy.Remove(c.Truth.VictimId);
            else if (body != null && built.Occupancy.HasRoom(body.RoomId))
                built.Occupancy.PlaceAt(c.Truth.VictimId, body.RoomId);
            // 함께 숨진 추가 피해자는 핵심 사건이 일어난 현장에 남는다.
            foreach (var extra in c.Truth.AdditionalVictimIds)
                if (built.Npcs.Exists(extra)) built.Occupancy.PlaceAt(extra, built.Timeline.SceneRoomId);
        }

        /// <summary>
        /// 경찰 초기 정보(04 「무작위」 C·계약 v1 police_snapshot). 인계 시각까지 경찰이 실제로 확보할 수 있었던 신고·현장 흔적·진술만 known에 넣고,
        /// 실제 진실 중 몰랐던 것은 unknown에 둔다. 대표 유형 분류 근거는 known만으로 설명한다. 존재하지 않는 사망·부상·절도·협박을 확정적으로 말하지 않는다.
        /// </summary>
        public static void BuildPoliceSnapshot(GeneratedContract built)
        {
            var c = built.Contract;
            var t = built.Timeline;
            var npcs = built.Npcs;
            string victim = Name(npcs, c.Truth.VictimId);
            string scene = Room(t.SceneRoomId);
            string reporter = t.Discovery?.ParticipantIds.FirstOrDefault();
            var known = c.PoliceKnownFacts;
            var unknown = c.PoliceUnknownFacts;
            known.Clear();
            unknown.Clear();

            if (t.Discovery != null)
                known.Add($"{c.IncidentDate} {Clock(c, t.Discovery.StartMinute)}경 {Name(npcs, reporter)}의 신고로 접수, 현장은 주택 {scene}.");

            var police = c.PoliceCrimeType;
            var actual = c.PrimaryCrimeType;
            string basis;
            switch (police)
            {
                case CrimeType.Murder when actual == CrimeType.Murder:
                    known.Add($"{AllVictimNames(c, npcs)}의 사망 확인.");
                    if (c.Truth.VictimsAreCouple && c.Truth.AdditionalVictimIds.Count > 0)
                        known.Add($"{victim}와(과) {Name(npcs, c.Truth.AdditionalVictimIds[0])}은(는) 부부·연인 관계로 확인.");
                    basis = $"{victim}의 시신과 외상이 확인되어 살인 가능성을 조사한다.";
                    break;
                case CrimeType.Murder when actual == CrimeType.Blackmail:
                    known.Add($"{scene}에서 혈흔과 몸싸움 흔적 확인, {victim}의 소재 불명.");
                    basis = $"현장 정황으로 {victim}의 살해 또는 시신 유기를 의심했다.";
                    unknown.Add($"{victim}이(가) 살아 있으며 협박을 피해 스스로 숨었다는 사실");
                    break;
                case CrimeType.Murder:
                    known.Add($"{scene}에서 다량의 혈흔과 몸싸움 흔적 확인.");
                    basis = actual == CrimeType.Missing
                        ? $"혈흔과 몸싸움 흔적으로 {victim}의 사망을 의심했으나 시신은 찾지 못했다."
                        : $"심하게 다친 {victim}와(과) 다량의 혈흔으로 치명적 공격을 의심했다.";
                    unknown.Add(actual == CrimeType.Missing ? $"{victim}의 실제 생존 여부와 소재" : $"{victim}의 상해가 치명적이지 않다는 사실");
                    break;
                case CrimeType.TheftRobbery:
                    known.Add($"{scene}이(가) 어질러져 있고 물건이 없어졌다는 신고.");
                    basis = "물품 부재와 어질러진 현장으로 절도·도난으로 분류했다.";
                    if (actual == CrimeType.AssaultInjury) unknown.Add($"{victim}이(가) 실제로 입은 부상");
                    if (actual == CrimeType.Murder) unknown.Add($"{victim}이(가) 이미 사망했다는 사실과 시신의 위치");
                    if (actual == CrimeType.Missing) unknown.Add($"물건 주인 {victim}이(가) 실종되었고 물건은 {victim}와(과) 함께 사라졌다는 사실");
                    break;
                case CrimeType.Blackmail:
                    known.Add($"{victim}이(가) 협박을 받았다는 신고.");
                    basis = "구체적인 협박 신고가 접수되어 협박으로 분류했다.";
                    if (actual == CrimeType.AssaultInjury) unknown.Add($"{victim}에 대한 실제 공격과 부상");
                    if (actual == CrimeType.Missing) unknown.Add($"{victim}의 현재 소재");
                    if (actual == CrimeType.Murder) unknown.Add($"{victim}이(가) 이미 사망했다는 사실과 시신의 위치");
                    break;
                case CrimeType.Missing:
                    known.Add($"{victim}의 소재가 확인되지 않음.");
                    basis = $"{victim}의 마지막 확인 이후 연락이 끊겨 실종으로 분류했다.";
                    if (actual == CrimeType.Murder) unknown.Add($"{victim}이(가) 이미 사망했다는 사실과 시신의 위치");
                    if (actual == CrimeType.AssaultInjury) unknown.Add($"{victim}이(가) 떠나기 전 실제로 공격받아 다쳤다는 사실");
                    if (actual == CrimeType.Blackmail) unknown.Add($"{victim}이(가) 협박을 피해 스스로 숨었다는 사실");
                    break;
                default:
                    known.Add($"{victim}의 부상 확인.");
                    basis = $"{victim}의 신체 피해가 확인되어 폭행·상해로 분류했다.";
                    if (actual == CrimeType.Missing) { known.Add($"인계 시점에 {victim}의 후속 출석·연락이 확인되지 않는다."); unknown.Add($"{victim}이(가) 스스로 떠난 이유와 현재 위치"); }
                    if (actual == CrimeType.Murder) unknown.Add($"{victim}이(가) 공격받은 뒤 다른 방에서 숨졌다는 사실");
                    break;
            }
            c.PoliceClassificationBasis = basis;
            unknown.Add("가해자의 정체");
            unknown.Add("범행 동기와 방법");
            if (c.Truth.CoverUp != CoverUpMethod.None) unknown.Add("외부에 알려진 경위와 실제의 차이");
        }

        public static string MotiveInfoId(string contractId) => $"{contractId}-fact-motive";
        public static string CrimeInfoId(string contractId) => $"{contractId}-fact-crime";
        public static string CoverUpInfoId(string contractId) => $"{contractId}-fact-coverup";

        /// <summary>
        /// 이 의뢰의 조사 오브젝트 목록. 단서가 있는 오브젝트는 모두 포함하고, 방마다 단서 없는 후보 2개를 의뢰 ID로 재현 가능하게 더한다
        /// (04 "모든 물체가 매 사건 조사 가능한 것은 아니다", "단서가 없는 조사 대상도 허용").
        /// </summary>
        public static List<InvestigationObject> BuildObjects(string contractId, IEnumerable<EvidenceMaterial> materials)
        {
            var result = new Dictionary<string, InvestigationObject>();
            foreach (var m in materials.Where(m => m.ObjectId != null))
                result[m.ObjectId] = new InvestigationObject { ObjectId = m.ObjectId, RoomId = CaseCatalog.ObjectRoom(m.ObjectId), Name = CaseCatalog.ObjectName(m.ObjectId) };

            foreach (var pair in CaseCatalog.RoomObjects)
            {
                var random = new Random(StableHash(contractId + ":" + pair.Key));
                foreach (var name in pair.Value.OrderBy(_ => random.Next()).Take(2))
                {
                    string id = CaseCatalog.ObjectId(pair.Key, name);
                    if (!result.ContainsKey(id))
                        result[id] = new InvestigationObject { ObjectId = id, RoomId = pair.Key, Name = name };
                }
            }
            return result.Values.OrderBy(o => o.RoomId, StringComparer.Ordinal).ThenBy(o => o.Name, StringComparer.Ordinal).ToList();
        }

        public static int StableHash(string text)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char ch in text ?? string.Empty) hash = (hash ^ ch) * 16777619;
                return (int)(hash & 0x7FFFFFFF);
            }
        }

        // ───────── TEST_TEMP 문장 ─────────

        private static string BodyNote(string methodId) => methodId switch
        {
            "METHOD-02" => "날붙이에 찔린 상처가 있다.",
            "METHOD-03" => "머리에 둔기로 맞은 상처가 있다.",
            "METHOD-05" => "목에 졸린 자국이 남아 있다.",
            "METHOD-06" => "입가에 이상한 냄새가 나고 외상은 거의 없다.",
            "METHOD-07" => "높은 곳에서 떨어진 듯 몸 여러 곳이 다쳤다.",
            _ => "몸싸움 끝에 맞은 흔적이 있다."
        };

        private static string SceneNote(CrimeType crime) => crime switch
        {
            CrimeType.Murder => "핏자국과 몸싸움 흔적이 있다.",
            CrimeType.AssaultInjury => "핏방울과 넘어진 가구 자국이 있다.",
            CrimeType.Missing => "누군가 급히 나간 듯 물건이 흐트러져 있다.",
            CrimeType.TheftRobbery => "서랍이 열려 있고 물건이 있던 자리가 비어 있다.",
            _ => "구겨진 쪽지 조각과 떨어진 단추가 있다."
        };

        private static string ActionPhrase(CrimeType crime, string victim) => crime switch
        {
            CrimeType.Murder => $"{victim}에게 덤벼드는",
            CrimeType.AssaultInjury => $"{victim}에게 손을 대는",
            CrimeType.Missing => $"{victim}을(를) 어디론가 데려가는",
            CrimeType.TheftRobbery => $"몰래 {victim}의 물건을 챙기는",
            _ => $"{victim}을(를) 위협하는"
        };

        public static string MotiveShort(string motiveId) => motiveId switch
        {
            "MOT-01" => "돈",
            "MOT-02" => "옛 원한",
            "MOT-03" => "관계",
            "MOT-04" => "숨기고 싶은 과거",
            "MOT-05" => "자리와 업무",
            "MOT-06" => "궁지에 몰린",
            "MOT-07" => "집착",
            _ => "개인적인"
        };

        private static string MotiveNote(string motiveId) => motiveId switch
        {
            "MOT-01" => "빌린 돈과 재산 문제로 압박하는",
            "MOT-02" => "지난 일을 절대 잊지 않겠다는 원망이 담긴",
            "MOT-03" => "둘 사이의 다툼과 관계를 끝내자는",
            "MOT-04" => "과거의 일을 덮어 달라는 부탁과 거절이 오간",
            "MOT-05" => "자리와 권한을 두고 다툰",
            "MOT-06" => "궁지에 몰렸다며 두려워하는",
            "MOT-07" => "집착에 가까운 감정을 드러낸",
            _ => "개인적인 불만을 쏟아낸"
        };

        private static string CoverUpNote(CoverUpMethod c) => c switch
        {
            CoverUpMethod.SuicideDisguise => "유서처럼 보이는 쪽지가 있지만 필체가 피해자 것과 다르다.",
            CoverUpMethod.AccidentalDeathDisguise => "미끄러진 사고처럼 꾸민 자국이 있지만 방향이 부자연스럽다.",
            CoverUpMethod.DisappearanceDisguise => "짐을 싸 떠난 것처럼 보이지만 지갑과 신분증은 그대로 있다.",
            CoverUpMethod.RecordOrTestimonyTampering => "출입 기록을 고쳐 쓴 흔적이 있다.",
            CoverUpMethod.BodyConcealment => "무거운 것을 끌어 옮긴 자국과 급히 닦아 낸 얼룩이 있다.",
            _ => "누군가 손댄 흔적이 있다."
        };

        private static (string Title, string Content) VictimDocument(ContractInstance c, string victim, string perp, string time) =>
            c.Truth.VictimDeparted
                ? ($"{victim}의 자필 메모", $"{victim}의 자필 메모: '{perp}을(를) 다시 마주치는 게 두렵다. 당분간 나를 찾지 말아 줘.'")
            : c.PrimaryCrimeType switch
            {
                CrimeType.TheftRobbery => ("보관 물품 목록",
                    $"{victim}의 보관 목록. '{StolenItem(c.Truth.ActualSubtypeId)}' 칸에 '사건 당일 {time} 이후 없어짐'이라고 적혀 있다. 열쇠를 가진 사람으로 {perp}의 이름이 있다."),
                CrimeType.Blackmail => ("협박 편지",
                    $"{victim} 앞으로 온 편지. '{BlackmailNote(c.Truth.ActualSubtypeId)}' 필체가 {perp}의 메모와 닮았다."),
                CrimeType.Missing => ($"{victim}의 메모",
                    $"{victim}의 메모. '{time} — {perp}이(가) 할 이야기가 있다며 따로 보자고 했다.' 이후 기록이 없다."),
                _ => ($"{victim}의 일기",
                    $"{victim}의 일기. '오늘 {time}쯤 {perp}와(과) 이야기하기로 했다. 또 그 문제로 다툴 것 같아 두렵다.'")
            };

        private static string StolenItem(string subtypeId) => subtypeId switch
        {
            "ACT-SUB-THE-01" => "회중시계",
            "ACT-SUB-THE-02" => "은 촛대와 보석함",
            "ACT-SUB-THE-03" => "현금 봉투",
            _ => "계약서 묶음"
        };

        private static string BlackmailNote(string subtypeId) => subtypeId switch
        {
            "ACT-SUB-THR-01" => "말을 듣지 않으면 몸이 성치 못할 것이다.",
            "ACT-SUB-THR-02" => "네 과거를 모두에게 알리겠다.",
            "ACT-SUB-THR-03" => "네 일자리와 재산을 지키고 싶다면 따르라.",
            _ => "본 것을 입 밖에 내지 마라."
        };
    }
}
