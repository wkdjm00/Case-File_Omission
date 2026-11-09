namespace CaseFileOmission.Endings
{
    /// <summary>10페이지 확정 엔딩 7종.</summary>
    public enum GameEndingType
    {
        None,

        // 메인 서사 엔딩 — 이후에도 의뢰 진행이 계속된다.
        RebelRecruitAccepted,
        BossHiddenEnding, // 반란군 영입 거절

        // 게임 종료 엔딩 — 현재 플레이가 종료된다.
        Arrest,
        PoliceAllEliminated,
        Immigration,
        OrganizationTrustZero,
        PhoneCallRejected
    }

    public static class GameEndingClassifier
    {
        /// <summary>true면 메인 서사 엔딩(이후에도 의뢰 반복 가능), false면 게임 종료(새 게임 필요).</summary>
        public static bool ContinuesAfterEnding(GameEndingType type)
        {
            return type == GameEndingType.RebelRecruitAccepted || type == GameEndingType.BossHiddenEnding;
        }
    }
}
