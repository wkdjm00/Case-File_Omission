using CaseFileOmission.Core;

namespace CaseFileOmission.Organization
{
    /// <summary>
    /// 05·06페이지 확정: 조직 신뢰도 0~100, 시작 40. 조건 달성 +5, 미달성 -5, 의뢰 포기 -2.
    /// 경찰 의심도와는 완전히 별개의 게이지다. 신뢰도 0은 별도 게임 종료 엔딩이며,
    /// 신뢰도를 회복하는 수단은 조직 조건 달성 외에는 없다(확정).
    /// </summary>
    public sealed class OrganizationState
    {
        public const float StartingTrust = 40f;
        public const float MinTrust = 0f;
        public const float MaxTrust = 100f;

        public float Trust { get; private set; } = StartingTrust;

        public bool HasReachedZero => Trust <= MinTrust;

        public float ApplyConditionAchieved() => Trust = ProbabilityCalculator.Clamp(Trust + 5f, MinTrust, MaxTrust);
        public float ApplyConditionFailed() => Trust = ProbabilityCalculator.Clamp(Trust - 5f, MinTrust, MaxTrust);
        public float ApplyContractAbandoned() => Trust = ProbabilityCalculator.Clamp(Trust - 2f, MinTrust, MaxTrust);

        /// <summary>저장/불러오기 전용. 게임플레이 중에는 Apply* 메서드만 사용한다.</summary>
        public void SetTrust(float value) => Trust = ProbabilityCalculator.Clamp(value, MinTrust, MaxTrust);
    }
}
