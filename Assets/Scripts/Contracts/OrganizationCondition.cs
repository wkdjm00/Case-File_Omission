using System;

namespace CaseFileOmission.Contracts
{
    /// <summary>
    /// 조직 조건(= 조직 목표). 의뢰 시작 전 고정되며 의뢰 시작 후에는 변경하지 않는다 (05페이지 확정).
    /// </summary>
    [Serializable]
    public sealed class OrganizationCondition
    {
        public OrganizationConditionType Type = OrganizationConditionType.None;

        /// <summary>회수 대상 등 조건이 특정 대상을 지정하는 경우의 ID. 미특정이면 null.</summary>
        public string TargetId;

        /// <summary>회수만 요구하는지, 조직에 전달까지 요구하는지 구분.</summary>
        public bool RequiresDeliveryToOrganization;

        /// <summary>null = 아직 판정 전, true = 달성, false = 미달성.</summary>
        public bool? Achieved;

        public bool HasCondition => Type != OrganizationConditionType.None;
    }
}
