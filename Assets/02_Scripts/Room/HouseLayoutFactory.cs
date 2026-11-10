namespace CaseFileOmission.Rooms
{
    /// <summary>
    /// §3-2 5단계 "주택 내부 공간 구성"의 임시 구현. 실제 모델링 결과가 나오기 전까지
    /// 고정된 표준 주택 평면(거실-주방, 거실-복도, 복도-침실, 복도-욕실)을 사용한다.
    /// 방 이름은 전부 임시 표시용이며 RoomId는 고정 상수로 다른 시스템이 참조한다.
    /// </summary>
    public static class HouseLayoutFactory
    {
        public const string LivingRoom = "living-room";
        public const string Kitchen = "kitchen";
        public const string Hallway = "hallway";
        public const string Bedroom = "bedroom";
        public const string Bathroom = "bathroom";

        public static readonly string[] AllRoomIds =
        {
            LivingRoom, Kitchen, Hallway, Bedroom, Bathroom
        };

        public static RoomGraph CreateStandardHouse()
        {
            var graph = new RoomGraph();
            graph.AddRoom(new RoomDefinition(LivingRoom, "거실(임시)"));
            graph.AddRoom(new RoomDefinition(Kitchen, "주방(임시)"));
            graph.AddRoom(new RoomDefinition(Hallway, "복도(임시)"));
            graph.AddRoom(new RoomDefinition(Bedroom, "침실(임시)"));
            graph.AddRoom(new RoomDefinition(Bathroom, "욕실(임시)"));

            graph.Connect(LivingRoom, Kitchen);
            graph.Connect(LivingRoom, Hallway);
            graph.Connect(Hallway, Bedroom);
            graph.Connect(Hallway, Bathroom);

            return graph;
        }
    }
}
