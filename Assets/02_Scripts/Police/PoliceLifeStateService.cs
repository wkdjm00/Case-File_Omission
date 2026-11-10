using System.Collections.Generic;
using System.Linq;

namespace CaseFileOmission.Police
{
    /// <summary>
    /// §5-2/§8단계 확정: 경찰별 생존·사망 상태를 지속적으로 저장한다.
    /// 이전에는 이 상태 저장소 자체가 없어 GameEndEvaluator가 외부에서 죽은 목록을 받아야 했다.
    /// </summary>
    public sealed class PoliceLifeStateService
    {
        private readonly HashSet<string> _deceased = new HashSet<string>();

        public bool IsAlive(string policeId) => !_deceased.Contains(policeId);

        public void MarkDeceased(string policeId) => _deceased.Add(policeId);

        public IReadOnlyCollection<string> GetDeceasedIds() => _deceased;

        /// <summary>경찰 전원 제거 엔딩(§11-2) 조건: 장기 의심도를 관리하는 경찰 5명 전원이 대상이다.</summary>
        public bool AllPoliceDeceased() => PoliceRoster.AllPoliceIds.All(id => _deceased.Contains(id));
    }
}
