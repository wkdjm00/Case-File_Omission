using System;

namespace CaseFileOmission.Evidence
{
    /// <summary>
    /// 카드 조작 1회의 결과. 원본 단서는 절대 덮어쓰지 않고, 조작 이력만 별도로 기록한다
    /// (06페이지 확정: "원본 단서는 변경·삭제하지 않는다. 조작 결과는 별도 데이터로 보관").
    /// </summary>
    [Serializable]
    public sealed class ManipulationRecord
    {
        public string OriginalClueId { get; }
        public ManipulationType Type { get; }
        public string ActorId { get; }
        public DateTime AppliedAtUtc { get; }

        /// <summary>
        /// 은폐(Conceal)일 때 실제로 제시되는 대체 단서 ID. 왜곡(Distort)일 때는 원본과 동일한 ID를 사용하되
        /// 표시 상태만 왜곡으로 취급한다.
        /// </summary>
        public string DisplayedClueId { get; }

        public ManipulationRecord(string originalClueId, ManipulationType type, string actorId, string displayedClueId, DateTime? appliedAtUtc = null)
        {
            if (string.IsNullOrEmpty(originalClueId))
                throw new ArgumentException("originalClueId는 비어 있을 수 없습니다.", nameof(originalClueId));
            if (string.IsNullOrEmpty(displayedClueId))
                throw new ArgumentException("displayedClueId는 비어 있을 수 없습니다.", nameof(displayedClueId));

            OriginalClueId = originalClueId;
            Type = type;
            ActorId = actorId;
            DisplayedClueId = displayedClueId;
            AppliedAtUtc = appliedAtUtc ?? DateTime.UtcNow;
        }
    }
}
