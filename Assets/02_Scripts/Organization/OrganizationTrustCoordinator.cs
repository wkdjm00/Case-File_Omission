using System;
using CaseFileOmission.Contracts;

namespace CaseFileOmission.Organization
{
    /// <summary>
    /// <see cref="ContractInstance.OrgCondition"/>(의뢰별 조직 조건의 달성 여부)와
    /// <see cref="OrganizationState"/>(장기 조직 신뢰도)는 둘 다 이미 구현되어 있지만
    /// 서로를 호출하는 코드가 없었다. 이 클래스가 05페이지 확정 규칙대로 그 둘을 연결한다:
    /// 조건 달성 +5, 미달성 -5, 의뢰 포기 -2, 조건이 없는 의뢰는 변화 없음.
    /// </summary>
    public static class OrganizationTrustCoordinator
    {
        public static void ApplyContractOutcome(OrganizationState state, ContractInstance contract, bool contractAbandoned)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (contract == null) throw new ArgumentNullException(nameof(contract));

            if (contractAbandoned)
            {
                state.ApplyContractAbandoned();
                return;
            }

            if (!contract.OrgCondition.HasCondition)
                return; // 조건이 없는 의뢰는 달성·미달성에 따른 신뢰도 변화를 적용하지 않는다(확정).

            if (contract.OrgCondition.Achieved == true)
                state.ApplyConditionAchieved();
            else if (contract.OrgCondition.Achieved == false)
                state.ApplyConditionFailed();

            // Achieved가 null(아직 판정 전)인 상태로 이 메서드를 호출하는 것은 호출자 책임이며,
            // 그 경우 신뢰도를 변경하지 않는다 — 판정 전에 임의로 변화를 적용하지 않기 위함이다.
        }
    }
}
