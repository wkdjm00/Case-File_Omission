namespace CaseFileOmission.Organization
{
    /// <summary>05페이지 확정 신뢰도 구간.</summary>
    public enum SupportTier
    {
        NoSupport,          // 1~20: 더 이상 단서를 제공하지 않음
        ExtraConditionAdded, // 21~39: 서브 단서 습득 조건 1개 추가
        Base,               // 40~69: 기본 지원
        OneConditionEased,  // 70~89: 조건 1개 완화
        TwoConditionsEased  // 90~100: 조건 2개 완화
    }

    public static class OrganizationSupportTierResolver
    {
        /// <summary>신뢰도 0은 게임 종료 엔딩이라 지원 구간 개념 자체가 없다. 호출 전에 별도로 처리해야 한다.</summary>
        public static SupportTier Resolve(float trust)
        {
            if (trust <= 0f)
                throw new System.InvalidOperationException("신뢰도 0은 게임 종료 엔딩 대상이며 지원 구간을 적용하지 않습니다.");

            if (trust <= 20f) return SupportTier.NoSupport;
            if (trust <= 39f) return SupportTier.ExtraConditionAdded;
            if (trust <= 69f) return SupportTier.Base;
            if (trust <= 89f) return SupportTier.OneConditionEased;
            return SupportTier.TwoConditionsEased;
        }

        /// <summary>조정 가능한 획득 조건 개수에 적용할 가감치. 양수=조건 추가, 음수=조건 완화.</summary>
        public static int GetConditionAdjustment(SupportTier tier)
        {
            return tier switch
            {
                SupportTier.ExtraConditionAdded => 1,
                SupportTier.OneConditionEased => -1,
                SupportTier.TwoConditionsEased => -2,
                _ => 0
            };
        }
    }
}
