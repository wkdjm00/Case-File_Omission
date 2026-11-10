using System;
using System.Collections.Generic;
using UnityEngine;

namespace CaseFileOmission.MainCase
{
    public enum BlankMatchMode
    {
        /// <summary>정답 목록 중 하나만 입력하면 칸이 채워진다.</summary>
        AnyOf,

        /// <summary>정답 목록을 모두 입력해야 칸이 채워진다(복수 단서 종합 칸).</summary>
        AllOf
    }

    [Serializable]
    public sealed class FinalReportAnswerEntry
    {
        public int BlankIndex;

        /// <summary>false면 정답을 정하지 않은 칸이다. 이 칸은 입력을 받지 않는다.</summary>
        public bool IsDetermined;

        public BlankMatchMode MatchMode = BlankMatchMode.AnyOf;
        public List<string> AcceptedClueNames = new List<string>();

        [TextArea] public string Basis;
        [TextArea] public string Notes;
    }

    /// <summary>
    /// 최종 보고서 14칸의 정답 단서 매핑. 데이터 자산으로 관리하므로 기획이 바뀌면 이 자산만 고치면 된다.
    /// - 3·4·6·9·10번: 기존 확정 매핑(단서표의 입증 사실과 직접 대응).
    /// - 0·1·2·5·7·8·11·12·13번: 노션 "최종 보고서 원문 및 타자기 연출" 2026-11-10 "미정 빈칸의 정답 단서 연결" 확정.
    /// - 단서명 비교 시 띄어쓰기는 무시한다(다른 글자·숫자·기호는 무시하지 않음) — 같은 페이지 "입력 판정 규칙".
    /// </summary>
    [CreateAssetMenu(fileName = "FinalReportAnswerConfig", menuName = "CaseFileOmission/MainCase/Final Report Answer Config")]
    public sealed class FinalReportAnswerConfig : ScriptableObject
    {
        [Tooltip("단서명 비교 시 띄어쓰기를 무시한다(2026-11-10 확정).")]
        public bool IgnoreWhitespaceInComparison = true;

        public List<FinalReportAnswerEntry> Entries = new List<FinalReportAnswerEntry>();

        public static FinalReportAnswerConfig CreateDefault()
        {
            var config = CreateInstance<FinalReportAnswerConfig>();
            config.IgnoreWhitespaceInComparison = true;
            config.Entries = BuildDefaultEntries();
            return config;
        }

        private static List<FinalReportAnswerEntry> BuildDefaultEntries()
        {
            FinalReportAnswerEntry Determined(int index, BlankMatchMode mode, string basis, params string[] clues) => new FinalReportAnswerEntry
            {
                BlankIndex = index,
                IsDetermined = true,
                MatchMode = mode,
                AcceptedClueNames = new List<string>(clues),
                Basis = basis
            };

            const string Decided = "노션 최종 보고서 페이지 2026-11-10 확정: ";

            return new List<FinalReportAnswerEntry>
            {
                Determined(0, BlankMatchMode.AnyOf,
                    Decided + "참가자 운영 규칙 안내문이 운영 기간(한 달)과 최종 생존자(1명)를 함께 입증한다. 한 번 입력하면 0·1번을 함께 완성한다.",
                    MainCaseClueNames.ParticipantOperatingRulesNotice),
                Determined(1, BlankMatchMode.AnyOf,
                    Decided + "0번과 같은 단서. 같은 이름을 다시 입력하도록 요구하지 않는다.",
                    MainCaseClueNames.ParticipantOperatingRulesNotice),
                Determined(2, BlankMatchMode.AnyOf,
                    Decided + "참가자 명단 전달 확인서 — 대상자 승인, 지정 이송 담당조, 거부·이탈 시에도 이송 절차를 중단하지 않는다는 지시.",
                    MainCaseClueNames.ParticipantRosterDeliveryConfirmation),
                Determined(3, BlankMatchMode.AnyOf,
                    "개인적 진실 단서표: '가족 통지·사고사 위장 관련 기록 — 가족에게 정상적인 교육·연수 과정 중 사고라고 설명', " +
                    "'조카의 사망 관련 보고서 — 가족에게 교육·연수 중 사고라고 안내하도록 지시한 내부 문서'. 두 자료가 각각 단독으로 입증한다.",
                    MainCaseClueNames.FamilyNotificationRecord, MainCaseClueNames.NieceDeathReport),
                Determined(4, BlankMatchMode.AllOf,
                    "개인적 진실 단서 연결 순서: 일기 표지의 실험 시작일(1984-10-18)과 사망 관련 보고서의 사망 처리일(1984-10-14)을 대조해 " +
                    "외부 기록이 실제 상황과 맞지 않음을 추론한다. 두 자료를 함께 대조해야 드러나는 사실이다.",
                    MainCaseClueNames.NieceDiary, MainCaseClueNames.NieceDeathReport),
                Determined(5, BlankMatchMode.AnyOf,
                    Decided + "상류층 접대·후원 기록 — 특별 관람 제공과 상류층 관계 유지·후원 협의의 연결.",
                    MainCaseClueNames.UpperClassHospitalityRecord),
                Determined(6, BlankMatchMode.AnyOf,
                    "권력의 진실 단서표: '상품 판매 정산표 — 상품 판매가 경제적 수익을 만든다'. 보고서 문장도 '기록물 및 상품의 판매는 경제적 수익을'이다.",
                    MainCaseClueNames.ProductSalesSettlement),
                Determined(7, BlankMatchMode.AllOf,
                    Decided + "종합 판단 칸. 접대·관람·상품 수익을 함께 연결하므로 세 자료를 모두 입력해야 완성한다.",
                    MainCaseClueNames.UpperClassHospitalityRecord, MainCaseClueNames.ViewingTicketRevenueSettlement,
                    MainCaseClueNames.ProductSalesSettlement),
                Determined(8, BlankMatchMode.AnyOf,
                    Decided + "정부 부서 연락 기록 — 외부 사망 처리와 내부 실험 기록의 불일치를 부서가 인지하고 기록 통제를 지시. 정부 전체로 일반화하지 않는다.",
                    MainCaseClueNames.GovernmentDepartmentContactLog),
                Determined(9, BlankMatchMode.AnyOf,
                    "권력의 진실 단서표: '상류층 접대·후원 기록 — 상류층 접대가 지지와 자금 확보로 이어진다'.",
                    MainCaseClueNames.UpperClassHospitalityRecord),
                Determined(10, BlankMatchMode.AnyOf,
                    "권력의 진실 단서표: '실험 지속 결정 기록 — 일부 관계자가 잔혹한 실상을 인지한 뒤에도 이익 때문에 실험을 지속했다'.",
                    MainCaseClueNames.ExperimentContinuationDecisionRecord),
                Determined(11, BlankMatchMode.AnyOf,
                    Decided + "반란 세력 내부 회람 메모 — 실험 관련 자료 수집과 협력자 접촉이 명시된 반란 세력 내부 문서.",
                    MainCaseClueNames.RebelInternalCircularMemo),
                Determined(12, BlankMatchMode.AnyOf,
                    Decided + "반란 세력 내부 회람 메모 — 확실한 종식 계획이 완성되었다고 알리지 말라고 명시. 11번과 같은 단서로 두 칸을 함께 완성한다.",
                    MainCaseClueNames.RebelInternalCircularMemo),
                Determined(13, BlankMatchMode.AllOf,
                    Decided + "반란 세력 내부 회람 메모 + 정부 부서 연락 기록. 설계자 이름을 맞히는 칸이 아니라, 현재 자료로 신원·동기를 단정할 수 없다는 판단을 뒷받침한다.",
                    MainCaseClueNames.RebelInternalCircularMemo, MainCaseClueNames.GovernmentDepartmentContactLog),
            };
        }
    }
}
