namespace CaseFileOmission.Police
{
    /// <summary>06페이지 확정 의심도 단계 구간.</summary>
    public enum SuspicionStage
    {
        VeryLow,   // 0~9
        Low,       // 10~19
        Normal,    // 20~34
        High,      // 35~54
        VeryHigh,  // 55~99
        Max        // 100
    }

    public static class SuspicionStageResolver
    {
        public static SuspicionStage Resolve(float suspicion)
        {
            if (suspicion >= 100f) return SuspicionStage.Max;
            if (suspicion >= 55f) return SuspicionStage.VeryHigh;
            if (suspicion >= 35f) return SuspicionStage.High;
            if (suspicion >= 20f) return SuspicionStage.Normal;
            if (suspicion >= 10f) return SuspicionStage.Low;
            return SuspicionStage.VeryLow;
        }
    }
}
