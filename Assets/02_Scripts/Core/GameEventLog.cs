using System.Collections.Generic;
using System.Linq;

namespace CaseFileOmission.Core
{
    /// <summary>
    /// 발생한 이벤트를 eventId 기준으로 기록하고 중복 처리를 막는다.
    /// 모든 모듈(단서, 조직 신뢰도, 경찰 의심도, 엔딩 판정 등)이 공유하는 기반 로그다.
    /// </summary>
    public sealed class GameEventLog
    {
        private readonly Dictionary<string, GameEvent> _eventsById = new Dictionary<string, GameEvent>();
        private readonly List<GameEvent> _ordered = new List<GameEvent>();

        public bool HasProcessed(string eventId) => _eventsById.ContainsKey(eventId);

        /// <summary>
        /// 이벤트를 기록한다. 동일 eventId가 이미 기록되어 있으면 아무 효과도 적용하지 않고 false를 반환한다.
        /// </summary>
        public bool TryRecord(GameEvent gameEvent)
        {
            if (gameEvent == null)
                return false;

            if (_eventsById.ContainsKey(gameEvent.EventId))
                return false;

            _eventsById[gameEvent.EventId] = gameEvent;
            _ordered.Add(gameEvent);
            return true;
        }

        public IReadOnlyList<GameEvent> GetOrdered() => _ordered;

        public IEnumerable<GameEvent> GetByContract(string contractId) =>
            _ordered.Where(e => e.ContractId == contractId);
    }
}
