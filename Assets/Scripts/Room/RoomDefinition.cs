using System;

namespace CaseFileOmission.Rooms
{
    /// <summary>
    /// §3-5 확정: 모든 의뢰 사건은 주택 내부에서 진행한다. 시각 표현은 임시 도형으로 대체 가능하며,
    /// 이 클래스는 논리적인 방 하나를 나타낸다(실제 모델/크기와 무관).
    /// </summary>
    [Serializable]
    public sealed class RoomDefinition
    {
        public string RoomId { get; }

        /// <summary>임시 표시 이름. 나중에 실제 모델링 결과로 교체해도 RoomId는 바뀌지 않는다.</summary>
        public string DisplayName;

        public RoomDefinition(string roomId, string displayName = null)
        {
            if (string.IsNullOrEmpty(roomId))
                throw new ArgumentException("roomId는 비어 있을 수 없습니다.", nameof(roomId));

            RoomId = roomId;
            DisplayName = displayName ?? roomId;
        }
    }
}
