using System;
using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.NPC;
using CaseFileOmission.Rooms;

namespace CaseFileOmission.ContractContent
{
    /// <summary>
    /// 07 2026-11-13 「NPC 시작 위치·반복 일정 데이터 계약」의 조사 기간 일정. 시간대(12시간) 하나 분량을 세계 시각 기준으로 만든다.
    /// - 같은 시드면 같은 일정(재현 가능). 시드는 의뢰 ID·시간대 시각·NPC ID에서 만들므로 저장하지 않아도 복원된다.
    /// - 일정 항목은 [시작, 끝) 동안 머무를 목적 공간이다. 실제 이동은 15분 턴마다 NPC 행동 계획기가 한 칸씩 한다.
    /// - 시각 정밀도는 10분(06 확정)이며 15분 턴에 맞춰 반올림하지 않는다.
    /// - 일정이 없는 시간은 만들지 않는다(빈틈 없이 시간대 끝까지 채운다). 무작위 배회는 없다 — 다음 목적지는 직업·시간대별 생활 공간에서 고른다.
    /// 직업별 생활 공간과 머무는 시간 범위는 원문에 없어 TEST_TEMP다(교체 지점: <see cref="PreferredRooms"/>, <see cref="StayRange"/>).
    /// </summary>
    public sealed class InvestigationScheduleGenerator
    {
        private readonly RoomGraph _rooms;

        public InvestigationScheduleGenerator(RoomGraph rooms)
        {
            _rooms = rooms ?? throw new ArgumentNullException(nameof(rooms));
        }

        /// <summary>TEST_TEMP: 직업별로 자주 머무는 공간.</summary>
        public static IReadOnlyList<string> PreferredRooms(string occupation) => occupation switch
        {
            "집사" or "가정부" => new[] { HouseLayoutFactory.Kitchen, HouseLayoutFactory.PantryUtility, HouseLayoutFactory.Dining, HouseLayoutFactory.EntranceHall },
            "비서" or "사무원" or "회계 담당자" or "변호사" or "공무원" => new[] { HouseLayoutFactory.Study, HouseLayoutFactory.Reception },
            "기자" => new[] { HouseLayoutFactory.Reception, HouseLayoutFactory.EntranceHall, HouseLayoutFactory.CorridorStairs },
            "교사·가정교사" or "학생" => new[] { HouseLayoutFactory.Study, HouseLayoutFactory.Bedroom, HouseLayoutFactory.Reception },
            "상인" or "점원" => new[] { HouseLayoutFactory.Reception, HouseLayoutFactory.Dining, HouseLayoutFactory.Kitchen },
            _ => new[] { HouseLayoutFactory.Reception, HouseLayoutFactory.Dining }
        };

        /// <summary>TEST_TEMP: 한 곳에 머무는 시간(분, 10분 단위). 겁이 많은 인물은 덜 움직인다. 밤(22시 이후)에는 오래 머문다.</summary>
        public static (int Min, int Max) StayRange(NpcPersonality personality, bool lateNight) =>
            lateNight ? (120, 240) : personality == NpcPersonality.Timid ? (60, 150) : (30, 120);

        public NpcSchedule Generate(NpcState npc, string currentRoom, int blockStart, int blockEnd, int seed, bool isFirstBlock)
        {
            if (npc == null) throw new ArgumentNullException(nameof(npc));
            var random = new Random(seed);
            var schedule = new NpcSchedule(npc.NpcId);
            var preferred = PreferredRooms(npc.Occupation).Where(_rooms.RoomExists).ToList();
            var all = _rooms.AllRoomIds.OrderBy(id => id, StringComparer.Ordinal).ToList();

            // 첫 시간대에는 시간표가 정한 위치(사건 이후 머문 곳)에서 시작한다. 그 위치가 없으면 생활 공간 중 하나.
            string room = currentRoom != null && _rooms.RoomExists(currentRoom)
                ? currentRoom
                : preferred.Count > 0 ? preferred[random.Next(preferred.Count)] : all[random.Next(all.Count)];

            int t = blockStart;
            while (t < blockEnd)
            {
                int minuteOfDay = t % 1440;
                bool lateNight = minuteOfDay >= 22 * 60 || minuteOfDay < 6 * 60;
                var (min, max) = StayRange(npc.Personality, lateNight);
                int stay = 10 * random.Next(min / 10, max / 10 + 1);
                int end = Math.Min(blockEnd, t + stay);
                schedule.TryAdd(t, end, room);
                t = end;
                room = PickNext(room, preferred, all, lateNight, random);
            }
            return schedule;
        }

        private static string PickNext(string current, List<string> preferred, List<string> all, bool lateNight, Random random)
        {
            var weighted = new List<(string Room, int Weight)>();
            foreach (var r in all)
            {
                if (r == current) continue;
                int w = preferred.Contains(r) ? 3 : 1;
                if (lateNight && r == HouseLayoutFactory.Bedroom) w += 2;
                weighted.Add((r, w));
            }
            int roll = random.Next(weighted.Sum(x => x.Weight));
            foreach (var (r, w) in weighted)
            {
                if (roll < w) return r;
                roll -= w;
            }
            return weighted[weighted.Count - 1].Room;
        }
    }
}
