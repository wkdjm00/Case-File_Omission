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

    public enum InfoTransferOutcome
    {
        NotAttempted,
        AlreadyTransferred,
        Failed,
        Succeeded
    }
}
