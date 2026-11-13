using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Contracts;
using CaseFileOmission.Rooms;

namespace CaseFileOmission.ContractContent
{
    /// <summary>고정 선택지 하나(ID + 한국어 표시명).</summary>
    public sealed class CatalogEntry
    {
        public string Id;
        public string Name;

        public CatalogEntry(string id, string name)
        {
            Id = id;
            Name = name;
        }
    }

    /// <summary>
    /// 04 확정 고정 선택지 표(원문 그대로): 방법 METHOD-01~17, 도구 TOOL-00~05, 동기 MOT-01~08, 관계 REL-01~07,
    /// 은폐 CONCEAL-00~06, 대표/실제 세부 종류, 직업 12종, 방별 조사 오브젝트 후보(2026-11-11).
    /// </summary>
    public static class CaseCatalog
    {
        public static readonly CatalogEntry[] Methods =
        {
            new CatalogEntry("METHOD-01", "맨손·신체를 이용한 공격"),
            new CatalogEntry("METHOD-02", "날붙이를 이용한 공격"),
            new CatalogEntry("METHOD-03", "둔기로 공격"),
            new CatalogEntry("METHOD-04", "총기를 이용한 공격"),
            new CatalogEntry("METHOD-05", "목 조름·질식"),
            new CatalogEntry("METHOD-06", "독성 물질을 이용한 중독"),
            new CatalogEntry("METHOD-07", "추락하도록 유도"),
            new CatalogEntry("METHOD-08", "응급조치 방해·의도적 방치"),
            new CatalogEntry("METHOD-09", "유인·기망"),
            new CatalogEntry("METHOD-10", "강제 이동·감금"),
            new CatalogEntry("METHOD-11", "몰래 침입해 절취"),
            new CatalogEntry("METHOD-12", "접근 권한을 악용한 절취"),
            new CatalogEntry("METHOD-13", "기회를 이용한 절취"),
            new CatalogEntry("METHOD-14", "속임수를 이용한 물건 취득"),
            new CatalogEntry("METHOD-15", "대면 위협"),
            new CatalogEntry("METHOD-16", "서신·전화 등 통신을 통한 위협"),
            new CatalogEntry("METHOD-17", "제3자를 통한 위협"),
        };

        public static readonly CatalogEntry[] Tools =
        {
            new CatalogEntry("TOOL-00", "도구 없음"),
            new CatalogEntry("TOOL-01", "칼·날붙이"),
            new CatalogEntry("TOOL-02", "둔기형 물체"),
            new CatalogEntry("TOOL-03", "총기"),
            new CatalogEntry("TOOL-04", "끈·줄"),
            new CatalogEntry("TOOL-05", "독성 물질"),
        };

        public static readonly CatalogEntry[] Motives =
        {
            new CatalogEntry("MOT-01", "금전·재산상 이익"),
            new CatalogEntry("MOT-02", "원한·복수"),
            new CatalogEntry("MOT-03", "관계 갈등"),
            new CatalogEntry("MOT-04", "비밀·과거 은폐"),
            new CatalogEntry("MOT-05", "지위·권한·업무상 이해관계"),
            new CatalogEntry("MOT-06", "두려움·자기보호·궁지"),
            new CatalogEntry("MOT-07", "욕망·집착"),
            new CatalogEntry("MOT-08", "기타 개인적 동기"),
        };

        /// <summary>REL-07(서로 모르는 사이)은 다른 관계와 같이 쓰지 않는다.</summary>
        public static readonly CatalogEntry[] Relationships =
        {
            new CatalogEntry("REL-01", "가족"),
            new CatalogEntry("REL-02", "연인·부부"),
            new CatalogEntry("REL-03", "친구·지인"),
            new CatalogEntry("REL-04", "이웃·같은 생활권 관계"),
            new CatalogEntry("REL-05", "직장 관계"),
            new CatalogEntry("REL-06", "업무상 이해관계"),
            new CatalogEntry("REL-07", "서로 모르는 사이"),
        };

        /// <summary>04 2026-11-12 직업 12종(경찰관·의사·공장 노동자·기술자·요리사·운전기사 제외).</summary>
        public static readonly string[] Occupations =
        {
            "비서", "사무원", "회계 담당자", "공무원", "변호사", "기자", "교사·가정교사", "상인", "점원", "집사", "가정부", "학생"
        };

        public static string ConcealmentName(CoverUpMethod c) => c switch
        {
            CoverUpMethod.None => "은폐 없음",
            CoverUpMethod.AccidentalDeathDisguise => "사고사 위장",
            CoverUpMethod.SuicideDisguise => "자살 위장",
            CoverUpMethod.DisappearanceDisguise => "실종 위장",
            CoverUpMethod.RecordOrTestimonyTampering => "기록·증언 조작",
            _ => c.ToString()
        };

        public static readonly Dictionary<CrimeType, CatalogEntry[]> RepresentativeSubtypes = new Dictionary<CrimeType, CatalogEntry[]>
        {
            [CrimeType.Murder] = new[]
            {
                new CatalogEntry("REP-SUB-MUR-01", "주택 내 사망자 발견"),
                new CatalogEntry("REP-SUB-MUR-02", "사망 원인 미확정"),
                new CatalogEntry("REP-SUB-MUR-03", "사고·자살 처리 기록이 있으나 타살 의심"),
                new CatalogEntry("REP-SUB-MUR-04", "사망 시점·장소·경위 정보 불일치"),
            },
            [CrimeType.Missing] = new[]
            {
                new CatalogEntry("REP-SUB-MIS-01", "주택 거주자 소재 미확인"),
                new CatalogEntry("REP-SUB-MIS-02", "주택 방문자 소재 미확인"),
                new CatalogEntry("REP-SUB-MIS-03", "귀가 예정자 미귀가"),
                new CatalogEntry("REP-SUB-MIS-04", "마지막 연락·목격 이후 연락 두절"),
            },
            [CrimeType.AssaultInjury] = new[]
            {
                new CatalogEntry("REP-SUB-ASS-01", "현장에서 상해 확인"),
                new CatalogEntry("REP-SUB-ASS-02", "사건 발생 후 시간 경과 뒤 상해 확인"),
                new CatalogEntry("REP-SUB-ASS-03", "상해 원인·시점·장소 미확정"),
                new CatalogEntry("REP-SUB-ASS-04", "관련자 진술이 엇갈리는 상해"),
            },
            [CrimeType.TheftRobbery] = new[]
            {
                new CatalogEntry("REP-SUB-THE-01", "개인 소지품 도난 신고"),
                new CatalogEntry("REP-SUB-THE-02", "가정 내 귀중품 도난 신고"),
                new CatalogEntry("REP-SUB-THE-03", "현금·금전 도난 신고"),
                new CatalogEntry("REP-SUB-THE-04", "문서·기록물 도난 신고"),
            },
            [CrimeType.Blackmail] = new[]
            {
                new CatalogEntry("REP-SUB-THR-01", "신체 위해 협박 신고"),
                new CatalogEntry("REP-SUB-THR-02", "비밀 폭로 협박 신고"),
                new CatalogEntry("REP-SUB-THR-03", "재산·생계·직업상 불이익 협박 신고"),
                new CatalogEntry("REP-SUB-THR-04", "특정 행동 강요·입막음 협박 신고"),
            },
        };

        public static readonly Dictionary<CrimeType, CatalogEntry[]> ActualSubtypes = new Dictionary<CrimeType, CatalogEntry[]>
        {
            [CrimeType.Murder] = new[]
            {
                new CatalogEntry("ACT-SUB-MUR-01", "사망 장소에서 시신 발견"),
                new CatalogEntry("ACT-SUB-MUR-02", "공격 후 생존 시간을 거쳐 사망"),
                new CatalogEntry("ACT-SUB-MUR-03", "사망 장소와 시신 발견 장소가 다름"),
            },
            [CrimeType.Missing] = new[]
            {
                new CatalogEntry("ACT-SUB-MIS-01", "거주자 실종"),
                new CatalogEntry("ACT-SUB-MIS-02", "방문자 실종"),
                new CatalogEntry("ACT-SUB-MIS-03", "귀가 예정자 미귀가"),
                new CatalogEntry("ACT-SUB-MIS-04", "마지막 연락·목격 이후 소재 미확인"),
            },
            [CrimeType.AssaultInjury] = new[]
            {
                new CatalogEntry("ACT-SUB-ASS-01", "단일 사건에서 즉시 확인된 비치명적 상해"),
                new CatalogEntry("ACT-SUB-ASS-03", "다른 주택 공간으로 이동한 뒤 상해 확인"),
                new CatalogEntry("ACT-SUB-ASS-04", "시간 간격을 두고 확인된 상해"),
            },
            [CrimeType.TheftRobbery] = new[]
            {
                new CatalogEntry("ACT-SUB-THE-01", "개인 소지품 도난"),
                new CatalogEntry("ACT-SUB-THE-02", "가정 내 귀중품 도난"),
                new CatalogEntry("ACT-SUB-THE-03", "현금·금전 도난"),
                new CatalogEntry("ACT-SUB-THE-04", "문서·기록물 도난"),
            },
            [CrimeType.Blackmail] = new[]
            {
                new CatalogEntry("ACT-SUB-THR-01", "신체 위해 협박"),
                new CatalogEntry("ACT-SUB-THR-02", "비밀 폭로 협박"),
                new CatalogEntry("ACT-SUB-THR-03", "재산·생계·직업상 불이익 협박"),
                new CatalogEntry("ACT-SUB-THR-04", "특정 행동 강요·입막음 협박"),
            },
        };

        /// <summary>
        /// 범죄 유형별 허용 방법(04 적용 기준). 총기(METHOD-04)는 의뢰 생성에서 조건부 허용이라 접근 경로 검증 전에는 후보에서 뺀다.
        /// METHOD-08(응급조치 방해)은 "구조할 기회가 실제로 있었을 때"만이라 기본 후보에서 뺀다.
        /// </summary>
        public static IReadOnlyList<string> MethodsFor(CrimeType crime) => crime switch
        {
            CrimeType.Murder => new[] { "METHOD-01", "METHOD-02", "METHOD-03", "METHOD-05", "METHOD-06", "METHOD-07" },
            CrimeType.AssaultInjury => new[] { "METHOD-01", "METHOD-02", "METHOD-03", "METHOD-05", "METHOD-07" },
            CrimeType.Missing => new[] { "METHOD-09", "METHOD-10" },
            CrimeType.TheftRobbery => new[] { "METHOD-11", "METHOD-12", "METHOD-13", "METHOD-14" },
            CrimeType.Blackmail => new[] { "METHOD-15", "METHOD-16", "METHOD-17" },
            _ => new[] { "METHOD-01" }
        };

        /// <summary>
        /// 방법이 실제로 쓸 수 있는 도구(04 호환성 규칙 "도구가 그 방법에 실제로 사용될 수 있는지"를 행위 의미로 적용).
        /// 원문에 방법별 대응표는 없다 — 행위 정의에서 도구가 필요한지·어떤 도구인지가 정해지는 것만 짝지었다.
        /// </summary>
        public static IReadOnlyList<string> ToolsFor(string methodId) => methodId switch
        {
            "METHOD-02" => new[] { "TOOL-01" },
            "METHOD-03" => new[] { "TOOL-02" },
            "METHOD-04" => new[] { "TOOL-03" },
            "METHOD-05" => new[] { "TOOL-00", "TOOL-04" },
            "METHOD-06" => new[] { "TOOL-05" },
            "METHOD-10" => new[] { "TOOL-00", "TOOL-04" },
            _ => new[] { "TOOL-00" }
        };

        public static string Name(IEnumerable<CatalogEntry> table, string id) => table.FirstOrDefault(e => e.Id == id)?.Name ?? id;

        /// <summary>04 2026-11-11 방별 조사 오브젝트 후보 예시(원문). 단서가 없는 조사 대상도 허용한다.</summary>
        public static readonly Dictionary<string, string[]> RoomObjects = new Dictionary<string, string[]>
        {
            [HouseLayoutFactory.EntranceHall] = new[] { "외투", "모자", "우산", "신발", "출입 흔적" },
            [HouseLayoutFactory.Reception] = new[] { "찻잔", "편지", "사진 액자", "재떨이", "깨진 장식품" },
            [HouseLayoutFactory.Dining] = new[] { "접시", "잔", "식기", "음식 용기", "식탁보" },
            [HouseLayoutFactory.Bedroom] = new[] { "일기장", "편지", "사진", "옷", "보석함", "약병" },
            [HouseLayoutFactory.Study] = new[] { "장부", "계약서", "봉투", "타자기", "서류 더미" },
            [HouseLayoutFactory.Kitchen] = new[] { "냄비", "조리 도구", "병", "재료 용기", "행주" },
            [HouseLayoutFactory.PantryUtility] = new[] { "병", "포장재", "상자", "청소 도구", "보관 용기" },
            [HouseLayoutFactory.CorridorStairs] = new[] { "이동 흔적", "떨어진 물건", "사건별 흔적" },
        };

        /// <summary>문서가 될 수 있는 오브젝트(편지·장부 등). 동기·기록 자료는 이 중에서 놓는다.</summary>
        public static readonly HashSet<string> DocumentObjects = new HashSet<string>
        {
            "편지", "일기장", "장부", "계약서", "봉투", "서류 더미", "사진"
        };

        public static string ObjectId(string roomId, string objectName) => $"{roomId}:{objectName}";

        public static string ObjectName(string objectId)
        {
            int i = objectId?.IndexOf(':') ?? -1;
            return i < 0 ? objectId : objectId.Substring(i + 1);
        }

        public static string ObjectRoom(string objectId)
        {
            int i = objectId?.IndexOf(':') ?? -1;
            return i < 0 ? null : objectId.Substring(0, i);
        }

        /// <summary>TEST_TEMP 인물 이름(서양풍 국가 배경, 정식 세계관 인물 아님).</summary>
        public static readonly string[] TempNames =
        {
            "에드윈 홀", "마거릿 레인", "토머스 그레이", "엘리너 크로스", "아서 웰스", "클라라 본드", "헨리 폭스", "루시 마시",
            "월터 리드", "에이다 블룸", "프랭크 모스", "이디스 케인"
        };
    }
}
