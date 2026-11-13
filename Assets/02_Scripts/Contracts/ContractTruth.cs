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

        // 04 고정 선택지 ID(표시명은 Method·Tool·PrimaryMotive·RelationshipType). 생성기가 같은 값으로 함께 채운다.
        public string MethodId;
        public string ToolId;
        public string MotiveId;
        public string RelationshipId;
        public string RepresentativeSubtypeId;
        public string ActualSubtypeId;

        /// <summary>피해자가 핵심 사건 뒤 스스로 집을 떠나 소재 미확인(자발적 이탈·은신). 강제 이동이 아니다.</summary>
        public bool VictimDeparted;

        /// <summary>살인 피해자의 시신이 인계 시점까지 발견되지 않음(시신 은닉).</summary>
        public bool BodyHidden;

        /// <summary>공격 뒤 한동안 살아 있다가 다른 방에서 숨짐(ACT-SUB-MUR-02). 경찰은 분류 당시 부상만 알았다(폭행→살인).</summary>
        public bool DelayedDeath;

        public readonly List<string> TimelineEvents = new List<string>();
    }
}
