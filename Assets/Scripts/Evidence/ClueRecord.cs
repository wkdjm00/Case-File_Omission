using System;
using System.Collections.Generic;

namespace CaseFileOmission.Evidence
{
    /// <summary>
    /// 단서 하나의 런타임 보유·확인 상태. 한 번 발견되면 보고서(레코드)에서 제거되지 않는다
    /// (06페이지: "문서를 버리거나 사건 보드에서 치워도 보고서에 저장된 기록은 남는다").
    /// </summary>
    [Serializable]
    public sealed class ClueRecord
    {
        public string ClueId { get; }
        public DateTime DiscoveredAtUtc { get; }

        /// <summary>현재 물리적으로 들고 있는 상태. 보고서 소유권과는 별개다.</summary>
        public ClueWorldState WorldState { get; private set; } = ClueWorldState.HeldByPlayer;

        /// <summary>단서를 확인한 경찰 ID 목록. 확인해도 소유권은 플레이어에게 남는다 (06페이지 확정).</summary>
        public readonly HashSet<string> ConfirmedByPoliceIds = new HashSet<string>();

        public ClueRecord(string clueId, DateTime? discoveredAtUtc = null)
        {
            if (string.IsNullOrEmpty(clueId))
                throw new ArgumentException("clueId는 비어 있을 수 없습니다.", nameof(clueId));

            ClueId = clueId;
            DiscoveredAtUtc = discoveredAtUtc ?? DateTime.UtcNow;
        }

        public void MarkDropped() => WorldState = ClueWorldState.DroppedInWorld;

        public void MarkHeldByPlayer() => WorldState = ClueWorldState.HeldByPlayer;

        /// <summary>NPC가 떨어진 단서를 자동 획득했을 때. 보고서 기록은 그대로 남는다.</summary>
        public void MarkHeldByNpc() => WorldState = ClueWorldState.HeldByNpc;

        /// <summary>저장/불러오기 전용.</summary>
        public void RestoreWorldState(ClueWorldState state) => WorldState = state;

        public void MarkConfirmedByPolice(string policeId)
        {
            if (!string.IsNullOrEmpty(policeId))
                ConfirmedByPoliceIds.Add(policeId);
        }
    }
}
