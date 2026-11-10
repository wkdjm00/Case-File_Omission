using System;
using System.Collections.Generic;

namespace CaseFileOmission.Evidence
{
    public enum HolderType
    {
        Police,
        Npc
    }

    public enum EvidenceAcquisitionOutcome
    {
        Acquired,
        AlreadyHeld,
        Replaced,
        RejectedAtCapacity
    }

    public sealed class EvidenceAcquisitionResult
    {
        public EvidenceAcquisitionOutcome Outcome;
        public string ReplacedClueId;
    }

    /// <summary>
    /// §4-4 확정: 경찰 1명당 5개, 일반 NPC 1명당 3개 한도. 한도가 찼을 때 자동으로 기존 단서를
    /// 삭제하지 않으며, 교체 여부를 판단하는 호출자의 비교 로직이 있을 때만 교체한다.
    /// 플레이어는 무제한이므로 이 매니저가 다루지 않는다(ClueRepository가 플레이어 보유를 전담).
    /// </summary>
    public sealed class EvidenceCapacityManager
    {
        private readonly Dictionary<string, List<string>> _heldByHolder = new Dictionary<string, List<string>>();

        public static int GetLimit(HolderType type) =>
            type == HolderType.Police ? ClueRepository.PoliceDirectHoldLimit : ClueRepository.NpcDirectHoldLimit;

        public IReadOnlyList<string> GetHeldClues(string holderId) =>
            _heldByHolder.TryGetValue(holderId, out var list) ? list : Array.Empty<string>();

        /// <summary>저장/불러오기 전용. 보유자별 보유 단서(획득 순서 유지).</summary>
        public IEnumerable<KeyValuePair<string, List<string>>> AllHoldings => _heldByHolder;

        /// <summary>
        /// 저장/불러오기 전용. 보유 목록을 획득 순서대로 되살린다. 한도를 넘는 저장값은 잘못된 데이터이므로
        /// 앞에서부터 한도까지만 받아들이고 받아들인 개수를 반환한다.
        /// </summary>
        public int RestoreHoldings(string holderId, HolderType type, IEnumerable<string> clueIds)
        {
            if (string.IsNullOrEmpty(holderId)) throw new ArgumentException("holderId는 비어 있을 수 없습니다.", nameof(holderId));

            var list = new List<string>();
            foreach (var clueId in clueIds ?? Array.Empty<string>())
            {
                if (string.IsNullOrEmpty(clueId) || list.Contains(clueId) || list.Count >= GetLimit(type))
                    continue;
                list.Add(clueId);
            }

            _heldByHolder[holderId] = list;
            return list.Count;
        }

        /// <summary>
        /// 새 단서 획득을 시도한다. 한도 미만이면 바로 추가한다. 한도에 도달한 경우
        /// shouldReplace(기존단서ID, 신규단서ID)가 true를 반환할 때만 가장 오래 보유한 단서를 교체한다.
        /// shouldReplace를 생략하면 한도 초과 시 무조건 획득을 거절한다(자동 삭제 금지 원칙).
        /// </summary>
        public EvidenceAcquisitionResult TryAcquire(
            string holderId,
            HolderType type,
            string newClueId,
            Func<string, string, bool> shouldReplace = null)
        {
            if (string.IsNullOrEmpty(holderId)) throw new ArgumentException("holderId는 비어 있을 수 없습니다.", nameof(holderId));
            if (string.IsNullOrEmpty(newClueId)) throw new ArgumentException("newClueId는 비어 있을 수 없습니다.", nameof(newClueId));

            if (!_heldByHolder.TryGetValue(holderId, out var list))
            {
                list = new List<string>();
                _heldByHolder[holderId] = list;
            }

            if (list.Contains(newClueId))
                return new EvidenceAcquisitionResult { Outcome = EvidenceAcquisitionOutcome.AlreadyHeld };

            int limit = GetLimit(type);
            if (list.Count < limit)
            {
                list.Add(newClueId);
                return new EvidenceAcquisitionResult { Outcome = EvidenceAcquisitionOutcome.Acquired };
            }

            // 06 §8 확정: 기본 NPC는 3개가 차면 추가로 획득하지 않고 지나가며 단서 교체를 판단하지 않는다.
            // 교체는 경찰만 "새 단서가 더 중요하다고 판단할 때" 한다(판단 기준은 판단 유형별 — 호출자 콜백).
            if (type == HolderType.Npc || shouldReplace == null)
                return new EvidenceAcquisitionResult { Outcome = EvidenceAcquisitionOutcome.RejectedAtCapacity };

            string oldest = list[0];
            if (!shouldReplace(oldest, newClueId))
                return new EvidenceAcquisitionResult { Outcome = EvidenceAcquisitionOutcome.RejectedAtCapacity };

            list.RemoveAt(0);
            list.Add(newClueId);
            return new EvidenceAcquisitionResult { Outcome = EvidenceAcquisitionOutcome.Replaced, ReplacedClueId = oldest };
        }
    }
}
