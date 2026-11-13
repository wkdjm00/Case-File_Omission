using System;
using System.Collections.Generic;
using System.Linq;

namespace CaseFileOmission.Reports
{
    /// <summary>04 「무작위」 2026-11-12 보고서 슬롯 상태. 문단 공개 여부(Unlocked)와 슬롯 완료 상태는 따로 저장한다.</summary>
    public enum ReportSlotStatus
    {
        Unanswered,
        AnswerPending,
        Incorrect,
        Partial,
        CorrectUnsupported,
        NeedsClarification,
        Completed
    }

    /// <summary>핵심 문장이 반드시 담아야 하는 개념 하나. 동의어 중 하나만 들어 있으면 충족(공백 무시).</summary>
    public sealed class ReportConcept
    {
        public string Label;
        public readonly List<string> Synonyms = new List<string>();
    }

    /// <summary>
    /// 보고서 목표 하나 = 문단 하나 = 핵심 문장 슬롯 하나(04 확정). 증거 묶음은 묶음 안 AND, 묶음 사이 OR.
    /// 해금: 첫 문단은 처음부터, 이후는 (해금 증거 묶음 하나 완성) OR (앞 문단 핵심 문장 completed).
    /// </summary>
    public sealed class ReportTarget
    {
        public int Index;
        public string Category;
        public string Question;
        public string CommonText;
        public string FollowUpHint;
        public string Key;
        public readonly List<ReportConcept> Concepts = new List<ReportConcept>();
        public readonly List<string> ContradictionTerms = new List<string>();
        public readonly List<List<string>> EvidenceGroups = new List<List<string>>();
        public readonly List<List<string>> UnlockGroups = new List<List<string>>();
    }

    [Serializable]
    public sealed class ReportSlotState
    {
        public int Index;
        public bool Unlocked;
        public ReportSlotStatus Status;
        public string LastAnswer;
    }

    /// <summary>
    /// 자연어 핵심 문장 판정(04 V-13: 내용 정확성과 획득 증거 충분성을 따로 검증, 둘 다 통과해야 완료).
    /// AI 검증(REPORT_ANSWER_VALIDATION) 연동 전의 결정적 검증기 — 아무 입력이나 통과시키지 않는다.
    /// 판정 순서(확정): 모호 → 모순 → 증거 부족 → 일부 → 전부. 획득하지 않은 단서는 증거로 치지 않는다.
    /// </summary>
    public static class NarrativeReportJudge
    {
        // 04 「무작위」 2026-11-12 피드백 문구 6종(원문).
        public const string FeedbackCorrectSupported = "작성 내용과 확보한 단서가 서로 맞습니다. 다음 조사 내용을 확인할 수 있습니다.";
        public const string FeedbackCorrectUnsupported = "작성 내용은 현재 기준과 일치하지만, 이를 뒷받침할 단서가 부족합니다. 관련 단서를 더 확보해 주세요.";
        public const string FeedbackPartial = "작성 내용 중 일부만 확인됩니다. 누락되거나 맞지 않는 부분을 다시 살펴보고 답변을 보완해 주세요.";
        public const string FeedbackIncorrect = "현재 작성 내용은 보고서의 판단 기준을 충족하지 못했습니다. 확보한 단서를 다시 검토하고 답변을 수정해 주세요.";
        public const string FeedbackNeedsClarification = "작성한 문장의 의미를 명확하게 판단하기 어렵습니다. 누구의 어떤 행동이나 사실을 말하는지 더 구체적으로 작성해 주세요.";
        public const string FeedbackValidationFailed = "답변을 확인하지 못했습니다. 아직 정오답이 판정되지 않았습니다. 다시 제출해 주세요.";

        public const int MinimumAnswerLength = 4;

        public static ReportSlotStatus Judge(ReportTarget target, string answer, ICollection<string> acquiredClueIds)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            string text = Normalize(answer);
            if (text.Length < MinimumAnswerLength)
                return ReportSlotStatus.NeedsClarification;

            if (target.ContradictionTerms.Any(term => text.Contains(Normalize(term))))
                return ReportSlotStatus.Incorrect;

            int matched = target.Concepts.Count(c => c.Synonyms.Any(s => text.Contains(Normalize(s))));
            if (matched == 0)
                return ReportSlotStatus.Incorrect;
            if (matched < target.Concepts.Count)
                return ReportSlotStatus.Partial;

            bool supported = target.EvidenceGroups.Any(g => g.Count > 0 && g.All(acquiredClueIds.Contains));
            return supported ? ReportSlotStatus.Completed : ReportSlotStatus.CorrectUnsupported;
        }

        /// <summary>
        /// AI 내용 판정(REPORT_ANSWER_VALIDATION)을 쓰는 경로. AI는 내용(정확성)만 판정하고, 증거 충분성은 시스템이 획득 단서로 따로 검사한다.
        /// 3회 안에 유효한 응답이 없으면 정오답을 정하지 않고 AnswerPending(판정 보류)을 돌려준다.
        /// 응답 형식: {"result_status": "correct" | "partial" | "incorrect" | "needs_clarification"}.
        /// </summary>
        public static ReportSlotStatus JudgeWithAi(ReportTarget target, string answer, ICollection<string> acquiredClueIds,
            Core.IAiContentProvider provider, Core.GameLogger logger = null, string contractId = null)
        {
            if (provider == null) return Judge(target, answer, acquiredClueIds);
            string request = "{\"question\":\"" + Escape(target.Question) + "\",\"category\":\"" + Escape(target.Category)
                             + "\",\"required_concepts\":[" + string.Join(",", target.Concepts.Select(c => "\"" + Escape(c.Label) + "\""))
                             + "],\"answer\":\"" + Escape(answer) + "\"}";
            var result = Core.AiStageRunner.Run(provider, Core.AiStage.ReportAnswerValidation, request, response =>
            {
                string status = Core.AiStageRunner.ReadStringField(response, "result_status");
                return status switch
                {
                    "correct" => (true, ReportSlotStatus.Completed),
                    "partial" => (true, ReportSlotStatus.Partial),
                    "incorrect" => (true, ReportSlotStatus.Incorrect),
                    "needs_clarification" => (true, ReportSlotStatus.NeedsClarification),
                    _ => (false, ReportSlotStatus.AnswerPending)
                };
            }, logger, contractId);

            if (!result.Success) return ReportSlotStatus.AnswerPending;
            if (result.Value != ReportSlotStatus.Completed) return result.Value;
            bool supported = target.EvidenceGroups.Any(g => g.Count > 0 && g.All(acquiredClueIds.Contains));
            return supported ? ReportSlotStatus.Completed : ReportSlotStatus.CorrectUnsupported;
        }

        private static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

        public static string Feedback(ReportSlotStatus status) => status switch
        {
            ReportSlotStatus.Completed => FeedbackCorrectSupported,
            ReportSlotStatus.CorrectUnsupported => FeedbackCorrectUnsupported,
            ReportSlotStatus.Partial => FeedbackPartial,
            ReportSlotStatus.Incorrect => FeedbackIncorrect,
            ReportSlotStatus.NeedsClarification => FeedbackNeedsClarification,
            ReportSlotStatus.AnswerPending => FeedbackValidationFailed,
            _ => ""
        };

        /// <summary>공개 상태를 다시 계산한다. 한 번 공개된 문단은 닫지 않는다.</summary>
        public static void UpdateUnlocks(IReadOnlyList<ReportTarget> targets, ContractReportState report, ICollection<string> acquiredClueIds)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                var slot = report.GetOrAddSlot(targets[i].Index);
                if (slot.Unlocked) continue;
                bool previousDone = i > 0 && report.GetOrAddSlot(targets[i - 1].Index).Status == ReportSlotStatus.Completed;
                bool evidence = targets[i].UnlockGroups.Any(g => g.Count > 0 && g.All(acquiredClueIds.Contains));
                slot.Unlocked = i == 0 || previousDone || evidence;
            }
        }

        private static string Normalize(string s) => new string((s ?? "").Where(ch => !char.IsWhiteSpace(ch) && ch != '.' && ch != ',').ToArray());
    }
}
