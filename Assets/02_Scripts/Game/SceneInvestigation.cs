using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.ContractContent;
using CaseFileOmission.NPC;

namespace CaseFileOmission.Game
{
    /// <summary>NPC·경찰이 현장 조사 대상을 살펴본 기록(행동 아이콘 표시용). 의뢰가 바뀌면 비운다.</summary>
    public sealed class SceneInvestigationRecord
    {
        public string ActorId, ObjectId, RoomId;
        public int Minute;
    }

    /// <summary>
    /// 07 「NPC 행동 결정 구조」의 '조사' 행동 후보(의심이 많은 성격 가중치 3 등)에 실제 효과를 붙인다.
    /// 원문에 조사 효과가 정의되어 있지 않아 TEST_TEMP: 같은 방의 조사 대상 하나를 살펴 거기 있는 자료가 말하는 사실을 알게 된다
    /// (07 "직접 목격·조사·대화로 알게 된 정보"). 자료를 가져가거나 옮기지 않으며, 이미 다 아는 대상은 후보에서 빠진다.
    /// 선택 여부는 기존 성격 가중치 정책이 정한다(일정 이동보다 무조건 앞서지 않는다).
    /// </summary>
    public sealed class NpcSceneInvestigationSource : INpcBehaviorCandidateSource
    {
        private readonly GameSession _session;

        public NpcSceneInvestigationSource(GameSession session) => _session = session;

        public IEnumerable<NpcBehaviorCandidate> GetCandidates(NpcState actor, string currentRoomId, int currentMinute)
        {
            var s = _session;
            if (s.CurrentContract == null || actor.ContractId != s.CurrentContract.ContractId) yield break;

            foreach (var obj in s.GetInvestigationObjects().Where(o => o.RoomId == currentRoomId))
            {
                var facts = s.Materials
                    .Where(m => m.ContractId == actor.ContractId && m.Acquisition == MaterialAcquisition.InvestigateRoom && m.ObjectId == obj.ObjectId
                                && !s.Campaign.StolenMaterialIds.Contains(m.MaterialId)) // 플레이어가 가져간 물건은 현장에 없다
                    .SelectMany(m => m.StatedInfoIds)
                    .Where(id => !string.IsNullOrEmpty(id) && !actor.Knows(id))
                    .Distinct()
                    .ToList();
                if (facts.Count == 0) continue;

                string objectId = obj.ObjectId;
                yield return new NpcBehaviorCandidate
                {
                    Kind = NpcBehaviorKind.Investigate,
                    TargetId = objectId,
                    RequiredRoomId = currentRoomId,
                    Execute = () =>
                    {
                        foreach (var id in facts) actor.LearnInfo(id, NpcJudgement.Believe);
                        s.RecordSceneInvestigation(actor.NpcId, objectId, currentRoomId, currentMinute);
                    }
                };
                yield break; // 한 턴에 대상 하나만 후보로 낸다.
            }
        }
    }
}
