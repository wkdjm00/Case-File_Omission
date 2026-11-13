namespace CaseFileOmission.Rooms
{
    /// <summary>
    /// 09 「Unity 임시 3D 공간 그래프·좌표·접근 거리 계약」(2026-11-13)의 TEMP_LAYOUT_V1 공간 그래프.
    /// 주택 기본 8개 공간과 7개 연결(DOOR_01~07)을 고정한다. 실외·욕실·손님방·지하실은 만들지 않는다.
    /// 계단은 아홉 번째 방이 아니라 현관 홀 안의 임시 오브젝트다(좌표·크기는 <see cref="TempLayoutV1"/>).
    /// RoomId는 고정 상수로 다른 시스템이 참조하며, 표시 이름은 임시다.
    /// </summary>
    public static class HouseLayoutFactory
    {
        public const string EntranceHall = "ROOM_ENTRANCE_HALL";
        public const string Reception = "ROOM_RECEPTION";
        public const string Dining = "ROOM_DINING";
        public const string Kitchen = "ROOM_KITCHEN";
        public const string PantryUtility = "ROOM_PANTRY_UTILITY";
        public const string CorridorStairs = "ROOM_CORRIDOR_STAIRS";
        public const string Bedroom = "ROOM_BEDROOM";
        public const string Study = "ROOM_STUDY";

        public static readonly string[] AllRoomIds =
        {
            EntranceHall, Reception, Dining, Kitchen, PantryUtility, CorridorStairs, Bedroom, Study
        };

        /// <summary>표시 이름(임시).</summary>
        public static string GetDisplayName(string roomId) => roomId switch
        {
            EntranceHall => "현관 홀",
            Reception => "응접실",
            Dining => "식당",
            Kitchen => "주방",
            PantryUtility => "식료품 저장실·다용도실",
            CorridorStairs => "복도·계단",
            Bedroom => "침실",
            Study => "서재",
            _ => roomId
        };

        /// <summary>09 확정: 컴퓨터가 있는 방(대상자 번호 파일 비밀번호 입력). 임시로 서재만 둔다.</summary>
        public static bool RoomHasComputer(string roomId) => roomId == Study;

        public static RoomGraph CreateStandardHouse()
        {
            var graph = new RoomGraph();
            foreach (var id in AllRoomIds)
                graph.AddRoom(new RoomDefinition(id, GetDisplayName(id)));

            foreach (var door in TempLayoutV1.Doors)
                graph.Connect(door.RoomA, door.RoomB);

            return graph;
        }
    }
}
