using System;
using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Contracts;
using CaseFileOmission.NPC;

namespace CaseFileOmission.ContractContent
{
    /// <summary>
    /// 03 2026-11-12 공통 규칙: "관련 의뢰가 우연히 생성되기를 기다리지 않는다. 먼저 배치할 자료를 정하고 … 제약 조건으로 전달."
    /// 메인 사건 단서의 사건 조건을 의뢰 생성기에 필수 제약으로 넘긴다. 제약을 지키지 못한 후보는 검증에서 버리고 다시 만든다.
    /// </summary>
    public sealed class ContractConstraints
    {
        /// <summary>이 제약으로 배치할 메인 사건 단서 이름(기록·저장용).</summary>
        public string PlannedClueName;

        /// <summary>실제 사건 유형만 살인으로 고정한다. 경찰 대표 유형·외부 설명은 기존 선택을 유지한다.</summary>
        public bool RequireMurder;

        /// <summary>함께 숨진 피해자 수(대표 피해자 포함). 1이면 추가 피해자 없음.</summary>
        public int VictimCount = 1;

        /// <summary>대표 피해자와 추가 피해자 한 명이 부부 또는 연인 관계(REL-02)이고 둘 다 숨진다.</summary>
        public bool CoupleVictims;

        /// <summary>대표 피해자의 직업(04 직업 12종 중 하나).</summary>
        public string VictimOccupation;

        /// <summary>범행 도구(04 도구 카탈로그 ID). 이 도구를 쓸 수 있는 방법 중에서만 고른다.</summary>
        public string ToolId;

        /// <summary>고정해야 하는 범행 방법 ID. null이면 해당 사건 유형의 유효 후보에서 고른다.</summary>
        public string MethodId;

        /// <summary>고정해야 하는 가해자-피해자 관계 ID. null이면 기존 후보 표에서 고른다.</summary>
        public string RelationshipId;

        /// <summary>가해자 성격.</summary>
        public NpcPersonality? PerpetratorPersonality;

        /// <summary>
        /// 03 2026-11-12 권력의 진실: 기존 NPC 한 명에게 부여할 문서 보유·접근 역할 태그(문서 원문의 역할 설명). null이면 보유자를 정하지 않는다.
        /// </summary>
        public string DocumentHolderTag;

        /// <summary>문서 보유자를 피해자로 고정한다(반복 실험 번호 목록 — "해당 피해자의 방").</summary>
        public bool HolderIsVictim;

        /// <summary>
        /// 문서가 존재할 수 있는 가장 이른 날(yyyy-MM-dd). 문서가 사건 당시 이미 보관되어 있으므로 사건 발생일은 이 날짜 이후여야 한다.
        /// 문서 작성일·정산 기간은 고정이고, 사건 일시만 이 조건에 맞춰 다시 고른다.
        /// </summary>
        public string EarliestIncidentDate;

        /// <summary>문서를 둘 기존 조사 오브젝트(방ID:오브젝트명). 플레이어가 이 오브젝트를 조사해야 발견한다.</summary>
        public string DocumentObjectId;

        public static string AdditionalVictimIdFor(string contractId, int index) => $"{contractId}-victim-{index + 2}";

        /// <summary>
        /// 후보의 유형을 제약에 맞춘다. 같은 후보의 검증 재시도에서 여러 번 불러도 결과가 같다.
        /// 살인 고정이면 대표=실제=살인(시신이 현장에 남는 조합)으로 두고, 이탈·은닉·지연 사망 경위는 쓰지 않는다.
        /// </summary>
        public void ApplyToCandidate(ContractInstance candidate)
        {
            candidate.PlannedMainCaseClue = PlannedClueName;
            if (!RequireMurder || candidate.PrimaryCrimeType == CrimeType.Murder)
                return;

            // 실제 사건 유형만 살인으로 보정한다. 경찰의 대표 분류는 별도 상태이므로 덮어쓰지 않는다.
            candidate.PrimaryCrimeType = CrimeType.Murder;
            candidate.Truth.VictimDeparted = ContractGenerator.RequiresVictimDeparture(
                candidate.RepresentativeCrimeType, CrimeType.Murder);
            candidate.Truth.BodyHidden = ContractGenerator.RequiresHiddenBody(
                candidate.RepresentativeCrimeType, CrimeType.Murder);
            candidate.Truth.DelayedDeath = ContractGenerator.RequiresDelayedDeath(
                candidate.RepresentativeCrimeType, CrimeType.Murder);

            if (candidate.Truth.BodyHidden)
                candidate.Truth.CoverUp = CoverUpMethod.BodyConcealment;
            else if (candidate.Truth.VictimDeparted)
                candidate.Truth.CoverUp = CoverUpMethod.None;
            else if (!ContractGenerator.GetApplicableCoverUps(CrimeType.Murder).Contains(candidate.Truth.CoverUp))
                candidate.Truth.CoverUp = CoverUpMethod.None;

            // 실제 사건 유형 변경으로 성립하지 않게 된 조직 조건만 제거한다.
            if (candidate.OrgCondition != null && candidate.OrgCondition.HasCondition
                && !ContractGenerator.IsConditionFeasible(candidate.OrgCondition.Type, candidate))
                candidate.OrgCondition.Type = OrganizationConditionType.None;
        }

        /// <summary>
        /// 사건 발생일이 문서 존재 시점보다 이르면 [가장 이른 날, 1984-12-31]에서 균등하게 다시 고른다(04: 사건 날짜는 1984-10-01~12-31 균등).
        /// </summary>
        public void ApplyIncidentDate(ContractInstance contract, Random random)
        {
            if (EarliestIncidentDate == null || !DateTime.TryParse(EarliestIncidentDate, out var earliest)) return;
            if (DateTime.TryParse(contract.IncidentDate, out var current) && current >= earliest) return;
            var last = new DateTime(1984, 12, 31);
            contract.IncidentDate = earliest.AddDays(random.Next((last - earliest).Days + 1)).ToString("yyyy-MM-dd");
        }

        /// <summary>문서 보유자를 정해 의뢰에 기록한다. 자료를 위해 새 인물을 만들지 않고 기존 의뢰 인물 중에서 고른다.</summary>
        public void AssignDocumentHolder(ContractInstance contract, NpcRegistry npcs, Random random)
        {
            contract.PlannedClueObjectId = DocumentObjectId;
            contract.DocumentHolderNpcId = null;
            contract.DocumentHolderTag = null;
            if (DocumentHolderTag == null && !HolderIsVictim) return;
            string holder;
            if (HolderIsVictim) holder = contract.Truth.VictimId;
            else
            {
                var cast = npcs.GetByContract(contract.ContractId).OrderBy(n => n.NpcId, StringComparer.Ordinal).ToList();
                holder = cast.Count == 0 ? null : cast[random.Next(cast.Count)].NpcId;
            }
            contract.DocumentHolderNpcId = holder;
            contract.DocumentHolderTag = DocumentHolderTag ?? "자료 보관자";
        }

        /// <summary>생성이 끝난 의뢰가 제약을 모두 지키는지. 지키지 못하면 후보를 버린다.</summary>
        public bool IsSatisfiedBy(ContractInstance contract, NpcRegistry npcs)
        {
            var victims = npcs.GetByContract(contract.ContractId).Where(n => n.ActualRole == NpcActualRole.Victim).ToList();
            if (RequireMurder && contract.PrimaryCrimeType != CrimeType.Murder) return false;
            if (victims.Count < VictimCount) return false;
            if (RequireMurder && victims.Any(v => v.IsAlive)) return false;
            if (CoupleVictims && (!contract.Truth.VictimsAreCouple || contract.Truth.AdditionalVictimIds.Count == 0)) return false;
            if (VictimOccupation != null && npcs.Get(contract.Truth.VictimId).Occupation != VictimOccupation) return false;
            if (ToolId != null && contract.Truth.ToolId != ToolId) return false;
            if (MethodId != null && contract.Truth.MethodId != MethodId) return false;
            if (RelationshipId != null && contract.Truth.RelationshipId != RelationshipId) return false;
            if (PerpetratorPersonality.HasValue && npcs.Get(contract.Truth.PerpetratorId).Personality != PerpetratorPersonality.Value) return false;
            if (EarliestIncidentDate != null
                && (!DateTime.TryParse(contract.IncidentDate, out var date) || date < DateTime.Parse(EarliestIncidentDate))) return false;
            if ((DocumentHolderTag != null || HolderIsVictim) && (contract.DocumentHolderNpcId == null || !npcs.Exists(contract.DocumentHolderNpcId))) return false;
            if (DocumentObjectId != null && contract.PlannedClueObjectId != DocumentObjectId) return false;
            return true;
        }

        /// <summary>방법 ID·도구 ID를 동시에 만족하는 방법 후보만 남긴다.</summary>
        public IReadOnlyList<string> FilterMethods(IReadOnlyList<string> methods)
        {
            if (methods == null) throw new ArgumentNullException(nameof(methods));
            IEnumerable<string> filtered = methods;
            if (MethodId != null)
                filtered = filtered.Where(m => m == MethodId);
            if (ToolId != null)
                filtered = filtered.Where(m => CaseCatalog.ToolsFor(m).Contains(ToolId));
            return filtered.ToList();
        }
    }
}
