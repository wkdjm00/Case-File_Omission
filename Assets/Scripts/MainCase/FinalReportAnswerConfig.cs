using System;
using System.Collections.Generic;
using UnityEngine;

namespace CaseFileOmission.MainCase
{
    public enum BlankMatchMode
    {
        /// <summary>정답 목록 중 하나만 입력하면 칸이 채워진다.</summary>
        AnyOf,

        /// <summary>정답 목록을 모두 입력해야 칸이 채워진다(노션: "빈칸을 채울 때 여러 단서가 함께 필요할 수 있다").</summary>
        AllOf
    }

    [Serializable]
    public sealed class FinalReportAnswerEntry
    {
        public int BlankIndex;

        /// <summary>false면 원문 근거가 없어 정답을 정하지 않은 칸이다. 이 칸은 입력을 받지 않는다.</summary>
        public bool IsDetermined;

        public BlankMatchMode MatchMode = BlankMatchMode.AnyOf;
        public List<string> AcceptedClueNames = new List<string>();

        [TextArea] public string Basis;
        [TextArea] public string Notes;
    }

    /// <summary>
    /// 최종 보고서 14칸의 정답 단서 매핑. 데이터 자산으로 관리하므로 기획이 확정되면 이 자산만 고치면 된다.
    ///
    /// 기본값 산출 규칙: 노션 단서표의 "자료가 보여 주는 사실 / 입증하는 핵심"이 칸의 문구와 직접 일치하는
    /// 단서만 정답으로 넣었다. 한 단서가 단독으로 그 사실을 입증하면 AnyOf, 두 자료를 대조해야만
    /// 드러나는 사실(불일치)이면 AllOf로 두었다. 단계 수준의 연결 설명만 있는 칸은 미결정으로 남겼다.
    /// </summary>
    [CreateAssetMenu(fileName = "FinalReportAnswerConfig", menuName = "CaseFileOmission/MainCase/Final Report Answer Config")]
    public sealed class FinalReportAnswerConfig : ScriptableObject
    {
        [Tooltip("띄어쓰기를 완전히 무시하고 비교할지 여부. 원문에 정해져 있지 않아 기본값은 false(연속 공백만 정리).")]
        public bool IgnoreWhitespaceInComparison;

        public List<FinalReportAnswerEntry> Entries = new List<FinalReportAnswerEntry>();

        public static FinalReportAnswerConfig CreateDefault()
        {
            var config = CreateInstance<FinalReportAnswerConfig>();
            config.Entries = BuildDefaultEntries();
            return config;
        }

        private static List<FinalReportAnswerEntry> BuildDefaultEntries()
        {
            FinalReportAnswerEntry Undetermined(int index, string basis, string notes) => new FinalReportAnswerEntry
            {
                BlankIndex = index,
                IsDetermined = false,
                Basis = basis,
                Notes = notes
            };

            FinalReportAnswerEntry Determined(int index, BlankMatchMode mode, string basis, params string[] clues) => new FinalReportAnswerEntry
            {
                BlankIndex = index,
                IsDetermined = true,
                MatchMode = mode,
                AcceptedClueNames = new List<string>(clues),
                Basis = basis
            };

            const string OperatingRulesCandidate =
                "후보: 참가자 운영 규칙 안내문(입증 사실: 운영 기간 약 한 달, 최종 생존자 한 명). " +
                "0번과 1번은 같은 문장에 있어 같은 단서가 두 칸의 정답이 되는데, 원문 연출은 '입력한 단어는 떠다니는 영역에서 사라진다'고 하므로 " +
                "같은 단어를 두 번 쓰는 구조인지 확정이 필요하다.";

            return new List<FinalReportAnswerEntry>
            {
                Undetermined(0, "실험의 진실 — 단서 연결 순서 1", OperatingRulesCandidate),
                Undetermined(1, "실험의 진실 — 단서 연결 순서 1", OperatingRulesCandidate),
                Undetermined(2,
                    "체계의 진실 '최종 보고서 연결'은 단계 전체가 이 칸을 뒷받침한다고만 적혀 있다.",
                    "체계의 진실 단서 10개 중 '강제 모집·이송의 주체'를 직접 입증한다고 적힌 단서가 없다."),
                Determined(3, BlankMatchMode.AnyOf,
                    "개인적 진실 단서표: '가족 통지·사고사 위장 관련 기록 — 가족에게 정상적인 교육·연수 과정 중 사고라고 설명', " +
                    "'조카의 사망 관련 보고서 — 가족에게 교육·연수 중 사고라고 안내하도록 지시한 내부 문서'. 두 자료가 각각 단독으로 입증한다.",
                    MainCaseClueNames.FamilyNotificationRecord, MainCaseClueNames.NieceDeathReport),
                Determined(4, BlankMatchMode.AllOf,
                    "개인적 진실 단서 연결 순서: 일기 표지의 실험 시작일(1984-10-18)과 사망 관련 보고서의 사망 처리일(1984-10-14)을 대조해 " +
                    "외부 기록이 실제 상황과 맞지 않음을 추론한다. 두 자료를 함께 대조해야 드러나는 사실이다.",
                    MainCaseClueNames.NieceDiary, MainCaseClueNames.NieceDeathReport),
                Undetermined(5,
                    "권력의 진실 '최종 보고서 연결'은 자료 전체가 '오락·접대 성격'을 뒷받침한다고만 적혀 있다.",
                    "후보: 상류층 접대·후원 기록(접대), 관람권 수입 정산표(관람). 어느 쪽이 정답인지 원문에 없다."),
                Determined(6, BlankMatchMode.AnyOf,
                    "권력의 진실 단서표: '상품 판매 정산표 — 상품 판매가 경제적 수익을 만든다'. 보고서 문장도 '기록물 및 상품의 판매는 경제적 수익을'이다.",
                    MainCaseClueNames.ProductSalesSettlement),
                Undetermined(7,
                    "원문 단서표 중 이 문장을 직접 입증한다고 적힌 단서가 없다.",
                    "종합 판단 성격의 문장이다."),
                Undetermined(8,
                    "권력의 진실 '최종 보고서 연결'은 자료 전체가 뒷받침한다고만 적혀 있다.",
                    "후보: 정부 부서 연락 기록(기록 담당 주석 — 담당 부서가 불일치를 인지). '운영과 은폐' 전체를 단독 입증한다고 적힌 단서는 없다."),
                Determined(9, BlankMatchMode.AnyOf,
                    "권력의 진실 단서표: '상류층 접대·후원 기록 — 상류층 접대가 지지와 자금 확보로 이어진다'.",
                    MainCaseClueNames.UpperClassHospitalityRecord),
                Determined(10, BlankMatchMode.AnyOf,
                    "권력의 진실 단서표: '실험 지속 결정 기록 — 일부 관계자가 잔혹한 실상을 인지한 뒤에도 이익 때문에 실험을 지속했다'. " +
                    "보고서 문장('잔혹한 실체를 인지한 뒤에도 중단하지 않았다. 그 이유는 정치적·경제적 이익')과 직접 대응한다.",
                    MainCaseClueNames.ExperimentContinuationDecisionRecord),
                Undetermined(11,
                    "03 메인 사건에 '익명의 피 묻은 반란군 내부 보고서'가 언급되지만 단서표에 정식 단서명이 없다.",
                    "정식 단서명이 정해지면 이 칸에 연결할 수 있다."),
                Undetermined(12,
                    "반란 세력의 계획을 확정할 수 없다는 문장이며, 이를 입증하는 단서가 단서표에 없다.",
                    "확정 필요."),
                Undetermined(13,
                    "03 메인 사건 확정 규칙: '최종 설계자를 특정하는 별도 단서나 증거 조합은 없다'.",
                    "빈칸은 단서명 입력 방식인데 이 칸에 해당하는 단서가 없다고 확정되어 있어 서로 충돌한다. 결정 필요."),
            };
        }
    }
}
