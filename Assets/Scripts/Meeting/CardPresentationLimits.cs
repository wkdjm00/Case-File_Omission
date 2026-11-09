namespace CaseFileOmission.Meeting
{
    /// <summary>06페이지 20절 확정: 일반 차례 제시 한도. 의문 제기 대응 카드는 한도 예외.</summary>
    public static class CardPresentationLimits
    {
        public const int PlayerNormalTurnLimit = 5;
        public const int PoliceNormalTurnLimit = 5;
        public const int BasicNpcNormalTurnLimit = 3;

        public const int MaxAdditionalVerificationsPerCardOrClaim = 2;
    }
}
