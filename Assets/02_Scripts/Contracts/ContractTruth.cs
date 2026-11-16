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

        /// <summary>
        /// 같은 핵심 사건에서 함께 숨진 다른 피해자(03 메인 단서 생성 제약: "피해자가 3명 이상인 사건", "부부 또는 연인 관계인 두 사람이 사망한 사건").
        /// 대표 피해자는 <see cref="VictimId"/>이고 여기에는 추가 피해자만 둔다.
        /// </summary>
        public readonly List<string> AdditionalVictimIds = new List<string>();

        /// <summary>대표 피해자와 첫 번째 추가 피해자가 부부 또는 연인 관계(REL-02)인지.</summary>
        public bool VictimsAreCouple;

        public readonly List<string> TimelineEvents = new List<string>();
    }
}
