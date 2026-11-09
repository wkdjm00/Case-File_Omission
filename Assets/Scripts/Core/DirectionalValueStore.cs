using System;
using System.Collections.Generic;

namespace CaseFileOmission.Core
{
    /// <summary>
    /// 방향성이 있는 A→B 값을 관리하는 공용 저장소. NPC 간 의심·신뢰, 경찰의 플레이어 의심도 등
    /// "A가 B를 보는 값"과 "B가 A를 보는 값"을 독립적으로 관리해야 하는 모든 곳에서 재사용한다
    /// (홈 문서 확정 원칙: "A→B와 B→A를 하나로 합치지 않는다").
    /// </summary>
    public sealed class DirectionalValueStore
    {
        private readonly Dictionary<(string Source, string Target), float> _values =
            new Dictionary<(string, string), float>();

        private readonly float _defaultValue;

        public DirectionalValueStore(float defaultValue = 0f)
        {
            _defaultValue = defaultValue;
        }

        public float Get(string sourceId, string targetId)
        {
            var key = (sourceId, targetId);
            return _values.TryGetValue(key, out var value) ? value : _defaultValue;
        }

        public void Set(string sourceId, string targetId, float value)
        {
            _values[(sourceId, targetId)] = value;
        }

        public float Add(string sourceId, string targetId, float delta)
        {
            float newValue = Get(sourceId, targetId) + delta;
            Set(sourceId, targetId, newValue);
            return newValue;
        }

        /// <summary>저장/불러오기 전용. 명시적으로 설정된 값만 나열한다(기본값인 쌍은 포함하지 않는다).</summary>
        public IEnumerable<(string Source, string Target, float Value)> Entries
        {
            get
            {
                foreach (var pair in _values)
                    yield return (pair.Key.Source, pair.Key.Target, pair.Value);
            }
        }

        public float AddClamped(string sourceId, string targetId, float delta, float min, float max)
        {
            float newValue = ProbabilityCalculator.Clamp(Get(sourceId, targetId) + delta, min, max);
            Set(sourceId, targetId, newValue);
            return newValue;
        }
    }
}
