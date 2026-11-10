using System;

namespace CaseFileOmission.Core
{
    /// <summary>
    /// 게임 전역에서 발생하는 주요 사건의 종류.
    /// 홈 문서 "공통 이벤트 처리 순서" 기준: 행동 결과 계산 이후 상태 변경과 함께 기록되는 이벤트 단위.
    /// </summary>
    public enum GameEventType
    {
        ClueDiscovered,
        ClueTransferred,
        InfoDelivered,
        ShotFired,
        SuspicionChanged,
        OrganizationTrustChanged,
        ContractResolved,
        PoliceDeath,
        EndingTriggered,
        Custom
    }

    /// <summary>
    /// 고유 eventId를 가지는 상태 변경 이벤트.
    /// 같은 eventId의 후속 효과는 두 번 적용하지 않는다 (홈 문서 "최소 데이터 계약" 확정 규칙).
    /// </summary>
    [Serializable]
    public sealed class GameEvent
    {
        public string EventId { get; }
        public GameEventType Type { get; }
        public DateTime OccurredAtUtc { get; }
        public string ContractId { get; }
        public string Payload { get; }

        public GameEvent(string eventId, GameEventType type, string contractId, string payload, DateTime? occurredAtUtc = null)
        {
            if (string.IsNullOrEmpty(eventId))
                throw new ArgumentException("eventId는 비어 있을 수 없습니다.", nameof(eventId));

            EventId = eventId;
            Type = type;
            ContractId = contractId;
            Payload = payload;
            OccurredAtUtc = occurredAtUtc ?? DateTime.UtcNow;
        }
    }
}
