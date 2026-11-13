using System;
using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Contracts;
using CaseFileOmission.Evidence;
using CaseFileOmission.NPC;

namespace CaseFileOmission.ContractContent
{
    /// <summary>
    /// 부수 범죄 생성. 가중치(절도 30·협박 30·폭행 20·살인 10·실종 관련 10)는 04 확정,
    /// 발생 판정 30%는 사용자 확정(2026-11-13, 구현 명세 4.7의 20%를 대체), 연결 조건·시점은 「의뢰사건_랜덤생성_구현명세」 4.7을 따른다.
    /// - 대표 사건 1건당 30% 확률로 발생 여부를 먼저 판정하고, 발생하면 연결 조건을 만족하는 후보만 남겨 가중치로 고른다. 후보가 없으면 붙이지 않는다.
    /// - 시점은 핵심 사건 직후 10~30분(10분 단위 균등), 장소는 연결 조건의 방. 시간표에 Aftermath 사건으로 넣고 연결 이유·인물을 기록한다.
    /// - 그 방에 부수 범죄 흔적 자료(E12)를 둔다(<see cref="AddTrace"/>).
    /// 살인·실종 관련 후보는 4.7 조건상 목격자 사망·시신 이동을 만들어야 하는데, 지금 자료 생성기는 그 인물의 증언·시신 위치를 전제로 해서
    /// 후보에서 뺀다(인수인계 문서에 기록).
    /// </summary>
    public static class SubCrimeGenerator
    {
        /// <summary>사용자 확정(2026-11-13): 부수 범죄 발생 여부를 먼저 30%로 판정하고, 발생할 때만 유효한 유형 후보 중에서 고른다.</summary>
        public const double AddChance = 0.30;

        public static void TryAdd(GeneratedContract built, ContractGenerator generator, Random random)
        {
            var c = built.Contract;
            var t = built.Timeline;
            var core = t.CoreIncident;
            if (core == null || random.NextDouble() >= AddChance) return;

            int minute = core.EndMinute + 10 * random.Next(1, 4);
            if (minute + t.SlotMinutes > t.DayEndMinute) return;

            string perp = c.Truth.PerpetratorId;
            string perpRoom = t.GetRoomAt(perp, minute);
            if (perpRoom == null) return;

            var candidates = new Dictionary<CrimeType, (string Room, string Target, string Reason)>();

            // 절도·도난: 핵심 사건 이후 가해자가 피해자 소유 물건에 접근 가능(현장과 같은 방 또는 인접 방).
            // 절도→실종 조합은 "타인이 불법으로 가져간 실제 절도는 없어야 한다"(04 §5)라 부수 절도를 붙이지 않는다.
            bool theftForbidden = c.PoliceCrimeType == CrimeType.TheftRobbery && c.PrimaryCrimeType == CrimeType.Missing;
            if (!theftForbidden && (perpRoom == t.SceneRoomId || built.Rooms.AreConnected(perpRoom, t.SceneRoomId)))
                candidates[CrimeType.TheftRobbery] = (perpRoom, c.Truth.VictimId, "핵심 사건 직후 가해자가 피해자의 물건을 챙김");

            // 협박: 목격자·사건 관계자가 1명 이상 생존하고, 가해자와의 관계가 '서로 모르는 사이'가 아님.
            var threatTarget = built.Npcs.GetByContract(c.ContractId)
                .Where(n => n.NpcId != perp && n.NpcId != c.Truth.VictimId && n.IsAlive
                            && (n.ActualRole == NpcActualRole.Witness || n.ActualRole == NpcActualRole.CaseRelatedA || n.ActualRole == NpcActualRole.CaseRelatedB))
                .OrderBy(n => n.NpcId, StringComparer.Ordinal).FirstOrDefault();
            if (threatTarget != null && c.Truth.RelationshipId != "REL-07")
                candidates[CrimeType.Blackmail] = (perpRoom, threatTarget.NpcId, "본 것을 말하지 못하게 가해자가 입막음을 함");

            // 폭행·상해: 핵심 사건 시각에 같은 방에 있던 비가해자 생존자 1명 이상 — 그 인물이 이 시각에도 가해자와 같은 방이어야 한다.
            var assaultTarget = t.GetPresentAt(core.RoomId, core.StartMinute)
                .Where(id => id != perp && id != c.Truth.VictimId && built.Npcs.Exists(id) && built.Npcs.Get(id).IsAlive
                             && t.GetRoomAt(id, minute) == perpRoom)
                .OrderBy(id => id, StringComparer.Ordinal).FirstOrDefault();
            if (assaultTarget != null)
                candidates[CrimeType.AssaultInjury] = (perpRoom, assaultTarget, "현장을 본 인물을 가해자가 위협하다 다치게 함");

            if (candidates.Count == 0) return;
            var excluded = new[] { CrimeType.Murder, CrimeType.Missing, CrimeType.AssaultInjury, CrimeType.TheftRobbery, CrimeType.Blackmail }
                .Where(type => !candidates.ContainsKey(type));
            CrimeType chosen;
            try { chosen = generator.PickSubCrimeType(excluded); }
            catch (InvalidOperationException) { return; }

            var (room, target, reason) = candidates[chosen];
            string id = $"{c.ContractId}-evt-{t.Events.Count + 1}-{TimelineEventKind.Aftermath}";
            var evt = new TimelineEvent
            {
                EventId = id,
                Kind = TimelineEventKind.Aftermath,
                StartMinute = minute,
                EndMinute = minute + t.SlotMinutes,
                RoomId = room,
                InfoId = id
            };
            evt.ParticipantIds.Add(perp);
            if (chosen == CrimeType.AssaultInjury) evt.ParticipantIds.Add(target);
            evt.CauseEventIds.Add(core.EventId);
            t.Events.Add(evt);

            // 지식: 참여자는 자기 행위로, 그 시각 그 방에 있던 다른 인물은 직접 목격으로 안다.
            foreach (var present in t.GetPresentAt(room, minute))
            {
                t.Knowledge.Add(new KnowledgeGrant
                {
                    NpcId = present,
                    InfoId = id,
                    Path = evt.ParticipantIds.Contains(present) ? InfoAcquisitionPath.OwnAction : InfoAcquisitionPath.DirectWitness,
                    Minute = minute,
                    SourceEventId = id
                });
            }

            c.AddSubCrime(chosen, reason, perp, target);
            c.SubCrimeLinks[c.SubCrimeLinks.Count - 1].EventId = id;
        }

        /// <summary>부수 범죄 흔적(E12): 부수 범죄 방의 조사 오브젝트에 놓는 사실 자료.</summary>
        public static void AddTrace(GeneratedContract built, Func<string, bool, string> pickObject, Func<string, string> nextId, string perpName)
        {
            var c = built.Contract;
            if (c.SubCrimeLinks.Count == 0) return;
            var link = c.SubCrimeLinks[0];
            var evt = built.Timeline.Events.FirstOrDefault(e => e.EventId == link.EventId);
            if (evt == null) return;
            string obj = pickObject(evt.RoomId, link.Type == CrimeType.Blackmail);
            string content = link.Type switch
            {
                CrimeType.TheftRobbery => $"{CaseCatalog.ObjectName(obj)} 안이 비어 있다. 피해자가 아끼던 물건이 사건 직후 사라진 흔적이다. 서랍 손잡이에 {perpName}의 장갑 자국이 남아 있다.",
                CrimeType.Blackmail => $"'본 것을 입 밖에 내지 마라.'라고 적힌 쪽지. 사건 직후 쓴 것으로 보이며 필체가 {perpName}의 메모와 닮았다.",
                _ => $"{CaseCatalog.ObjectName(obj)} 주변에 사건 직후 또 한 번 몸싸움이 있었던 흔적이 있다."
            };
            built.Materials.Add(new EvidenceMaterial
            {
                MaterialId = nextId("subcrime-trace"),
                ContractId = c.ContractId,
                Format = link.Type == CrimeType.Blackmail ? ClueFormat.Document : ClueFormat.PhysicalObject,
                RoomId = evt.RoomId,
                ObjectId = obj,
                Acquisition = MaterialAcquisition.InvestigateRoom,
                AboutEventId = evt.EventId,
                AboutMinute = evt.StartMinute,
                AboutRoomId = evt.RoomId,
                SubjectNpcId = c.Truth.PerpetratorId,
                StatedInfoIds = { evt.InfoId },
                Title = "부수 범죄의 흔적",
                Content = content
            });
        }
    }
}
