using System.Collections.Generic;
using CaseFileOmission.Core;

namespace CaseFileOmission.NPC
{
    /// <summary>
    /// NPC 간 방향성 관계(의심·신뢰). sourceNpcId, targetNpcId, suspicion, trust로 관리한다
    /// (홈 문서 "최소 데이터 계약" 확정 구조). A→B와 B→A는 독립적이다.
    /// </summary>
    public sealed class NpcRelationshipStore
    {
        private readonly DirectionalValueStore _suspicion = new DirectionalValueStore(0f);
        private readonly DirectionalValueStore _trust = new DirectionalValueStore(0f);

        public float GetSuspicion(string sourceNpcId, string targetNpcId) => _suspicion.Get(sourceNpcId, targetNpcId);
        public float GetTrust(string sourceNpcId, string targetNpcId) => _trust.Get(sourceNpcId, targetNpcId);

        public void SetSuspicion(string sourceNpcId, string targetNpcId, float value) => _suspicion.Set(sourceNpcId, targetNpcId, value);
        public void SetTrust(string sourceNpcId, string targetNpcId, float value) => _trust.Set(sourceNpcId, targetNpcId, value);

        /// <summary>저장/불러오기 전용.</summary>
        public IEnumerable<(string Source, string Target, float Value)> SuspicionEntries => _suspicion.Entries;

        /// <summary>저장/불러오기 전용.</summary>
        public IEnumerable<(string Source, string Target, float Value)> TrustEntries => _trust.Entries;
    }
}
