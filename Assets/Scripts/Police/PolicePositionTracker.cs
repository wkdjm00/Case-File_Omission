using System.Collections.Generic;

namespace CaseFileOmission.Police
{
    /// <summary>
    /// §6-5 확정: "경찰은 마지막으로 확인한 위치와 전달받은 정보를 이용해 플레이어를 추적한다.
    /// 경찰이 플레이어의 정확한 위치를 계속 알고 있는 것으로 처리하지 않는다."
    /// 명시적으로 보고(목격·정보 전달)된 경우에만 위치를 기록하며, 그 외에는 "모른다"로 취급한다.
    /// </summary>
    public sealed class PolicePositionTracker
    {
        private readonly Dictionary<string, string> _lastKnownRoomByPolice = new Dictionary<string, string>();

        /// <summary>경찰이 실제로 목격했거나 정보를 전달받아 플레이어 위치를 알게 되었을 때만 호출한다.</summary>
        public void ReportSighting(string policeId, string roomId)
        {
            _lastKnownRoomByPolice[policeId] = roomId;
        }

        public bool TryGetLastKnownRoom(string policeId, out string roomId) =>
            _lastKnownRoomByPolice.TryGetValue(policeId, out roomId);

        /// <summary>플레이어가 시야를 벗어나 더 이상 신뢰할 수 있는 정보가 아니게 되었을 때 사용할 수 있다.</summary>
        public void ClearSighting(string policeId) => _lastKnownRoomByPolice.Remove(policeId);
    }
}
