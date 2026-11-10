using System;
using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Core;

namespace CaseFileOmission.Rooms
{
    /// <summary>
    /// 주택 내부 방과 연결 관계를 그래프로 관리한다(§3-5, §2-1 "실내 공간 제한").
    /// Core.IRoomConnectivityProvider를 구현해 ActionGuards와 직접 연결된다.
    /// </summary>
    public sealed class RoomGraph : IRoomConnectivityProvider
    {
        private readonly Dictionary<string, RoomDefinition> _rooms = new Dictionary<string, RoomDefinition>();
        private readonly Dictionary<string, HashSet<string>> _adjacency = new Dictionary<string, HashSet<string>>();

        public void AddRoom(RoomDefinition room)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            if (_rooms.ContainsKey(room.RoomId))
                throw new InvalidOperationException($"방 '{room.RoomId}'는 이미 등록되어 있습니다.");

            _rooms[room.RoomId] = room;
            _adjacency[room.RoomId] = new HashSet<string>();
        }

        public bool RoomExists(string roomId) => _rooms.ContainsKey(roomId);

        /// <summary>§3-5·§2-4: 존재하지 않는 방을 연결하려 하면 예외를 던진다.</summary>
        public void Connect(string roomIdA, string roomIdB)
        {
            if (!RoomExists(roomIdA))
                throw new KeyNotFoundException($"방 '{roomIdA}'가 존재하지 않습니다.");
            if (!RoomExists(roomIdB))
                throw new KeyNotFoundException($"방 '{roomIdB}'가 존재하지 않습니다.");

            _adjacency[roomIdA].Add(roomIdB);
            _adjacency[roomIdB].Add(roomIdA);
        }

        /// <summary>직접 인접 여부만 확인한다(상호작용 가드용).</summary>
        public bool AreConnected(string roomIdA, string roomIdB)
        {
            if (!RoomExists(roomIdA) || !RoomExists(roomIdB))
                return false;

            return _adjacency[roomIdA].Contains(roomIdB);
        }

        public IReadOnlyCollection<string> GetAdjacentRooms(string roomId)
        {
            if (!RoomExists(roomId))
                throw new KeyNotFoundException($"방 '{roomId}'가 존재하지 않습니다.");

            return _adjacency[roomId];
        }

        /// <summary>
        /// 여러 방을 거쳐서라도 도달 가능한지 확인한다(§3-2 "이동 가능 경로 확인").
        /// AreConnected(직접 인접)와는 다른 검사다.
        /// </summary>
        public bool CanReach(string fromRoomId, string toRoomId)
        {
            if (!RoomExists(fromRoomId) || !RoomExists(toRoomId))
                return false;

            if (fromRoomId == toRoomId)
                return true;

            var visited = new HashSet<string> { fromRoomId };
            var queue = new Queue<string>();
            queue.Enqueue(fromRoomId);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var neighbor in _adjacency[current])
                {
                    if (neighbor == toRoomId)
                        return true;

                    if (visited.Add(neighbor))
                        queue.Enqueue(neighbor);
                }
            }

            return false;
        }

        /// <summary>
        /// 막힌 방을 피해 출발지에서 목적지까지의 최단 경로(출발지 제외, 목적지 포함)를 찾는다.
        /// 경로가 없거나 목적지 자체가 막혀 있으면 null. 출발지와 목적지가 같으면 빈 목록.
        /// </summary>
        public IReadOnlyList<string> FindPath(string fromRoomId, string toRoomId, Func<string, bool> isBlocked = null)
        {
            if (!RoomExists(fromRoomId) || !RoomExists(toRoomId))
                return null;
            if (fromRoomId == toRoomId)
                return Array.Empty<string>();
            if (isBlocked != null && isBlocked(toRoomId))
                return null;

            var previous = new Dictionary<string, string> { [fromRoomId] = null };
            var queue = new Queue<string>();
            queue.Enqueue(fromRoomId);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                // 결과가 실행마다 달라지지 않도록 이웃을 정렬해 탐색한다.
                foreach (var neighbor in _adjacency[current].OrderBy(id => id, StringComparer.Ordinal))
                {
                    if (previous.ContainsKey(neighbor) || (isBlocked != null && isBlocked(neighbor)))
                        continue;

                    previous[neighbor] = current;
                    if (neighbor == toRoomId)
                    {
                        var path = new List<string>();
                        for (var step = toRoomId; step != fromRoomId; step = previous[step])
                            path.Add(step);
                        path.Reverse();
                        return path;
                    }
                    queue.Enqueue(neighbor);
                }
            }

            return null;
        }

        public IReadOnlyList<string> AllRoomIds => _rooms.Keys.ToList();
    }
}
