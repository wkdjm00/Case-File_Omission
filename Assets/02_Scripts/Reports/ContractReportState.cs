using System;
using System.Collections.Generic;

namespace CaseFileOmission.Reports
{
    [Serializable]
    public sealed class ReportSectionEntry
    {
        public string Content;
        public readonly List<string> LinkedClueIds = new List<string>();

        /// <summary>
        /// Perpetrator 섹션처럼 "누구를 지목하는지"가 구조적으로 비교 가능해야 하는 항목에서 사용한다.
        /// 다른 섹션에서는 비워 둔다. 자유 서술(Content)과 별도로 둔 이유는 §9-3의
        /// "실제 진실과 공식 결론 비교"를 문자열 파싱 없이 수행하기 위해서다.
        /// </summary>
        public string ClaimedNpcId;

        public bool HasContent => !string.IsNullOrWhiteSpace(Content);
    }

    /// <summary>
    /// 의뢰 사건 보고서 하나의 런타임 상태. 섹션별로 내용과 근거 단서를 연결한다
    /// (04페이지: "자유 서술 입력이나 단서 이름 직접 입력을 기본 방식으로 추가하지 않는다").
    /// </summary>
    [Serializable]
    public sealed class ContractReportState
    {
        public string ContractId { get; }
        public bool IsSubmitted { get; private set; }

        private readonly Dictionary<ReportSection, ReportSectionEntry> _sections =
            new Dictionary<ReportSection, ReportSectionEntry>();

        public ContractReportState(string contractId)
        {
            if (string.IsNullOrEmpty(contractId))
                throw new ArgumentException("contractId는 비어 있을 수 없습니다.", nameof(contractId));

            ContractId = contractId;
            foreach (ReportSection section in Enum.GetValues(typeof(ReportSection)))
                _sections[section] = new ReportSectionEntry();
        }

        public ReportSectionEntry GetSection(ReportSection section) => _sections[section];

        public void SetContent(ReportSection section, string content) => _sections[section].Content = content;

        public void SetClaimedNpcId(ReportSection section, string npcId) => _sections[section].ClaimedNpcId = npcId;

        public void LinkClue(ReportSection section, string clueId)
        {
            if (string.IsNullOrEmpty(clueId))
                throw new ArgumentException("clueId는 비어 있을 수 없습니다.", nameof(clueId));

            var entry = _sections[section];
            if (!entry.LinkedClueIds.Contains(clueId))
                entry.LinkedClueIds.Add(clueId);
        }

        public bool UnlinkClue(ReportSection section, string clueId) => _sections[section].LinkedClueIds.Remove(clueId);

        /// <summary>자연어 핵심 문장 슬롯 상태(04 V-13). 목표 자체는 의뢰 데이터에서 다시 만들 수 있어 저장하지 않는다.</summary>
        public readonly List<ReportSlotState> Slots = new List<ReportSlotState>();

        public ReportSlotState GetOrAddSlot(int index)
        {
            var slot = Slots.Find(s => s.Index == index);
            if (slot == null)
            {
                slot = new ReportSlotState { Index = index };
                Slots.Add(slot);
            }
            return slot;
        }

        public void MarkSubmitted() => IsSubmitted = true;
    }
}
