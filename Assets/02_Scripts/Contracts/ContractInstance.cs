using System;
using System.Collections.Generic;
using CaseFileOmission.Core;

namespace CaseFileOmission.Contracts
{
    /// <summary>
    /// 부수 범죄 하나가 대표 사건과 어떻게 연결되는지(04 확정: 인과관계나 인물 관계로 연결된 후보만,
    /// 시점·장소·인물·연결 이유를 경위에 반영하고 검증한다).
    /// </summary>
    [Serializable]
    public sealed class SubCrimeLink
    {
        public CrimeType Type;
        public string LinkReason;

        /// <summary>시간표에 넣은 부수 범죄 사건 ID(경위 반영 검증·흔적 자료 연결용).</summary>
        public string EventId;
        public readonly List<string> LinkedNpcIds = new List<string>();
    }

    /// <summary>
    /// 의뢰 사건 하나의 런타임 데이터. 실제 진실/외부 상태/공식 결론/조직 조건을
    /// 서로 다른 필드로 분리해 관리한다 (04페이지 "데이터 기록 구조" 확정 원칙).
    /// </summary>
    [Serializable]
    public sealed class ContractInstance
    {
        public string ContractId { get; }

        /// <summary>실제 사건 유형(실제 진실).</summary>
        public CrimeType PrimaryCrimeType;

        /// <summary>
        /// 경찰이 인계 시점에 분류한 대표 사건 유형(04 2026-11-12). 실제 유형과 다를 수 있으며 덮어쓰지 않는다. null이면 실제 유형과 같다.
        /// </summary>
        public CrimeType? RepresentativeCrimeType;

        public CrimeType PoliceCrimeType => RepresentativeCrimeType ?? PrimaryCrimeType;

        /// <summary>경찰 초기 정보 스냅샷(04 「무작위」 C): 인계 시각까지 경찰이 실제로 확보한 사실, 몰랐던 사실, 분류 근거.</summary>
        public readonly List<string> PoliceKnownFacts = new List<string>();
        public readonly List<string> PoliceUnknownFacts = new List<string>();
        public string PoliceClassificationBasis;

        public LocationCategory LocationCategory;

        /// <summary>최종 장소 목록 확정 전까지 사용하는 스텁 장소.</summary>
        public StubLocation Location;

        /// <summary>사건이 시작되는 조사 구간. 의뢰 데이터에서 지정한다(06 2026-11-10: 모두 아침으로 고정하지 않는다).</summary>
        public TimeBlock StartTimeBlock = TimeBlock.Morning;

        /// <summary>이 의뢰를 맡은 담당 경찰 ID(경찰 5명 중 1명, 07 확정). 의뢰 수락 시 배정한다.</summary>
        public string HandlerPoliceId;

        /// <summary>고정 테스트 사건 ID(TEST_TEMP_CASE_001 등). 생성 의뢰면 null. Editor/Development 전용.</summary>
        public string FixtureId;

        /// <summary>
        /// 03 2026-11-12 "단서 우선 배치 → 의뢰 생성 제약": 이 의뢰를 만들 때 먼저 정한 메인 사건 단서 이름. 제약 없이 만든 의뢰면 null.
        /// </summary>
        public string PlannedMainCaseClue;

        /// <summary>배치한 메인 사건 문서를 둔 조사 오브젝트(방ID:오브젝트명). 문서를 특정 오브젝트에 두지 않으면 null.</summary>
        public string PlannedClueObjectId;

        /// <summary>문서를 보유·보관한 기존 의뢰 인물과 그 역할 태그(03 2026-11-12 권력의 진실 자료 보유 경로). 실제 역할·진실과 별개다.</summary>
        public string DocumentHolderNpcId;
        public string DocumentHolderTag;

        /// <summary>고정 사건 전용: 플레이어 시작 방(null이면 사건 현장).</summary>
        public string PlayerStartRoomId;

        /// <summary>고정 사건 전용: 조사 시작 세계 시각(분). -1이면 인계일 시작 시간대 첫 시각.</summary>
        public int InvestigationStartMinuteOverride = -1;

        /// <summary>04 2026-11-12 확정: 핵심 사건 발생일(1984-10-01~12-31, 가중치 없음). yyyy-MM-dd.</summary>
        public string IncidentDate = "1984-10-01";

        /// <summary>사건 경위 시간표의 0분이 발생일의 몇 시 몇 분인지(분). 경위 시각 = 이 값 + 시간표 분.</summary>
        public int IncidentClockMinute;

        /// <summary>04 확정 경찰 선행 수사 기간: 발생일로부터 며칠 뒤 플레이어에게 인계되는지(살인 3~5, 실종 2~5, 폭행·절도 1~3, 협박 1~4).</summary>
        public int HandoverDayOffset = 1;

        public readonly List<CrimeType> SubCrimes = new List<CrimeType>();

        /// <summary><see cref="SubCrimes"/>의 각 항목이 대표 사건과 연결되는 근거.</summary>
        public readonly List<SubCrimeLink> SubCrimeLinks = new List<SubCrimeLink>();

        public ContractTruth Truth = new ContractTruth();
        public ContractExternalState ExternalState = new ContractExternalState();
        public ContractOfficialConclusion OfficialConclusion = new ContractOfficialConclusion();
        public OrganizationCondition OrgCondition = new OrganizationCondition();

        public ContractInstance(string contractId)
        {
            if (string.IsNullOrEmpty(contractId))
                throw new ArgumentException("contractId는 비어 있을 수 없습니다.", nameof(contractId));

            ContractId = contractId;
        }

        /// <summary>부수 범죄를 연결 근거와 함께 추가한다.</summary>
        public void AddSubCrime(CrimeType type, string linkReason, params string[] linkedNpcIds)
        {
            SubCrimes.Add(type);
            var link = new SubCrimeLink { Type = type, LinkReason = linkReason };
            if (linkedNpcIds != null)
                link.LinkedNpcIds.AddRange(linkedNpcIds);
            SubCrimeLinks.Add(link);
        }
    }
}
