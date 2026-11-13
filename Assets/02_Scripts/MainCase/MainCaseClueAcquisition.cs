using System;
using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Contracts;
using CaseFileOmission.Evidence;
using CaseFileOmission.NPC;

namespace CaseFileOmission.MainCase
{
    /// <summary>
    /// 체계의 진실 단서 중 "가해자 자료"(통합 명세서의 가해자_01~05)와, 그 조건에 직접 연결된 두 단서.
    /// 이름은 노션 단서표의 정식 단서명을 그대로 쓴다.
    /// </summary>
    public enum MainCaseClueCode
    {
        GovernmentDepartmentContactLog,          // 가해자_01 — 정부 부서 연락 기록
        SubjectManagementTable,                  // 가해자_02 — 실험 대상자 관리표
        ResultReportRecording,                   // 가해자_03 — 실험 결과 보고 녹음
        ParticipantRosterDeliveryConfirmation,   // 가해자_04 — 참가자 명단 전달 확인서
        SeniorOfficialLetter,                    // 가해자_05 — 고위 관계자 서신
        NumberNoteLeftForLover,                  // 관리표의 선행 단서 — 연인에게 남긴 번호 쪽지
        SubjectNumberFile                        // 서신의 후행 단서 — 대상자 번호 파일
    }

    public enum MainCaseClueRoute
    {
        /// <summary>해당 의뢰 현장에서 발견한다.</summary>
        FoundInContract,

        /// <summary>조직이 의뢰 종료 후 다음 의뢰 전(보드게임 진입 전)에 전달한다.</summary>
        OrganizationDelivery,

        /// <summary>무작위로 등장한다. 노션은 "무작위 또는 조직 전달 가능"이라고만 하고 확률은 정하지 않았다.</summary>
        RandomAppearance
    }

    public enum MainCaseClueAcquireOutcome
    {
        Acquired,
        AlreadyAcquired,
        NotEligible,
        RouteNotAllowed,
        OutsideDeliveryWindow,
        RandomPolicyNotConfigured,
        RandomRollFailed,
        PasswordNotConfigured,
        WrongPassword
    }

    /// <summary>의뢰 결과에서 단서 조건 판정에 필요한 사실만 모은 입력값.</summary>
    public sealed class MainCaseClueContext
    {
        public string ContractId;
        public int VictimCount;
        public readonly List<string> VictimOccupations = new List<string>();
        public string CauseOfDeathTool;
        /// <summary>04 도구 카탈로그 ID(TOOL-01 등). 생성 의뢰는 이 값으로 비교한다.</summary>
        public string CauseOfDeathToolId;
        public NpcPersonality? PerpetratorPersonality;

        /// <summary>"부부 또는 연인 관계인 두 사람이 사망한 사건". 현재 사건 데이터에 관계 그래프가 없어 호출자가 판정해 넣는다.</summary>
        public bool TwoDeceasedInSpousalOrRomanticRelationship;

        /// <summary>"방에 컴퓨터가 있고". 조사 가능 물체 목록이 미정이라 호출자가 넣는다.</summary>
        public bool RoomHasComputer;

        /// <summary>의뢰 종료 후 다음 의뢰를 받기 전, 미니어처 보드게임 공간에 들어가기 전인지(03 확정 전달 시점).</summary>
        public bool IsInterContractDeliveryWindow;

        /// <summary>의뢰·NPC 데이터에서 계산할 수 있는 값만 채운다. 나머지 플래그는 호출자가 설정한다.</summary>
        public static MainCaseClueContext FromContract(ContractInstance contract, NpcRegistry npcs)
        {
            var context = new MainCaseClueContext
            {
                ContractId = contract.ContractId,
                CauseOfDeathTool = contract.Truth.Tool,
                CauseOfDeathToolId = contract.Truth.ToolId
            };

            foreach (var npc in npcs.GetByContract(contract.ContractId))
            {
                if (npc.ActualRole == NpcActualRole.Victim)
                {
                    context.VictimCount++;
                    if (!string.IsNullOrEmpty(npc.Occupation))
                        context.VictimOccupations.Add(npc.Occupation);
                }

                if (npc.NpcId == contract.Truth.PerpetratorId)
                    context.PerpetratorPersonality = npc.Personality;
            }

            return context;
        }
    }

    /// <summary>
    /// 노션 원문의 조건값. 데이터로 분리해 기획이 바뀌면 여기만 고친다.
    /// </summary>
    public sealed class MainCaseClueRuleConfig
    {
        public string SecretaryOccupation = "비서";
        public string KnifeTool = "칼";
        /// <summary>04 도구 카탈로그의 칼(TOOL-01 칼·날붙이). 03 2026-11-12: 칼 자료는 TOOL-01로 생성한다.</summary>
        public string KnifeToolId = "TOOL-01";
        public string KnifeToolCatalogName = "칼·날붙이";
        public int MinimumVictimCountForContactLog = 3;
        public int ContractsAfterNumberNoteForManagementTable = 2;

        /// <summary>노션 "의심 많은 자" = 성격 오브젝트 "의심이 많은".</summary>
        public NpcPersonality SuspiciousTrait = NpcPersonality.Suspicious;

        /// <summary>03 2026-11-12 확정: 고위 관계자 서신의 가해자 성격은 '욕심이 많은'.</summary>
        public NpcPersonality DesireTrait = NpcPersonality.Greedy;

        /// <summary>
        /// 대상자 번호 파일의 비밀번호. 03 2026-11-12 확정: "84-031은 조카의 번호", 피 묻은 익명 보고서의 84-031을 검색 비밀번호로 쓴다.
        /// </summary>
        public string SubjectNumberFilePassword = "84-031";

        /// <summary>03 2026-11-12 확정: 실험 대상자 관리표의 무작위 등장 확률 25%(조직 전달 경로도 유지).</summary>
        public float RandomAppearanceChance = 0.25f;

        /// <summary>실험 대상자 관리표의 무작위 등장 판정. 비우면 서비스가 주입받은 난수와 RandomAppearanceChance로 판정한다.</summary>
        public Func<bool> RandomAppearanceRoll;
    }

    /// <summary>
    /// 가해자 자료 단서의 획득 조건을 평가하고, 조건을 만족하면 단서 저장소와 메인 사건 진행 상태에 반영한다.
    /// 조건 원문(체계의 진실 단서표):
    /// - 정부 부서 연락 기록: 피해자가 3명 이상인 사건
    /// - 실험 대상자 관리표: 연인에게 남긴 번호 쪽지 확보 후 다른 사건 2건이 더 진행된 경우 등에 무작위 또는 조직 전달 가능
    /// - 실험 결과 보고 녹음: 피해자가 비서이고 칼에 의해 살해되며 가해자 특성이 '의심 많은 자'인 사건
    /// - 참가자 명단 전달 확인서: 실험 결과 보고 녹음과 같은 사건 조건
    /// - 고위 관계자 서신: 가해자 특성이 '욕망이 가득한 자'인 사건
    /// - 연인에게 남긴 번호 쪽지: 부부 또는 연인 관계인 두 사람이 사망한 사건
    /// - 대상자 번호 파일: 방에 컴퓨터가 있고 고위 관계자 서신 이후 등장 가능, 비밀번호는 조카의 실험 번호
    /// </summary>
    public sealed class MainCaseClueAcquisitionService
    {
        private readonly MainCaseClueRuleConfig _rules;
        private readonly Random _random;

        /// <summary>random을 넘기면 무작위 등장 판정에 쓴다. 넘기지 않고 RandomAppearanceRoll도 없으면 무작위 경로는 쓰지 않는다.</summary>
        public MainCaseClueAcquisitionService(MainCaseClueRuleConfig rules = null, Random random = null)
        {
            _rules = rules ?? new MainCaseClueRuleConfig();
            _random = random;
        }

        private bool IsKnife(MainCaseClueContext c) =>
            c.CauseOfDeathToolId == _rules.KnifeToolId || c.CauseOfDeathTool == _rules.KnifeTool || c.CauseOfDeathTool == _rules.KnifeToolCatalogName;

        public static string GetClueName(MainCaseClueCode code) => code switch
        {
            MainCaseClueCode.GovernmentDepartmentContactLog => MainCaseClueNames.GovernmentDepartmentContactLog,
            MainCaseClueCode.SubjectManagementTable => MainCaseClueNames.SubjectManagementTable,
            MainCaseClueCode.ResultReportRecording => MainCaseClueNames.ResultReportRecording,
            MainCaseClueCode.ParticipantRosterDeliveryConfirmation => MainCaseClueNames.ParticipantRosterDeliveryConfirmation,
            MainCaseClueCode.SeniorOfficialLetter => MainCaseClueNames.SeniorOfficialLetter,
            MainCaseClueCode.NumberNoteLeftForLover => MainCaseClueNames.NumberNoteLeftForLover,
            MainCaseClueCode.SubjectNumberFile => MainCaseClueNames.SubjectNumberFile,
            _ => throw new ArgumentOutOfRangeException(nameof(code))
        };

        public static IReadOnlyList<MainCaseClueRoute> GetAllowedRoutes(MainCaseClueCode code) => code switch
        {
            MainCaseClueCode.SubjectManagementTable => new[] { MainCaseClueRoute.RandomAppearance, MainCaseClueRoute.OrganizationDelivery },
            _ => new[] { MainCaseClueRoute.FoundInContract }
        };

        public bool IsEligible(MainCaseClueCode code, MainCaseClueContext context, MainCaseProgressTracker tracker)
        {
            switch (code)
            {
                case MainCaseClueCode.GovernmentDepartmentContactLog:
                    return context.VictimCount >= _rules.MinimumVictimCountForContactLog;

                case MainCaseClueCode.SubjectManagementTable:
                {
                    int? since = tracker.ContractsCompletedSinceAcquired(MainCaseClueNames.NumberNoteLeftForLover);
                    return since.HasValue && since.Value >= _rules.ContractsAfterNumberNoteForManagementTable;
                }

                case MainCaseClueCode.ResultReportRecording:
                case MainCaseClueCode.ParticipantRosterDeliveryConfirmation:
                    return context.VictimOccupations.Contains(_rules.SecretaryOccupation)
                           && IsKnife(context)
                           && context.PerpetratorPersonality == _rules.SuspiciousTrait;

                case MainCaseClueCode.SeniorOfficialLetter:
                    return context.PerpetratorPersonality == _rules.DesireTrait;

                case MainCaseClueCode.NumberNoteLeftForLover:
                    return context.TwoDeceasedInSpousalOrRomanticRelationship;

                case MainCaseClueCode.SubjectNumberFile:
                    return context.RoomHasComputer && tracker.HasAcquired(MainCaseClueNames.SeniorOfficialLetter);

                default:
                    return false;
            }
        }

        public IReadOnlyList<MainCaseClueCode> GetEligibleClues(MainCaseClueContext context, MainCaseProgressTracker tracker) =>
            ((MainCaseClueCode[])Enum.GetValues(typeof(MainCaseClueCode)))
            .Where(code => !tracker.HasAcquired(GetClueName(code)) && IsEligible(code, context, tracker))
            .ToList();

        public MainCaseClueAcquireOutcome TryAcquire(
            MainCaseClueCode code,
            MainCaseClueRoute route,
            MainCaseClueContext context,
            MainCaseProgressTracker tracker,
            ClueRepository clueRepository,
            string enteredPassword = null)
        {
            string clueName = GetClueName(code);

            if (tracker.HasAcquired(clueName))
                return MainCaseClueAcquireOutcome.AlreadyAcquired;

            if (!GetAllowedRoutes(code).Contains(route))
                return MainCaseClueAcquireOutcome.RouteNotAllowed;

            if (!IsEligible(code, context, tracker))
                return MainCaseClueAcquireOutcome.NotEligible;

            if (route == MainCaseClueRoute.OrganizationDelivery && !context.IsInterContractDeliveryWindow)
                return MainCaseClueAcquireOutcome.OutsideDeliveryWindow;

            if (route == MainCaseClueRoute.RandomAppearance)
            {
                if (_rules.RandomAppearanceRoll == null && _random == null)
                    return MainCaseClueAcquireOutcome.RandomPolicyNotConfigured;
                bool appeared = _rules.RandomAppearanceRoll != null
                    ? _rules.RandomAppearanceRoll()
                    : _random.NextDouble() < _rules.RandomAppearanceChance;
                if (!appeared)
                    return MainCaseClueAcquireOutcome.RandomRollFailed;
            }

            if (code == MainCaseClueCode.SubjectNumberFile)
            {
                if (string.IsNullOrEmpty(_rules.SubjectNumberFilePassword))
                    return MainCaseClueAcquireOutcome.PasswordNotConfigured;
                if (enteredPassword != _rules.SubjectNumberFilePassword)
                    return MainCaseClueAcquireOutcome.WrongPassword;
            }

            clueRepository.Discover(new ClueDefinition(ToClueId(code), context.ContractId ?? "main-case")
            {
                Format = code == MainCaseClueCode.ResultReportRecording ? ClueFormat.Recording : ClueFormat.Document,
                TruthStatus = ClueTruthStatus.Factual,
                DisplayName = clueName,
                SourceDescription = route.ToString(),
                AcquisitionCondition = DescribeCondition(code)
            });
            tracker.AcquireClue(clueName);
            return MainCaseClueAcquireOutcome.Acquired;
        }

        /// <summary>
        /// 03 확정: "같은 시점에 전달 조건을 충족한 추가 단서가 여러 개라면 모두 한꺼번에 전달한다."
        /// 조직 전달이 허용된 단서 중 지금 조건을 만족하는 것을 전부 전달한다.
        /// </summary>
        public IReadOnlyList<MainCaseClueCode> DeliverEligibleViaOrganization(
            MainCaseClueContext context,
            MainCaseProgressTracker tracker,
            ClueRepository clueRepository)
        {
            var delivered = new List<MainCaseClueCode>();
            if (!context.IsInterContractDeliveryWindow)
                return delivered;

            foreach (var code in GetEligibleClues(context, tracker))
            {
                if (!GetAllowedRoutes(code).Contains(MainCaseClueRoute.OrganizationDelivery))
                    continue;

                if (TryAcquire(code, MainCaseClueRoute.OrganizationDelivery, context, tracker, clueRepository) == MainCaseClueAcquireOutcome.Acquired)
                    delivered.Add(code);
            }

            return delivered;
        }

        public static string ToClueId(MainCaseClueCode code) => $"maincase:{code}";

        private static string DescribeCondition(MainCaseClueCode code) => code switch
        {
            MainCaseClueCode.GovernmentDepartmentContactLog => "피해자가 3명 이상인 사건",
            MainCaseClueCode.SubjectManagementTable => "연인에게 남긴 번호 쪽지 확보 후 다른 사건 2건이 더 진행된 경우 등에 무작위 또는 조직 전달 가능",
            MainCaseClueCode.ResultReportRecording => "피해자가 비서이고 칼에 의해 살해되며 가해자 특성이 '의심 많은 자'인 사건",
            MainCaseClueCode.ParticipantRosterDeliveryConfirmation => "실험 결과 보고 녹음과 같은 사건 조건",
            MainCaseClueCode.SeniorOfficialLetter => "가해자 특성이 '욕망이 가득한 자'인 사건",
            MainCaseClueCode.NumberNoteLeftForLover => "부부 또는 연인 관계인 두 사람이 사망한 사건",
            MainCaseClueCode.SubjectNumberFile => "방에 컴퓨터가 있고 고위 관계자 서신 이후 등장 가능. 비밀번호는 조카의 실험 번호",
            _ => string.Empty
        };
    }
}
