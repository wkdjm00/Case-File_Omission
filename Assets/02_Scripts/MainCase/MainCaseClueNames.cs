using System.Collections.Generic;

namespace CaseFileOmission.MainCase
{
    /// <summary>
    /// 노션 "03 — 메인 사건" 페이지에서 확인한 실제 단서명이다(이번 대화 세션에서 직접 조회한 원문
    /// 기준, 2026-11-06 버전). 지시서 원칙(§10 "단서명은 원본 기획과 정확히 일치시킨다")에 따라
    /// 이름을 바꾸거나 새로 만들지 않는다. 이 목록을 전체 단서 목록으로 간주하지 말 것 — 노션
    /// 페이지가 갱신되었을 수 있으므로 실제 구현 직전에 재대조가 필요하다(완료 기준에서 별도 표시).
    /// 가해자_01~05 명칭은 통합 명세서가 제시한 요약 표현이며 노션 원문과 다르므로 사용하지 않는다.
    /// </summary>
    public static class MainCaseClueNames
    {
        public const string NieceDiary = "조카의 일기";
        public const string NieceDeathReport = "조카의 사망 관련 보고서";

        /// <summary>
        /// 노션 "02 — 스토리" 단서 목록의 단서명 열 표기. 같은 자료의 게임 문서 제목은
        /// "가족 통지 및 사고 처리 기록"으로 다르게 표기되어 있어 그 표기는 별칭으로 받는다.
        /// </summary>
        public const string FamilyNotificationRecord = "가족 통지·사고사 위장 관련 기록";

        /// <summary>03 개인적 진실 하위 문서(2026-11-13): 프롤로그 자료 묶음의 기본 보고서. 단계 필수 단서가 아닌 정보용 문서.</summary>
        public const string BloodiedRebelReport = "피 묻은 익명 반란 세력 내부 보고서";
        public const string FamilyDeathReport = "형제자매 가족 사망·사고 처리 보고서";
        public const string BloodiedRebelReportId = "EVIDENCE_BLOODIED_REBEL_REPORT";
        public const string FamilyDeathReportId = "EVIDENCE_FAMILY_DEATH_REPORT";

        public const string GovernmentDepartmentContactLog = "정부 부서 연락 기록";
        public const string SubjectManagementTable = "실험 대상자 관리표";
        public const string ResultReportRecording = "실험 결과 보고 녹음";
        public const string ParticipantRosterDeliveryConfirmation = "참가자 명단 전달 확인서";
        public const string SeniorOfficialLetter = "고위 관계자 서신";
        public const string RepeatedExperimentNumberList = "반복 실험 번호 목록";
        public const string NumberNoteLeftForLover = "연인에게 남긴 번호 쪽지";
        public const string SubjectNumberFile = "대상자 번호 파일";
        public const string CaseAssignmentMemo = "의뢰 배정 메모";
        public const string AdditionalMaterialDeliveryConfirmation = "추가 자료 전달 확인서";

        public const string ProductPurchaseWishList = "상품 구매 희망자 명단";
        public const string ProductSalesSettlement = "상품 판매 정산표";
        public const string GovernmentSupportRewardLetter = "정부 지지 보상 서신";
        public const string ExperimentCompositionDealProposal = "실험 구성 거래 제안서";
        public const string ViewingTicketRevenueSettlement = "관람권 수입 정산표";
        public const string UpperClassHospitalityRecord = "상류층 접대·후원 기록";
        public const string ExperimentContinuationDecisionRecord = "실험 지속 결정 기록";
        public const string CollaboratorRewardDissenterPenaltyRecord = "협력자 보상·반대자 불이익 기록";

        /// <summary>참가자에게 배포된 공식 안내문(특조-84-1017-04). 실험의 진실 필수 단서이자 최종 보고서 0·1번 빈칸의 정답 단서명.</summary>
        public const string ParticipantOperatingRulesNotice = "참가자 운영 규칙 안내문";

        /// <summary>
        /// 운영 담당자가 보관한 내부 상세 규정(운영-규칙-84-01, 보조 조사 단서). 화면 문서명은 같은 "참가자 운영 규칙 안내문"이지만
        /// 배포본과 다른 문서이므로 진행 추적 키를 따로 둔다. 단계 필수 단서가 아니다(2026-11-16 사용자 지정 게임 적용 기준).
        /// </summary>
        public const string ParticipantOperatingRulesInternal = "참가자 운영 규칙 안내문#운영-규칙-84-01";
        public const string StudentInfoAcquisitionMemo = "학생 정보 확보 메모";
        public const string SurvivorHandoverRecord = "생존자 인계 기록";

        /// <summary>
        /// 최종 보고서 11·12·13번 빈칸의 정답 단서(노션 최종 보고서 페이지 2026-11-10). 게임 문서 원문은 보관본
        /// "최종 설계자와 궁극적 목표" 아래에 있으며(보관 위치: 반란 세력 연락 거점의 서류 봉투), 현재 기준 문서에
        /// 획득 조건이 없어 어느 진실 단계의 필수 단서 목록에도 넣지 않는다.
        /// </summary>
        public const string RebelInternalCircularMemo = "반란 세력 내부 회람 메모";

        public static readonly IReadOnlyCollection<string> PersonalTruthClues = new[]
        {
            NieceDiary, NieceDeathReport, FamilyNotificationRecord
        };

        public static readonly IReadOnlyCollection<string> SystemicTruthClues = new[]
        {
            GovernmentDepartmentContactLog, SubjectManagementTable, ResultReportRecording,
            ParticipantRosterDeliveryConfirmation, SeniorOfficialLetter, RepeatedExperimentNumberList,
            NumberNoteLeftForLover, SubjectNumberFile, CaseAssignmentMemo, AdditionalMaterialDeliveryConfirmation
        };

        public static readonly IReadOnlyCollection<string> PowerTruthClues = new[]
        {
            ProductPurchaseWishList, ProductSalesSettlement, GovernmentSupportRewardLetter,
            ExperimentCompositionDealProposal, ViewingTicketRevenueSettlement, UpperClassHospitalityRecord,
            ExperimentContinuationDecisionRecord, CollaboratorRewardDissenterPenaltyRecord
        };

        public static readonly IReadOnlyCollection<string> ExperimentTruthClues = new[]
        {
            ParticipantOperatingRulesNotice, StudentInfoAcquisitionMemo, SurvivorHandoverRecord
        };

        /// <summary>
        /// 설계자의 진실 단계는 노션 원문이 "최종 설계자를 특정하는 별도 단서나 증거 조합은 없다"고
        /// 명시적으로 확정했으므로 의도적으로 비워 둔다(수화기 장면으로 대신 공개됨).
        /// </summary>
        public static readonly IReadOnlyCollection<string> DesignerTruthClues = System.Array.Empty<string>();

        /// <summary>
        /// 노션 원문에서 같은 자료를 가리키지만 표기가 다른 이름(별칭 → 정식 단서명).
        /// 원문에 실제로 존재하는 표기만 넣는다.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>
        {
            ["가족 통지 및 사고 처리 기록"] = FamilyNotificationRecord,
            ["조카의 일기 원문"] = NieceDiary,
            ["조카의 사망 관련 보고서 원문"] = NieceDeathReport,
        };

        /// <summary>입력값을 정규화한 뒤 별칭이면 정식 단서명으로 바꾼다. 별칭이 아니면 정규화된 입력을 그대로 돌려준다.</summary>
        public static string ResolveCanonical(string input, bool ignoreWhitespace = false)
        {
            string normalized = ClueNameNormalizer.Normalize(input, ignoreWhitespace);
            foreach (var pair in Aliases)
            {
                if (ClueNameNormalizer.Normalize(pair.Key, ignoreWhitespace) == normalized)
                    return ClueNameNormalizer.Normalize(pair.Value, ignoreWhitespace);
            }

            return normalized;
        }
    }
}
