namespace CaseFileOmission.Meeting
{
    /// <summary>
    /// 06페이지 20절 확정 수치표. 연속 횟수는 1부터 시작하며, 표에 없는 구간(4회 이상 등)은 고정값을 쓴다.
    /// 실제 적용 순서(감소 먼저, 증가 다음, 0~100 클램프)는 Police 모듈이 책임진다 — 여기서는
    /// "델타 값 자체"만 계산한다.
    /// </summary>
    public static class MeetingSuspicionTable
    {
        /// <summary>차례를 연속으로 넘길 때 본인 의심도 상승량.</summary>
        public static float ConsecutiveTurnSkipIncrease(int consecutiveCount)
        {
            return consecutiveCount switch
            {
                <= 0 => 0f,
                1 => 5f,
                2 => 8f,
                3 => 12f,
                _ => 15f
            };
        }

        /// <summary>같은 상대에게 카드가 연속으로 일반 수용될 때 의심도 감소량(음수).</summary>
        public static float ConsecutiveAcceptDecrease(int consecutiveAcceptCount)
        {
            return consecutiveAcceptCount switch
            {
                <= 0 => 0f,
                1 => -4f,
                2 => -7f,
                3 => -11f,
                _ => -16f
            };
        }

        /// <summary>의문 제기 후 검증을 거쳐 최종 수용되었을 때 추가되는 보너스 감소량(음수, 연속 수용 감소량에 더한다).</summary>
        public const float VerifiedAcceptBonus = -8f;

        /// <summary>대응 실패 시 의심도 상승량(연속 횟수 기준).</summary>
        public static float ConsecutiveResponseFailIncrease(int consecutiveFailCount)
        {
            return consecutiveFailCount switch
            {
                <= 0 => 0f,
                1 => 8f,
                2 => 11f,
                3 => 15f,
                _ => 20f
            };
        }

        /// <summary>
        /// 보류한 의문을 다음 회의에서도 처리하지 않고 넘겼을 때 상승량(사용자 확정 2026-11-20, 이전 확정값 복구).
        /// 일반 차례 넘기기·대응 실패·포기 수치와 중복 적용하지 않는다.
        /// </summary>
        public const float CarriedOverDeferIncrease = 10f;

        /// <summary>대응 포기 시 의심도 상승량(연속 횟수 기준).</summary>
        public static float ConsecutiveGiveUpIncrease(int consecutiveGiveUpCount)
        {
            return consecutiveGiveUpCount switch
            {
                <= 0 => 0f,
                1 => 12f,
                2 => 15f,
                3 => 19f,
                _ => 24f
            };
        }
    }
}
