using System;
using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Combat;
using CaseFileOmission.ContractContent;
using CaseFileOmission.Contracts;
using CaseFileOmission.Core;
using CaseFileOmission.Endings;
using CaseFileOmission.Evidence;
using CaseFileOmission.MainCase;
using CaseFileOmission.Meeting;
using CaseFileOmission.NPC;
using CaseFileOmission.Organization;
using CaseFileOmission.Police;
using CaseFileOmission.Reports;
using CaseFileOmission.Rooms;
using CaseFileOmission.SaveLoad;

namespace CaseFileOmission.Game
{
    public sealed class GameSessionConfig
    {
        public string PlayerId = "player";
        public ContractGenerationConfig ContractConfig;
        public FinalReportAnswerConfig FinalReportConfig;
        public MainCaseClueRuleConfig MainCaseRules;

        /// <summary>AI 제공자(배포 환경에서 설정). null이면 결정적 생성·검증기를 쓴다.</summary>
        public IAiContentProvider AiProvider;

        /// <summary>재현 가능한 게임용 난수 시드. 설정하면 외부 Random보다 우선한다.</summary>
        public int? RandomSeed;

        /// <summary>테스트에서 커스텀 Random 구현을 주입할 때 사용한다.</summary>
        public Random Random;
    }

    /// <summary>
    /// 개별 시스템을 하나의 게임 루프로 묶는 진행 계층. UI·씬은 이 클래스의 메서드만 호출하면 된다.
    ///
    /// 한 의뢰의 흐름(06·03·05·10 확정 흐름을 따른다):
    /// 1) <see cref="OfferContract"/> — 조직이 전화로 의뢰를 제안한다. 조직 조건은 이때 공개된다.
    /// 2) <see cref="AcceptContract"/> — 수락하면 조건이 고정되고 담당 경찰 1명이 배정되며 조사가 시작된다.
    ///    조직_01(자살 위장 조건 의뢰)은 이 시점에 검사한다.
    /// 3) 조사 — 각 세계 시간대는 12시간이고 플레이어 조사 예산은 8시간(32칸)이다. 시간 소모 행동만큼 NPC가 행동한다.
    ///    구간이 끝나면(예산 소진 또는 <see cref="EndInvestigationEarly"/>) <see cref="StartMeeting"/> 또는 <see cref="AdvanceToNextTimeBlock"/>.
    /// 4) 회의 — 카드 제시·의문 대응·차례 넘기기, <see cref="ProposeConclusion"/>로 결론(과반 채택 / 추가 검증 / 미제).
    ///    결론 없이 <see cref="LeaveMeeting"/>하면 다음 구간 조사로 돌아가고, 보류한 의문은 다음 회의에서 먼저 처리한다.
    /// 5) 종료 정산 — 경찰 의심도 종료 효과, 조직 조건 판정과 신뢰도, 조직_02, 메인 사건 진행, 의뢰 사이 조직 전달, 엔딩 판정.
    /// 엔딩은 상태가 바뀌는 행동마다 검사하고, 게임 종료 엔딩이 실행되면 모든 행동이 막힌다.
    ///
    /// 원문에 없어 임시로 정한 것은 해당 메서드 주석에 "(임시)"로 표시했다.
    /// </summary>
    public sealed class GameSession
    {
        public const string MainCaseContractId = "main-case";

        /// <summary>06·07 2026-11-13 확정: 세계 시각 15분마다 NPC 의사결정 1턴. 조사 예산 1칸도 15분이다.</summary>
        public const int NpcMinutesPerTimeSlot = GameStateController.SlotMinutes;

        // 06 2026-11-11/12 확정 행동 비용(칸 = 15분). 이미 확보한 자료 열람과 UI 조작은 시간 비용 0.
        public const int MoveCost = 0;
        public const int TalkCost = 2;
        public const int ObserveCost = 2;
        public const int OverhearCost = 2;
        public const int InvestigateCost = 4;
        public const int ReadDocumentCost = 4;
        public const int DropClueCost = 1;
        public const int DefaultActionCost = 4;

        /// <summary>임시값: 결론 투표를 넘기지 않았을 때, 참가자가 플레이어 의심도가 이 값 미만이면 결론을 받아들인다고 본다.</summary>
        public const float ProvisionalConclusionAcceptThreshold = 35f;

        /// <summary>임시값: 물리 조작 판정에서 "대상이 의심하는 상태"(-15)로 보는 담당 경찰 의심도 경계(06 의심도 단계 '높음' 35).</summary>
        public const float ProvisionalSuspiciousThreshold = 35f;

        private readonly Random _random;
        private readonly MainCaseClueRuleConfig _mainCaseRules;
        private readonly IAiContentProvider _aiProvider;
        private readonly ContractPipeline _pipeline;
        private readonly FirearmResolver _firearmResolver;
        private readonly NpcPersuasionService _persuasion;
        private readonly ClueConfirmationRequestService _confirmation;
        private readonly MainCaseClueAcquisitionService _mainCaseClues;

        private int _meetingIndex;
        private int _actionCounter;
        private List<CardQuestion> _carriedOverQuestions = new List<CardQuestion>();

        /// <summary>결론 없이 마친 회의에서 다음 회의로 넘긴 의문(다음 회의에서 가장 먼저 처리). 저장한다.</summary>
        public IReadOnlyList<CardQuestion> CarriedOverQuestions => _carriedOverQuestions;

        public string PlayerId { get; }

        // ── 의뢰를 넘나드는 상태 ──
        public GameStateController State { get; } = new GameStateController();
        public GameLogger Logger { get; } = new GameLogger();
        public OrganizationState Organization { get; } = new OrganizationState();
        public OrganizationSupportClueService OrganizationSupport { get; } = new OrganizationSupportClueService();

        /// <summary>조직 보조 자료 20개(ORG_SUPPLEMENTARY_REPORT) 판정·전달 상태.</summary>
        public OrgSupplementarySupportService OrgSupplementary { get; private set; }
        public PoliceSuspicionService PoliceSuspicion { get; } = new PoliceSuspicionService();
        public PoliceLifeStateService PoliceLife { get; } = new PoliceLifeStateService();
        public PolicePursuitController Pursuit { get; }
        public MainCaseProgressTracker MainCase { get; } = new MainCaseProgressTracker();
        public PostReportBranching PostReport { get; } = new PostReportBranching();
        public MainCaseFinalReportCoordinator FinalReport { get; }
        public EndingExecutor Endings { get; }
        public ClueRepository Clues { get; } = new ClueRepository();
        public EvidenceManipulationService Manipulation { get; }

        /// <summary>물리적 증거·기록 조작 판정(기본 70%). 조작 가능 시점은 <see cref="IsPhysicalManipulationUnlocked"/>.</summary>
        public EvidenceManipulationOutcomeResolver PhysicalManipulation { get; }
        public EvidenceCapacityManager Capacity { get; } = new EvidenceCapacityManager();
        public FirearmState Firearm { get; } = new FirearmState();
        public CampaignProgress Campaign { get; } = new CampaignProgress();
        public List<ContractReportState> Reports { get; } = new List<ContractReportState>();

        /// <summary>의뢰별 UI-003 사건보드 배치·연결선. 보고서·단서 상태와 별개이며 판정에 쓰지 않는다.</summary>
        public List<CaseBoardState> CaseBoards { get; } = new List<CaseBoardState>();

        /// <summary>지금 의뢰의 사건보드. 처음 열 때 의뢰 ID에서 만든 고정 seed로 배치하고, 보유 자료를 동기화한다.</summary>
        public CaseBoardState GetCaseBoard()
        {
            if (CurrentContract == null) return null;
            string id = CurrentContract.ContractId;
            var board = CaseBoards.FirstOrDefault(b => b.ContractId == id);
            if (board == null)
            {
                board = new CaseBoardState(id, StableSeed(id));
                CaseBoards.Add(board);
            }
            board.SyncMaterials(Clues.GetAllClueIds(id));
            return board;
        }

        /// <summary>실행·플랫폼과 무관하게 같은 문자열이면 같은 값(FNV-1a).</summary>
        public static int StableSeed(string text)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in text ?? string.Empty) hash = (hash ^ c) * 16777619;
                return (int)(hash & 0x7FFFFFFF);
            }
        }

        // ── 현재 의뢰 ──
        public GeneratedContract PendingOffer { get; private set; }
        public ContractInstance CurrentContract { get; private set; }
        public ContractInstance LastContract { get; private set; }
        public NpcRegistry Npcs { get; private set; } = new NpcRegistry();
        public RoomGraph Rooms { get; private set; } = HouseLayoutFactory.CreateStandardHouse();
        public RoomOccupancyTracker Occupancy { get; private set; }
        public List<EvidenceMaterial> Materials { get; private set; } = new List<EvidenceMaterial>();
        public NpcRelationshipStore Relationships { get; private set; } = new NpcRelationshipStore();
        public NpcInfoTransferService InfoTransfer { get; private set; }
        public NpcBehaviorPlanner Planner { get; private set; }
        public ContractReportState Report { get; private set; }

        // ── 회의 ──
        public MeetingSession Meeting { get; private set; }
        public MeetingCardResolver MeetingResolver { get; private set; }
        public MeetingFlow MeetingFlow { get; private set; }
        public CardPresentationResolution LastCardResolution { get; private set; }

        public bool IsGameOver => Endings.IsGameOver;
        public string PlayerRoom => Occupancy.HasPosition(PlayerId) ? Occupancy.GetRoom(PlayerId) : null;
        public string HandlerPoliceId => CurrentContract?.HandlerPoliceId;

        /// <summary>모든 외부 행동은 동기식으로 완료되어 반환되므로, 조사·회의 상태 경계에서 저장할 수 있다.</summary>
        public bool CanSave => State.CurrentPhase != GamePhase.Resolved;

        public GameSession(GameSessionConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            PlayerId = string.IsNullOrEmpty(config.PlayerId) ? "player" : config.PlayerId;
            _random = config.RandomSeed.HasValue
                ? new ReplayableRandom(config.RandomSeed.Value)
                : config.Random ?? new Random();
            _aiProvider = config.AiProvider;

            var contractConfig = config.ContractConfig ?? ContractGenerationConfig.CreateDefault();
            _pipeline = new ContractPipeline(contractConfig, _random, Logger);
            _firearmResolver = new FirearmResolver(_random);
            _persuasion = new NpcPersuasionService(_random);
            _confirmation = new ClueConfirmationRequestService(_random);
            _mainCaseRules = config.MainCaseRules ?? new MainCaseClueRuleConfig();
            _mainCaseClues = new MainCaseClueAcquisitionService(_mainCaseRules, _random);
            OrgSupplementary = new OrgSupplementarySupportService(_random);

            Pursuit = new PolicePursuitController(PoliceSuspicion, State.EventLog);
            Endings = new EndingExecutor(new GameEndEvaluator(PoliceSuspicion, Organization));
            Manipulation = new EvidenceManipulationService(Clues, _random);
            PhysicalManipulation = new EvidenceManipulationOutcomeResolver(Manipulation, _random)
            {
                UnlockCondition = () => IsPhysicalManipulationUnlocked
            };
            FinalReport = new MainCaseFinalReportCoordinator(MainCase, PostReport,
                config.FinalReportConfig ?? FinalReportAnswerConfig.CreateDefault());
            Occupancy = new RoomOccupancyTracker(Rooms);
            InfoTransfer = new NpcInfoTransferService(_random);
        }

        // ───────────────────────── 게임 시작 ─────────────────────────

        /// <summary>새 게임. 03 확정: 조카의 일기는 게임 시작부터 보유한다.</summary>
        public void StartNewGame()
        {
            Campaign.RunId = Guid.NewGuid().ToString("N");
            Campaign.SaveSequence = 0;
            GrantMainCaseDocument("maincase:NieceDiary", MainCaseClueNames.NieceDiary);
        }

        /// <summary>프롤로그 종료. 03 확정: 조카의 사망 관련 보고서는 프롤로그에서 확보한다.</summary>
        public void CompletePrologue()
        {
            GrantMainCaseDocument("maincase:NieceDeathReport", MainCaseClueNames.NieceDeathReport);
            // 03 2026-11-13: 프롤로그 자료 묶음의 기본 보고서 2종(고정 출처 ID, 별도 획득 판정 없음).
            GrantMainCaseDocument(MainCaseClueNames.BloodiedRebelReportId, MainCaseClueNames.BloodiedRebelReport);
            GrantMainCaseDocument(MainCaseClueNames.FamilyDeathReportId, MainCaseClueNames.FamilyDeathReport);
            // TEST_TEMP: 「가족 통지 및 사고 처리 기록」은 획득 시점·장소·경로가 노션에 아직 없다(03 2026-11-11).
            // 1단계 필수 단서라 지급하지 않으면 메인 사건이 진행되지 않으므로, 경로가 정해질 때까지 프롤로그에서 함께 지급한다.
            GrantMainCaseDocument("maincase:FamilyNotificationRecord", MainCaseClueNames.FamilyNotificationRecord);
            AdvanceMainCaseStages();
        }

        // ───────────────────────── 의뢰 제안·수락 ─────────────────────────

        /// <summary>마지막 의뢰 생성 결과(후보 수·검증 횟수·실패 오류 코드). 진단용.</summary>
        public ContractGenerationResult LastGenerationResult { get; private set; }

        /// <summary>
        /// 조직의 의뢰 제안. 진실 → 유형 → 조직 조건 → 인물·시간표·단서 → 검증 순으로 만든다.
        /// 후보 최대 3개 × 각 후보 검증 최대 3회 안에 검증을 통과한 의뢰가 없으면 예외 없이 null을 돌려준다(생성 실패, 플레이어에게 제공하지 않음).
        /// </summary>
        public GeneratedContract OfferContract()
        {
            if (IsGameOver) throw new InvalidOperationException("게임이 종료되었습니다.");
            if (State.CurrentPhase != GamePhase.Idle) throw new InvalidOperationException("진행 중인 의뢰가 있습니다.");

            if (PendingOffer == null)
            {
                var constraints = PlanMainCaseConstraints();
                PendingOffer = _pipeline.TryGenerate(
                    () => $"contract-{++Campaign.ContractCounter}",
                    out var result,
                    Campaign.PreviousConditionType,
                    constraints);
                LastGenerationResult = result;

                // 확정된 메인 사건 단서의 조건을 맞춘 의뢰가 생성되지 않으면 실패로 돌려준다.
                // 제약 없는 일반 의뢰로 우회하면 필수 단서의 획득 경로가 보장되지 않으므로 대체 의뢰를 만들지 않는다.
                if (PendingOffer == null && constraints != null)
                {
                    string errors = result == null || result.LastErrors.Count == 0
                        ? "검증 가능한 후보를 만들지 못했습니다."
                        : string.Join(", ", result.LastErrors);
                    Logger.LogError("MAINCASE_CONSTRAINT_UNMET", nameof(GameSession), State.CurrentPhase,
                        $"메인 사건 단서 '{constraints.PlannedClueName}'의 생성에 실패했습니다: {errors}",
                        recoverable: true);
                }
            }
            return PendingOffer;
        }

        /// <summary>
        /// 03 2026-11-12 "단서 우선 배치 → 의뢰 생성 제약": 지금 진실 단계에서 아직 얻지 못한, 사건 조건이 있는 메인 사건 단서를 하나 골라
        /// 그 조건을 의뢰 생성의 필수 제약으로 만든다. 관련 의뢰가 우연히 생성되기를 기다리지 않는다.
        /// - 정부 부서 연락 기록: 피해자가 3명 이상인 사건
        /// - 실험 결과 보고 녹음·참가자 명단 전달 확인서: 피해자 직업 비서 + 칼(TOOL-01)에 의한 사망 + 가해자 '의심이 많은'(세 조건을 모두 충족)
        /// - 고위 관계자 서신: 가해자 '욕심이 많은'(다른 자료 조건과 합치지 않고 별도 의뢰)
        /// - 연인에게 남긴 번호 쪽지: 부부 또는 연인 관계인 두 피해자가 모두 사망
        /// 관리표(쪽지 뒤 의뢰 2건·25%·조직 전달)와 대상자 번호 파일(컴퓨터·비밀번호)은 사건 조건 단서가 아니라 여기서 배치하지 않는다.
        /// 고르는 순서는 원문에 없어 단서표 순서를 쓴다(임시).
        /// </summary>
        /// <summary>여러 피해자 조건(2~3명) 단서를 이번 의뢰에 붙일 확률(%). 사용자 지정 "가끔" — 정확한 수치는 TEST_TEMP.</summary>
        public const float MultiVictimContractChance = 20f;

        public ContractConstraints PlanMainCaseConstraints()
        {
            if (MainCase.CurrentStage == MainCaseTruthStage.PowerTruth) return PlanPowerTruthConstraints();
            if (MainCase.CurrentStage == MainCaseTruthStage.ExperimentTruth) return PlanExperimentTruthConstraints();
            if (MainCase.CurrentStage != MainCaseTruthStage.SystemicTruth) return null;
            var rules = _mainCaseRules;
            bool Missing(string name) => !MainCase.HasAcquired(name);

            // 사용자 지정 2026-11-20: 의뢰 시작 피해자는 대부분 1명, 2~3명은 가끔(최대 3명). 여러 피해자 조건 단서는 이번 의뢰에 붙일지 굴려서 정한다.
            bool allowMultiVictim = _random.NextDouble() * 100.0 < MultiVictimContractChance;
            if (allowMultiVictim && Missing(MainCaseClueNames.GovernmentDepartmentContactLog))
                return new ContractConstraints { PlannedClueName = MainCaseClueNames.GovernmentDepartmentContactLog, RequireMurder = true, VictimCount = rules.MinimumVictimCountForContactLog };
            if (Missing(MainCaseClueNames.ResultReportRecording) || Missing(MainCaseClueNames.ParticipantRosterDeliveryConfirmation))
                return new ContractConstraints
                {
                    PlannedClueName = Missing(MainCaseClueNames.ResultReportRecording) ? MainCaseClueNames.ResultReportRecording : MainCaseClueNames.ParticipantRosterDeliveryConfirmation,
                    RequireMurder = true,
                    VictimOccupation = rules.SecretaryOccupation,
                    ToolId = rules.KnifeToolId,
                    PerpetratorPersonality = rules.SuspiciousTrait
                };
            if (Missing(MainCaseClueNames.SeniorOfficialLetter))
                return new ContractConstraints { PlannedClueName = MainCaseClueNames.SeniorOfficialLetter, PerpetratorPersonality = rules.DesireTrait };
            if (allowMultiVictim && Missing(MainCaseClueNames.NumberNoteLeftForLover))
                return new ContractConstraints { PlannedClueName = MainCaseClueNames.NumberNoteLeftForLover, RequireMurder = true, CoupleVictims = true };
            // 03 최신 조건: 피해자 직업 기자 + 실제 살인 + METHOD-04 총기 공격 + TOOL-03 총기 + REL-06 업무상 이해관계.
            // 원문은 오래된 봉투로만 지정하고 특정 방 ID를 고정하지 않는다. 봉투는 주택 8공간 중 기존 오브젝트인 서재 봉투를 사용한다.
            // 총기의 실제 소유·접근 경로는 04의 조건부 허용 규칙이 요구하나 현재 NPC·시간표 모델에 독립 데이터가 없어
            // 이 경로의 검증은 별도 차단 항목으로 남긴다(이 조건을 충족한 것처럼 임의 소유자를 만들지 않는다).
            if (Missing(MainCaseClueNames.RepeatedExperimentNumberList))
                return new ContractConstraints
                {
                    PlannedClueName = MainCaseClueNames.RepeatedExperimentNumberList,
                    RequireMurder = true,
                    VictimOccupation = "기자",
                    MethodId = "METHOD-04",
                    ToolId = "TOOL-03",
                    RelationshipId = "REL-06",
                    HolderIsVictim = true,
                    DocumentObjectId = CaseCatalog.ObjectId(HouseLayoutFactory.Study, "봉투")
                };
            return null;
        }

        /// <summary>
        /// 03 2026-11-12 「권력의 진실 자료 8개 획득 조건」. 단계가 열리면 아직 없는 자료 하나를 골라 그 자료의 보유 경로(기존 NPC 역할 태그),
        /// 문서 존재 시점(사건 발생일은 그 이후), 보관 오브젝트를 생성 제약으로 넘긴다. 순차 해금이 아니며 관리 번호 순서로 고르는 것은 임시다.
        /// 자료가 요구하지 않는 피해자 직업·범행 방법·도구·가해자 성격·사건 유형은 제약하지 않는다. 조직이 문서를 직접 전달하지 않고 현장 조사로 발견한다.
        /// 보관 오브젝트는 원문 보관 설명에 맞는 기존 조사 오브젝트를 골랐다: 개인 서신은 침실 '편지', 계약 문서는 서재 '계약서',
        /// 회계 장부류는 서재 '장부', 서류함·내부 서류는 서재 '서류 더미'(주택 오브젝트 목록에 '서류함'이 따로 없음).
        /// </summary>
        private ContractConstraints PlanExperimentTruthConstraints()
        {
            // 03 최신 확정 규칙: 실제 살인·방법·도구·관계 ID·피해자 직업·사건 날짜를 모두 만족해야 한다.
            // 문서는 현 의뢰에 이미 존재하는 조사 오브젝트에 배치한다. 새 인물·장소·소품은 생성하지 않는다.
            // 배치 순서: 생존자 인계 기록 → 참가자 운영 규칙 안내문 → 학생 정보 획득 메모.
            // 사건 날짜 하한이 가장 늦은 조건부터 처리한다. 각 단서의 나머지 획득 조건은 그대로 유지한다.
            if (!MainCase.HasAcquired(MainCaseClueNames.SurvivorHandoverRecord))
            {
                return new ContractConstraints
                {
                    PlannedClueName = MainCaseClueNames.SurvivorHandoverRecord,
                    RequireMurder = true,
                    VictimOccupation = "공무원",
                    MethodId = "METHOD-02",
                    ToolId = "TOOL-01",
                    RelationshipId = "REL-05",
                    EarliestIncidentDate = "1984-11-20",
                    HolderIsVictim = true,
                    DocumentObjectId = CaseCatalog.ObjectId(HouseLayoutFactory.Study, "서류 더미")
                };
            }

            if (!MainCase.HasAcquired(MainCaseClueNames.ParticipantOperatingRulesNotice))
            {
                return new ContractConstraints
                {
                    PlannedClueName = MainCaseClueNames.ParticipantOperatingRulesNotice,
                    RequireMurder = true,
                    VictimOccupation = "학생",
                    MethodId = "METHOD-03",
                    ToolId = "TOOL-02",
                    RelationshipId = "REL-01",
                    EarliestIncidentDate = "1984-10-19",
                    HolderIsVictim = true,
                    DocumentObjectId = CaseCatalog.ObjectId(HouseLayoutFactory.Bedroom, "편지")
                };
            }

            if (!MainCase.HasAcquired(MainCaseClueNames.StudentInfoAcquisitionMemo))
            {
                return new ContractConstraints
                {
                    PlannedClueName = MainCaseClueNames.StudentInfoAcquisitionMemo,
                    RequireMurder = true,
                    VictimOccupation = "사무원",
                    MethodId = "METHOD-06",
                    ToolId = "TOOL-05",
                    RelationshipId = "REL-06",
                    EarliestIncidentDate = "1984-10-01",
                    HolderIsVictim = true,
                    DocumentObjectId = CaseCatalog.ObjectId(HouseLayoutFactory.Study, "서류 더미")
                };
            }

            return null;
        }

        private ContractConstraints PlanPowerTruthConstraints()
        {
            string Study(string obj) => CaseCatalog.ObjectId(HouseLayoutFactory.Study, obj);
            var plans = new[]
            {
                (Name: MainCaseClueNames.ExperimentCompositionDealProposal,
                    HolderTag: "특별사업 협의 담당자·외부 제안자와의 거래/연락 자료 접근자",
                    Date: "1984-10-21", ObjectId: Study("계약서")),
                (Name: MainCaseClueNames.ProductPurchaseWishList,
                    HolderTag: "민간 거래 대행인·개인 서류 관리",
                    Date: "1984-10-29", ObjectId: Study("서류 더미")),
                (Name: MainCaseClueNames.ProductSalesSettlement,
                    HolderTag: "특별사업 상품 판매·회계 자료 접근/보관자",
                    Date: "1984-11-01", ObjectId: Study("장부")),
                (Name: MainCaseClueNames.ViewingTicketRevenueSettlement,
                    HolderTag: "관람권·특별 접근권 발급/판매/정산 자료 접근자",
                    Date: "1984-11-01", ObjectId: Study("장부")),
                (Name: MainCaseClueNames.GovernmentSupportRewardLetter,
                    HolderTag: "정부 지지 협조·개인적 혜택 수신자",
                    Date: "1984-11-04", ObjectId: CaseCatalog.ObjectId(HouseLayoutFactory.Bedroom, "편지")),
                (Name: MainCaseClueNames.UpperClassHospitalityRecord,
                    HolderTag: "상류층 접대·후원 관계 기록 관리/열람자",
                    Date: "1984-11-11", ObjectId: Study("장부")),
                (Name: MainCaseClueNames.ExperimentContinuationDecisionRecord,
                    HolderTag: "실험 운영 문제·기록 불일치 제한 열람 검토 기록 접근자",
                    Date: "1984-11-14", ObjectId: Study("서류 더미")),
                (Name: MainCaseClueNames.CollaboratorRewardDissenterPenaltyRecord,
                    HolderTag: "협력·비협조에 따른 행정 조치 기록 관리/열람자",
                    Date: "1984-11-15", ObjectId: Study("서류 더미")),
            };

            // 03의 8개 자료는 고정 획득 순서를 갖지 않는다. 아직 미획득인 자료 중 하나를 먼저 선택하고,
            // 문서 원문이 지정한 보유/접근 역할, 날짜 및 기존 주택 조사 오브젝트를 제약으로 전달한다.
            // 특정 자료에 적혀 있지 않은 피해자 직업·범행 방법·도구·가해자 성격·대표 유형은 강제하지 않는다.
            // 서류함은 별도 오브젝트 목록에 없으므로 개인 서류는 기존 서류 더미로 대체한다.
            var remaining = plans.Where(p => !MainCase.HasAcquired(p.Name)).ToArray();
            if (remaining.Length == 0) return null;
            var selected = remaining[_random.Next(remaining.Length)];
            return new ContractConstraints
            {
                PlannedClueName = selected.Name,
                DocumentHolderTag = selected.HolderTag,
                EarliestIncidentDate = selected.Date,
                DocumentObjectId = selected.ObjectId
            };
        }

        /// <summary>
        /// 제안된 의뢰를 수락한다. 조직 조건 고정, 담당 경찰 배정(5명 중 1명), 조사 시작, 조직_01 검사.
        /// 담당 경찰 배정 규칙은 원문에 없어 살아 있는 경찰 중 무작위로 정한다(임시). 직전 의뢰에서 담당 경찰이 사망했으면
        /// 그때 정해 둔 다음 담당 경찰(+15를 받은 경찰)을 쓴다.
        /// </summary>
        public GameActionResult AcceptContract()
        {
            if (IsGameOver) return GameActionResult.Fail(GameActionFailure.GameOver);
            if (PendingOffer == null) return GameActionResult.Fail(GameActionFailure.NoOffer);
            if (State.CurrentPhase != GamePhase.Idle) return GameActionResult.Fail(GameActionFailure.WrongPhase);

            var alive = PoliceRoster.AllPoliceIds.Where(PoliceLife.IsAlive).ToList();
            if (alive.Count == 0) return GameActionResult.Fail(GameActionFailure.GameOver);

            var offer = PendingOffer;
            var contract = offer.Contract;
            contract.OrgCondition.Lock();

            string handler = Campaign.NextHandlerPoliceId != null && PoliceLife.IsAlive(Campaign.NextHandlerPoliceId)
                ? Campaign.NextHandlerPoliceId
                : NextHandlerFromQueue(alive);
            Campaign.HandlerQueue.Remove(handler);
            contract.HandlerPoliceId = handler;
            Campaign.NextHandlerPoliceId = null;
            Campaign.LastHandlerPoliceId = handler;
            Campaign.PersuasionAttempted = false;
            Campaign.CulpritIdentified = false;
            Opinions.Clear();
            Campaign.PublicRoles.Clear();
            InvestigatedObjectIds.Clear();
            RecentExchanges = new List<OverheardExchange>();
            RecentInvestigations = new List<SceneInvestigationRecord>();
            Campaign.PoliceInspectedObjectIds.Clear();
            Campaign.PhysicallyDistortedClueIds.Clear();
            Campaign.PlayerAccusedCaseId = null; // 이전 의뢰의 지목 상태를 다음 의뢰에 재사용하지 않는다.
            Campaign.StolenMaterialIds.Clear();
            Campaign.LaterVictimIds.Clear();
            Campaign.BlocksUntilSilencingCheck = 0;
            Campaign.SilencingPlanned = false;
            Campaign.TheftIncidents.Clear();
            if (Campaign.PendingHandlerDeathBonus)
            {
                // 이전 담당 경찰 사망의 추가 +10(기본 +5와 합쳐 +15)은 새로 배정된 담당 경찰에게 한 번만 적용한다.
                if (State.EventLog.TryRecord(new GameEvent($"handler-death-bonus:{contract.ContractId}", GameEventType.Custom, contract.ContractId, handler)))
                    PoliceSuspicion.ApplyBatch(handler, PlayerId, new[] { 10f });
                Campaign.PendingHandlerDeathBonus = false;
            }
            Campaign.HandlerStartingSuspicion = PoliceSuspicion.GetSuspicion(handler, PlayerId);
            Campaign.MeetingsEndedInContract = 0;

            State.BeginContract(contract.ContractId, contract.StartTimeBlock);

            CurrentContract = contract;
            Npcs = offer.Npcs;
            Rooms = offer.Rooms;
            Occupancy = offer.Occupancy;
            Materials = offer.Materials.ToList();
            Campaign.SceneRoomId = offer.Timeline.SceneRoomId;

            // 세계 시각: 사건 발생일 00:00을 0분으로 센다. 조사는 경찰 선행 수사 뒤 인계일의 시작 시간대(06:00/18:00)부터.
            Campaign.NpcMinute = InvestigationStartMinute(contract);
            Campaign.BlockStartMinute = BlockStartOf(Campaign.NpcMinute);

            // 06: 보드가 현장으로 확대되고 경찰이 상황을 설명한다 — 플레이어와 담당 경찰은 현장에서 시작한다(임시 배치).
            string startRoom = contract.PlayerStartRoomId != null && Rooms.RoomExists(contract.PlayerStartRoomId) ? contract.PlayerStartRoomId : Campaign.SceneRoomId;
            Occupancy.PlaceAt(PlayerId, startRoom);
            // 고정 사건은 담당 경찰도 원문 배치(응접실)를 쓴다. 생성 의뢰는 현장.
            Occupancy.PlaceAt(handler, contract.FixtureId != null ? HouseLayoutFactory.Reception : Campaign.SceneRoomId);

            BuildNpcSystems();
            ApplyBlockSchedules(placeAtScheduleStart: true);

            Firearm.ResetForContract();
            Report = new ContractReportState(contract.ContractId);
            Reports.Add(Report);
            _carriedOverQuestions = new List<CardQuestion>();
            _meetingIndex = 0;
            PendingOffer = null;

            State.EventLog.TryRecord(new GameEvent($"contract-accepted:{contract.ContractId}", GameEventType.Custom,
                contract.ContractId, $"handler={handler}"));

            if (OrganizationSupport.TryGrantOrg01(contract, Organization.Trust))
                GrantMainCaseDocument(OrganizationSupportClueService.Org01ClueId, MainCaseClueNames.CaseAssignmentMemo);

            PlaceRebelMemoIfNeeded(offer);
            RevealVictimFromPoliceSnapshot(contract);
            if (contract.PlannedMainCaseClue != null)
                Campaign.GeneratedMainDocuments.Add(contract.PlannedMainCaseClue);

            var result = GameActionResult.Ok();
            CheckEndings();
            return result;
        }

        /// <summary>
        /// 07 2026-11-13 담당 경찰 배정: 캠페인 시작 시 A~E를 게임 난수로 한 번 섞은 대기열에서 앞부터 배정하고, 소진 전에는 중복 배정하지 않는다.
        /// 사망·제거된 경찰은 건너뛴다. 소진되면 생존 경찰로 다시 섞되 직전 담당이 바로 다시 나오지 않게 한다(생존자 1명이면 그 경찰).
        /// </summary>
        private string NextHandlerFromQueue(List<string> alive)
        {
            Campaign.HandlerQueue.RemoveAll(id => !alive.Contains(id));
            if (Campaign.HandlerQueue.Count == 0)
            {
                var shuffled = alive.OrderBy(_ => _random.Next()).ToList();
                if (shuffled.Count > 1 && shuffled[0] == Campaign.LastHandlerPoliceId)
                {
                    shuffled.RemoveAt(0);
                    shuffled.Add(Campaign.LastHandlerPoliceId);
                }
                Campaign.HandlerQueue.AddRange(shuffled);
            }
            return Campaign.HandlerQueue[0];
        }

        /// <summary>NPC·경찰의 개인 의견 점수(06). 공식 결론 투표에 쓴다. 의뢰가 바뀌면 비운다.</summary>
        public OpinionScoreService Opinions { get; } = new OpinionScoreService();

        // ───────────────────────── 조사 ─────────────────────────

        /// <summary>이 의뢰의 조사 오브젝트(방별 후보 중 이번 사건에서 조사 가능한 것). 의뢰 데이터에서 다시 만들 수 있다.</summary>
        public IReadOnlyList<InvestigationObject> GetInvestigationObjects()
        {
            if (CurrentContract == null) return new List<InvestigationObject>();
            var objects = CaseDetailGenerator.BuildObjects(CurrentContract.ContractId, Materials);
            // 이 의뢰에 배치한 메인 사건 문서의 보관 오브젝트는 항상 조사할 수 있어야 한다.
            string planned = CurrentContract.PlannedClueObjectId;
            if (planned != null && objects.All(o => o.ObjectId != planned))
                objects.Add(new InvestigationObject { ObjectId = planned, RoomId = CaseCatalog.ObjectRoom(planned), Name = CaseCatalog.ObjectName(planned) });
            return objects;
        }

        /// <summary>조사한 오브젝트(다시 조사해도 새로 얻는 것은 없다 — 표시용).</summary>
        public HashSet<string> InvestigatedObjectIds { get; } = new HashSet<string>();

        /// <summary>
        /// 문서·자료 열람. 처음 확인하는 문서는 1시간(4칸), 이미 읽은 자료를 다시 여는 동작은 0칸이다.
        /// 의뢰 사이처럼 시간 예산이 없는 구간의 첫 열람도 0칸으로 기록한다.
        /// </summary>
        public GameActionResult ReadDocument(string documentId)
        {
            if (string.IsNullOrWhiteSpace(documentId))
                return GameActionResult.Fail(GameActionFailure.InvalidInput);

            if (Campaign.ReadDocumentIds.Contains(documentId))
                return GameActionResult.Ok();

            if (CurrentContract == null)
            {
                Campaign.ReadDocumentIds.Add(documentId);
                return GameActionResult.Ok();
            }

            var guard = GuardInvestigation(ReadDocumentCost);
            if (!guard.Success)
                return guard;

            var result = SpendTime(ReadDocumentCost);
            Campaign.ReadDocumentIds.Add(documentId);
            return AfterAction(result);
        }

        /// <summary>
        /// 지금 방의 조사 오브젝트 하나를 조사한다(06: 새 대상·물건 조사 1시간 = 4칸). 단서가 없을 수도 있다.
        /// 담당 경찰이 같은 방이면 새 단서마다 단서 확인 요청을 굴린다.
        /// </summary>
        public GameActionResult InvestigateObject(string objectId)
        {
            var guard = GuardInvestigation(InvestigateCost);
            if (!guard.Success) return guard;
            var obj = GetInvestigationObjects().FirstOrDefault(o => o.ObjectId == objectId);
            if (obj == null) return GameActionResult.Fail(GameActionFailure.UnknownTarget);
            if (obj.RoomId != PlayerRoom) return GameActionResult.Fail(GameActionFailure.TargetNotInRoom);

            var result = SpendTime(InvestigateCost);
            InvestigatedObjectIds.Add(objectId);
            foreach (var material in Materials.Where(m => m.Acquisition == MaterialAcquisition.InvestigateRoom && m.ObjectId == objectId && Unlocked(m)).ToList())
                DiscoverMaterial(material, result);

            if (PlayerRoom == Campaign.SceneRoomId)
                DiscoverMainCaseCluesAtScene(result);
            if (PlayerRoom == Campaign.RebelMemoRoomId)
                AcquireRebelMemo(result);
            DiscoverPlannedMainCaseDocument(objectId, result);

            RollClueConfirmations(result);
            return AfterAction(result);
        }

        // ───────────────────────── 절도·시신 탈취(06) ─────────────────────────

        /// <summary>시신 탈취 15분(06 「시신 탈취 — 실행 및 목격 조건」, 실패해도 소모).</summary>
        public const int BodyRemovalCost = 1;

        /// <summary>
        /// '금전적으로 도움이 될 것 같은' 물건이 놓이는 조사 대상(TEST_TEMP — 노션에 목록 없음. 사용자 지시 2026-11-20:
        /// 조직이 훔쳐 오라는 특정 물건 또는 금전적으로 도움이 될 것 같은 단서만 절도 대상).
        /// </summary>
        public static readonly HashSet<string> ValuableObjectNames = new HashSet<string> { "보석함", "장부", "계약서" };

        /// <summary>
        /// 훔칠 수 있는 자료인지: 지금 방 현장에 실제로 있고(조사로 얻는 자료·아직 훔치지 않음), NPC가 보관한 단서가 아니며,
        /// 조직 조건이 지정한 물건이거나 귀중품 대상의 물리 증거이거나 시신이어야 한다.
        /// </summary>
        public bool IsStealable(EvidenceMaterial m)
        {
            if (CurrentContract == null || m == null || m.ContractId != CurrentContract.ContractId) return false;
            if (m.Acquisition != MaterialAcquisition.InvestigateRoom || m.RoomId != PlayerRoom) return false;
            if (Campaign.StolenMaterialIds.Contains(m.MaterialId)) return false;
            if (Clues.Exists(m.MaterialId) && Clues.GetRecord(m.MaterialId).WorldState == ClueWorldState.HeldByNpc) return false;
            return IsBodyMaterial(m.MaterialId)
                   || m.MaterialId == CurrentContract.OrgCondition.TargetId
                   || (m.Format == ClueFormat.PhysicalObject && m.ObjectId != null && ValuableObjectNames.Contains(CaseCatalog.ObjectName(m.ObjectId)));
        }

        public bool IsBodyMaterial(string materialId) =>
            CurrentContract != null && (materialId == ContractPipeline.BodyMaterialId(CurrentContract.ContractId)
                || Materials.Any(m => m.MaterialId == materialId && m.IsBody));

        public IReadOnlyList<EvidenceMaterial> StealableMaterialsHere() => Materials.Where(IsStealable).ToList();

        /// <summary>
        /// 물건 절도(1시간, 항상 성공) / 시신 탈취(15분). 가져간 것은 플레이어 자료가 되고(인벤토리 없음) 현장에서 사라진다.
        /// 같은 방의 살아 있는 인물(NPC·경찰)은 즉시 목격한다(TEST_TEMP — 시야 기준이 노션에 없음). 목격이 있으면 부수사건을 하나 만든다.
        /// 목격이 없으면 나중에 그 물건을 아는 NPC가 그 방을 다시 확인할 때 부재를 발견한다(<see cref="CheckTheftAbsence"/>).
        /// 실제 진실·기존 기억·기록은 바꾸지 않는다. 경찰 의심도 변화는 노션에 수치가 없어 적용하지 않는다.
        /// </summary>
        public GameActionResult Steal(string materialId)
        {
            var m = Materials.FirstOrDefault(x => x.MaterialId == materialId);
            if (m == null) return GameActionResult.Fail(GameActionFailure.UnknownTarget);
            bool body = IsBodyMaterial(materialId);
            var guard = GuardInvestigation(body ? BodyRemovalCost : DefaultActionCost);
            if (!guard.Success) return guard;
            if (!IsStealable(m)) return GameActionResult.Fail(GameActionFailure.InvalidInput);

            var result = SpendTime(body ? BodyRemovalCost : DefaultActionCost);
            DiscoverMaterial(m, result);
            Campaign.StolenMaterialIds.Add(m.MaterialId);
            if (body)
            {
                string bodyNpcId = m.SubjectNpcId;
                if (string.IsNullOrEmpty(bodyNpcId) && m.MaterialId == ContractPipeline.BodyMaterialId(CurrentContract.ContractId))
                    bodyNpcId = CurrentContract.Truth.VictimId;
                if (!string.IsNullOrEmpty(bodyNpcId) && Npcs.Exists(bodyNpcId))
                    Occupancy.Remove(bodyNpcId); // 가져간 시신만 현장에서 치운다.
            }

            var incident = new TheftIncident { IncidentId = "theft:" + m.MaterialId, MaterialId = m.MaterialId, RoomId = m.RoomId, Minute = Campaign.NpcMinute, IsBody = body };
            foreach (var npc in Npcs.GetByContract(CurrentContract.ContractId).Where(n => n.IsAlive && Occupancy.HasPosition(n.NpcId) && Occupancy.GetRoom(n.NpcId) == PlayerRoom))
            {
                incident.WitnessIds.Add(npc.NpcId);
                npc.LearnInfo(incident.IncidentId, NpcJudgement.Believe, new NpcInfoRecord
                {
                    State = NpcInformationState.KnowsFact, Method = NpcInfoAcquisitionMethod.DirectObservation,
                    Minute = Campaign.NpcMinute, SourceEventId = incident.IncidentId
                });
                npc.MarkWitnessed(incident.IncidentId);
            }
            foreach (var police in PoliceRoster.AllPoliceIds.Where(p => PoliceLife.IsAlive(p) && Occupancy.HasPosition(p) && Occupancy.GetRoom(p) == PlayerRoom))
                incident.WitnessIds.Add(police);
            if (incident.WitnessIds.Count > 0 && Campaign.TheftIncidents.All(i => i.IncidentId != incident.IncidentId))
                Campaign.TheftIncidents.Add(incident);

            RollClueConfirmations(result);
            return AfterAction(result);
        }

        /// <summary>
        /// 부재 발견(06): 그 물건을 아는(그 자료의 사실을 보유한) 살아 있는 NPC가 그 방에 있으면 물건이 없어진 것을 알게 된다.
        /// '없어졌다'는 정보만 주고 범인은 확정하지 않는다. 같은 물건의 부수사건은 하나이며 발견자는 NPC별로 덧붙인다. 15분 턴마다 호출.
        /// </summary>
        /// <summary>
        /// NPC 신고(사용자 확정 2026-11-20 임시 효과): 플레이어의 절도·시신 탈취를 직접 본 NPC가 담당 경찰과 같은 방에 있으면 그 목격을 경찰에 전달한다.
        /// 부수사건의 목격자 목록에 경찰을 더하고 이벤트로 남긴다(목격자·사건마다 한 번). 의심도 수치는 노션에 없어 바꾸지 않는다.
        /// </summary>
        private void ReportWitnessedCrimesToPolice()
        {
            string police = HandlerPoliceId;
            if (CurrentContract == null || police == null || !PoliceLife.IsAlive(police) || !Occupancy.HasPosition(police)) return;
            string room = Occupancy.GetRoom(police);
            foreach (var incident in Campaign.TheftIncidents)
            {
                foreach (var witness in incident.WitnessIds.ToList())
                {
                    if (!Npcs.Exists(witness) || !Npcs.Get(witness).IsAlive || !Occupancy.HasPosition(witness) || Occupancy.GetRoom(witness) != room) continue;
                    if (!State.EventLog.TryRecord(new GameEvent($"reported:{incident.IncidentId}:{witness}", GameEventType.Custom, CurrentContract.ContractId, police)))
                        continue;
                    if (!incident.WitnessIds.Contains(police)) incident.WitnessIds.Add(police);
                }
            }
        }

        private void CheckTheftAbsence()
        {
            if (CurrentContract == null) return;
            foreach (var id in Campaign.StolenMaterialIds)
            {
                var m = Materials.FirstOrDefault(x => x.MaterialId == id);
                if (m == null) continue;
                var incident = Campaign.TheftIncidents.FirstOrDefault(i => i.MaterialId == id);
                foreach (var npc in Npcs.GetByContract(CurrentContract.ContractId).Where(n => n.IsAlive && Occupancy.HasPosition(n.NpcId) && Occupancy.GetRoom(n.NpcId) == m.RoomId))
                {
                    if (!m.StatedInfoIds.Any(npc.Knows)) continue;
                    if (incident != null && (incident.WitnessIds.Contains(npc.NpcId) || incident.AbsenceNoticedIds.Contains(npc.NpcId))) continue;
                    if (incident == null)
                    {
                        incident = new TheftIncident { IncidentId = "theft:" + id, MaterialId = id, RoomId = m.RoomId, Minute = Campaign.NpcMinute, IsBody = IsBodyMaterial(id) };
                        Campaign.TheftIncidents.Add(incident);
                    }
                    incident.AbsenceNoticedIds.Add(npc.NpcId);
                    npc.LearnInfo("missing:" + id, NpcJudgement.Believe, new NpcInfoRecord
                    {
                        State = NpcInformationState.KnowsFact, Method = NpcInfoAcquisitionMethod.DirectObservation,
                        Minute = Campaign.NpcMinute, SourceEventId = incident.IncidentId
                    });
                }
            }
        }

        /// <summary>
        /// 같은 방의 NPC를 관찰한다(06: 0.5시간 = 2칸). 그 인물이 지금 들고 있는 단서(현장에서 주운 것)와 하는 일을 확인한다.
        /// 관찰로 알게 된 단서는 플레이어가 소유하지 않는다(원문 — 관찰은 정보 획득 경로).
        /// </summary>
        public GameActionResult Observe(string npcId)
        {
            var guard = GuardInvestigation(ObserveCost);
            if (!guard.Success) return guard;
            var target = GuardNpcInRoom(npcId);
            if (!target.Success) return target;

            var result = SpendTime(ObserveCost);
            LastObservation = new ObservationRecord
            {
                NpcId = npcId,
                Minute = Campaign.NpcMinute,
                HeldClueIds = Capacity.GetHeldClues(npcId).ToList(),
                ScheduledRoom = Planner?.GetScheduledRoom(npcId, Campaign.NpcMinute + ObserveCost * NpcMinutesPerTimeSlot)
            };
            return AfterAction(result);
        }

        public ObservationRecord LastObservation { get; private set; }

        /// <summary>
        /// 08 엿듣기 대상: 지금(현재 15분 차례) 플레이어와 같은 방에서 오가는 NPC 대화만. 이미 지나간 대화는 들을 수 없다.
        /// </summary>
        public IReadOnlyList<OverheardExchange> CurrentOverhearableExchanges() =>
            CurrentContract == null || PlayerRoom == null
                ? (IReadOnlyList<OverheardExchange>)Array.Empty<OverheardExchange>()
                : RecentExchanges.Where(e => !e.HeardByPlayer && e.RoomId == PlayerRoom && e.Minute == Campaign.NpcMinute).ToList();

        /// <summary>
        /// 엿듣기(06: 0.5시간 = 2칸). 지금 같은 방에서 오가는 NPC 대화를 듣고 그 내용을 증언 자료로 얻는다(08: 현재 대화만).
        /// 들을 대화가 없으면 실행하지 않는다(시간도 쓰지 않음). 경찰이 목격하면 의심도에 영향이 있다고만 되어 있어 수치는 적용하지 않는다(원문 미정).
        /// </summary>
        public GameActionResult Overhear()
        {
            var guard = GuardInvestigation(OverhearCost);
            if (!guard.Success) return guard;
            var targets = CurrentOverhearableExchanges().ToList();
            if (targets.Count == 0) return GameActionResult.Fail(GameActionFailure.UnknownTarget);

            var result = SpendTime(OverhearCost);
            foreach (var exchange in targets)
            {
                exchange.HeardByPlayer = true;
                string id = $"{CurrentContract.ContractId}-overheard-{exchange.Minute}-{exchange.SpeakerId}";
                if (Clues.Exists(id)) continue;
                var material = new EvidenceMaterial
                {
                    MaterialId = id,
                    ContractId = CurrentContract.ContractId,
                    Format = ClueFormat.Testimony,
                    SourceNpcId = exchange.SpeakerId,
                    Acquisition = MaterialAcquisition.Overheard,
                    StatedInfoIds = { exchange.InfoId },
                    Title = $"엿들은 대화 — {Npcs.Get(exchange.SpeakerId).DisplayName}",
                    Content = $"{Npcs.Get(exchange.SpeakerId).DisplayName}이(가) {Npcs.Get(exchange.ListenerId).DisplayName}에게 낮은 목소리로 말했다: \"{DescribeInfo(exchange.InfoId)}\""
                };
                Materials.Add(material);
                DiscoverMaterial(material, result);
            }
            return AfterAction(result);
        }

        /// <summary>정보 ID를 엿들은 문장으로 바꾼다(TEST_TEMP 문장). 사건 정보면 그 사건을, 위치 정보면 그 위치를 말한다.</summary>
        private string DescribeInfo(string infoId)
        {
            var related = Materials.FirstOrDefault(m => m.StatedInfoIds.Contains(infoId) && m.Content != null);
            if (related != null) return related.Content.Contains(": \"") ? related.Content.Substring(related.Content.IndexOf(": \"") + 3).TrimEnd('"') : related.Content;
            return "그날 일 말이야… 아무한테도 말하지 마.";
        }

        /// <summary>인접한 방으로 이동한다. 06 확정: 이동은 0시간 — 세계 시각도 NPC도 진행하지 않는다.</summary>
        public GameActionResult MovePlayer(string roomId)
        {
            var guard = GuardInvestigation(MoveCost);
            if (!guard.Success) return guard;
            if (!Rooms.RoomExists(roomId) || !Rooms.AreConnected(PlayerRoom, roomId))
                return GameActionResult.Fail(GameActionFailure.RoomNotConnected);

            Occupancy.TryMove(PlayerId, roomId);
            return AfterAction(GameActionResult.Ok());
        }

        /// <summary>
        /// 지금 방을 조사한다. 기본 1칸, 상세 수색은 2칸(06 2026-11-10). 이 방에서 얻을 수 있는 자료를 얻고, 떨어진 단서를 줍고,
        /// 사건 현장이면 조건이 맞는 메인 사건 단서를 발견한다. 담당 경찰이 같은 방이면 새 단서마다 단서 확인 요청을 굴린다.
        /// </summary>
        public GameActionResult InvestigateRoom(bool detailedSearch = false)
        {
            int cost = InvestigateCost; // 06 확정: 상세 수색이라고 시간이 추가되지 않는다.
            var guard = GuardInvestigation(cost);
            if (!guard.Success) return guard;

            var result = SpendTime(cost);
            string room = PlayerRoom;

            foreach (var material in Materials.Where(m => m.Acquisition == MaterialAcquisition.InvestigateRoom && m.RoomId == room))
                DiscoverMaterial(material, result);

            foreach (var clueId in Occupancy.GetOccupants(room).ToList())
            {
                if (Clues.Exists(clueId) && Clues.GetRecord(clueId).WorldState == ClueWorldState.DroppedInWorld)
                {
                    Clues.PickUpByPlayer(clueId);
                    Occupancy.Remove(clueId);
                }
            }

            if (room == Campaign.SceneRoomId)
                DiscoverMainCaseCluesAtScene(result);

            if (room == Campaign.RebelMemoRoomId)
                AcquireRebelMemo(result);

            RollClueConfirmations(result);
            return AfterAction(result);
        }

        /// <summary>같은 방의 살아 있는 NPC와 대화해 그 NPC의 증언을 얻는다(06: 0.5시간 = 2칸).</summary>
        public GameActionResult TalkTo(string npcId)
        {
            var guard = GuardInvestigation(TalkCost);
            if (!guard.Success) return guard;
            var target = GuardNpcInRoom(npcId);
            if (!target.Success) return target;

            var result = SpendTime(TalkCost);
            foreach (var material in Materials.Where(m => m.Acquisition == MaterialAcquisition.TalkToNpc && m.SourceNpcId == npcId && Unlocked(m)).ToList())
                DiscoverMaterial(material, result);

            RollClueConfirmations(result);
            return AfterAction(result);
        }

        /// <summary>
        /// 실제 가해자 회유(1칸). playerKnowsActualCulprit는 "플레이어가 실제 가해자를 알아냈는지"이며 그 판정 기준이 원문에 없어
        /// 호출하는 쪽(UI·추리 단계)이 넘긴다. 성공하면 그 NPC가 아는 모든 정보를 공개하고, 그 NPC에게서 얻을 자료를 전부 얻는다.
        /// </summary>
        public GameActionResult Persuade(string npcId, bool playerKnowsActualCulprit, bool hasStrongSupportingEvidence, bool hasUnfavorableDirectEvidence)
        {
            // 기존 호출부 호환용 오버로드다. 판정은 호출자가 넘긴 bool이 아니라 검증된 캠페인 상태와 실제 보유 자료로 다시 계산한다.
            return PersuadeFromValidatedState(npcId, GetPersuasionStrongEvidence(), GetPersuasionDirectEvidence());
        }

        private GameActionResult PersuadeFromValidatedState(string npcId, bool hasStrongSupportingEvidence, bool hasUnfavorableDirectEvidence)
        {
            var guard = GuardInvestigation(DefaultActionCost);
            if (!guard.Success) return guard;
            // 07: 단순 지목이나 호출 인자만으로는 안 된다. 보고서 판정에서 실제 가해자 식별이 기록되어 있어야 한다.
            if (Campaign.PersuasionAttempted || !Campaign.CulpritIdentified)
                return GameActionResult.Fail(GameActionFailure.InvalidInput);

            var target = GuardNpcInRoom(npcId);
            if (!target.Success) return target;

            var npc = Npcs.Get(npcId);
            if (!npc.IsActualPerpetrator)
                return GameActionResult.Fail(GameActionFailure.InvalidInput);

            Campaign.PersuasionAttempted = true;
            var result = SpendTime(DefaultActionCost);
            var persuasion = _persuasion.TryPersuade(npc,
                playerKnowsActualCulprit: true,
                hasStrongSupportingEvidence: hasStrongSupportingEvidence,
                hasUnfavorableDirectEvidence: hasUnfavorableDirectEvidence,
                targetAlreadyTrustsPlayer: Relationships.GetTrust(npcId, PlayerId) > 0f);
            if (persuasion.Success)
            {
                foreach (var material in Materials.Where(m => m.SourceNpcId == npcId))
                    DiscoverMaterial(material, result);
            }
            LastPersuasion = persuasion;
            return AfterAction(result);
        }

        public PersuasionResult LastPersuasion { get; private set; }

        /// <summary>
        /// 07 2026-11-12 회유(시간 비용은 원문에 없어 기본 1시간). 시도 조건: 의뢰 보고서의 가해자 문단이 내용·증거 모두 통과해
        /// 실제 가해자를 정답으로 식별한 상태가 기록되어 있어야 한다. 강한 신원 근거(+20)는 가해자를 특정하는 검증된 자료,
        /// 불리한 직접 증거(-15)는 가해 행위를 직접 뒷받침하는 물리 자료(범행 도구)다. 같은 자료가 둘 다면 직접 증거만 적용한다.
        /// </summary>
        public GameActionResult Persuade(string npcId) =>
            PersuadeFromValidatedState(npcId, GetPersuasionStrongEvidence(), GetPersuasionDirectEvidence());

        private bool GetPersuasionDirectEvidence() =>
            CurrentContract != null && Clues.GetAllClueIds(CurrentContract.ContractId)
                .Any(id => Clues.GetDefinition(id).IsDirectPerpetratorEvidence);

        private bool GetPersuasionStrongEvidence() =>
            CurrentContract != null && Clues.GetAllClueIds(CurrentContract.ContractId)
                .Any(id => Clues.GetDefinition(id).IdentifiesPerpetrator
                           && !Clues.GetDefinition(id).IsDirectPerpetratorEvidence);

        /// <summary>선행 자료 조건: 행동 전에 이미 보유한 자료만 센다(같은 행동에서 얻은 자료로 바로 다음 자료가 열리지 않음).</summary>
        private bool Unlocked(EvidenceMaterial m) => m.RequiresClueId == null || Clues.Exists(m.RequiresClueId);

        private EvidenceMaterial PlaytestMaterial(string id) => Materials.FirstOrDefault(m => m.MaterialId == id);

        /// <summary>들고 있는 단서를 지금 방에 버린다(0칸). 보고서 기록은 남고, NPC·경찰이 주울 수 있다.</summary>
        public GameActionResult DropClue(string clueId)
        {
            var guard = GuardInvestigation(DropClueCost);
            if (!guard.Success) return guard;
            if (!Clues.Exists(clueId) || Clues.GetRecord(clueId).WorldState != ClueWorldState.HeldByPlayer)
                return GameActionResult.Fail(GameActionFailure.InvalidInput);

            var dropResult = SpendTime(DropClueCost);
            Clues.Drop(clueId);
            Occupancy.PlaceAt(clueId, PlayerRoom);
            // 06 §8: 경찰이 보는 앞에서 버리면 발각된다(버린 이유를 의심할 수 있음). 시야 정의·의심도 수치가 노션에 없어
            // 같은 방의 살아 있는 경찰을 목격자로 기록만 한다(TEST_TEMP). 보고서 기록은 그대로 남는다.
            foreach (var police in PoliceRoster.AllPoliceIds.Where(p => PoliceLife.IsAlive(p) && Occupancy.HasPosition(p) && Occupancy.GetRoom(p) == PlayerRoom))
                State.EventLog.TryRecord(new GameEvent($"drop-seen:{clueId}:{police}", GameEventType.Custom, CurrentContract.ContractId, police));
            return AfterAction(dropResult);
        }

        /// <summary>현장에 떨어진 단서(내가 버린 것 등)를 같은 방에서 다시 줍는다(06: 물건 조사와 같은 1시간).</summary>
        public GameActionResult PickUpClue(string clueId)
        {
            var guard = GuardInvestigation(InvestigateCost);
            if (!guard.Success) return guard;
            if (!Clues.Exists(clueId) || Clues.GetRecord(clueId).WorldState != ClueWorldState.DroppedInWorld
                || !Occupancy.HasPosition(clueId) || Occupancy.GetRoom(clueId) != PlayerRoom)
                return GameActionResult.Fail(GameActionFailure.InvalidInput);

            var result = SpendTime(InvestigateCost);
            Clues.PickUpByPlayer(clueId);
            Occupancy.Remove(clueId);
            return AfterAction(result);
        }

        /// <summary>지금 방 바닥에 떨어져 있는 단서.</summary>
        public IReadOnlyList<string> DroppedCluesHere() =>
            PlayerRoom == null ? new List<string>()
            : Occupancy.GetOccupants(PlayerRoom).Where(id => Clues.Exists(id) && Clues.GetRecord(id).WorldState == ClueWorldState.DroppedInWorld).ToList();

        /// <summary>재장전(06 확정: 1시간 = 4칸).</summary>
        public GameActionResult Reload()
        {
            var guard = GuardInvestigation(DefaultActionCost);
            if (!guard.Success) return guard;
            if (!Firearm.CanReload) return GameActionResult.Fail(GameActionFailure.NothingToReload);

            var result = SpendTime(DefaultActionCost);
            Firearm.Reload();
            return AfterAction(result);
        }

        public ShotEvent LastShot { get; private set; }

        /// <summary>
        /// 같은 방의 NPC나 경찰을 쏜다(06 확정: 1시간 = 4칸). 사격·목격·사망은 별도 이벤트로 기록하며 사격만으로 의심도를 올리지 않는다(06 확정).
        /// 같은 방의 살아 있는 NPC는 사격을 목격한다. 명중 시 표적이 사망한다 — 표적 상태 변화 규칙이 원문에 없어 임시 해석이다.
        /// 담당 경찰이 사망하면 의뢰는 미제로 끝난다. 사망은 추적 사건 발동 검사(3명 이상 70 이상 + 추가 사망)로 이어진다.
        /// </summary>
        public GameActionResult FireAt(string targetId, bool isCloseRange, bool hasUnstableAim, bool targetMovingFast, bool pathPartiallyBlocked)
        {
            var guard = GuardInvestigation(DefaultActionCost);
            if (!guard.Success) return guard;
            if (!Firearm.CanFire) return GameActionResult.Fail(GameActionFailure.NoAmmo);

            bool isPolice = PoliceRoster.AllPoliceIds.Contains(targetId);
            bool isNpc = Npcs.Exists(targetId);
            if (!isPolice && !isNpc) return GameActionResult.Fail(GameActionFailure.UnknownTarget);
            if (!Occupancy.HasPosition(targetId) || Occupancy.GetRoom(targetId) != PlayerRoom)
                return GameActionResult.Fail(GameActionFailure.TargetNotInRoom);
            if ((isPolice && !PoliceLife.IsAlive(targetId)) || (isNpc && !Npcs.Get(targetId).IsAlive))
                return GameActionResult.Fail(GameActionFailure.TargetDeceased);

            var result = SpendTime(DefaultActionCost);
            string room = PlayerRoom;
            var shot = _firearmResolver.Fire(Firearm, PlayerId, targetId, isCloseRange, hasUnstableAim, targetMovingFast,
                pathPartiallyBlocked, room, State.EventLog, CurrentContract.ContractId, NextEventId("shot"));
            LastShot = shot;

            foreach (var witness in Npcs.GetByContract(CurrentContract.ContractId)
                         .Where(n => n.IsAlive && n.NpcId != targetId && Occupancy.HasPosition(n.NpcId) && Occupancy.GetRoom(n.NpcId) == room))
            {
                shot.WitnessNpcIds.Add(witness.NpcId);
                shot.InfoAcquiredByNpcIds.Add(witness.NpcId);
                witness.LearnInfo(shot.EventId, NpcJudgement.Believe, new NpcInfoRecord
                {
                    State = NpcInformationState.KnowsFact, Method = NpcInfoAcquisitionMethod.DirectObservation,
                    Minute = Campaign.NpcMinute, SourceEventId = shot.EventId
                });
                witness.MarkWitnessed(shot.EventId);
            }
            // 같은 방에 있던 살아 있는 경찰도 목격자로 기록한다(06 사격 이벤트 "목격자" 기록). 사격만으로 의심도는 바꾸지 않는다.
            foreach (var police in PoliceRoster.AllPoliceIds.Where(p => p != targetId && PoliceLife.IsAlive(p) && Occupancy.HasPosition(p) && Occupancy.GetRoom(p) == room))
                shot.WitnessNpcIds.Add(police);

            if (shot.Hit)
            {
                shot.TargetStateChangeNote = "사망(임시 해석)";
                string deathEventId = shot.EventId + ":death";

                if (isPolice)
                {
                    PoliceLife.MarkDeceased(targetId);
                    State.EventLog.TryRecord(new GameEvent(deathEventId, GameEventType.PoliceDeath, CurrentContract.ContractId, targetId));
                }
                else
                {
                    Npcs.Get(targetId).MarkDeceased();
                    if (!Campaign.LaterVictimIds.Contains(targetId))
                        Campaign.LaterVictimIds.Add(targetId);
                    State.EventLog.TryRecord(new GameEvent(deathEventId, GameEventType.Custom, CurrentContract.ContractId, "npc-death:" + targetId));
                    RegisterNewDeathBody(targetId, room, deathEventId);
                }

                if (Pursuit.Stage == PursuitStage.NotActive)
                    Pursuit.TryTrigger(PlayerId, deathEventId, PoliceRoster.AllPoliceIds.Where(PoliceLife.IsAlive));

                if (isPolice && targetId == HandlerPoliceId)
                {
                    FinalizeContract(ContractOutcome.HandlerPoliceDeceased, abandoned: false);
                    return result;
                }
            }

            return AfterAction(result);
        }

        /// <summary>
        /// 물리적 증거·기록 조작 가능 여부(04 §5, 사용자 확정 2026-11-10):
        /// 메인 사건의 최종 보고서(타자기 보고서) 14칸이 모두 완성되었고, 지금 의뢰에서 첫 회의가 끝났을 때부터 가능하다.
        /// 의뢰 보고서 제출은 이 조건과 무관하다. 조사·회의 중 거짓말과 정보 은폐(카드 거짓 진술·왜곡·은폐)는 이 제한을 받지 않는다.
        /// "첫 회의"를 지금 의뢰의 첫 회의로 보는 것은 해석이다(의뢰마다 다시 센다).
        /// </summary>
        public bool IsPhysicalManipulationUnlocked =>
            FinalReport.Report.IsComplete && Campaign.MeetingsEndedInContract >= 1;

        /// <summary>
        /// 물리적 증거·기록 조작 시도(06 확정: 보유 단서 조작 1시간 = 4칸).
        /// 조작 가능 시점 전이면 Locked로 거부되고 시간도 쓰지 않는다. 06 2026-11-11·12 확정: 플레이어가 보유한 물리적 증거 단서만 대상이며
        /// 성공 70%·실패 30% 고정이고, 성공·실패 모두 NPC나 경찰에게 발각되지 않는다(보정값 없음 — targetUnawareOfFacts는 호환용으로 무시).
        /// </summary>
        public ManipulationOutcomeResult AttemptPhysicalManipulation(string clueId, ManipulationType type, bool targetUnawareOfFacts = false)
        {
            var guard = GuardInvestigation(DefaultActionCost);
            if (!guard.Success)
                return new ManipulationOutcomeResult
                {
                    // 시간 부족은 잠김과 구분해 알린다(06 시간 부족 행동 처리 — 행동·시간 진행 모두 시작하지 않음).
                    Outcome = guard.Failure == GameActionFailure.NotEnoughTime ? ManipulationOutcome.NotEnoughTime : ManipulationOutcome.Locked
                };
            if (!IsPhysicalManipulationUnlocked)
                return new ManipulationOutcomeResult { Outcome = ManipulationOutcome.Locked };
            if (!Clues.Exists(clueId) || Clues.GetRecord(clueId).WorldState != ClueWorldState.HeldByPlayer)
                throw new ArgumentException("플레이어가 들고 있는 단서만 조작할 수 있습니다.", nameof(clueId));
            if (Clues.GetDefinition(clueId).Format != ClueFormat.PhysicalObject)
                throw new ArgumentException("물리적 증거 단서만 조작할 수 있습니다.", nameof(clueId));

            if (type == ManipulationType.Conceal && Clues.GetHeldByPlayerClueIds(CurrentContract.ContractId).Count < 2)
                throw new ArgumentException("은폐하려면 대체할 다른 단서가 있어야 합니다.", nameof(type));

            var result = SpendTime(DefaultActionCost);
            var outcome = PhysicalManipulation.Attempt(type, clueId, PlayerId, false, false, false, false,
                Clues.GetHeldByPlayerClueIds(CurrentContract.ContractId));
            // 2026-11-16 확정 은폐: 지정한 단서 하나의 접근 상태만 바꾼다. 보유 단서 조작은 성공·실패 모두 발각되지 않는다(06).
            if (type == ManipulationType.Conceal && outcome.Outcome != ManipulationOutcome.Locked)
                Clues.GetRecord(clueId).ApplyConcealment(outcome.Outcome == ManipulationOutcome.Succeeded, detected: false);
            // 06 2026-11-12: 왜곡에 성공한 보유 단서는 이후 회의에 제시하면 조작된 내용으로 판정·개인 의견에 반영한다.
            if (type == ManipulationType.Distort && outcome.Outcome == ManipulationOutcome.Succeeded && !Campaign.PhysicallyDistortedClueIds.Contains(clueId))
                Campaign.PhysicallyDistortedClueIds.Add(clueId);
            AfterAction(result);
            return outcome;
        }

        /// <summary>
        /// 시간 보내기(09 2026-11-12). 다른 행동 없이 지금 위치에 머문 채 남은 조사 예산과 세계 시각을 함께 진행하고,
        /// 그동안 NPC 일정·위치·정보 전달을 시간순으로 갱신한다. 남은 예산보다 긴 시간은 거부한다(상태 변화 없음).
        /// 1칸 = 15분(06 2026-11-12 확정 시간 단위).
        /// 예산을 다 쓰면 구간이 끝나며, 이후 처리(회의 또는 다음 구간)는 기존 구간 종료 흐름을 따른다.
        /// </summary>
        public GameActionResult PassTime(int slots)
        {
            if (slots <= 0) return GameActionResult.Fail(GameActionFailure.InvalidInput);
            var guard = GuardInvestigation(slots);
            if (!guard.Success) return guard;
            return AfterAction(SpendTime(slots));
        }

        /// <summary>
        /// 예산이 남아 있어도 이번 조사 구간을 끝낸다. 06 확정: 끝내면 시간대 끝(18:00 또는 다음 날 06:00)까지 자동 진행하며,
        /// 그동안 플레이어 행동은 없고 NPC 일정·위치·정보 전달만 시간순으로 처리한다.
        /// </summary>
        public GameActionResult EndInvestigationEarly()
        {
            if (IsGameOver) return GameActionResult.Fail(GameActionFailure.GameOver);
            if (State.CurrentPhase != GamePhase.Investigation) return GameActionResult.Fail(GameActionFailure.WrongPhase);
            if (State.IsTimeBlockEnded) return GameActionResult.Fail(GameActionFailure.TimeBlockEnded);
            State.EndTimeBlockEarly();
            AdvanceWorldTo(Campaign.BlockStartMinute + GameStateController.BlockMinutes);
            CheckEndings();
            return GameActionResult.Ok();
        }

        /// <summary>구간이 끝난 뒤 회의 대신 다음 조사 구간으로 넘어간다(아침→저녁→다음 날 아침). 횟수 제한 없음(06 확정).</summary>
        public GameActionResult AdvanceToNextTimeBlock()
        {
            if (IsGameOver) return GameActionResult.Fail(GameActionFailure.GameOver);
            if (State.CurrentPhase != GamePhase.Investigation) return GameActionResult.Fail(GameActionFailure.WrongPhase);
            if (!State.IsTimeBlockEnded) return GameActionResult.Fail(GameActionFailure.TimeBlockNotEnded);
            State.AdvanceTimeBlock();
            StartNextWorldBlock();
            return GameActionResult.Ok();
        }

        /// <summary>그 세계 시각이 속한 시간대의 시작 시각(06:00 또는 18:00).</summary>
        public static int BlockStartOf(int worldMinute)
        {
            int day = worldMinute / 1440, m = worldMinute % 1440;
            if (m >= GameStateController.EveningStartMinuteOfDay) return day * 1440 + GameStateController.EveningStartMinuteOfDay;
            if (m >= GameStateController.MorningStartMinuteOfDay) return day * 1440 + GameStateController.MorningStartMinuteOfDay;
            return (day - 1) * 1440 + GameStateController.EveningStartMinuteOfDay;
        }

        /// <summary>
        /// 고정 테스트 사건을 제안한다(TEST_TEMP — Editor/Development 전용). 원문 배정 경찰이 있으면 그 경찰을 다음 담당으로 둔다.
        /// 생성 파이프라인·검증기를 거치지 않는 손으로 쓴 데이터다.
        /// </summary>
        public void OfferFixture(GeneratedContract fixture, string assignedPoliceId = null)
        {
            if (IsGameOver) throw new InvalidOperationException("게임이 종료되었습니다.");
            if (State.CurrentPhase != GamePhase.Idle) throw new InvalidOperationException("진행 중인 의뢰가 있습니다.");
            PendingOffer = fixture ?? throw new ArgumentNullException(nameof(fixture));
            if (assignedPoliceId != null && PoliceLife.IsAlive(assignedPoliceId))
                Campaign.NextHandlerPoliceId = assignedPoliceId;
        }

        /// <summary>현재 세계 시각(사건 발생일 00:00 기준 분).</summary>
        public int WorldMinute => Campaign.NpcMinute;

        /// <summary>이번 시간대가 끝나는 세계 시각.</summary>
        public int BlockEndMinute => Campaign.BlockStartMinute + GameStateController.BlockMinutes;

        /// <summary>조사 시작 세계 시각: 인계일(사건일 + 경찰 선행 수사 일수)의 시작 시간대 첫 시각.</summary>
        public static int InvestigationStartMinute(ContractInstance contract) =>
            contract.InvestigationStartMinuteOverride >= 0 ? contract.InvestigationStartMinuteOverride :
            contract.HandoverDayOffset * 1440 + (contract.StartTimeBlock == TimeBlock.Morning
                ? GameStateController.MorningStartMinuteOfDay
                : GameStateController.EveningStartMinuteOfDay);

        /// <summary>
        /// 의뢰 포기(06: 보드게임 공간에서 경찰에게 대화로 포기, 신뢰도 -2). 회의 중 포기는 원문에 없어 조사 단계에서만 허용한다.
        /// 포기 시 경찰 의심도 변화는 원문에 없어 적용하지 않는다(임시).
        /// </summary>
        public GameActionResult AbandonContract()
        {
            if (IsGameOver) return GameActionResult.Fail(GameActionFailure.GameOver);
            if (State.CurrentPhase != GamePhase.Investigation) return GameActionResult.Fail(GameActionFailure.WrongPhase);
            FinalizeContract(ContractOutcome.Unsolved, abandoned: true);
            return GameActionResult.Ok();
        }

        // ───────────────────────── 의뢰 보고서 ─────────────────────────

        /// <summary>의뢰 보고서 제출. 통과하면 지목한 인물이 공식 결론의 지목 대상으로 기록된다(결론 확정은 회의가 한다).</summary>
        public ReportSubmissionResult SubmitReport()
        {
            if (CurrentContract == null) throw new InvalidOperationException("진행 중인 의뢰가 없습니다.");
            return ReportSubmissionCoordinator.Submit(CurrentContract, Report, Clues, new ContractReportValidator());
        }

        // ───────────────────────── 회의 ─────────────────────────

        /// <summary>
        /// 조사 구간이 끝난 뒤 회의를 연다. 증거가 부족해도 열 수 있다. 참가자는 플레이어, 살아 있는 의뢰 NPC, 살아 있는 담당 경찰.
        /// 플레이어에게 개인 단서 카드 5장을 무작위로 준다. 차례 순서는 원문에 없어 플레이어부터(임시).
        /// </summary>
        public GameActionResult StartMeeting() => StartMeetingCore(null);

        /// <summary>
        /// 09·06 최신 확정: 회의 전에 보고서의 본인 획득 카드 중 손패 5장(보유가 5장 미만이면 전부)을 직접 골라 회의를 연다.
        /// 고르지 않은 카드는 대체용 카드 묶음이 된다. 잘못된 선택이면 회의를 열지 않는다.
        /// </summary>
        public GameActionResult StartMeeting(IReadOnlyList<string> chosenHand)
        {
            if (chosenHand == null) return GameActionResult.Fail(GameActionFailure.InvalidInput);
            return StartMeetingCore(chosenHand);
        }

        private GameActionResult StartMeetingCore(IReadOnlyList<string> chosenHand)
        {
            if (IsGameOver) return GameActionResult.Fail(GameActionFailure.GameOver);
            if (State.CurrentPhase != GamePhase.Investigation) return GameActionResult.Fail(GameActionFailure.WrongPhase);
            if (!State.IsTimeBlockEnded) return GameActionResult.Fail(GameActionFailure.TimeBlockNotEnded);

            var held = Clues.GetHeldByPlayerClueIds(CurrentContract.ContractId);
            var flow = new MeetingFlow(PlayerId);
            if (chosenHand == null)
                flow.DealPlayerHand(held, _random);
            else if (!flow.TrySetPlayerHand(chosenHand, held))
                return GameActionResult.Fail(GameActionFailure.InvalidInput);

            State.EnterMeeting();
            _meetingIndex++;
            // 07 3-3: 추적 중 회의가 플레이어 대응 단계다(기존 카드·진술 시스템). 성공은 의심도로 판정(CheckEndings → Pursuit.Evaluate),
            // 대응에 실패해도 추적은 계속되고 즉시 게임 오버가 아니다. 의심도 100이면 체포가 우선한다.
            if (Pursuit.Stage == PursuitStage.GatheringInformation)
                Pursuit.AdvanceToPlayerResponse();
            Meeting = new MeetingSession(CurrentContract.ContractId, Manipulation);
            _participantPresented.Clear();
            MeetingResolver = new MeetingCardResolver(Meeting, PlayerId, _carriedOverQuestions);
            _carriedOverQuestions = new List<CardQuestion>();

            MeetingFlow = flow;
            // 06 2026-11-13: 회의 시작 시점에 회의가 열린 장소에 실제로 있는 생존 인물 전원이 참가한다(실종자·사망자·미배치 인물 제외).
            // 사용자 확정 2026-11-20: 회의는 카드게임이라 한 방에 모일 필요가 없다 — '장소'는 사건 주택(현장) 전체로 본다.
            // 주택에 배치되지 않은 인물(실종 등)은 사건 데이터에 있어도 소환하지 않는다.
            foreach (var npc in Npcs.GetByContract(CurrentContract.ContractId)
                         .Where(n => n.IsAlive && Occupancy.HasPosition(n.NpcId)))
                MeetingFlow.AddParticipant(npc.NpcId, MeetingParticipantKind.Npc);
            foreach (var policeId in PoliceRoster.AllPoliceIds
                         .Where(id => PoliceLife.IsAlive(id) && Occupancy.HasPosition(id)))
                MeetingFlow.AddParticipant(policeId, MeetingParticipantKind.Police);

            MeetingFlow.ApplyConfirmedTurnOrder(_random);
            // NPC·경찰이 자기 차례에 어떤 카드를 낼지는 원문에 판단 알고리즘이 없어 그 차례는 넘긴다(의심도 변화 없음) — 플레이어 차례까지 진행.
            AdvanceToPlayerTurn();
            ApplyWitnessOpinions();
            TryHandlerAccusation();
            return GameActionResult.Ok();
        }

        /// <summary>
        /// 직접 목격한 NPC는 자기가 본 사실을 '확인한 증거'로 갖는다 — 핵심 사건 참여자(가해자)에 대한 강한 의심 증가(+3).
        /// 의뢰마다 인물별 1회만 적용한다(이벤트 ID로 중복 방지).
        /// </summary>
        private void ApplyWitnessOpinions()
        {
            string perp = CurrentContract?.Truth.PerpetratorId;
            if (perp == null) return;
            foreach (var npc in Npcs.GetByContract(CurrentContract.ContractId).Where(n => n.IsAlive && n.NpcId != perp))
            {
                bool sawIncident = CurrentContract.Truth.TimelineEvents.Any(e => e.Contains("CoreIncident") && npc.WitnessedEventIds.Contains(e));
                if (!sawIncident) continue;
                if (!State.EventLog.TryRecord(new GameEvent($"opinion-witness:{CurrentContract.ContractId}:{npc.NpcId}", GameEventType.Custom, CurrentContract.ContractId, npc.NpcId)))
                    continue;
                Opinions.ApplySimultaneous(npc.NpcId, npc.Personality, new[] { new OpinionInput(perp, 3, true) });
            }
        }

        /// <summary>회의에서 NPC·경찰이 낸 카드(최근 차례 묶음). UI 대사 표시용 — 판정 결과는 개인 의견에 이미 반영됐다.</summary>
        public sealed class ParticipantCardPlay
        {
            public string PresenterId, MaterialId, TargetId;
            public readonly List<string> AcceptedBy = new List<string>();
        }

        /// <summary>마지막으로 플레이어 차례까지 진행하는 동안 다른 참가자가 낸 카드·넘김(넘김은 MaterialId null).</summary>
        public List<ParticipantCardPlay> LastParticipantPlays { get; } = new List<ParticipantCardPlay>();

        private readonly HashSet<(string Presenter, string Material)> _participantPresented = new HashSet<(string, string)>();

        /// <summary>참가자 한 명이 회의에서 낼 수 있는 카드 수(사용자 확정 2026-11-20: 플레이어와 같은 5장).</summary>
        public const int ParticipantCardLimit = 5;
        /// <summary>제시 대상이 되는 '가장 의심하는 인물'의 최소 개인 의견(사용자 확정 60).</summary>
        public const float ParticipantSuspectThreshold = 60f;

        /// <summary>
        /// 사용자 확정 2026-11-20: NPC·경찰도 플레이어와 같은 규칙으로 자기 차례에 카드를 낸다 — 자기가 가장 의심하는 인물(개인 의견 60 이상)에 관해
        /// 실제로 아는 정보를 차례당 1장(회의당 최대 5장). 그런 정보가 없거나 한도면 넘긴다.
        /// 다른 참가자의 수용 판정 기준은 노션에 없어 TEST_TEMP: 듣는 쪽이 낸 사람을 의심하는 정도(관계 의심도)가 50 미만이면 받아들인다.
        /// 받아들인 참가자만 기존 개인 의견 계산(06)으로 갱신한다. 실제 진실은 바꾸지 않는다.
        /// </summary>
        private bool TryParticipantCardPlay(string actor)
        {
            if (_participantPresented.Count(p => p.Presenter == actor) >= ParticipantCardLimit) return false;
            var suspects = Npcs.GetByContract(CurrentContract.ContractId).Where(n => n.NpcId != actor).Select(n => n.NpcId).Append(PlayerId).Where(id => id != actor);
            var target = suspects.Select(id => (id, score: Opinions.Get(actor, id))).OrderByDescending(x => x.score).ThenBy(x => x.id, StringComparer.Ordinal).FirstOrDefault();
            if (target.id == null || target.score < ParticipantSuspectThreshold) return false;

            bool isPolice = PoliceRoster.AllPoliceIds.Contains(actor);
            var known = Materials.Where(m => m.ContractId == CurrentContract.ContractId
                    && (m.SubjectNpcId == target.id || (m.IdentifiesPerpetrator && CurrentContract.Truth.PerpetratorId == target.id))
                    && !_participantPresented.Contains((actor, m.MaterialId))
                    && (isPolice
                        ? Clues.Exists(m.MaterialId) && Clues.GetRecord(m.MaterialId).ConfirmedByPoliceIds.Contains(actor)
                        : Npcs.Exists(actor) && m.StatedInfoIds.Any(Npcs.Get(actor).Knows)))
                .OrderBy(m => m.MaterialId, StringComparer.Ordinal).FirstOrDefault();
            if (known == null) return false;

            _participantPresented.Add((actor, known.MaterialId));
            var play = new ParticipantCardPlay { PresenterId = actor, MaterialId = known.MaterialId, TargetId = target.id };
            var inputs = OpinionInputsFor(known.MaterialId, distorted: false).ToList();
            foreach (var judge in MeetingFlow.ParticipantIds.Where(id => id != actor && id != PlayerId))
            {
                float trustGap = PoliceRoster.AllPoliceIds.Contains(judge) ? 0f : Relationships.GetSuspicion(judge, actor);
                if (trustGap >= 50f) continue;
                play.AcceptedBy.Add(judge);
                Opinions.ApplySimultaneous(judge, PersonalityOf(judge), inputs);
            }
            LastParticipantPlays.Add(play);
            string cardEventId = NextEventId("participant-card");
            State.EventLog.TryRecord(new GameEvent(cardEventId, GameEventType.Custom, CurrentContract.ContractId, $"{actor}:{known.MaterialId}"));
            foreach (var judgeId in play.AcceptedBy)
                RecordAcceptedFalseInformation(cardEventId, actor, known.MaterialId, presenterIsLying: false, wasDistorted: false, receiverId: judgeId);
            return true;
        }

        /// <summary>
        /// 플레이어 차례가 올 때까지 다른 참가자의 차례를 진행한다. NPC·경찰은 TryParticipantCardPlay 규칙으로 카드를 내고, 낼 카드가 없으면 넘긴다.
        /// NPC가 넘기면 06 확정 표대로 그 NPC에 대한 다른 NPC들의 관계 의심도가 오른다(연속 넘기기 +5/+8/+12/+15).
        /// 경찰의 '본인 의심도'를 담는 상태는 없어 경찰의 넘기기는 기록만 한다.
        /// </summary>
        private void AdvanceToPlayerTurn()
        {
            LastParticipantPlays.Clear();
            do
            {
                MeetingFlow.AdvanceTurn();
                string passer = MeetingFlow.CurrentTurnId;
                if (passer == PlayerId) continue;
                if (TryParticipantCardPlay(passer)) continue;
                LastParticipantPlays.Add(new ParticipantCardPlay { PresenterId = passer });
                if (!Npcs.Exists(passer)) continue;
                var skip = Meeting.RecordTurnSkip(NextEventId("npc-skip"), passer, passer);
                if (skip.WasDuplicateAction) continue;
                foreach (var observer in MeetingFlow.ParticipantIds.Where(id => id != passer && id != PlayerId && Npcs.Exists(id)))
                    Relationships.SetSuspicion(observer, passer,
                        ProbabilityCalculator.Clamp(Relationships.GetSuspicion(observer, passer) + skip.Delta, 0f, 100f));
            } while (MeetingFlow.CurrentTurnId != PlayerId);
        }

        private NpcPersonality? PersonalityOf(string judgeId) =>
            Npcs.Exists(judgeId) ? Npcs.Get(judgeId).Personality : (NpcPersonality?)null;

        /// <summary>
        /// 카드 내용이 누구에 대한 의심을 어느 방향·강도로 바꾸는지(06 정보 기본 점수 표). 강도 분류 기준은 원문에 "내용과 검증 수준에 따라"뿐이라
        /// 자료 종류로 정한다(TEST_TEMP): 가해자를 특정하는 목격 진술 강함(+3), 대상과 연결된 물건·문서·관계 진술 보통(+2),
        /// 현장 시각에 현장에 있었다는 진술 약함(+1), 다른 곳에 있었다는 진술(거짓 알리바이 포함) 의심 감소(-2/-1).
        /// 물리 증거·문서는 '확인한 증거', 증언은 '수용한 주장' 가중치를 쓴다.
        /// </summary>
        private IEnumerable<OpinionInput> OpinionInputsFor(string clueId, bool distorted)
        {
            var m = Materials.FirstOrDefault(x => x.MaterialId == clueId);
            if (m == null || CurrentContract == null) yield break;
            int sign = distorted ? -1 : 1;
            bool evidence = m.Format != ClueFormat.Testimony;
            string perp = CurrentContract.Truth.PerpetratorId;

            if (m.IdentifiesPerpetrator && perp != null)
                yield return new OpinionInput(perp, 3 * sign, evidence);
            else if (m.MaterialId.Contains("false-alibi") || m.MaterialId.Contains("presence-testimony"))
            {
                string subject = m.SubjectNpcId ?? m.SourceNpcId;
                bool atScene = m.AboutRoomId == Campaign.SceneRoomId;
                yield return new OpinionInput(subject, (atScene ? 1 : -2) * sign, evidence);
            }
            else if (m.SubjectNpcId != null)
                yield return new OpinionInput(m.SubjectNpcId, 2 * sign, evidence);
        }

        // ───────────────────────── 의뢰 보고서(자연어 핵심 문장) ─────────────────────────

        /// <summary>이 의뢰의 보고서 목표(문단). 의뢰 데이터에서 결정적으로 만든다.</summary>
        public IReadOnlyList<ReportTarget> GetReportTargets() =>
            CurrentContract == null ? new List<ReportTarget>()
            : CurrentContract.FixtureId == TestFixtureCase001.FixtureId ? TestFixtureCase001.BuildTargets()
            : ReportTargetBuilder.Build(CurrentContract, Npcs, Materials, Campaign.SceneRoomId);

        /// <summary>확보한 자료로 문단 공개 상태를 갱신한다(첫 문단 항상, 이후는 증거 묶음 또는 앞 문단 완료).</summary>
        public void RefreshReportUnlocks()
        {
            if (CurrentContract == null || Report == null) return;
            NarrativeReportJudge.UpdateUnlocks(GetReportTargets(), Report, Clues.GetAllClueIds(CurrentContract.ContractId).ToList());
        }

        /// <summary>
        /// 핵심 문장 제출(04 V-13). 재제출 제한 없음. completed일 때만 완료하고 후속 조사 방향을 공개한다(새 단서를 바로 주지는 않는다).
        /// 가해자 문단이 완료되면 '실제 가해자 정답 식별 상태'를 기록하고 공식 지목 후보로 둔다(회유 시도 조건).
        /// </summary>
        public ReportSlotStatus SubmitReportAnswer(int targetIndex, string answer)
        {
            if (CurrentContract == null || Report == null) throw new InvalidOperationException("진행 중인 의뢰가 없습니다.");
            RefreshReportUnlocks();
            var targets = GetReportTargets();
            var target = targets.FirstOrDefault(t => t.Index == targetIndex);
            var slot = Report.GetOrAddSlot(targetIndex);
            if (target == null || !slot.Unlocked) return slot.Status;
            if (slot.Status == ReportSlotStatus.Completed) return slot.Status;

            var held = Clues.GetAllClueIds(CurrentContract.ContractId).ToList();
            slot.LastAnswer = answer;
            slot.Status = _aiProvider != null
                ? NarrativeReportJudge.JudgeWithAi(target, answer, held, _aiProvider, Logger, CurrentContract.ContractId)
                : NarrativeReportJudge.Judge(target, answer, held);
            if (slot.Status == ReportSlotStatus.Completed && target.Key == ReportTargetBuilder.CulpritKey)
            {
                Campaign.CulpritIdentified = true;
                Report.SetClaimedNpcId(ReportSection.Perpetrator, CurrentContract.Truth.PerpetratorId);
            }
            RefreshReportUnlocks();
            return slot.Status;
        }

        /// <summary>
        /// 플레이어가 자기 차례에 카드를 낸다. 뒤집기=왜곡, 끌어오기=은폐(대체 카드는 받은 카드 중 무작위).
        /// lying은 "거짓으로 바꾸어 말했는지". 판정자(NPC·담당 경찰)의 수용·의문·발각 결과를 의심도에 반영한다.
        /// </summary>
        public GameActionResult PresentCard(string clueId, CardManipulation manipulation, bool lying, bool hasSupportingEvidence)
        {
            var guard = GuardMeeting();
            if (!guard.Success) return guard;
            if (MeetingResolver.PendingCarriedOverQuestions.Count > 0)
                return GameActionResult.Fail(GameActionFailure.PendingCarriedOverQuestions);
            if (MeetingFlow.CurrentTurnId != PlayerId) return GameActionResult.Fail(GameActionFailure.NotPlayersTurn);
            if (!MeetingFlow.PlayerHand.Contains(clueId)) return GameActionResult.Fail(GameActionFailure.CardNotInHand);
            if (!MeetingFlow.CanPresentNormalCard(PlayerId, clueId)) return GameActionResult.Fail(GameActionFailure.TurnLimitReached);
            // 은폐의 대체 카드는 대체용 카드 묶음에서만 고른다(09 최신). 06 확정: 묶음이 비어 있으면 보충 없이 은폐만 하고 손패가 줄어든다.
            string eventId = NextEventId("card");
            var card = Meeting.PresentCard(eventId, PlayerId, clueId, manipulation, MeetingFlow.PlayerReserve.Concat(new[] { clueId }).ToList());
            MeetingFlow.RecordNormalCardPresented(PlayerId);
            if (manipulation == CardManipulation.Conceal)
                MeetingFlow.ConcealFromHand(clueId, card.ManipulationRecordApplied?.DisplayedClueId);
            else
                MeetingFlow.ConsumeFromHand(clueId);

            if (manipulation == CardManipulation.Conceal && card.ManipulationRecordApplied != null && card.ManipulationRecordApplied.DisplayedClueId == null)
            {
                // 대체 카드 없이 은폐: 다른 참가자 앞에 내놓인 카드가 없으므로 반응·판정·개인 의견 변화가 없다. 지정한 카드의 접근 상태만 바꾼다.
                LastCardResolution = null;
                if (Clues.Exists(clueId)) Clues.GetRecord(clueId).ApplyConcealment(true, detected: false);
                return AfterMeetingAction();
            }

            LastCardResolution = MeetingResolver.Resolve(eventId, card, lying, hasSupportingEvidence, BuildJudges());
            foreach (var delta in LastCardResolution.SuspicionDeltas)
                ApplySuspicionDelta(delta);

            // 2026-11-16 확정 은폐: 회의 카드 은폐도 플레이어가 지정한 카드(단서) 하나의 접근 상태만 바꾼다.
            // 대체로 제시된 카드·다른 단서·다른 참가자가 이미 가진 정보는 바꾸지 않는다. 발각(거짓 탐지)은 별도 상태로 남긴다.
            if (manipulation == CardManipulation.Conceal && card.ManipulationRecordApplied != null && Clues.Exists(clueId))
                Clues.GetRecord(clueId).ApplyConcealment(true, LastCardResolution.Judgements.Any(j => j.Participated && j.LieDetected));

            // 수용한 주장·증거는 그 인물의 개인 의견에만 반영된다(거절하면 반영하지 않는다). 은폐는 대체 카드, 왜곡은 뒤집힌 방향으로 본다.
            string shown = card.ManipulationRecordApplied?.DisplayedClueId ?? clueId;
            // 미리 물리적으로 왜곡해 둔 단서는 그대로 내도 조작된 내용으로 받아들여진다(발각 없음, 06 2026-11-11/12).
            bool distorted = manipulation == CardManipulation.Distort
                             || (manipulation == CardManipulation.None && Campaign.PhysicallyDistortedClueIds.Contains(clueId));
            var inputs = OpinionInputsFor(shown, distorted).ToList();
            foreach (var judgement in LastCardResolution.Judgements.Where(j => j.Participated && j.Accepts))
            {
                Opinions.ApplySimultaneous(judgement.JudgeId, PersonalityOf(judgement.JudgeId), inputs);
                RecordAcceptedFalseInformation(
                    eventId, PlayerId, shown, lying,
                    manipulation == CardManipulation.Distort || Campaign.PhysicallyDistortedClueIds.Contains(shown),
                    judgement.JudgeId);
            }
            RevealWitnessIfVerified(shown, lying, manipulation);

            return AfterMeetingAction();
        }

        public GameActionResult RespondToQuestion(string questionId, bool success) =>
            ApplyQuestionAction(MeetingResolver?.Respond(NextEventId("respond"), questionId, success));

        public GameActionResult GiveUpQuestion(string questionId) =>
            ApplyQuestionAction(MeetingResolver?.GiveUp(NextEventId("giveup"), questionId));

        public GameActionResult VerifyQuestion(string questionId, bool success) =>
            ApplyQuestionAction(MeetingResolver?.AdditionalVerification(NextEventId("verify"), questionId, success));

        public GameActionResult DeferQuestion(string questionId) => ApplyQuestionAction(MeetingResolver?.Defer(questionId));

        /// <summary>
        /// 플레이어 차례를 끝낸다. 이번 차례에 카드를 하나도 내지 않았으면 차례 넘기기로 보고 각 판정자의 의심도를 올린다(06 확정 표).
        /// 이후 NPC·경찰은 개인 의견 60 이상인 최의심 대상에 대해 아는 자료를 최대 1장 제시하고, 조건에 맞는 자료가 없으면 넘긴다(2026-11-20 확정).
        /// </summary>
        public GameActionResult EndPlayerTurn()
        {
            var guard = GuardMeeting();
            if (!guard.Success) return guard;
            if (MeetingFlow.CurrentTurnId != PlayerId) return GameActionResult.Fail(GameActionFailure.NotPlayersTurn);

            if (MeetingFlow.PresentedThisTurn(PlayerId) == 0)
            {
                // 연속 넘기기 단계는 넘긴 사람 기준으로 한 번만 센다. 그 증가량을 각 판정자의 의심도에 똑같이 적용한다.
                var skip = Meeting.RecordTurnSkip(NextEventId("skip"), PlayerId, PlayerId);
                if (!skip.WasDuplicateAction)
                {
                    foreach (var judge in BuildJudges())
                        ApplySuspicionDelta(new SuspicionDeltaOutcome
                        {
                            ObserverId = judge.JudgeId,
                            SuspectId = PlayerId,
                            Delta = skip.Delta,
                            ConsecutiveCountUsed = skip.ConsecutiveCountUsed
                        });
                }
            }

            AdvanceToPlayerTurn();
            return AfterMeetingAction();
        }

        /// <summary>
        /// 최종 결론 제안. accusedId가 null이면 제출한 의뢰 보고서의 지목 대상을 쓴다. votes는 참가자별 수용 여부이며,
        /// 넘기지 않으면 참가자의 플레이어 의심도가 35 미만이면 받아들인다고 본다(임시 — 결론 수용 판단 규칙은 원문에 없음).
        /// 채택되면 정상 해결, 추가 검증 2회 뒤에도 결론이 나지 않으면 미제로 의뢰가 끝난다.
        /// </summary>
        public ConclusionResult ProposeConclusion(string accusedId = null, IReadOnlyDictionary<string, bool> votes = null)
        {
            if (IsGameOver || State.CurrentPhase != GamePhase.Meeting)
                throw new InvalidOperationException("회의 중에만 결론을 제안할 수 있습니다.");

            accusedId ??= Report?.GetSection(ReportSection.Perpetrator).ClaimedNpcId;
            // 06·07 2026-11-12 확정: 참가자별 지목 인물에 대한 개인 의견 점수가 50 초과면 찬성, 50 이하면 반대.
            votes ??= BuildJudges().ToDictionary(j => j.JudgeId, j => Opinions.VotesGuilty(j.JudgeId, accusedId));

            var conclusion = MeetingFlow.EvaluateConclusion(accusedId, votes);
            if (conclusion.Outcome == ConclusionOutcome.Adopted)
            {
                CurrentContract.OfficialConclusion.OfficiallyAccusedNpcId = accusedId;
                // 10 2026-11-13 체포 조건 ②: 공식 결론이 플레이어를 가해자로 지목해 확정되면 즉시 체포한다. 조직 보상·다음 의뢰는 실행하지 않는다.
                if (accusedId == PlayerId)
                {
                    CurrentContract.OfficialConclusion.Outcome = ContractOutcome.Resolved;
                    CurrentContract.OfficialConclusion.IsFinalized = true;
                    CloseMeeting();
                    ExecuteEnding(GameEndingType.Arrest);
                    return conclusion;
                }
                if (IsSuicideClaim(accusedId))
                {
                    // 자살 주장: 숨진 인물을 스스로 죽음을 초래한 인물로 지목해 과반이 인정하면 공식 결론 '자살'. 가해자 공개 역할은 바꾸지 않는다.
                    FinalizeContract(ContractOutcome.ResolvedAsSuicide, abandoned: false);
                    return conclusion;
                }
                SetPublicRole(accusedId, PublicRole.Perpetrator);
                FinalizeContract(ContractOutcome.Resolved, abandoned: false);
            }
            else if (conclusion.Outcome == ConclusionOutcome.Unsolved)
            {
                FinalizeContract(ContractOutcome.Unsolved, abandoned: false);
            }
            return conclusion;
        }

        /// <summary>
        /// 자살 결론 주장인지: 이번 의뢰에서 숨진 인물을 지목한 경우(사용자 확정 2026-11-20 — 자살 결론은 기존 회의 투표로 낸다.
        /// 참가자는 그 인물에 대한 개인 의견 점수로 찬반을 정한다). 실제 진실은 바꾸지 않는다.
        /// </summary>
        public bool IsSuicideClaim(string accusedId) =>
            CurrentContract != null && accusedId != null && Npcs.Exists(accusedId)
            && Npcs.Get(accusedId).ContractId == CurrentContract.ContractId && !Npcs.Get(accusedId).IsAlive;

        /// <summary>결론 없이 회의를 마치고 다음 구간 조사로 돌아간다. 처리하지 못한 의문은 다음 회의에서 가장 먼저 처리한다.</summary>
        public GameActionResult LeaveMeeting()
        {
            var guard = GuardMeeting();
            if (!guard.Success) return guard;

            foreach (var delta in MeetingResolver.DeferUnhandledCarriedOver())
                ApplySuspicionDelta(delta);
            _carriedOverQuestions = MeetingResolver.Questions.Where(q => !q.IsClosed).ToList();
            CloseMeeting();
            Campaign.MeetingsEndedInContract++;
            State.ReturnToInvestigation();
            StartNextWorldBlock();
            return GameActionResult.Ok();
        }

        // ───────────────────────── 메인 사건 ─────────────────────────

        /// <summary>최종 보고서 입력(설계자의 진실 단계에서만). 14칸이 모두 채워지면 수화기 질문으로 이어진다.</summary>
        public FillBlankOutcome WriteFinalReport(string committedText, bool isComposing = false) =>
            FinalReport.SubmitInput(committedText, isComposing);

        /// <summary>수화기 질문 응답. 거절하면 즉시 사망(게임 종료), 수락하면 반란군 영입 제안으로 이어진다.</summary>
        public GameEndingType? AnswerPhoneQuestion(bool wantsInformation)
        {
            var ending = PostReport.AnswerPhoneQuestion(wantsInformation);
            // 04 2026-11-13 구현 규칙 7절: 질문·듣기·거절은 서로 다른 분기 ID로 저장한다(이벤트 ID로 한 번만).
            string branch = wantsInformation ? "PHONE_FINAL_INFO_ACCEPTED" : "PHONE_FINAL_INFO_REFUSED";
            State.EventLog.TryRecord(new GameEvent(branch, GameEventType.Custom, null, branch));
            if (ending.HasValue)
                ExecuteEnding(ending.Value);
            return ending;
        }

        /// <summary>반란군 영입 선택. 수락=반란군 영입 엔딩, 거절=보스 히든 엔딩. 둘 다 이후에도 의뢰가 계속된다.</summary>
        public GameEndingType DecideRebelRecruitment(bool accept)
        {
            var ending = PostReport.DecideRebelRecruitment(accept);
            string branch = accept ? "REBEL_RECRUIT_ACCEPTED" : "REBEL_RECRUIT_REFUSED";
            State.EventLog.TryRecord(new GameEvent(branch, GameEventType.Custom, null, branch));
            ExecuteEnding(ending);
            return ending;
        }

        /// <summary>대상자 번호 파일: 방에 컴퓨터가 있고 고위 관계자 서신 이후, 비밀번호 입력(03). 컴퓨터 유무는 공간 데이터가 없어 호출자가 넘긴다.</summary>
        public MainCaseClueAcquireOutcome EnterSubjectFilePassword(string password, bool roomHasComputer)
        {
            if (CurrentContract == null) return MainCaseClueAcquireOutcome.NotEligible;
            var context = MainCaseClueContext.FromContract(CurrentContract, Npcs);
            context.RoomHasComputer = roomHasComputer;
            var outcome = _mainCaseClues.TryAcquire(MainCaseClueCode.SubjectNumberFile, MainCaseClueRoute.FoundInContract,
                context, MainCase, Clues, password);
            AdvanceMainCaseStages();
            return outcome;
        }

        // ───────────────────────── 저장 ─────────────────────────

        public SaveGameData CaptureSave()
        {
            if (!CanSave) throw new InvalidOperationException("처리 중인 상태에서는 저장할 수 없습니다.");
            var data = SaveGameService.Capture(State, Organization, PoliceSuspicion, PoliceLife, MainCase, Endings, PlayerId);
            if (_random is ReplayableRandom replayableRandom)
            {
                data.RandomSeed = replayableRandom.Seed;
                data.RandomState = replayableRandom.CaptureState();
            }
            data.World = WorldStateSaveService.Capture(BuildSaveTargets());
            return data;
        }

        /// <summary>
        /// 슬롯 저장(06: 수동 3 + 자동 1). 처리 중이 아닌 안정 경계(CanSave)에서만. 저장 순번을 하나 올리고 같은 슬롯만 덮어쓴다.
        /// </summary>
        public bool SaveToSlot(SaveSlotStore store, int slot)
        {
            if (store == null || !CanSave) return false;
            Campaign.SaveSequence++;
            store.Write(slot, CaptureSave(), Campaign.RunId, Campaign.SaveSequence);
            return true;
        }

        public static GameSession Load(SaveGameData data, GameSessionConfig config)
        {
            var suppliedConfig = config ?? new GameSessionConfig();
            GameSession session;
            if (data != null && !string.IsNullOrWhiteSpace(data.RandomState))
            {
                session = new GameSession(new GameSessionConfig
                {
                    PlayerId = suppliedConfig.PlayerId,
                    ContractConfig = suppliedConfig.ContractConfig,
                    FinalReportConfig = suppliedConfig.FinalReportConfig,
                    MainCaseRules = suppliedConfig.MainCaseRules,
                    AiProvider = suppliedConfig.AiProvider,
                    RandomSeed = data.RandomSeed
                });
                if (session._random is ReplayableRandom replayableRandom
                    && !replayableRandom.TryRestoreState(data.RandomState))
                {
                    session.Logger.LogError("SAVE_INVALID_RANDOM_STATE", nameof(GameSession), data.Phase,
                        "저장된 난수 상태를 읽을 수 없어 저장된 시드의 초기 상태에서 다시 시작합니다.", recoverable: true);
                }
            }
            else
            {
                session = new GameSession(suppliedConfig);
            }

            SaveGameService.Restore(data, session.State, session.Organization, session.PoliceSuspicion, session.PoliceLife,
                session.MainCase, session.Endings, session.PlayerId, session.Logger);

            session.Rooms = HouseLayoutFactory.CreateStandardHouse();
            session.Occupancy = new RoomOccupancyTracker(session.Rooms);

            // 회의 세션 객체는 세부 월드 상태를 복원하기 전에 만들어야 카운터·처리 이벤트를 복원할 수 있다.
            if (session.State.CurrentPhase == GamePhase.Meeting
                && data?.World?.Meeting?.HasSession == true
                && data.World.Meeting.IsActive
                && !string.IsNullOrEmpty(data.World.Meeting.ContractId))
                session.Meeting = new MeetingSession(data.World.Meeting.ContractId, session.Manipulation);

            var targets = session.BuildSaveTargets();
            WorldStateSaveService.Restore(data?.World, targets, session.Logger);

            if ((session.State.CurrentPhase == GamePhase.Investigation || session.State.CurrentPhase == GamePhase.Meeting)
                && targets.Contract != null)
            {
                session.CurrentContract = targets.Contract;
                session.Report = session.Reports.FirstOrDefault(r => r.ContractId == targets.Contract.ContractId)
                                 ?? new ContractReportState(targets.Contract.ContractId);
                if (!session.Reports.Contains(session.Report))
                    session.Reports.Add(session.Report);
            }
            else
            {
                session.Materials.Clear();
            }

            if (session.State.CurrentPhase == GamePhase.Meeting)
            {
                if (session.CurrentContract != null && session.Meeting != null
                    && session.Meeting.ContractId == session.CurrentContract.ContractId
                    && targets.MeetingFlow != null && targets.MeetingResolver != null)
                {
                    session.MeetingFlow = targets.MeetingFlow;
                    session.MeetingResolver = targets.MeetingResolver;
                }
                else
                {
                    session.Logger.LogError("SAVE_MEETING_RESTORE_FAILED", nameof(GameSession), GamePhase.Meeting,
                        "저장된 회의 상태를 완전히 복원할 수 없어 조사 단계로 되돌립니다.", recoverable: true);
                    session.Meeting = null;
                    session.MeetingFlow = null;
                    session.MeetingResolver = null;
                    if (session.CurrentContract != null)
                        session.State.RestoreState(session.CurrentContract.ContractId, GamePhase.Investigation,
                            session.State.CurrentTimeBlock, session.State.CurrentDay);
                    else
                        session.State.ResetToIdle();
                }
            }

            session.BuildNpcSystems(keepTransferLog: true);
            // 일정은 의뢰 ID·시간대 시각에서 재현 가능하게 다시 만든다(저장하지 않는다).
            if (session.CurrentContract != null)
                session.ApplyBlockSchedules(placeAtScheduleStart: false);
            // 막힌 이동 횟수와 보류 목적지는 일정과 별개인 진행 상태라 저장값을 복원한다.
            var savedMovement = data?.World?.Npc?.MovementState;
            if (savedMovement != null && !session.Planner.TryRestoreMovementState(savedMovement))
                session.Logger.LogError("SAVE_INVALID_NPC_MOVEMENT_STATE", nameof(GameSession), session.State.CurrentPhase,
                    "NPC 이동 막힘 상태가 현재 공간 구조와 맞지 않아 이동 막힘 기록을 초기화했습니다.", recoverable: true);
            // 불러온 뒤 새로 만드는 이벤트 ID가 저장된 이벤트와 겹치지 않게 한다.
            session._actionCounter = session.State.EventLog.GetOrdered().Count;
            return session;
        }

        private WorldStateTargets BuildSaveTargets() => new WorldStateTargets
        {
            CarriedOverQuestions = _carriedOverQuestions,
            Meeting = Meeting, // 진행 중 회의(연속 카운터·처리 이벤트). 없으면 회의 중 저장이 조사 단계로 되돌아간다.
            MeetingFlow = MeetingFlow,
            MeetingResolver = MeetingResolver,
            ParticipantPresented = _participantPresented,
            Clues = Clues,
            Manipulation = Manipulation,
            ManipulationOutcomes = PhysicalManipulation,
            Capacity = Capacity,
            Npcs = Npcs,
            Relationships = Relationships,
            InfoTransfer = InfoTransfer,
            Planner = Planner,
            Reports = Reports,
            CaseBoards = CaseBoards,
            Opinions = Opinions,
            MainCase = MainCase,
            FinalReport = FinalReport.Report,
            PostReport = PostReport,
            Occupancy = Occupancy,
            EventLog = State.EventLog,
            Contract = CurrentContract,
            Pursuit = Pursuit,
            Firearm = Firearm,
            OrganizationSupport = OrganizationSupport,
            OrgSupplementary = OrgSupplementary,
            Campaign = Campaign,
            Materials = Materials
        };

        // ───────────────────────── 종료 정산 ─────────────────────────

        /// <summary>
        /// 의뢰 종료 정산(확정 규칙):
        /// - 공식 결론 확정. 정상 해결은 담당 경찰 55 미만 -10·이상 -5(시작값보다 낮아지면 나머지 -5), 미제는 회복 없음,
        ///   담당 경찰 사망은 전원 +5·다음 담당 +10 추가(조직 신뢰도 영향 없음).
        /// - 조직 조건을 실제 상태로 판정해 신뢰도 ±5(조건 없으면 변화 없음), 포기는 -2. 회수 대상은 의뢰가 끝나면 자동 회수된다(05).
        /// - 조직_02 검사(종료 시점), 메인 사건 종료 의뢰 수 기록, 의뢰 사이 조직 전달, 진실 단계 진행, 엔딩 판정.
        /// </summary>
        private void FinalizeContract(ContractOutcome outcome, bool abandoned)
        {
            var contract = CurrentContract;
            // 체크리스트 1-1: 의뢰 종료 효과(신뢰도·의심도·조직 조건·지원 단서)는 이벤트 ID로 한 번만 적용한다. 먼저 기록해 막는다.
            if (contract == null || !State.EventLog.TryRecord(new GameEvent($"contract-finalizing:{contract.ContractId}", GameEventType.Custom,
                    contract.ContractId, abandoned ? "abandoned" : outcome.ToString())))
                return;
            contract.OfficialConclusion.Outcome = outcome;
            contract.OfficialConclusion.IsFinalized = true;

            bool inMeeting = State.CurrentPhase == GamePhase.Meeting;
            if (Meeting != null) CloseMeeting();

            if (abandoned)
            {
                OrganizationTrustCoordinator.ApplyContractOutcome(Organization, contract, contractAbandoned: true);
            }
            else
            {
                var kind = outcome switch
                {
                    ContractOutcome.Resolved => ContractEndKind.Resolved,
                    // 공식 결론 '자살' 확정도 정상 해결로 끝난 의뢰다(사용자 확정 2026-11-20: 기존 투표·공식 결론 시스템 그대로).
                    ContractOutcome.ResolvedAsSuicide => ContractEndKind.Resolved,
                    ContractOutcome.HandlerPoliceDeceased => ContractEndKind.HandlerPoliceDeceased,
                    _ => ContractEndKind.Unsolved
                };

                // 07 2026-11-13: 담당 경찰 사망 시 다음 담당은 다음 의뢰 생성 때 배정 대기열에서 고르고,
                // 추가 +10은 그 담당에게 배정 시점에 적용한다(미적용 여부를 저장). 여기서는 전원 +5만 적용한다.
                if (kind == ContractEndKind.HandlerPoliceDeceased)
                    Campaign.PendingHandlerDeathBonus = true;
                PoliceSuspicion.ApplyContractEndEffects(PlayerId, contract.HandlerPoliceId, Campaign.HandlerStartingSuspicion, kind, null);

                if (contract.OrgCondition.HasCondition)
                    contract.OrgCondition.Achieved = OrganizationConditionEvaluator.Evaluate(contract, Clues, deliveredToOrganization: true) ?? false;
                if (kind != ContractEndKind.HandlerPoliceDeceased)
                    OrganizationTrustCoordinator.ApplyContractOutcome(Organization, contract, contractAbandoned: false);

                var contractHistory = Manipulation.History
                    .Where(r => Clues.Exists(r.OriginalClueId) && Clues.GetDefinition(r.OriginalClueId).ContractId == contract.ContractId)
                    .ToList();
                if (OrganizationSupport.TryGrantOrg02(Clues, contractHistory, Organization.Trust))
                    GrantMainCaseDocument(OrganizationSupportClueService.Org02ClueId, MainCaseClueNames.AdditionalMaterialDeliveryConfirmation);
            }

            MainCase.RecordContractCompleted();
            if (!abandoned) Campaign.CompletedContractCount++;
            Campaign.PreviousConditionType = contract.OrgCondition.Type;

            if (inMeeting) State.ResolveContract();
            else State.EndContractEarly();

            State.EventLog.TryRecord(new GameEvent($"contract-resolved:{contract.ContractId}", GameEventType.ContractResolved,
                contract.ContractId, abandoned ? "abandoned" : outcome.ToString()));

            LastContract = contract;
            CurrentContract = null;
            Campaign.RebelContactNpcId = null;
            Campaign.RebelMemoRoomId = null;
            Report = null;
            Materials = new List<EvidenceMaterial>();
            _carriedOverQuestions = new List<CardQuestion>();
            State.ResetToIdle();

            DeliverOrganizationMainCaseClues(contract);
            AdvanceMainCaseStages();
            CheckEndings();

            // 06 의뢰 종료 순서: 결과 확정 → 조직 조건·신뢰도 변화 → 신뢰도 0 검사(CheckEndings) → 놓친 자료의 조직 지원 판정.
            // 판정은 변화 뒤 신뢰도로 한다. 의뢰가 끝나 대기 상태가 되었으므로 성공한 자료는 바로 전달하고 전화로 한 번 알린다.
            if (!IsGameOver)
            {
                OrgSupplementary.Evaluate(BuildOrgSupportContext(contract.ContractId));
                DeliverOrgSupplementaryReports();
            }
        }

        public const string OrgDeliveryPhoneLine = "조직이 보유한 기존 기록의 발췌 자료를 보냈다. 컴퓨터의 조직 전달 자료 폴더에서 확인해라.";

        private OrgSupportContext BuildOrgSupportContext(string contractId) => new OrgSupportContext
        {
            Trust = Organization.Trust,
            ContractId = contractId,
            PlayerHolds = name => MainCase.HasAcquired(name),
            StageUnlocked = stage => MainCase.CurrentStage >= ToMainStage(stage),
            ContractsCompletedSinceUnlock = stage =>
                Campaign.StageUnlockedAtCompleted.TryGetValue(ToMainStage(stage).ToString(), out var at) ? Campaign.CompletedContractCount - at : 0,
            // 고정 설정은 명시적으로 등록된 ID만 허용한다. 문서는 플레이어 소지와 별개로 캠페인에 배치된 자료도 원본 존재로 본다.
            // 임의의 ASCII 문서 ID가 고정 설정으로 오인되어 없는 출처의 보고서가 적격 처리되지 않도록 한다.
            SourceAvailable = label => OrgSupplementaryReports.IsKnownFixedDataSource(label)
                || MainCase.HasAcquired(label)
                || Campaign.GeneratedMainDocuments.Contains(label),
            FinalReportComplete = FinalReport.Report.IsComplete
        };

        private static MainCaseTruthStage ToMainStage(OrgTruthStage stage) => stage switch
        {
            OrgTruthStage.Systemic => MainCaseTruthStage.SystemicTruth,
            OrgTruthStage.Power => MainCaseTruthStage.PowerTruth,
            OrgTruthStage.Experiment => MainCaseTruthStage.ExperimentTruth,
            _ => MainCaseTruthStage.DesignerTruth
        };

        /// <summary>
        /// 06: 의뢰·조사·사건보드·회의 중에는 전달하지 않는다. 대기 상태에서만 대기열을 비우고, 같은 경계에서 여러 건이 와도 전화는 1회다.
        /// </summary>
        private void DeliverOrgSupplementaryReports()
        {
            if (CurrentContract != null || State.CurrentPhase != GamePhase.Idle) return;
            if (OrgSupplementary.DeliverPending(WorldMinute).Count > 0)
                Campaign.PendingOrgDeliveryCall = true;
        }

        /// <summary>조직 전달 자료 알림 전화를 아직 하지 않았으면 true를 돌려주고 알린 것으로 표시한다.</summary>
        public bool ConsumeOrgDeliveryCall()
        {
            if (!Campaign.PendingOrgDeliveryCall) return false;
            Campaign.PendingOrgDeliveryCall = false;
            return true;
        }

        /// <summary>조직 전달 자료 폴더 목록(전달된 자료만, 전달 순).</summary>
        public IReadOnlyList<OrgReportState> DeliveredOrgReports =>
            OrgSupplementary.States.Where(s => s.DeliveryStatus == OrgReportDeliveryStatus.Delivered)
                .OrderBy(s => s.DeliveredWorldMinute).ThenBy(s => s.Id, StringComparer.Ordinal).ToList();

        /// <summary>
        /// 03 확정: 의뢰 종료 후 다음 의뢰를 받기 전에만 조직이 추가 단서를 전달하며, 여러 개면 한꺼번에 전달한다.
        /// 신뢰도 1–20에서는 조직 지원이 없으므로 전달하지 않는다(05 구간 규칙을 적용한 해석).
        /// </summary>
        private void DeliverOrganizationMainCaseClues(ContractInstance finishedContract)
        {
            if (!OrganizationSupportClueService.TierAllowsSupport(Organization.Trust))
                return;

            var context = MainCaseClueContext.FromContract(finishedContract, Npcs);
            context.IsInterContractDeliveryWindow = true;
            _mainCaseClues.DeliverEligibleViaOrganization(context, MainCase, Clues);
        }

        // ───────────────────────── 내부 ─────────────────────────

        private void BuildNpcSystems(bool keepTransferLog = false)
        {
            if (!keepTransferLog)
            {
                Relationships = new NpcRelationshipStore();
                InfoTransfer = new NpcInfoTransferService(_random);
            }
            InfoTransfer.CurrentMinute = () => Campaign.NpcMinute;
            Planner = new NpcBehaviorPlanner(Npcs, Occupancy, Rooms, InfoTransfer, Relationships, Clues, Capacity, random: _random);
            Planner.AddCandidateSource(new NpcSceneInvestigationSource(this));
            Planner.AddCandidateSource(new NpcInteractionBehaviorSource(this));
        }

        /// <summary>
        /// 06 확정: 구간이 끝나면(예산 소진·조사 종료) 추가 조사 입력을 막는다. 비용이 남은 예산보다 크면 시작하지 않고
        /// 시간도 쓰지 않는다(같으면 실행 가능). 비용 0인 행동에는 예산 검사를 하지 않는다.
        /// </summary>
        private GameActionResult GuardInvestigation(int cost)
        {
            if (IsGameOver) return GameActionResult.Fail(GameActionFailure.GameOver);
            if (State.CurrentPhase != GamePhase.Investigation || CurrentContract == null)
                return GameActionResult.Fail(GameActionFailure.WrongPhase);
            if (State.IsTimeBlockEnded) return GameActionResult.Fail(GameActionFailure.TimeBlockEnded);
            if (cost > State.RemainingTimeSlots) return GameActionResult.Fail(GameActionFailure.NotEnoughTime);
            return GameActionResult.Ok();
        }

        private GameActionResult GuardNpcInRoom(string npcId)
        {
            if (string.IsNullOrEmpty(npcId) || !Npcs.Exists(npcId)) return GameActionResult.Fail(GameActionFailure.UnknownTarget);
            if (!Npcs.Get(npcId).IsAlive) return GameActionResult.Fail(GameActionFailure.TargetDeceased);
            if (!Occupancy.HasPosition(npcId) || Occupancy.GetRoom(npcId) != PlayerRoom)
                return GameActionResult.Fail(GameActionFailure.TargetNotInRoom);
            return GameActionResult.Ok();
        }

        private GameActionResult GuardMeeting()
        {
            if (IsGameOver) return GameActionResult.Fail(GameActionFailure.GameOver);
            if (State.CurrentPhase != GamePhase.Meeting || Meeting == null) return GameActionResult.Fail(GameActionFailure.WrongPhase);
            return GameActionResult.Ok();
        }

        private GameActionResult SpendTime(int slots)
        {
            if (slots > 0) State.TrySpendTime(slots);
            return new GameActionResult { TimeSpent = slots };
        }

        /// <summary>
        /// 행동 효과 뒤에 쓴 칸만큼 세계 시각을 진행하고 15분마다 NPC가 1턴씩 행동한다(화면 밖 포함). 예산을 다 쓰면
        /// 시간대 끝까지 자동 진행한다(06 확정). 마지막에 엔딩을 검사한다.
        /// </summary>
        private GameActionResult AfterAction(GameActionResult result)
        {
            if (CurrentContract != null && result.TimeSpent > 0)
            {
                AdvanceWorldTo(Campaign.NpcMinute + result.TimeSpent * NpcMinutesPerTimeSlot);
                if (State.IsTimeBlockEnded)
                    AdvanceWorldTo(BlockEndMinute);
            }
            CheckEndings();
            return result;
        }

        /// <summary>NPC가 15분 턴마다 한 일 중 정보 전달 기록(엿듣기 판정용). 의뢰가 바뀌면 비운다.</summary>
        public List<OverheardExchange> RecentExchanges { get; private set; } = new List<OverheardExchange>();

        /// <summary>세계 시각을 target까지 15분씩 진행하며 NPC 의사결정 턴을 시간순으로 처리한다. 건너뛰거나 중복 계산하지 않는다.</summary>
        private void AdvanceWorldTo(int targetMinute)
        {
            if (CurrentContract == null || Planner == null) return;
            while (Campaign.NpcMinute + NpcMinutesPerTimeSlot <= targetMinute)
            {
                Campaign.NpcMinute += NpcMinutesPerTimeSlot;
                var results = Planner.ExecuteTurn($"{CurrentContract.ContractId}-m{Campaign.NpcMinute}", CurrentContract.ContractId, Campaign.NpcMinute);
                PolicePatrolTurn();
                PerpetratorSilencingTurn();
                LivingVictimsFollowPolice();
                CheckTheftAbsence();
                ReportWitnessedCrimesToPolice();
                // 07 3-3: 추적 발동 다음 턴부터 경찰 정보 수집 단계(기존 조사 흐름 — 별도 미니게임 없음).
                if (Pursuit.Stage == PursuitStage.Triggered)
                    Pursuit.AdvanceToGatheringInformation();
                foreach (var r in results.Where(r => r.Kind == NpcBehaviorKind.ShareInfo && r.TransferResult != null))
                {
                    if (!Occupancy.HasPosition(r.NpcId)) continue;
                    RecentExchanges.Add(new OverheardExchange
                    {
                        SpeakerId = r.NpcId,
                        ListenerId = r.TargetId,
                        InfoId = r.InfoId,
                        RoomId = Occupancy.GetRoom(r.NpcId),
                        Minute = Campaign.NpcMinute,
                        Succeeded = r.TransferResult.Outcome == InfoTransferOutcome.Succeeded
                    });
                }
            }
        }

        /// <summary>다음 시간대 시작: 예산은 GameStateController가 32칸으로 되돌리고, 여기서는 시간대 시각·NPC 일정을 새로 맞춘다.</summary>
        private void StartNextWorldBlock()
        {
            Campaign.BlockStartMinute += GameStateController.BlockMinutes;
            if (Campaign.NpcMinute < Campaign.BlockStartMinute)
                AdvanceWorldTo(Campaign.BlockStartMinute);
            Campaign.NpcMinute = Campaign.BlockStartMinute;
            ApplyBlockSchedules(placeAtScheduleStart: false);
            RollSilencingForBlock();
        }

        /// <summary>
        /// 07 2026-11-13: 이번 시간대의 NPC 일정을 의뢰 ID·시간대 시각으로 재현 가능하게 만든다(같은 시드 → 같은 일정, 저장 불필요).
        /// 살아 있고 배치된 NPC만 일정을 받는다. 의뢰 첫 시간대에는 일정의 첫 장소를 시작 위치로 쓴다.
        /// </summary>
        private void ApplyBlockSchedules(bool placeAtScheduleStart)
        {
            if (CurrentContract == null || Planner == null) return;
            var generator = new InvestigationScheduleGenerator(Rooms);
            foreach (var npc in Npcs.GetByContract(CurrentContract.ContractId).Where(n => n.IsAlive))
            {
                // 실종자처럼 씬에 배치되지 않은 인물은 일정도 없다.
                if (!Occupancy.HasPosition(npc.NpcId)) continue;
                string current = Occupancy.GetRoom(npc.NpcId);

                var schedule = generator.Generate(npc, current, Campaign.BlockStartMinute, Campaign.BlockStartMinute + GameStateController.BlockMinutes,
                    StableSeed($"{CurrentContract.ContractId}:{Campaign.BlockStartMinute}:{npc.NpcId}"), placeAtScheduleStart);
                Planner.SetSchedule(schedule);
                if (placeAtScheduleStart && schedule.Entries.Count > 0)
                    Occupancy.PlaceAt(npc.NpcId, schedule.Entries[0].RoomId);
            }
        }

        private GameActionResult AfterMeetingAction()
        {
            TryHandlerAccusation();
            CheckEndings();
            return GameActionResult.Ok();
        }

        /// <summary>
        /// 거짓·조작 정보가 NPC에게 수용되었을 때만 그 잘못된 믿음과 출처를 기록한다.
        /// 실제 사건의 진실과 의심도는 여기서 변경하지 않는다(06·07 확정 규칙).
        /// </summary>
        private void RecordAcceptedFalseInformation(
            string actionEventId,
            string presenterId,
            string clueId,
            bool presenterIsLying,
            bool wasDistorted,
            string receiverId)
        {
            if (CurrentContract == null || string.IsNullOrEmpty(actionEventId)
                || string.IsNullOrEmpty(receiverId) || !Npcs.Exists(receiverId) || !Npcs.Get(receiverId).IsAlive)
                return;

            var material = Materials.FirstOrDefault(m => m.MaterialId == clueId && m.ContractId == CurrentContract.ContractId);
            if (material == null || material.StatedInfoIds.Count == 0)
                return;

            bool presentedFalseInformation = presenterIsLying;
            switch (material.TruthStatus)
            {
                case ClueTruthStatus.Factual:
                    presentedFalseInformation |= wasDistorted;
                    break;
                case ClueTruthStatus.False:
                    presentedFalseInformation |= !wasDistorted;
                    break;
                case ClueTruthStatus.Fabricated:
                    presentedFalseInformation = true;
                    break;
                // 부분적 사실은 현재 정보 ID가 참·거짓 부분을 따로 구분하지 않으므로,
                // 왜곡만으로 전체 정보를 잘못 믿었다고 기록하지 않는다.
            }
            if (!presentedFalseInformation)
                return;

            var receiver = Npcs.Get(receiverId);
            foreach (var infoId in material.StatedInfoIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal))
            {
                string beliefEventId = $"npc-false-belief:{actionEventId}:{receiverId}:{infoId}";
                if (!State.EventLog.TryRecord(new GameEvent(beliefEventId, GameEventType.Custom, CurrentContract.ContractId,
                        $"presenter={presenterId};clue={clueId};info={infoId}")))
                    continue;

                receiver.LearnInfo(infoId, NpcJudgement.Believe, new NpcInfoRecord
                {
                    State = NpcInformationState.BelievesFalse,
                    Method = NpcInfoAcquisitionMethod.Dialogue,
                    Minute = Campaign.NpcMinute,
                    SourceEventId = beliefEventId,
                    SourceEvidenceId = clueId,
                    SourcePersonId = presenterId
                });
            }
        }

        private GameActionResult ApplyQuestionAction(QuestionActionResult action)
        {
            var guard = GuardMeeting();
            if (!guard.Success) return guard;
            if (action == null || action.Outcome != QuestionActionOutcome.Applied)
                return GameActionResult.Fail(GameActionFailure.InvalidInput);
            if (action.SuspicionDelta != null)
                ApplySuspicionDelta(action.SuspicionDelta);
            if (action.State == CardQuestionState.AcceptedAfterVerification)
                RecordAcceptedFalseInformation(
                    action.ActionEventId, action.PresenterId, action.ClueId, action.PresenterIsLying,
                    action.PresenterUsedDistortion || Campaign.PhysicallyDistortedClueIds.Contains(action.ClueId),
                    action.JudgeId);
            return AfterMeetingAction();
        }

        private void DiscoverMaterial(EvidenceMaterial material, GameActionResult result)
        {
            if (Clues.Exists(material.MaterialId))
                return;
            var definition = material.ToClueDefinition();
            Clues.Discover(definition);
            result.NewClueIds.Add(material.MaterialId);
            RefreshReportUnlocks();
        }

        /// <summary>NPC·경찰이 최근 조사 대상을 살펴본 기록(행동 아이콘용, 저장하지 않음 — 이미 일어난 결과만 보여 준다).</summary>
        public List<SceneInvestigationRecord> RecentInvestigations { get; private set; } = new List<SceneInvestigationRecord>();

        public void RecordSceneInvestigation(string actorId, string objectId, string roomId, int minute) =>
            RecentInvestigations.Add(new SceneInvestigationRecord { ActorId = actorId, ObjectId = objectId, RoomId = roomId, Minute = minute });

        /// <summary>경찰 조사 1건에 걸리는 세계 시간(분). 플레이어의 새 대상 조사(1시간)와 같게 둔다(TEST_TEMP).</summary>
        public const int PoliceInspectMinutes = 60;

        /// <summary>
        /// 담당 경찰 현장 조사(TEST_TEMP — 07에 경찰의 현장 이동·조사 규칙이 없다. 07 §21 A·B형 "담당 경찰이 해당 방에서 발견한 단서"와
        /// 「경찰 시스템」 "현재까지 확보한 정보로 판단"을 근거로 만든 임시 동작). 15분 턴마다 호출한다.
        /// 1시간마다 지금 방의 아직 보지 않은 조사 대상 하나를 살핀다. 그 방을 다 보면 아직 볼 대상이 남은 가장 가까운 방으로 한 칸 옮긴다.
        /// 자료를 가져가지 않으며 플레이어의 단서 보유·획득에 영향을 주지 않는다.
        /// </summary>
        /// <summary>
        /// 경찰이 직접 확보한 단서를 5개까지 보유한다. 한도가 찼으면 우선도가 가장 낮은 보유 단서와 비교하고,
        /// 새 자료의 우선도가 더 높을 때만 교체한다. 우선도 동률이면 기존 자료를 유지한다.
        /// 판정은 자료의 형태와 명시된 증거 역할만 사용하며, 실제 진실·자료의 참거짓을 들여다보지 않는다.
        /// </summary>
        private int PoliceCluePriority(string materialId)
        {
            var material = Materials.FirstOrDefault(m => m.MaterialId == materialId && m.ContractId == CurrentContract?.ContractId);
            if (material == null) return int.MinValue;

            int score = material.IdentifiesPerpetrator ? 100 : 0;
            if (material.IsDirectPerpetratorEvidence) score += 50;
            score += material.Format switch
            {
                ClueFormat.PhysicalObject => 30,
                ClueFormat.Recording => 25,
                ClueFormat.Document => 20,
                ClueFormat.Testimony => 10,
                _ => 0
            };
            return score;
        }

        private EvidenceAcquisitionResult TryAcquirePoliceMaterial(string policeId, EvidenceMaterial material)
        {
            if (material == null || CurrentContract == null || material.ContractId != CurrentContract.ContractId)
                return new EvidenceAcquisitionResult { Outcome = EvidenceAcquisitionOutcome.RejectedAtCapacity };

            var outcome = Capacity.TryAcquire(
                policeId,
                HolderType.Police,
                material.MaterialId,
                shouldReplace: (oldId, newId) => PoliceCluePriority(newId) > PoliceCluePriority(oldId),
                priority: PoliceCluePriority);

            if (outcome.Outcome == EvidenceAcquisitionOutcome.Acquired
                || outcome.Outcome == EvidenceAcquisitionOutcome.Replaced)
            {
                string suffix = outcome.Outcome == EvidenceAcquisitionOutcome.Replaced
                    ? $":{outcome.ReplacedClueId}:{material.MaterialId}"
                    : $":{material.MaterialId}";
                State.EventLog.TryRecord(new GameEvent(
                    $"police-clue-{outcome.Outcome.ToString().ToLowerInvariant()}:{CurrentContract.ContractId}:{policeId}{suffix}",
                    GameEventType.Custom, CurrentContract.ContractId, policeId));

                if (Clues.Exists(material.MaterialId))
                    Clues.GetRecord(material.MaterialId).MarkConfirmedByPolice(policeId);
            }
            return outcome;
        }

        /// <summary>조사 중 새로 사망한 인물의 시신은 현장 오브젝트로 추가한다. 플레이어가 자동으로 단서를 받지는 않는다.</summary>
        private EvidenceMaterial RegisterNewDeathBody(string npcId, string roomId, string deathEventId)
        {
            if (CurrentContract == null || !Npcs.Exists(npcId) || !Rooms.RoomExists(roomId))
                return null;

            string materialId = $"body:{CurrentContract.ContractId}:{npcId}";
            var existing = Materials.FirstOrDefault(m => m.MaterialId == materialId);
            if (existing != null) return existing;

            var npc = Npcs.Get(npcId);
            string objectId = CaseCatalog.ObjectId(roomId, "시신-" + npcId);
            var body = new EvidenceMaterial
            {
                MaterialId = materialId,
                ContractId = CurrentContract.ContractId,
                Format = ClueFormat.PhysicalObject,
                TruthStatus = ClueTruthStatus.Factual,
                RoomId = roomId,
                Acquisition = MaterialAcquisition.InvestigateRoom,
                AboutEventId = deathEventId,
                AboutMinute = Campaign.NpcMinute,
                AboutRoomId = roomId,
                SubjectNpcId = npcId,
                ObjectId = objectId,
                IsBody = true,
                Title = $"{npc.DisplayName ?? npcId}의 시신",
                Content = $"{npc.DisplayName ?? npcId}의 시신이 {CaseCatalog.ObjectName(objectId)}이 놓인 공간에서 발견될 수 있다. 이 자료만으로 사망 원인이나 가해자를 확정할 수는 없다."
            };
            body.StatedInfoIds.Add($"corpse:{CurrentContract.ContractId}:{npcId}");
            Materials.Add(body);
            return body;
        }

        /// <summary>담당 경찰이 새 시신이 있는 방에 들어오면 발견 즉시 조사 기록과 직접 확보 자료에 반영한다.</summary>
        private void DiscoverNewBodiesForPolice(string policeId, string roomId)
        {
            if (CurrentContract == null || string.IsNullOrEmpty(roomId)) return;

            foreach (var npcId in Campaign.LaterVictimIds.Distinct().ToList())
            {
                if (!Npcs.Exists(npcId) || Npcs.Get(npcId).IsAlive
                    || !Occupancy.HasPosition(npcId) || Occupancy.GetRoom(npcId) != roomId)
                    continue;

                string eventId = $"police-found-body:{CurrentContract.ContractId}:{npcId}";
                if (!State.EventLog.TryRecord(new GameEvent(eventId, GameEventType.Custom, CurrentContract.ContractId, policeId)))
                    continue;

                var body = RegisterNewDeathBody(npcId, roomId, "police-found:" + npcId);
                if (body == null) continue;

                Campaign.PoliceInspectedObjectIds.Add(body.ObjectId);
                RecordSceneInvestigation(policeId, body.ObjectId, roomId, Campaign.NpcMinute);
                TryAcquirePoliceMaterial(policeId, body);
            }
        }

        /// <summary>
        /// 경찰 조사(15분 턴마다). 새 시신은 방에 들어오면 즉시 확인하고, 일반 조사 대상은 시간당 하나씩 살핀다.
        /// 대상의 내용은 플레이어에게 자동 지급되지 않는다. 경찰은 한도·우선도 규칙에 따라 직접 보유 자료를 갱신한다.
        /// </summary>
        private void PolicePatrolTurn()
        {
            string police = HandlerPoliceId;
            if (police == null || !PoliceLife.IsAlive(police) || !Occupancy.HasPosition(police)) return;
            string here = Occupancy.GetRoom(police);
            DiscoverNewBodiesForPolice(police, here);
            var objects = GetInvestigationObjects();
            var todo = objects.FirstOrDefault(o => o.RoomId == here && !Campaign.PoliceInspectedObjectIds.Contains(o.ObjectId));
            if (todo != null)
            {
                if (Campaign.NpcMinute % PoliceInspectMinutes != 0) return; // 살피는 중
                Campaign.PoliceInspectedObjectIds.Add(todo.ObjectId);
                RecordSceneInvestigation(police, todo.ObjectId, here, Campaign.NpcMinute);
                foreach (var material in Materials.Where(m => m.ContractId == CurrentContract.ContractId
                            && m.Acquisition == MaterialAcquisition.InvestigateRoom
                            && m.ObjectId == todo.ObjectId && Unlocked(m)).ToList())
                    TryAcquirePoliceMaterial(police, material);
                return;
            }

            // 이 방은 다 봤다 — 볼 대상이 남은 가장 가까운 방으로 한 칸 이동한다.
            List<string> best = null;
            foreach (var room in objects.Where(o => !Campaign.PoliceInspectedObjectIds.Contains(o.ObjectId)).Select(o => o.RoomId).Distinct())
            {
                var path = Rooms.FindPath(here, room)?.ToList();
                if (path != null && path.Count > 0 && (best == null || path.Count < best.Count)) best = path;
            }
            if (best != null) Occupancy.PlaceAt(police, best[0]);
        }

        /// <summary>한 의뢰의 사망자 상한(최초 피해자 포함, 사용자 지정 2026-11-20).</summary>
        public const int MaxContractDeaths = 5;
        /// <summary>2~3 조사 시간대(턴)마다 한 번 굴리는 입막음 살해 계획 확률(%, 사용자 지정 2026-11-20).</summary>
        public const float SilencingChancePerTurn = 10f;

        /// <summary>
        /// 사용자 지정 2026-11-20: 턴 = 조사 시간대 하나가 끝나는 것. 2~3턴마다 10%로 살해를 계획하고,
        /// 계획되면 이후 가해자가 범행을 아는 NPC와 단둘이 되는 첫 순간에 실행한다.
        /// </summary>
        private void RollSilencingForBlock()
        {
            if (CurrentContract == null || Campaign.SilencingPlanned) return;
            if (Campaign.BlocksUntilSilencingCheck <= 0) Campaign.BlocksUntilSilencingCheck = _random.Next(2, 4);
            if (--Campaign.BlocksUntilSilencingCheck > 0) return;
            Campaign.BlocksUntilSilencingCheck = _random.Next(2, 4);
            if (_random.NextDouble() * 100.0 < SilencingChancePerTurn) Campaign.SilencingPlanned = true;
        }

        /// <summary>이번 의뢰에서 숨진 NPC 수(최초 피해자·추가 살해·사격 사망 모두 포함).</summary>
        public int ContractDeathCount => CurrentContract == null ? 0 : Npcs.GetByContract(CurrentContract.ContractId).Count(n => !n.IsAlive);

        /// <summary>
        /// 사용자 지정 2026-11-20: 조사 중 시간이 지나면 추가 피해자가 생길 수 있다(최초 피해자 포함 최대 5명).
        /// 규칙 세부는 노션에 없어 TEST_TEMP: 살아 있는 실제 가해자가 자기 범행을 아는 NPC와 단둘이 같은 방에 있고
        /// (플레이어·경찰·다른 NPC 없음 — 목격자 없음) 턴마다 10% 확률로 그 NPC를 살해한다. 시신은 그 방에 남는다.
        /// 사격 사망도 상한에 센다. 상한에 이르면 더 이상 일어나지 않는다.
        /// </summary>
        private void PerpetratorSilencingTurn()
        {
            if (CurrentContract == null || ContractDeathCount >= MaxContractDeaths) return;
            string perpId = CurrentContract.Truth.PerpetratorId;
            if (perpId == null || !Npcs.Exists(perpId) || !Npcs.Get(perpId).IsAlive || !Occupancy.HasPosition(perpId)) return;
            string room = Occupancy.GetRoom(perpId);
            if (PlayerRoom == room) return;
            if (PoliceRoster.AllPoliceIds.Any(p => PoliceLife.IsAlive(p) && Occupancy.HasPosition(p) && Occupancy.GetRoom(p) == room)) return;

            var others = Npcs.GetByContract(CurrentContract.ContractId)
                .Where(n => n.NpcId != perpId && n.IsAlive && Occupancy.HasPosition(n.NpcId) && Occupancy.GetRoom(n.NpcId) == room).ToList();
            if (others.Count != 1) return;
            var target = others[0];
            // 자기 범행(핵심 사건)을 아는 인물만 입막음 대상이 된다.
            // 가해자를 가리키는 자료(가해자 특정·가해자에 관한 자료)의 정보를 실제로 알고 있어야 한다.
            if (!Materials.Any(m => m.ContractId == CurrentContract.ContractId && (m.IdentifiesPerpetrator || m.SubjectNpcId == perpId)
                                    && m.StatedInfoIds.Any(target.Knows)))
                return;
            // 시간대 판정(RollSilencingForBlock)에서 계획된 살해만 실행한다.
            if (!Campaign.SilencingPlanned) return;
            Campaign.SilencingPlanned = false;

            target.MarkDeceased();
            Campaign.LaterVictimIds.Add(target.NpcId);
            string deathEventId = $"later-murder:{target.NpcId}";
            State.EventLog.TryRecord(new GameEvent(deathEventId, GameEventType.Custom, CurrentContract.ContractId, $"{perpId}:{room}:{Campaign.NpcMinute}"));
            RegisterNewDeathBody(target.NpcId, room, deathEventId);
        }

        /// <summary>
        /// 사용자 지정 2026-11-20: 살아 있는 피해자는 담당 경찰 주변에만 머문다. 15분 턴마다 일정 이동 뒤에 호출해,
        /// 경찰과 다른 방에 있으면 경찰 방 쪽으로 한 칸 옮긴다(경찰도 턴마다 최대 한 칸 움직이므로 곧 같은 방에 붙는다).
        /// </summary>
        private void LivingVictimsFollowPolice()
        {
            string police = HandlerPoliceId;
            if (police == null || !PoliceLife.IsAlive(police) || !Occupancy.HasPosition(police)) return;
            string policeRoom = Occupancy.GetRoom(police);
            foreach (var victim in new[] { CurrentContract.Truth.VictimId }.Concat(CurrentContract.Truth.AdditionalVictimIds).Distinct())
            {
                if (victim == null || !Npcs.Exists(victim) || !Npcs.Get(victim).IsAlive || !Occupancy.HasPosition(victim)) continue;
                string here = Occupancy.GetRoom(victim);
                if (here == policeRoom) continue;
                var path = Rooms.FindPath(here, policeRoom)?.ToList();
                if (path != null && path.Count > 0) Occupancy.PlaceAt(victim, path[0]);
            }
        }

        /// <summary>담당 경찰이 그 방에서 발견한 단서 수(07 §21 A·B형 보정): 경찰이 살펴본 대상에 놓인 자료 수.</summary>
        public int PoliceCluesFoundIn(string roomId)
        {
            if (CurrentContract == null || roomId == null) return 0;
            return Materials.Count(m => m.ContractId == CurrentContract.ContractId && m.Acquisition == MaterialAcquisition.InvestigateRoom
                                        && m.RoomId == roomId && m.ObjectId != null && Campaign.PoliceInspectedObjectIds.Contains(m.ObjectId));
        }

        /// <summary>06 §21 단서 확인 요청: 같은 방일 때만, 새로 얻은 단서마다 굴린다(검사 빈도는 원문에 없어 임시).</summary>
        private void RollClueConfirmations(GameActionResult result)
        {
            string handler = HandlerPoliceId;
            if (handler == null || !PoliceLife.IsAlive(handler) || !Occupancy.HasPosition(handler))
                return;

            bool sameRoom = Occupancy.GetRoom(handler) == PlayerRoom;
            int acceptedLies = MeetingResolver?.GetAcceptedLieCount(PlayerId) ?? 0;
            foreach (var clueId in result.NewClueIds)
            {
                // 은폐한 단서는 접근 가능 상태가 아니므로 확인 요청 대상에서 뺀다(그 단서 하나에만 적용).
                if (Clues.Exists(clueId) && Clues.GetRecord(clueId).IsConcealed) continue;
                var roll = _confirmation.Roll(handler, sameRoom, PoliceSuspicion.GetSuspicion(handler, PlayerId), PoliceCluesFoundIn(PlayerRoom), acceptedLies);
                if (roll.Requested)
                {
                    Clues.GetRecord(clueId).MarkConfirmedByPolice(handler);
                    result.ConfirmedByPoliceClueIds.Add(clueId);
                }
            }
        }

        private void DiscoverMainCaseCluesAtScene(GameActionResult result)
        {
            var context = MainCaseClueContext.FromContract(CurrentContract, Npcs);
            foreach (var code in _mainCaseClues.GetEligibleClues(context, MainCase))
            {
                if (code == MainCaseClueCode.SubjectNumberFile)
                    continue; // 비밀번호 입력이 필요하다.
                var route = MainCaseClueRoute.FoundInContract;
                if (code == MainCaseClueCode.SubjectManagementTable)
                {
                    // 03 2026-11-12: 관리표는 현장 무작위 등장 25%(의뢰마다 한 번만 판정, 재추첨 없음) + 조직 전달 경로.
                    var rollEvent = new GameEvent($"maincase-random-table:{CurrentContract.ContractId}", GameEventType.Custom, CurrentContract.ContractId, "SubjectManagementTable");
                    if (!State.EventLog.TryRecord(rollEvent)) continue;
                    route = MainCaseClueRoute.RandomAppearance;
                }
                if (_mainCaseClues.TryAcquire(code, route, context, MainCase, Clues) == MainCaseClueAcquireOutcome.Acquired)
                    result.NewMainCaseClues.Add(MainCaseClueAcquisitionService.GetClueName(code));
            }
            AdvanceMainCaseStages();
        }

        /// <summary>
        /// 이 의뢰에 배치한 메인 사건 문서(권력의 진실 자료·반복 실험 번호 목록)를 보관 오브젝트에서 찾는다. 같은 문서는 한 번만 얻는다.
        /// </summary>
        private void DiscoverPlannedMainCaseDocument(string objectId, GameActionResult result)
        {
            var c = CurrentContract;
            if (c?.PlannedMainCaseClue == null || c.PlannedClueObjectId == null || c.PlannedClueObjectId != objectId) return;
            if (MainCase.HasAcquired(c.PlannedMainCaseClue)) return;
            GrantMainCaseDocument($"maincase:{c.ContractId}:{c.PlannedMainCaseClue}", c.PlannedMainCaseClue);
            result.NewMainCaseClues.Add(c.PlannedMainCaseClue);
        }

        public const string RebelMemoClueId = "maincase:RebelInternalCircularMemo";

        /// <summary>
        /// 노션 "최종 보고서 원문 및 타자기 연출" 2026-11-10 '반란 세력 내부 회람 메모' 획득 경로:
        /// 메인 진실 단계가 설계자의 진실에 도달한 뒤 수락한 의뢰에서, 사건 관련 인물 또는 피해자(가해자 제외) 1명을
        /// 반란 세력 연락책으로 정하고, 그 인물이 사건 당시 머문 방에 '서류 봉투'로 둔다. 확보할 때까지 의뢰마다 다시 놓는다.
        /// 연락책 지정은 실제 역할·진실을 바꾸지 않고 별도 값으로만 기록한다. 조직 전달·신뢰도 구간은 적용하지 않는다.
        /// </summary>
        private void PlaceRebelMemoIfNeeded(GeneratedContract offer)
        {
            Campaign.RebelContactNpcId = null;
            Campaign.RebelMemoRoomId = null;

            if (MainCase.CurrentStage != MainCaseTruthStage.DesignerTruth || MainCase.HasAcquired(MainCaseClueNames.RebelInternalCircularMemo))
                return;

            var candidates = Npcs.GetByContract(CurrentContract.ContractId)
                .Where(n => n.ActualRole == NpcActualRole.CaseRelatedA || n.ActualRole == NpcActualRole.CaseRelatedB
                            || n.ActualRole == NpcActualRole.Victim)
                .OrderBy(n => n.NpcId, StringComparer.Ordinal)
                .ToList();
            if (candidates.Count == 0)
                return;

            var contact = candidates[_random.Next(candidates.Count)];
            var core = offer.Timeline.CoreIncident;
            string room = core != null ? offer.Timeline.GetRoomAt(contact.NpcId, core.StartMinute) : null;

            Campaign.RebelContactNpcId = contact.NpcId;
            Campaign.RebelMemoRoomId = room != null && Rooms.RoomExists(room) ? room : Campaign.SceneRoomId;
        }

        /// <summary>메모가 놓인 방을 조사하면 확보한다. 메모는 의뢰 보고서 근거 자료가 아니므로 조사 자료 목록에 넣지 않는다.</summary>
        private void AcquireRebelMemo(GameActionResult result)
        {
            if (MainCase.HasAcquired(MainCaseClueNames.RebelInternalCircularMemo))
                return;

            GrantMainCaseDocument(RebelMemoClueId, MainCaseClueNames.RebelInternalCircularMemo);
            result.NewMainCaseClues.Add(MainCaseClueNames.RebelInternalCircularMemo);
            Campaign.RebelMemoRoomId = null;
        }

        private void GrantMainCaseDocument(string clueId, string clueName)
        {
            if (!Clues.Exists(clueId))
            {
                Clues.Discover(new ClueDefinition(clueId, MainCaseContractId)
                {
                    Format = ClueFormat.Document,
                    TruthStatus = ClueTruthStatus.Factual,
                    DisplayName = clueName
                });
            }
            MainCase.AcquireClue(clueName);
            AdvanceMainCaseStages();
        }

        private void AdvanceMainCaseStages()
        {
            while (MainCase.TryAdvanceStage()) { }
            // 조직 보조 자료 조건 D의 기준점: 각 진실 단계가 처음 열린 시점의 완료 의뢰 수.
            foreach (MainCaseTruthStage stage in Enum.GetValues(typeof(MainCaseTruthStage)))
                if (stage <= MainCase.CurrentStage && !Campaign.StageUnlockedAtCompleted.ContainsKey(stage.ToString()))
                    Campaign.StageUnlockedAtCompleted[stage.ToString()] = Campaign.CompletedContractCount;
        }

        private List<ICardJudge> BuildJudges()
        {
            var lie = new LieDetectionService(Clues, _random);
            var contradictions = EvidenceMaterialGenerator.BuildContradictionIndex(Materials);
            var judges = new List<ICardJudge>();
            foreach (var npc in Npcs.GetByContract(CurrentContract.ContractId).Where(n => n.IsAlive && Occupancy.HasPosition(n.NpcId)))
                judges.Add(new NpcCardJudge(npc, Relationships, lie, contradictions));
            if (HandlerPoliceId != null && PoliceLife.IsAlive(HandlerPoliceId))
                judges.Add(new PoliceCardJudge(HandlerPoliceId, PoliceSuspicion, PoliceLife, Clues, Capacity, lie, contradictions));
            return judges;
        }

        private float SuspicionTowardPlayer(string observerId) =>
            PoliceRoster.AllPoliceIds.Contains(observerId)
                ? PoliceSuspicion.GetSuspicion(observerId, PlayerId)
                : Relationships.GetSuspicion(observerId, PlayerId);

        /// <summary>경찰 판정자의 변화는 경찰 의심도에, NPC 판정자의 변화는 그 NPC의 플레이어에 대한 관계 의심도(0~100)에 반영한다.</summary>
        private void ApplySuspicionDelta(SuspicionDeltaOutcome delta)
        {
            if (delta == null || delta.WasDuplicateAction || delta.Delta == 0f)
                return;

            if (PoliceRoster.AllPoliceIds.Contains(delta.ObserverId))
                PoliceSuspicion.ApplyBatch(delta.ObserverId, delta.SuspectId, new[] { delta.Delta });
            else
                Relationships.SetSuspicion(delta.ObserverId, delta.SuspectId,
                    ProbabilityCalculator.Clamp(Relationships.GetSuspicion(delta.ObserverId, delta.SuspectId) + delta.Delta, 0f, 100f));
        }

        /// <summary>NPC의 공개 역할(07 public_role). 기록이 없으면 '사건 관련 인물'.</summary>
        public PublicRole GetPublicRole(string npcId) =>
            npcId != null && Campaign.PublicRoles.TryGetValue(npcId, out var r) && Enum.TryParse(r, out PublicRole role) ? role : PublicRole.CaseRelated;

        private void SetPublicRole(string npcId, PublicRole role)
        {
            if (string.IsNullOrEmpty(npcId) || npcId == PlayerId) return;
            // 공식 가해자 지목은 다른 공개 역할보다 우선한다.
            if (GetPublicRole(npcId) == PublicRole.Perpetrator && role != PublicRole.Perpetrator) return;
            Campaign.PublicRoles[npcId] = role.ToString();
        }

        /// <summary>07 2026-11-12: 경찰 초기 정보(police_snapshot)에 피해자로 나온 인물만 처음부터 '피해자'로 공개한다.</summary>
        private void RevealVictimFromPoliceSnapshot(ContractInstance contract)
        {
            foreach (var victim in new[] { contract.Truth.VictimId }.Concat(contract.Truth.AdditionalVictimIds))
            {
                if (victim == null || !Npcs.Exists(victim)) continue;
                string name = Npcs.Get(victim).DisplayName;
                if (!string.IsNullOrEmpty(name) && contract.PoliceKnownFacts.Any(f => f.Contains(name)))
                    SetPublicRole(victim, PublicRole.Victim);
            }
        }

        /// <summary>
        /// 07 2026-11-12: 목격자는 검증된 증언이 공식 기록에 들어갔을 때만 '목격자'로 공개한다.
        /// 회의에서 거짓·조작 없이 낸 목격 증언 카드를 판정자가 받아들이면 공식 기록에 들어간 것으로 본다(임시 해석).
        /// </summary>
        private void RevealWitnessIfVerified(string clueId, bool lying, CardManipulation manipulation)
        {
            if (lying || manipulation != CardManipulation.None) return;
            var m = Materials.FirstOrDefault(x => x.MaterialId == clueId);
            if (m == null || m.Format != ClueFormat.Testimony || m.SourceNpcId == null || !Npcs.Exists(m.SourceNpcId)) return;
            if (Npcs.Get(m.SourceNpcId).ActualRole != NpcActualRole.Witness) return;
            if (LastCardResolution.Judgements.Any(j => j.Participated && j.Accepts))
                SetPublicRole(m.SourceNpcId, PublicRole.Witness);
        }

        private void CloseMeeting()
        {
            if (Meeting != null && Meeting.IsActive)
                Meeting.EndSession();
            Meeting = null;
            MeetingResolver = null;
            MeetingFlow = null;
        }

        // ───────────────────────── 이민(10, 사용자 확정 2026-11-20) ─────────────────────────

        /// <summary>
        /// 지금 의뢰에서 플레이어가 가해자로 명시적으로 지목된 사건 상태를 기록한다(player_accused_case_id). 현재 의뢰에만 묶이며
        /// 다음 의뢰로 넘어가지 않는다. 단순 의심·추측·개인 의견으로는 호출하지 않는다 — 지목 사건을 만드는 쪽이 부른다.
        /// </summary>
        /// <summary>담당 경찰이 회의에서 플레이어를 가해자로 공개 지목하는 의심도 기준(사용자 확정: 담당 경찰이 지목 — 수치는 TEST_TEMP).</summary>
        public const float HandlerAccusationThreshold = 80f;

        /// <summary>
        /// 회의 중 담당 경찰의 플레이어 의심도가 기준 이상이면(100 미만 — 100은 체포) 경찰이 플레이어를 가해자로 공개 지목한다.
        /// 이것이 이민 자격의 '지목' 사건이다. 의뢰당 한 번 기록.
        /// </summary>
        private void TryHandlerAccusation()
        {
            string handler = HandlerPoliceId;
            if (State.CurrentPhase != GamePhase.Meeting || handler == null || !PoliceLife.IsAlive(handler)) return;
            float suspicion = PoliceSuspicion.GetSuspicion(handler, PlayerId);
            if (suspicion >= HandlerAccusationThreshold && suspicion < PolicePursuitController.ArrestThreshold)
                RecordPlayerAccused("handler-police:" + handler);
        }

        public bool RecordPlayerAccused(string source)
        {
            if (CurrentContract == null || IsGameOver) return false;
            if (!State.EventLog.TryRecord(new GameEvent($"player-accused:{CurrentContract.ContractId}", GameEventType.Custom, CurrentContract.ContractId, source)))
                return false;
            Campaign.PlayerAccusedCaseId = CurrentContract.ContractId;
            return true;
        }

        /// <summary>
        /// 이민(도주) 선택 가능 여부: 실제 사건 유형이 살인인 현재 의뢰에서 플레이어 지목 상태가 기록됐고, 경찰 추적 시작 전이며,
        /// 체포 조건(살아 있는 경찰 의심도 100, 공식 결론의 플레이어 지목)이 없을 때만.
        /// </summary>
        public bool CanEmigrate =>
            !IsGameOver && CurrentContract != null
            && CurrentContract.PrimaryCrimeType == CrimeType.Murder
            && Campaign.PlayerAccusedCaseId == CurrentContract.ContractId
            && Pursuit.Stage == PursuitStage.NotActive
            && CurrentContract.OfficialConclusion.OfficiallyAccusedNpcId != PlayerId
            && !PoliceRoster.AllPoliceIds.Where(PoliceLife.IsAlive).Any(p => PoliceSuspicion.GetSuspicion(p, PlayerId) >= PolicePursuitController.ArrestThreshold);

        /// <summary>이민 선택: 준비 없이 해외로 도주 → 조직에 추적당해 살해. 게임 오버.</summary>
        public GameActionResult Emigrate()
        {
            if (!CanEmigrate) return GameActionResult.Fail(GameActionFailure.InvalidInput);
            ExecuteEnding(GameEndingType.Immigration);
            return GameActionResult.Ok();
        }

        private void ExecuteEnding(GameEndingType ending)
        {
            if (Endings.TryExecute(ending))
                State.EventLog.TryRecord(new GameEvent($"ending:{ending}", GameEventType.EndingTriggered, CurrentContract?.ContractId, ending.ToString()));
        }

        /// <summary>상태 기반 엔딩(체포·경찰 전원 제거·조직 신뢰도 0)을 검사한다. 체포 판정은 진행 중 의뢰의 담당 경찰 기준이다.</summary>
        public void CheckEndings()
        {
            // 경찰 추적 중: 의심도 100인 경찰이 한 명이라도 있으면 추적 성공 판정보다 즉시 체포가 우선한다.
            // 아니면 의심도 70 이상 경찰이 2명 이하일 때 추적 성공(의심도 약 50 조정·추적 종료·일반 의뢰 복귀).
            if (!IsGameOver && Pursuit.Stage != PursuitStage.NotActive)
            {
                var check = Pursuit.Evaluate(PlayerId, PoliceRoster.AllPoliceIds.Where(PoliceLife.IsAlive));
                if (check == PursuitCheckResult.ArrestRequired)
                {
                    ExecuteEnding(GameEndingType.Arrest);
                    return;
                }
                if (check == PursuitCheckResult.CompletedSuccess)
                    State.EventLog.TryRecord(new GameEvent($"pursuit-ended:{NextEventId("pursuit")}", GameEventType.Custom, CurrentContract?.ContractId, "success"));
            }

            var before = Endings.GameOverEnding;
            if (Endings.TryEvaluateAndExecute(PlayerId, PoliceLife, HandlerPoliceId) && before == null && Endings.GameOverEnding.HasValue)
            {
                var ending = Endings.GameOverEnding.Value;
                State.EventLog.TryRecord(new GameEvent($"ending:{ending}", GameEventType.EndingTriggered, CurrentContract?.ContractId, ending.ToString()));
            }
        }

        private string NextEventId(string kind) =>
            $"{CurrentContract?.ContractId ?? "none"}-mtg{_meetingIndex}-{kind}-{++_actionCounter}";
    }
}
