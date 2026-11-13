using System;
using System.Collections.Generic;
using System.Linq;

namespace CaseFileOmission.Rooms
{
    /// <summary>방 하나의 임시 바닥 사각형. X/Z 평면 미터, 중심은 바닥 중심, 가로(X)·세로(Z)는 벽 안쪽 유효 치수.</summary>
    public sealed class RoomRect
    {
        public string RoomId;
        public float CenterX, CenterZ, Width, Depth;

        public float MinX => CenterX - Width * 0.5f;
        public float MaxX => CenterX + Width * 0.5f;
        public float MinZ => CenterZ - Depth * 0.5f;
        public float MaxZ => CenterZ + Depth * 0.5f;

        public bool Contains(float x, float z, float margin = 0f) =>
            x >= MinX + margin && x <= MaxX - margin && z >= MinZ + margin && z <= MaxZ - margin;
    }

    /// <summary>두 방 사이 문(벽 개구부). 이번 임시 구현에서는 열림/잠김 상태가 없다.</summary>
    public sealed class DoorLayout
    {
        public string DoorId, RoomA, RoomB;
        public float CenterX, CenterZ;
    }

    /// <summary>
    /// 09 2026-11-13 TEMP_LAYOUT_V1의 좌표·크기·거리 값. 공간 ID·연결 그래프(<see cref="HouseLayoutFactory"/>)와 분리한 교체 가능한 임시 기술값이다.
    ///
    /// 원문 표의 좌표를 그대로 쓰면 주방(-2.0, -5.95)과 식료품 저장실(3.95, -5.95)이 현관 홀(z -5~5)과 겹친다.
    /// 원문 지시("구현상 필요하면 벽 두께만큼 중심 좌표를 조정하되 겹치는 방을 만들지 않는다")에 따라 두 방을 남쪽으로 내려
    /// 윗벽을 현관 홀 아랫벽 0.2 m 바깥(z -5.2)에 맞췄고, DOOR_04·DOOR_05의 Z도 두 방이 실제로 맞닿는 구간 가운데로 옮겼다.
    /// 방 내부 유효 치수·공간 ID·연결 그래프는 원문 그대로다.
    /// </summary>
    public static class TempLayoutV1
    {
        public const string LayoutName = "TEMP_LAYOUT_V1";

        public const float RoomHeight = 3.0f;
        public const float WallThickness = 0.2f;
        public const float DoorWidth = 1.0f;
        public const float DoorHeight = 2.1f;

        // 거리·속도(임시). 세계 시간이나 조사 예산을 진행시키지 않는다.
        public const float PlayerMoveAnimationSpeed = 3.0f;
        public const float NpcMoveAnimationSpeed = 2.0f;
        public const float InteractionRange = 1.5f;
        public const float ConversationRange = 2.0f;
        public const float NpcObservationRange = 8.0f;
        public const float NpcOverhearRange = 4.0f;

        public static readonly IReadOnlyList<RoomRect> Rooms = new[]
        {
            Room(HouseLayoutFactory.EntranceHall, 0f, 0f, 10f, 10f),
            Room(HouseLayoutFactory.Reception, -9.7f, 0f, 9f, 6f),
            Room(HouseLayoutFactory.Dining, -9.7f, -5.95f, 8f, 5.5f),
            Room(HouseLayoutFactory.Kitchen, -2.0f, -7.95f, 7f, 5.5f),          // 원문 Z -5.95 → 겹침 해소
            Room(HouseLayoutFactory.PantryUtility, 3.95f, -7.45f, 4.5f, 4.5f),  // 원문 Z -5.95 → 겹침 해소
            Room(HouseLayoutFactory.CorridorStairs, 0f, 11.2f, 2.2f, 12f),
            Room(HouseLayoutFactory.Bedroom, -4.3f, 11.2f, 6f, 5f),
            Room(HouseLayoutFactory.Study, 4.3f, 11.2f, 6f, 5f),
        };

        public static readonly IReadOnlyList<DoorLayout> Doors = new[]
        {
            Door("DOOR_01", HouseLayoutFactory.EntranceHall, HouseLayoutFactory.Reception, -5.1f, 0f),
            Door("DOOR_02", HouseLayoutFactory.EntranceHall, HouseLayoutFactory.CorridorStairs, 0f, 5.1f),
            Door("DOOR_03", HouseLayoutFactory.Reception, HouseLayoutFactory.Dining, -9.7f, -3.1f),
            Door("DOOR_04", HouseLayoutFactory.Dining, HouseLayoutFactory.Kitchen, -5.6f, -6.95f),       // 원문 Z -5.95
            Door("DOOR_05", HouseLayoutFactory.Kitchen, HouseLayoutFactory.PantryUtility, 1.6f, -7.45f), // 원문 Z -5.95
            Door("DOOR_06", HouseLayoutFactory.CorridorStairs, HouseLayoutFactory.Bedroom, -1.2f, 11.2f),
            Door("DOOR_07", HouseLayoutFactory.CorridorStairs, HouseLayoutFactory.Study, 1.2f, 11.2f),
        };

        /// <summary>현관 홀 안의 임시 계단 오브젝트(가로·세로 m). 문 앞을 막지 않게 홀 북동쪽 구석에 둔다.</summary>
        public static readonly RoomRect StairsInHall = new RoomRect
        {
            RoomId = HouseLayoutFactory.EntranceHall, CenterX = 3.2f, CenterZ = 2.8f, Width = 2.4f, Depth = 3.6f
        };

        public static RoomRect GetRoom(string roomId) => Rooms.FirstOrDefault(r => r.RoomId == roomId);

        public static DoorLayout FindDoor(string roomA, string roomB) =>
            Doors.FirstOrDefault(d => (d.RoomA == roomA && d.RoomB == roomB) || (d.RoomA == roomB && d.RoomB == roomA));

        /// <summary>
        /// 09 「생성 및 Unity 자동 검증」 중 좌표만으로 확인할 수 있는 항목. 문제가 없으면 빈 목록.
        /// - 모든 공간 ID가 정확히 한 번 정의되고 그래프에 포함되며, 그래프가 하나로 이어져 있다.
        /// - 방끼리 겹치지 않는다(공유 벽 두께만큼 떨어져 있다).
        /// - 각 문이 두 방 사이 벽 안에 있고, 문 폭만큼 양쪽 방과 맞닿아 있다.
        /// - 계단 오브젝트가 현관 홀 안에 있고 문 앞(접근 거리 안)을 막지 않는다.
        /// </summary>
        public static List<string> Validate()
        {
            var issues = new List<string>();
            var graph = HouseLayoutFactory.CreateStandardHouse();

            foreach (var id in HouseLayoutFactory.AllRoomIds)
            {
                int count = Rooms.Count(r => r.RoomId == id);
                if (count != 1) issues.Add($"{id}: 배치가 {count}개입니다.");
            }
            foreach (var r in Rooms)
                if (!graph.RoomExists(r.RoomId)) issues.Add($"{r.RoomId}: 그래프에 없는 방입니다.");

            var first = HouseLayoutFactory.AllRoomIds[0];
            foreach (var id in HouseLayoutFactory.AllRoomIds)
                if (!graph.CanReach(first, id)) issues.Add($"{id}: {first}에서 도달할 수 없습니다.");

            for (int i = 0; i < Rooms.Count; i++)
            for (int j = i + 1; j < Rooms.Count; j++)
            {
                var a = Rooms[i];
                var b = Rooms[j];
                float gapX = Math.Max(a.MinX - b.MaxX, b.MinX - a.MaxX);
                float gapZ = Math.Max(a.MinZ - b.MaxZ, b.MinZ - a.MaxZ);
                if (Math.Max(gapX, gapZ) < WallThickness - 0.001f)
                    issues.Add($"{a.RoomId}와 {b.RoomId}가 겹치거나 벽 두께보다 가깝습니다.");
            }

            foreach (var d in Doors)
            {
                var a = GetRoom(d.RoomA);
                var b = GetRoom(d.RoomB);
                if (a == null || b == null) { issues.Add($"{d.DoorId}: 방 배치가 없습니다."); continue; }
                if (!graph.AreConnected(d.RoomA, d.RoomB)) issues.Add($"{d.DoorId}: 그래프에 연결이 없습니다.");
                if (!DoorFitsWall(d, a, b)) issues.Add($"{d.DoorId}: 두 방 사이 벽 위에 있지 않거나 문 폭만큼 맞닿지 않습니다.");
            }

            var hall = GetRoom(HouseLayoutFactory.EntranceHall);
            var s = StairsInHall;
            if (!hall.Contains(s.MinX, s.MinZ) || !hall.Contains(s.MaxX, s.MaxZ))
                issues.Add("계단 오브젝트가 현관 홀 밖으로 나갑니다.");
            foreach (var d in Doors.Where(d => d.RoomA == hall.RoomId || d.RoomB == hall.RoomId))
            {
                float dx = Math.Max(0f, Math.Max(s.MinX - d.CenterX, d.CenterX - s.MaxX));
                float dz = Math.Max(0f, Math.Max(s.MinZ - d.CenterZ, d.CenterZ - s.MaxZ));
                if (Math.Sqrt(dx * dx + dz * dz) < InteractionRange)
                    issues.Add($"계단 오브젝트가 {d.DoorId} 앞을 막습니다.");
            }
            return issues;
        }

        /// <summary>문이 두 방 사이 벽(두께 0.2 m) 안에 있고, 벽을 따라 문 폭 전체가 두 방 모두와 맞닿는지.</summary>
        public static bool DoorFitsWall(DoorLayout d, RoomRect a, RoomRect b)
        {
            const float eps = 0.01f;
            float half = DoorWidth * 0.5f;

            // 벽이 X축에 수직(두 방이 좌우로 붙음)
            bool betweenX = (Near(a.MaxX + WallThickness, b.MinX) && d.CenterX > a.MaxX - eps && d.CenterX < b.MinX + eps)
                         || (Near(b.MaxX + WallThickness, a.MinX) && d.CenterX > b.MaxX - eps && d.CenterX < a.MinX + eps);
            if (betweenX)
                return d.CenterZ - half >= Math.Max(a.MinZ, b.MinZ) - eps && d.CenterZ + half <= Math.Min(a.MaxZ, b.MaxZ) + eps;

            bool betweenZ = (Near(a.MaxZ + WallThickness, b.MinZ) && d.CenterZ > a.MaxZ - eps && d.CenterZ < b.MinZ + eps)
                         || (Near(b.MaxZ + WallThickness, a.MinZ) && d.CenterZ > b.MaxZ - eps && d.CenterZ < a.MinZ + eps);
            if (betweenZ)
                return d.CenterX - half >= Math.Max(a.MinX, b.MinX) - eps && d.CenterX + half <= Math.Min(a.MaxX, b.MaxX) + eps;

            return false;
        }

        private static bool Near(float a, float b) => Math.Abs(a - b) < 0.01f;

        private static RoomRect Room(string id, float x, float z, float w, float d) =>
            new RoomRect { RoomId = id, CenterX = x, CenterZ = z, Width = w, Depth = d };

        private static DoorLayout Door(string id, string a, string b, float x, float z) =>
            new DoorLayout { DoorId = id, RoomA = a, RoomB = b, CenterX = x, CenterZ = z };
    }
}
