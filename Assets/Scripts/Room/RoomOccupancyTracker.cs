using System;
using System.Collections.Generic;

namespace CaseFileOmission.Rooms
{
    /// <summary>
    /// 인물(플레이어·NPC·경찰) 및 사건 관련 오브젝트의 현재 위치를 관리한다
    /// (§3-2 "인물의 초기 위치", §2-1 "인물과 사건의 위치가 일관되게 관리된다").
    /// </summary>
    public sealed class RoomOccupancyTracker
    {
        private readonly RoomGraph _graph;
        private readonly Dictionary<string, string> _occupantRoom = new Dictionary<string, string>();

        public RoomOccupancyTracker(RoomGraph graph)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        }

        /// <summary>최초 배치. 연결 여부와 무관하게 지정한 방에 놓는다.</summary>
        public void PlaceAt(string occupantId, string roomId)
        {
            if (!_graph.RoomExists(roomId))
                throw new KeyNotFoundException($"방 '{roomId}'가 존재하지 않습니다.");

            _occupantRoom[occupantId] = roomId;
        }

        public string GetRoom(string occupantId)
        {
            if (!_occupantRoom.TryGetValue(occupantId, out var roomId))
                throw new KeyNotFoundException($"'{occupantId}'의 위치가 아직 설정되지 않았습니다.");

            return roomId;
        }

        public bool HasPosition(string occupantId) => _occupantRoom.ContainsKey(occupantId);

        /// <summary>오브젝트가 누군가에게 획득되는 등 더 이상 방에 놓여 있지 않을 때 위치를 지운다.</summary>
        public bool Remove(string occupantId) => _occupantRoom.Remove(occupantId);

        /// <summary>지정한 방에 있는 인물·오브젝트 ID.</summary>
        public IEnumerable<string> GetOccupants(string roomId)
        {
            foreach (var pair in _occupantRoom)
            {
                if (pair.Value == roomId)
                    yield return pair.Key;
            }
        }

        /// <summary>저장/불러오기 전용.</summary>
        public IEnumerable<KeyValuePair<string, string>> AllPositions => _occupantRoom;

        /// <summary>
        /// 현재 위치에서 목적지로 이동한다. 목적지가 현재 위치와 직접 연결되어 있지 않으면 이동하지 않고
        /// false를 반환한다(§2-4 "연결되지 않은 방에서 상호작용할 수 없다"를 이동에도 동일하게 적용).
        /// </summary>
        public bool TryMove(string occupantId, string destinationRoomId)
        {
            if (!_graph.RoomExists(destinationRoomId))
                return false;

            string currentRoomId = GetRoom(occupantId);
            if (currentRoomId == destinationRoomId)
                return true;

            if (!_graph.AreConnected(currentRoomId, destinationRoomId))
                return false;

            _occupantRoom[occupantId] = destinationRoomId;
            return true;
        }
    }
}
