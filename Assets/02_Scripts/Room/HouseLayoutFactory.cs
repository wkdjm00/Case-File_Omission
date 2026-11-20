namespace CaseFileOmission.Rooms
{
    /// <summary>
    /// 09 최신 확정 공간 구조: 7개 실제 방과 연결용 복도 노드 1개.
    /// 복도 노드는 경로 계산을 위해 그래프에 남기지만 별도 조사실로 취급하지 않는다.
    /// 계단은 복도 노드가 아니라 현관 홀 내부에 배치하는 공간 요소다.
    /// 내부 ID는 저장 데이터·자료 배치 호환성을 위해 유지하고, 표시명만 최신 기준을 따른다.
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
            PantryUtility => "창고",
            CorridorStairs => "복도",
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
