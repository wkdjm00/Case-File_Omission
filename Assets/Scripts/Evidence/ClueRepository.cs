using System;
using System.Collections.Generic;
using System.Linq;

namespace CaseFileOmission.Evidence
{
    /// <summary>
    /// 플레이어의 단서 저장소(= 보고서). 06페이지 확정 규칙:
    /// 플레이어 보유량은 무제한이며, 별도 인벤토리 없이 전부 여기 저장된다.
    /// 경찰 직접 보유 한도 5개, 기본 NPC 보유 한도 3개는 이후 Police/NPC 모듈이
    /// "단서를 직접 챙길지" 판단할 때 참조할 상수로만 여기 둔다(이 저장소는 그 판단을 하지 않는다).
    /// </summary>
    public sealed class ClueRepository
    {
        public const int PoliceDirectHoldLimit = 5;
        public const int NpcDirectHoldLimit = 3;

        private readonly Dictionary<string, ClueDefinition> _definitions = new Dictionary<string, ClueDefinition>();
        private readonly Dictionary<string, ClueRecord> _records = new Dictionary<string, ClueRecord>();

        public bool Exists(string clueId) => _definitions.ContainsKey(clueId);

        /// <summary>
        /// 단서를 발견해 저장소에 등록한다. 동일 clueId를 다시 발견하면 새로 만들지 않고
        /// 기존 기록을 그대로 반환한다(통합 구현 명세서 §4-3 확정: "동일 단서를 다시 조사하면
        /// 기존 기록을 반환한다"). 새 정의로 덮어쓰지 않으므로 최초 발견 시점의 정의가 유지된다.
        /// </summary>
        public ClueRecord Discover(ClueDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            if (_records.TryGetValue(definition.ClueId, out var existing))
                return existing;

            _definitions[definition.ClueId] = definition;
            var record = new ClueRecord(definition.ClueId);
            _records[definition.ClueId] = record;
            return record;
        }

        public ClueDefinition GetDefinition(string clueId)
        {
            if (!_definitions.TryGetValue(clueId, out var def))
                throw new KeyNotFoundException($"단서 '{clueId}' 정의를 찾을 수 없습니다.");
            return def;
        }

        public ClueRecord GetRecord(string clueId)
        {
            if (!_records.TryGetValue(clueId, out var record))
                throw new KeyNotFoundException($"단서 '{clueId}' 기록을 찾을 수 없습니다.");
            return record;
        }

        public IReadOnlyList<string> GetAllClueIds(string contractId) =>
            _definitions.Values.Where(d => d.ContractId == contractId).Select(d => d.ClueId).ToList();

        public IReadOnlyList<string> GetHeldByPlayerClueIds(string contractId) =>
            GetAllClueIds(contractId)
                .Where(id => _records[id].WorldState == ClueWorldState.HeldByPlayer)
                .ToList();

        /// <summary>저장/불러오기 전용.</summary>
        public IEnumerable<ClueDefinition> AllDefinitions => _definitions.Values;

        /// <summary>저장/불러오기 전용.</summary>
        public IEnumerable<ClueRecord> AllRecords => _records.Values;

        /// <summary>
        /// 저장/불러오기 전용. 정의와 기록을 발견 이벤트 없이 그대로 되살린다. 이미 같은 ID가 있으면
        /// 덮어쓰지 않고 false를 반환한다.
        /// </summary>
        public bool RestoreEntry(ClueDefinition definition, ClueRecord record)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (definition.ClueId != record.ClueId)
                throw new ArgumentException("정의와 기록의 clueId가 다릅니다.", nameof(record));

            if (_definitions.ContainsKey(definition.ClueId))
                return false;

            _definitions[definition.ClueId] = definition;
            _records[definition.ClueId] = record;
            return true;
        }

        /// <summary>플레이어가 단서를 버린다. 보고서 기록은 남고 물리적 소지 상태만 바뀐다.</summary>
        public void Drop(string clueId) => GetRecord(clueId).MarkDropped();

        /// <summary>조사 공간에 남아 있던 단서를 플레이어가 다시 집는다.</summary>
        public void PickUpByPlayer(string clueId) => GetRecord(clueId).MarkHeldByPlayer();
    }
}
