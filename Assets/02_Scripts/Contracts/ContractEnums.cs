namespace CaseFileOmission.Contracts
{
    /// <summary>
    /// 대표 사건 유형. 04페이지 2026-11-06 확정 가중치: 살인30% 실종30% 폭행·상해20% 절도·도난12% 협박8%.
    /// 부수 범죄 가중치는 별도 표를 사용하며 혼용하지 않는다.
    /// </summary>
    public enum CrimeType
    {
        Murder,
        Missing,
        AssaultInjury,
        TheftRobbery,
        Blackmail
    }

    /// <summary>
    /// 사용자 지시(2026-11-09): 의뢰 사건을 진행하는 공간에서 실외를 제외한다.
    /// Outdoor는 과거 "미정 후보"로 남아 있던 값이며, 이제 생성기는 이 값을 사용하지 않고
    /// 검증기도 이 값을 항상 무효로 처리한다. 열거값 자체는 과거 데이터·문서 대조용으로 남겨 둔다.
    /// </summary>
    public enum LocationCategory
    {
        Indoor,
        Outdoor
    }

    /// <summary>
    /// 최종 장소 목록은 모델링 완료 후 확정 (04페이지 미정 항목).
    /// 그 전까지 검증 로직을 테스트하기 위한 더미 스텁 장소만 사용한다.
    /// House만 문서상 확정("실내 사건 장소는 주택으로 한정")이고, StubOutdoor는 실제 설정이 아니다.
    /// 사용자 지시(2026-11-09)로 실외가 제외되어 StubOutdoor는 이제 생성되지 않는다(과거 데이터 대조용으로만 남김).
    /// </summary>
    public enum StubLocation
    {
        House,
        StubOutdoor
    }

    /// <summary>
    /// 은폐 방식. 04페이지는 예시로만 제시하고("이 예시는 적용 조건과 최종 선택지를 모두 확정한 목록이 아니다")
    /// 최종 목록을 확정하지 않았으므로 이후 조건이 추가될 수 있다.
    /// </summary>
    public enum CoverUpMethod
    {
        None,
        AccidentalDeathDisguise,
        SuicideDisguise,
        DisappearanceDisguise,
        RecordOrTestimonyTampering
    }

    /// <summary>
    /// 05페이지가 "조건 예시"로 명시한 4종. 전체 조건 목록·조건별 가중치는 미정이다.
    /// </summary>
    public enum OrganizationConditionType
    {
        None,
        RecoverBody,
        RecoverItem,
        DesignateOfficialCulprit,
        KeepUnsolved
    }

    /// <summary>
    /// 04페이지 2026-11-09 구현 명세가 정의한 내부 오류 코드. 플레이어 UI에는 직접 노출하지 않는다.
    /// </summary>
    public enum ContractValidationErrorCode
    {
        LOCATION_INVALID,
        TIMELINE_CONFLICT,
        KNOWLEDGE_LEAK,
        CLUE_UNREACHABLE,
        CONTRADICTION,
        CONTRACT_INVALID
    }

    public enum ContractOutcome
    {
        Pending,
        Resolved,
        Unsolved,
        HandlerPoliceDeceased
    }
}
