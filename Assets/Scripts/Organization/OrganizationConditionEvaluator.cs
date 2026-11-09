using CaseFileOmission.Contracts;
using CaseFileOmission.Evidence;

namespace CaseFileOmission.Organization
{
    /// <summary>
    /// §8-3 확정: "조건을 단순한 Achieved 불리언 값으로만 처리하지 말고, 해당 조건이 실제로
    /// 달성되었는지 게임 상태를 근거로 평가한다." 조건 종류별로 실제 존재하는 데이터
    /// (ClueRepository, ContractOfficialConclusion)를 근거로 판정한다.
    ///
    /// 반환값: null = 아직 판정할 수 없음(공식 결론이 아직 확정되지 않은 등), true/false = 실제 평가 결과.
    /// 이 평가 결과를 <see cref="ContractInstance.OrgCondition"/>.Achieved에 대입한 뒤
    /// 기존 <see cref="OrganizationTrustCoordinator.ApplyContractOutcome"/>를 호출하는 순서를 그대로 따른다.
    /// </summary>
    public static class OrganizationConditionEvaluator
    {
        public static bool? Evaluate(ContractInstance contract, ClueRepository clueRepository, bool deliveredToOrganization = false)
        {
            var condition = contract.OrgCondition;
            if (!condition.HasCondition)
                return null;

            switch (condition.Type)
            {
                case OrganizationConditionType.RecoverBody:
                case OrganizationConditionType.RecoverItem:
                    return EvaluateRecovery(condition, clueRepository, deliveredToOrganization);

                case OrganizationConditionType.DesignateOfficialCulprit:
                    if (!contract.OfficialConclusion.IsFinalized)
                        return null;
                    return contract.OfficialConclusion.OfficiallyAccusedNpcId == condition.TargetId;

                case OrganizationConditionType.KeepUnsolved:
                    if (!contract.OfficialConclusion.IsFinalized)
                        return null;
                    return contract.OfficialConclusion.Outcome == ContractOutcome.Unsolved;

                default:
                    return null;
            }
        }

        private static bool? EvaluateRecovery(OrganizationCondition condition, ClueRepository clueRepository, bool deliveredToOrganization)
        {
            if (string.IsNullOrEmpty(condition.TargetId) || !clueRepository.Exists(condition.TargetId))
                return false;

            var record = clueRepository.GetRecord(condition.TargetId);
            bool recovered = record.WorldState == ClueWorldState.HeldByPlayer;
            if (!recovered)
                return false;

            return !condition.RequiresDeliveryToOrganization || deliveredToOrganization;
        }
    }
}
