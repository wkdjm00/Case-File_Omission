using System;
using System.Collections.Generic;
using System.Linq;

namespace CaseFileOmission.Evidence
{
    /// <summary>
    /// 전혀 없는 "조작 성공/발각 판정"은 포함하지 않는다(06페이지: 성공·발각 계산은 미정).
    /// 여기서는 06페이지 20절이 확정한 "조작 입력" 자체만 구현한다:
    /// 뒤집기=왜곡, 플레이어 쪽으로 끌기=은폐, 은폐한 카드의 대체 카드는 자기 단서 묶음에서 무작위 선택.
    /// </summary>
    public sealed class EvidenceManipulationService
    {
        private readonly ClueRepository _repository;
        private readonly Random _random;
        private readonly List<ManipulationRecord> _history = new List<ManipulationRecord>();

        public EvidenceManipulationService(ClueRepository repository, Random random = null)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _random = random ?? new Random();
        }

        public IReadOnlyList<ManipulationRecord> History => _history;

        /// <summary>저장/불러오기 전용. 조작을 다시 적용하지 않고 이력만 되살린다.</summary>
        public void RestoreHistory(ManipulationRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            _history.Add(record);
        }

        /// <summary>카드를 뒤집어 왜곡해 제시한다. 원본 단서 정의는 변경하지 않는다.</summary>
        public ManipulationRecord Distort(string clueId, string actorId)
        {
            if (!_repository.Exists(clueId))
                throw new KeyNotFoundException($"단서 '{clueId}'를 찾을 수 없습니다.");

            var record = new ManipulationRecord(clueId, ManipulationType.Distort, actorId, clueId);
            _history.Add(record);
            return record;
        }

        /// <summary>
        /// 카드를 플레이어 쪽으로 끌어와 은폐한다. 대체 카드는 actorHeldClueIds(자신의 단서 묶음)에서
        /// 은폐 대상을 제외하고 무작위로 선택한다. 06 확정: 묶음에 카드가 남아 있을 때만 보충하므로, 대체할 단서가 없으면
        /// 대체 없이 은폐만 기록한다(DisplayedClueId null).
        /// </summary>
        public ManipulationRecord Conceal(string clueId, string actorId, IReadOnlyList<string> actorHeldClueIds)
        {
            if (!_repository.Exists(clueId))
                throw new KeyNotFoundException($"단서 '{clueId}'를 찾을 수 없습니다.");
            if (actorHeldClueIds == null)
                throw new ArgumentNullException(nameof(actorHeldClueIds));

            var candidates = actorHeldClueIds.Where(id => id != clueId).ToList();
            string substituteId = candidates.Count == 0 ? null : candidates[_random.Next(candidates.Count)];
            var record = new ManipulationRecord(clueId, ManipulationType.Conceal, actorId, substituteId);
            _history.Add(record);
            return record;
        }
    }
}
