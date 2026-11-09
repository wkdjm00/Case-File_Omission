using System;
using System.Collections.Generic;
using System.Linq;

namespace CaseFileOmission.NPC
{
    /// <summary>일정 한 칸. [StartMinute, EndMinute) 동안 RoomId에 있어야 한다.</summary>
    public readonly struct NpcScheduleEntry
    {
        public readonly int StartMinute;
        public readonly int EndMinute;
        public readonly string RoomId;

        public NpcScheduleEntry(int startMinute, int endMinute, string roomId)
        {
            StartMinute = startMinute;
            EndMinute = endMinute;
            RoomId = roomId;
        }

        public bool Contains(int minute) => minute >= StartMinute && minute < EndMinute;

        public bool Overlaps(NpcScheduleEntry other) => StartMinute < other.EndMinute && other.StartMinute < EndMinute;
    }

    public enum NpcScheduleAddResult
    {
        Added,
        InvalidRange,
        MissingRoom,
        ConflictsWithExisting
    }

    /// <summary>
    /// 07 "시간·일정 — 행동 시점, 이동과 장소 방문". NPC가 언제 어느 방에 있어야 하는지만 담는다.
    /// 한 NPC가 같은 시간에 두 장소에 있을 수 없으므로 겹치는 일정은 추가하지 않는다(일정 충돌 방지).
    /// 일정이 없는 시간에는 할 일이 없는 것으로 보고 대기한다(07 확정 "할 일이 없으면 대기").
    /// </summary>
    public sealed class NpcSchedule
    {
        private readonly List<NpcScheduleEntry> _entries = new List<NpcScheduleEntry>();

        public string NpcId { get; }

        public NpcSchedule(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) throw new ArgumentException("npcId는 비어 있을 수 없습니다.", nameof(npcId));
            NpcId = npcId;
        }

        public IReadOnlyList<NpcScheduleEntry> Entries => _entries;

        public NpcScheduleAddResult TryAdd(int startMinute, int endMinute, string roomId)
        {
            if (endMinute <= startMinute || startMinute < 0)
                return NpcScheduleAddResult.InvalidRange;
            if (string.IsNullOrEmpty(roomId))
                return NpcScheduleAddResult.MissingRoom;

            var entry = new NpcScheduleEntry(startMinute, endMinute, roomId);
            if (_entries.Any(e => e.Overlaps(entry)))
                return NpcScheduleAddResult.ConflictsWithExisting;

            _entries.Add(entry);
            _entries.Sort((a, b) => a.StartMinute.CompareTo(b.StartMinute));
            return NpcScheduleAddResult.Added;
        }

        public bool TryGetEntryAt(int minute, out NpcScheduleEntry entry)
        {
            foreach (var e in _entries)
            {
                if (e.Contains(minute))
                {
                    entry = e;
                    return true;
                }
            }

            entry = default;
            return false;
        }

        /// <summary>해당 시각에 일정상 있어야 하는 방. 일정이 없으면 null.</summary>
        public string GetScheduledRoom(int minute) => TryGetEntryAt(minute, out var e) ? e.RoomId : null;
    }
}
