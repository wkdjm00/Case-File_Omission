using System.Collections.Generic;

namespace CaseFileOmission.MainCase
{
    /// <summary>메인 사건 문서 한 건의 표시 정보(문서명·문서 번호·날짜·작성 부서·발견 장소). 본문은 화면 계층이 따로 가진다.</summary>
    public sealed class MainCaseDocumentInfo
    {
        /// <summary>진행 추적 키(정식 단서명 또는 같은 문서명의 다른 판본을 구분하는 키).</summary>
        public string ClueName;

        /// <summary>화면에 보이는 문서명. 판본이 달라도 같은 이름일 수 있다.</summary>
        public string Title;
        public string DocumentNumber;
        public string WrittenDate;
        public string DistributionConfirmedDate;
        public string Department;

        /// <summary>발견 장소(기획 기준). 실제 배치·해금 조건은 기존 기획이 정해질 때 적용한다.</summary>
        public string FoundAt;

        /// <summary>true면 단계 진행 필수 단서, false면 보조 조사 단서.</summary>
        public bool IsStageClue;
    }

    /// <summary>
    /// 같은 문서명을 가진 판본을 서로 다른 문서로 구분하는 표시 정보.
    /// 2026-11-16 사용자 지정 「참가자 운영 규칙 안내문 — 게임 적용 기준」:
    /// 특조-84-1017-04는 참가자에게 실제로 배포된 공식 안내문(주요 조사 단서), 운영-규칙-84-01은 운영 담당자가 보관한 내부 상세 규정(보조 조사 단서).
    /// 두 문서는 서로 다른 조사 오브젝트·문서 번호·본문·발견 장소를 가지며, 내부 상세본은 참가자 침상에 배포하지 않는다.
    /// 발견 조건과 해금 순서는 기존 기획(실험의 진실 자료 배치)이 정해지면 적용한다 — 여기서 새 조건을 만들지 않는다.
    /// </summary>
    public static class MainCaseDocumentCatalog
    {
        public static readonly IReadOnlyDictionary<string, MainCaseDocumentInfo> Infos = new Dictionary<string, MainCaseDocumentInfo>
        {
            [MainCaseClueNames.ParticipantOperatingRulesNotice] = new MainCaseDocumentInfo
            {
                ClueName = MainCaseClueNames.ParticipantOperatingRulesNotice,
                Title = "참가자 운영 규칙 안내문",
                DocumentNumber = "특조-84-1017-04",
                WrittenDate = "1984년 10월 17일",
                DistributionConfirmedDate = "1984년 10월 18일",
                Department = "국가보안국 특별조사과",
                FoundAt = "실험 구역 입구 안내판 또는 참가자 침상 옆 서류함",
                IsStageClue = true
            },
            [MainCaseClueNames.ParticipantOperatingRulesInternal] = new MainCaseDocumentInfo
            {
                ClueName = MainCaseClueNames.ParticipantOperatingRulesInternal,
                Title = "참가자 운영 규칙 안내문",
                DocumentNumber = "운영-규칙-84-01",
                FoundAt = "운영 담당자 사무실 또는 내부 문서 보관 구역",
                IsStageClue = false
            },
            // 2026-11-16 최종 확정: 실험 자료는 기존에 지정된 장소·획득 조건을 그대로 쓰고 새 장소를 만들지 않는다.
            [MainCaseClueNames.StudentInfoAcquisitionMemo] = new MainCaseDocumentInfo
            {
                ClueName = MainCaseClueNames.StudentInfoAcquisitionMemo,
                Title = "학생 정보 확보 메모",
                DocumentNumber = "특조-84-0928-12",
                FoundAt = "후보자 선정 서류철 안쪽",
                IsStageClue = true
            },
            [MainCaseClueNames.SurvivorHandoverRecord] = new MainCaseDocumentInfo
            {
                ClueName = MainCaseClueNames.SurvivorHandoverRecord,
                Title = "생존자 인계 기록",
                DocumentNumber = "특조-84-1119-03",
                FoundAt = "다음 회차 운영 담당자 서류함",
                IsStageClue = true
            },
        };

        /// <summary>목록에 보일 제목. 판본 정보가 있으면 "문서명 (문서 번호)", 없으면 단서명.</summary>
        public static string DisplayTitle(string clueName) =>
            Infos.TryGetValue(clueName, out var info) ? $"{info.Title} ({info.DocumentNumber})" : clueName;

        /// <summary>본문 위에 따로 보일 문서 정보(문서 번호·작성일·배포 확인일·작성 부서). 정보가 없으면 빈 문자열.</summary>
        public static string Header(string clueName)
        {
            if (!Infos.TryGetValue(clueName, out var info)) return string.Empty;
            var lines = new List<string> { $"문서명: {info.Title}", $"문서 번호: {info.DocumentNumber}" };
            if (info.WrittenDate != null) lines.Add($"문서 작성일: {info.WrittenDate}");
            if (info.DistributionConfirmedDate != null) lines.Add($"배포 확인일: {info.DistributionConfirmedDate}");
            if (info.Department != null) lines.Add($"작성 부서: {info.Department}");
            return string.Join("\n", lines);
        }
    }
}
