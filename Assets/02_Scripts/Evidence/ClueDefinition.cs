using System;

namespace CaseFileOmission.Evidence
{
    /// <summary>
    /// 단서의 고정 정의. 04페이지 "모든 단서는 공통적으로 다음 정보를 관리한다" 기준.
    /// 현재는 의뢰마다 절차적으로 생성되는 런타임 데이터로 다루며(ScriptableObject 자산이 아님),
    /// 추후 재사용 가능한 템플릿이 필요해지면 고정 데이터로 전환할 수 있다.
    /// </summary>
    [Serializable]
    public sealed class ClueDefinition
    {
        public string ClueId { get; }
        public string ContractId { get; }
        public ClueFormat Format;
        public ClueTruthStatus TruthStatus = ClueTruthStatus.Factual;

        /// <summary>플레이어에게 표시되는 이름. clueId와 별개로 관리한다 (06페이지 "최소 데이터 계약").</summary>
        public string DisplayName;

        /// <summary>플레이어에게 표시되는 내용.</summary>
        public string DisplayedContent;

        /// <summary>이 단서가 실제로 뒷받침하는 사실에 대한 설명(자유 텍스트, 콘텐츠 작성 단계에서 구체화).</summary>
        public string RelatedTruthNote;

        public string SourceDescription;
        public string AcquisitionCondition;

        /// <summary>이 단서가 가해자를 특정하는 데 쓰이는지 여부. 05페이지 조직_02 지원단서 조건
        /// ("가해자를 특정할 단서 2개를 숨긴 채 사건 종료")을 판정하는 데 사용한다.</summary>
        public bool IdentifiesPerpetrator;

        /// <summary>물리 자료가 가해 행위를 직접 뒷받침하는지. ID 문자열 규칙에 의존하지 않는다.</summary>
        public bool IsDirectPerpetratorEvidence;

        public ClueDefinition(string clueId, string contractId)
        {
            if (string.IsNullOrEmpty(clueId))
                throw new ArgumentException("clueId는 비어 있을 수 없습니다.", nameof(clueId));
            if (string.IsNullOrEmpty(contractId))
                throw new ArgumentException("contractId는 비어 있을 수 없습니다.", nameof(contractId));

            ClueId = clueId;
            ContractId = contractId;
        }
    }
}
