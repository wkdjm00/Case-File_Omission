using System;
using System.Collections.Generic;

namespace CaseFileOmission.Contracts
{
    /// <summary>
    /// 실제 사건의 진실. 외부에 알려진 상태(<see cref="ContractExternalState"/>)나
    /// 공식 결론(<see cref="ContractOfficialConclusion"/>)과 절대 하나로 합치지 않는다.
    /// 플레이어의 행동으로 공식 결론이 바뀌어도 이 데이터는 자동으로 바뀌지 않는다 (04페이지 확정 원칙).
    /// </summary>
    [Serializable]
    public sealed class ContractTruth
    {
        public string PerpetratorId;
        public string VictimId;
        public string PrimaryMotive;
        public string SecondaryMotive;
        public string Method;
        public string Tool;
        public string RelationshipType;
        public CoverUpMethod CoverUp = CoverUpMethod.None;

        public readonly List<string> TimelineEvents = new List<string>();
    }
}
