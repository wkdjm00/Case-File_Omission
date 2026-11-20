namespace CaseFileOmission.NPC
{
    /// <summary>
    /// 08페이지 "일반 NPC 성격 오브젝트 10종". 몸체당 성격 오브젝트는 하나다.
    /// </summary>
    public enum NpcPersonality
    {
        Suspicious,      // 의심이 많은
        Greedy,          // 욕심이 많은
        Timid,           // 겁이 많은
        FrequentLiar,    // 거짓말을 자주 하는
        Secretive,       // 비밀을 감추는
        Guilty,          // 죄책감이 있는
        BlindlyTrusting, // 남을 맹목적으로 믿는
        Indifferent,     // 무관심한
        Jealous,         // 질투하는
        Dependent        // 의존하는
    }

    /// <summary>
    /// 07·08페이지: 몸체는 사건 관련 인물 2종 + 목격자·피해자·가해자 각 1종, 총 5종.
    /// CaseRelatedA/B는 역할이 아직 공개되지 않은 사건 관계자를 구분 없이 나타내는 두 변형이다.
    /// </summary>
    public enum NpcActualRole
    {
        CaseRelatedA,
        CaseRelatedB,
        Witness,
        Victim,
        Perpetrator
    }

    /// <summary>
    /// 04페이지 "NPC가 사실이라고 믿는지, 의심하는지, 판단을 보류하는지".
    /// </summary>
    public enum NpcJudgement
    {
        Believe,
        Suspect,
        Withhold
    }

    /// <summary>AI 계약 v1(2026-11-12) information_state 4종. 보유 여부와 별개다.</summary>
    public enum NpcInformationState
    {
        /// <summary>직접 목격·경험 또는 검증 가능한 출처로 해당 사실을 앎.</summary>
        KnowsFact,
        /// <summary>사실이라고 믿지만 확인된 사실과 같다고 보장할 수 없음.</summary>
        BelievesTrue,
        /// <summary>가능성을 의심하지만 확정적으로 믿지는 않음.</summary>
        Suspects,
        /// <summary>실제 진실과 다른 내용을 사실로 잘못 믿음.</summary>
        BelievesFalse
    }

    /// <summary>AI 계약 v1 acquisition_method 허용값.</summary>
    public enum NpcInfoAcquisitionMethod
    {
        DirectObservation,
        DirectExperience,
        Investigation,
        DocumentReview,
        Dialogue,
        NpcTransfer
    }

    /// <summary>정보 1건의 획득 기록(AI 계약 v1 필드). 해당 없는 참조는 null로 둔다.</summary>
    public sealed class NpcInfoRecord
    {
        public string InfoId;
        public NpcInformationState State;
        public NpcInfoAcquisitionMethod Method;
        /// <summary>획득 시각(의뢰 경위·조사 분 단위). 모르면 null.</summary>
        public int? Minute;
        public string SourceEventId;
        public string SourceEvidenceId;
        /// <summary>전달받은 정보의 전달자.</summary>
        public string SourcePersonId;
    }

    public enum InfoTransferOutcome
    {
        NotAttempted,
        AlreadyTransferred,
        Failed,
        Succeeded
    }
}
