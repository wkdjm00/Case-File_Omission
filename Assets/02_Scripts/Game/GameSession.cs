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

        /// <summary>테스트에서 시드를 고정할 때 넘긴다. null이면 새 Random.</summary>
        public Random Random;
    }

    /// <summary>
    /// 개별 시스템을 하나의 게임 루프로 묶는 진행 계층. UI·씬은 이 클래스의 메서드만 호출하면 된다.
    ///
    /// 한 의뢰의 흐름(06·03·05·10 확정 흐름을 따른다):
    /// 1) <see cref="OfferContract"/> — 조직이 전화로 의뢰를 제안한다. 조직 조건은 이때 공개된다.
    /// 2) <see cref="AcceptContract"/> — 수락하면 조건이 고정되고 담당 경찰 1명이 배정되며 조사가 시작된다.
    ///    조직_01(자살 위장 조건 의뢰)은 이 시점에 검사한다.
    /// 3) 조사 — 아침·저녁 구간마다 12칸. 이동·방 조사·대화·회유·사격·재장전이 칸을 쓰고, 쓴 칸만큼 NPC가 행동한다.
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

        /// <summary>임시값: 조사 시간 1칸(1시간)이 지날 때 NPC 시간표 기준 시각이 얼마나 흐르는지(분).</summary>
        public const int NpcMinutesPerTimeSlot = 60;

        /// <summary>임시값: 결론 투표를 넘기지 않았을 때, 참가자가 플레이어 의심도가 이 값 미만이면 결론을 받아들인다고 본다.</summary>
        public const float ProvisionalConclusionAcceptThreshold = 35f;

        /// <summary>임시값: 물리 조작 판정에서 "대상이 의심하는 상태"(-15)로 보는 담당 경찰 의심도 경계(06 의심도 단계 '높음' 35).</summary>
        public const float ProvisionalSuspiciousThreshold = 35f;

        private readonly Random _random;
        private readonly ContractPipeline _pipeline;
        private readonly FirearmResolver _firearmResolver;
        private readonly NpcPersuasionService _persuasion;
        private readonly ClueConfirmationRequestService _confirmation;
        private readonly MainCaseClueAcquisitionService _mainCaseClues;

        private int _meetingIndex;
        private int _actionCounter;
        private List<CardQuestion> _carriedOverQuestions = new List<CardQuestion>();

        public string PlayerId { get; }

        // ── 의뢰를 넘나드는 상태 ──
        public GameStateController State { get; } = new GameStateController();
        public GameLogger Logger { get; } = new GameLogger();
        public OrganizationState Organization { get; } = new OrganizationState();
        public OrganizationSupportClueService OrganizationSupport { get; } = new OrganizationSupportClueService();
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

        /// <summary>회의 도중에는 저장하지 않는다(회의 중 의문·차례 상태는 저장 대상이 아니다).</summary>
        public bool CanSave => State.CurrentPhase != GamePhase.Meeting;

        public GameSession(GameSessionConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            PlayerId = string.IsNullOrEmpty(config.PlayerId) ? "player" : config.PlayerId;
            _random = config.Random ?? new Random();

            var contractConfig = config.ContractConfig ?? ContractGenerationConfig.CreateDefault();
            _pipeline = new ContractPipeline(contractConfig, _random, Logger);
            _firearmResolver = new FirearmResolver(_random);
            _persuasion = new NpcPersuasionService(_random);
            _confirmation = new ClueConfirmationRequestService(_random);
            _mainCaseClues = new MainCaseClueAcquisitionService(config.MainCaseRules);

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
            GrantMainCaseDocument("maincase:NieceDiary", MainCaseClueNames.NieceDiary);
        }

        /// <summary>프롤로그 종료. 03 확정: 조카의 사망 관련 보고서는 프롤로그에서 확보한다.</summary>
        public void CompletePrologue()
        {
            GrantMainCaseDocument("maincase:NieceDeathReport", MainCaseClueNames.NieceDeathReport);
            AdvanceMainCaseStages();
        }

        // ───────────────────────── 의뢰 제안·수락 ─────────────────────────

        /// <summary>마지막 의뢰 생성 결과(후보 수·검증 횟수·실패 오류 코드). 진단용.</summary>
        public ContractGenerationResult LastGenerationResult { get; private set; }

        /// <summary>
        /// 조직의 의뢰 제안. 진실 → 유형 → 조직 조건 → 인물·시간표·단서 → 검증 순으로 만든다.
        /// 후보 10개 × 검증 3회 안에 검증을 통과한 의뢰가 없으면 예외 없이 null을 돌려준다(생성 실패, 플레이어에게 제공하지 않음).
        /// </summary>
        public GeneratedContract OfferContract()
        {
            if (IsGameOver) throw new InvalidOperationException("게임이 종료되었습니다.");
            if (State.CurrentPhase != GamePhase.Idle) throw new InvalidOperationException("진행 중인 의뢰가 있습니다.");

            if (PendingOffer == null)
            {
                PendingOffer = _pipeline.TryGenerate(() => $"contract-{++Campaign.ContractCounter}", out var result, Campaign.PreviousConditionType);
                LastGenerationResult = result;
            }
            return PendingOffer;
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
                : alive[_random.Next(alive.Count)];
            contract.HandlerPoliceId = handler;
            Campaign.NextHandlerPoliceId = null;
            Campaign.HandlerStartingSuspicion = PoliceSuspicion.GetSuspicion(handler, PlayerId);
            Campaign.MeetingsEndedInContract = 0;

            State.BeginContract(contract.ContractId, contract.StartTimeBlock);

            CurrentContract = contract;
            Npcs = offer.Npcs;
            Rooms = offer.Rooms;
            Occupancy = offer.Occupancy;
            Materials = offer.Materials.ToList();
            Campaign.SceneRoomId = offer.Timeline.SceneRoomId;
            Campaign.NpcMinute = offer.Timeline.InvestigationStartMinute;

            // 06: 보드가 현장으로 확대되고 경찰이 상황을 설명한다 — 플레이어와 담당 경찰은 현장에서 시작한다(임시 배치).
            Occupancy.PlaceAt(PlayerId, Campaign.SceneRoomId);
            Occupancy.PlaceAt(handler, Campaign.SceneRoomId);

            BuildNpcSystems();
            foreach (var schedule in offer.Timeline.Schedules.Values)
                Planner.SetSchedule(schedule);

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

            var result = GameActionResult.Ok();
            CheckEndings();
            return result;
        }

        // ───────────────────────── 조사 ─────────────────────────

        /// <summary>인접한 방으로 이동한다(1칸).</summary>
        public GameActionResult MovePlayer(string roomId)
        {
            var guard = GuardInvestigation(1);
            if (!guard.Success) return guard;
            if (!Rooms.RoomExists(roomId) || !Rooms.AreConnected(PlayerRoom, roomId))
                return GameActionResult.Fail(GameActionFailure.RoomNotConnected);

            var result = SpendTime(1);
            Occupancy.TryMove(PlayerId, roomId);
            return AfterAction(result);
        }

        /// <summary>
        /// 지금 방을 조사한다. 기본 1칸, 상세 수색은 2칸(06 2026-11-10). 이 방에서 얻을 수 있는 자료를 얻고, 떨어진 단서를 줍고,
        /// 사건 현장이면 조건이 맞는 메인 사건 단서를 발견한다. 담당 경찰이 같은 방이면 새 단서마다 단서 확인 요청을 굴린다.
        /// </summary>
        public GameActionResult InvestigateRoom(bool detailedSearch = false)
        {
            int cost = detailedSearch ? 2 : 1;
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

        /// <summary>같은 방의 살아 있는 NPC와 대화해 그 NPC의 증언을 얻는다(1칸).</summary>
        public GameActionResult TalkTo(string npcId)
        {
            var guard = GuardInvestigation(1);
            if (!guard.Success) return guard;
            var target = GuardNpcInRoom(npcId);
            if (!target.Success) return target;

            var result = SpendTime(1);
            foreach (var material in Materials.Where(m => m.Acquisition == MaterialAcquisition.TalkToNpc && m.SourceNpcId == npcId))
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
            var guard = GuardInvestigation(1);
            if (!guard.Success) return guard;
            var target = GuardNpcInRoom(npcId);
            if (!target.Success) return target;

            var npc = Npcs.Get(npcId);
            if (!playerKnowsActualCulprit || !npc.IsActualPerpetrator)
                return GameActionResult.Fail(GameActionFailure.InvalidInput);

            var result = SpendTime(1);
            var persuasion = _persuasion.TryPersuade(npc, playerKnowsActualCulprit, hasStrongSupportingEvidence,
                hasUnfavorableDirectEvidence, Relationships.GetTrust(npcId, PlayerId) > 0f);
            if (persuasion.Success)
            {
                foreach (var material in Materials.Where(m => m.SourceNpcId == npcId))
                    DiscoverMaterial(material, result);
            }
            LastPersuasion = persuasion;
            return AfterAction(result);
        }

        public PersuasionResult LastPersuasion { get; private set; }

        /// <summary>들고 있는 단서를 지금 방에 버린다(0칸). 보고서 기록은 남고, NPC·경찰이 주울 수 있다.</summary>
        public GameActionResult DropClue(string clueId)
        {
            var guard = GuardInvestigation(0);
            if (!guard.Success) return guard;
            if (!Clues.Exists(clueId) || Clues.GetRecord(clueId).WorldState != ClueWorldState.HeldByPlayer)
                return GameActionResult.Fail(GameActionFailure.InvalidInput);

            Clues.Drop(clueId);
            Occupancy.PlaceAt(clueId, PlayerRoom);
            return AfterAction(GameActionResult.Ok());
        }

        /// <summary>재장전(행동 1회 = 1칸으로 본다 — 행동 1회와 시간 칸의 관계는 원문에 없어 임시).</summary>
        public GameActionResult Reload()
        {
            var guard = GuardInvestigation(1);
            if (!guard.Success) return guard;
            if (!Firearm.CanReload) return GameActionResult.Fail(GameActionFailure.NothingToReload);

            var result = SpendTime(1);
            Firearm.Reload();
            return AfterAction(result);
        }

        public ShotEvent LastShot { get; private set; }

        /// <summary>
        /// 같은 방의 NPC나 경찰을 쏜다(1칸). 사격·목격·사망은 별도 이벤트로 기록하며 사격만으로 의심도를 올리지 않는다(06 확정).
        /// 같은 방의 살아 있는 NPC는 사격을 목격한다. 명중 시 표적이 사망한다 — 표적 상태 변화 규칙이 원문에 없어 임시 해석이다.
        /// 담당 경찰이 사망하면 의뢰는 미제로 끝난다. 사망은 추적 사건 발동 검사(3명 이상 70 이상 + 추가 사망)로 이어진다.
        /// </summary>
        public GameActionResult FireAt(string targetId, bool isCloseRange, bool hasUnstableAim, bool targetMovingFast, bool pathPartiallyBlocked)
        {
            var guard = GuardInvestigation(1);
            if (!guard.Success) return guard;
            if (!Firearm.CanFire) return GameActionResult.Fail(GameActionFailure.NoAmmo);

            bool isPolice = PoliceRoster.AllPoliceIds.Contains(targetId);
            bool isNpc = Npcs.Exists(targetId);
            if (!isPolice && !isNpc) return GameActionResult.Fail(GameActionFailure.UnknownTarget);
            if (!Occupancy.HasPosition(targetId) || Occupancy.GetRoom(targetId) != PlayerRoom)
                return GameActionResult.Fail(GameActionFailure.TargetNotInRoom);
            if ((isPolice && !PoliceLife.IsAlive(targetId)) || (isNpc && !Npcs.Get(targetId).IsAlive))
                return GameActionResult.Fail(GameActionFailure.TargetDeceased);

            var result = SpendTime(1);
            string room = PlayerRoom;
            var shot = _firearmResolver.Fire(Firearm, PlayerId, targetId, isCloseRange, hasUnstableAim, targetMovingFast,
                pathPartiallyBlocked, room, State.EventLog, CurrentContract.ContractId, NextEventId("shot"));
            LastShot = shot;

            foreach (var witness in Npcs.GetByContract(CurrentContract.ContractId)
                         .Where(n => n.IsAlive && n.NpcId != targetId && Occupancy.HasPosition(n.NpcId) && Occupancy.GetRoom(n.NpcId) == room))
            {
                shot.WitnessNpcIds.Add(witness.NpcId);
                shot.InfoAcquiredByNpcIds.Add(witness.NpcId);
                witness.LearnInfo(shot.EventId, NpcJudgement.Believe);
                witness.MarkWitnessed(shot.EventId);
            }

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
                    State.EventLog.TryRecord(new GameEvent(deathEventId, GameEventType.Custom, CurrentContract.ContractId, "npc-death:" + targetId));
                }

                if (Pursuit.Stage == PursuitStage.NotActive)
                    Pursuit.TryTrigger(PlayerId, deathEventId);

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
        /// 물리적 증거·기록 조작 시도(조사 중, 1칸 — 시간 비용은 원문에 없어 다른 행동과 같은 1칸으로 둠(임시)).
        /// 조작 가능 시점 전이면 Locked로 거부되고 시간도 쓰지 않는다. 성공 확률은 기본 70%에 확정 보정을 적용한다:
        /// 담당 경찰의 플레이어 의심도 35 이상(-15, 경계값은 임시), 같은 방에 다른 생존 인물(-20),
        /// 담당 경찰이 이미 확인한 단서(-10), 대상이 사실을 모름(+10, 호출자가 넘김).
        /// </summary>
        public ManipulationOutcomeResult AttemptPhysicalManipulation(string clueId, ManipulationType type, bool targetUnawareOfFacts = false)
        {
            var guard = GuardInvestigation(1);
            if (!guard.Success)
                return new ManipulationOutcomeResult { Outcome = ManipulationOutcome.Locked };
            if (!IsPhysicalManipulationUnlocked)
                return new ManipulationOutcomeResult { Outcome = ManipulationOutcome.Locked };
            if (!Clues.Exists(clueId) || Clues.GetRecord(clueId).WorldState != ClueWorldState.HeldByPlayer)
                throw new ArgumentException("플레이어가 들고 있는 단서만 조작할 수 있습니다.", nameof(clueId));

            if (type == ManipulationType.Conceal && Clues.GetHeldByPlayerClueIds(CurrentContract.ContractId).Count < 2)
                throw new ArgumentException("은폐하려면 대체할 다른 단서가 있어야 합니다.", nameof(type));

            string handler = HandlerPoliceId;
            bool suspicious = handler != null && PoliceSuspicion.GetSuspicion(handler, PlayerId) >= ProvisionalSuspiciousThreshold;
            bool witnessed = Occupancy.GetOccupants(PlayerRoom).Any(id =>
                id != PlayerId && ((Npcs.Exists(id) && Npcs.Get(id).IsAlive) || (PoliceRoster.AllPoliceIds.Contains(id) && PoliceLife.IsAlive(id))));
            bool confirmed = handler != null && Clues.GetRecord(clueId).ConfirmedByPoliceIds.Contains(handler);

            var result = SpendTime(1);
            var outcome = PhysicalManipulation.Attempt(type, clueId, PlayerId, suspicious, witnessed, confirmed, targetUnawareOfFacts,
                Clues.GetHeldByPlayerClueIds(CurrentContract.ContractId));
            AfterAction(result);
            return outcome;
        }

        /// <summary>예산이 남아 있어도 이번 조사 구간을 끝낸다.</summary>
        public GameActionResult EndInvestigationEarly()
        {
            if (IsGameOver) return GameActionResult.Fail(GameActionFailure.GameOver);
            if (State.CurrentPhase != GamePhase.Investigation) return GameActionResult.Fail(GameActionFailure.WrongPhase);
            State.EndTimeBlockEarly();
            return GameActionResult.Ok();
        }

        /// <summary>구간이 끝난 뒤 회의 대신 다음 조사 구간으로 넘어간다(아침→저녁→다음 날 아침).</summary>
        public GameActionResult AdvanceToNextTimeBlock()
        {
            if (IsGameOver) return GameActionResult.Fail(GameActionFailure.GameOver);
            if (State.CurrentPhase != GamePhase.Investigation) return GameActionResult.Fail(GameActionFailure.WrongPhase);
            if (!State.IsTimeBlockEnded) return GameActionResult.Fail(GameActionFailure.TimeBlockNotEnded);
            State.AdvanceTimeBlock();
            return GameActionResult.Ok();
        }

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
        public GameActionResult StartMeeting()
        {
            if (IsGameOver) return GameActionResult.Fail(GameActionFailure.GameOver);
            if (State.CurrentPhase != GamePhase.Investigation) return GameActionResult.Fail(GameActionFailure.WrongPhase);
            if (!State.IsTimeBlockEnded) return GameActionResult.Fail(GameActionFailure.TimeBlockNotEnded);

            State.EnterMeeting();
            _meetingIndex++;
            Meeting = new MeetingSession(CurrentContract.ContractId, Manipulation);
            MeetingResolver = new MeetingCardResolver(Meeting, PlayerId, _carriedOverQuestions);
            _carriedOverQuestions = new List<CardQuestion>();

            MeetingFlow = new MeetingFlow(PlayerId);
            foreach (var npc in Npcs.GetByContract(CurrentContract.ContractId).Where(n => n.IsAlive))
                MeetingFlow.AddParticipant(npc.NpcId, MeetingParticipantKind.Npc);
            if (HandlerPoliceId != null && PoliceLife.IsAlive(HandlerPoliceId))
                MeetingFlow.AddParticipant(HandlerPoliceId, MeetingParticipantKind.Police);

            MeetingFlow.DealPlayerHand(Clues.GetHeldByPlayerClueIds(CurrentContract.ContractId), _random);
            MeetingFlow.AdvanceTurn();
            return GameActionResult.Ok();
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
            if (manipulation == CardManipulation.Conceal && MeetingFlow.PlayerHand.Count < 2)
                return GameActionResult.Fail(GameActionFailure.InvalidInput);

            string eventId = NextEventId("card");
            var card = Meeting.PresentCard(eventId, PlayerId, clueId, manipulation, MeetingFlow.PlayerHand);
            MeetingFlow.RecordNormalCardPresented(PlayerId);

            LastCardResolution = MeetingResolver.Resolve(eventId, card, lying, hasSupportingEvidence, BuildJudges());
            foreach (var delta in LastCardResolution.SuspicionDeltas)
                ApplySuspicionDelta(delta);

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
        /// NPC·경찰이 자기 차례에 어떤 카드를 낼지는 판단 규칙이 원문에 없어 그 차례는 넘긴다(임시, 의심도 변화 없음).
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

            do { MeetingFlow.AdvanceTurn(); } while (MeetingFlow.CurrentTurnId != PlayerId);
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
            votes ??= BuildJudges().ToDictionary(j => j.JudgeId, j => SuspicionTowardPlayer(j.JudgeId) < ProvisionalConclusionAcceptThreshold);

            var conclusion = MeetingFlow.EvaluateConclusion(accusedId, votes);
            if (conclusion.Outcome == ConclusionOutcome.Adopted)
            {
                CurrentContract.OfficialConclusion.OfficiallyAccusedNpcId = accusedId;
                FinalizeContract(ContractOutcome.Resolved, abandoned: false);
            }
            else if (conclusion.Outcome == ConclusionOutcome.Unsolved)
            {
                FinalizeContract(ContractOutcome.Unsolved, abandoned: false);
            }
            return conclusion;
        }

        /// <summary>결론 없이 회의를 마치고 다음 구간 조사로 돌아간다. 처리하지 못한 의문은 다음 회의에서 가장 먼저 처리한다.</summary>
        public GameActionResult LeaveMeeting()
        {
            var guard = GuardMeeting();
            if (!guard.Success) return guard;

            _carriedOverQuestions = MeetingResolver.Questions.Where(q => !q.IsClosed).ToList();
            CloseMeeting();
            Campaign.MeetingsEndedInContract++;
            State.ReturnToInvestigation();
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
            if (ending.HasValue)
                ExecuteEnding(ending.Value);
            return ending;
        }

        /// <summary>반란군 영입 선택. 수락=반란군 영입 엔딩, 거절=보스 히든 엔딩. 둘 다 이후에도 의뢰가 계속된다.</summary>
        public GameEndingType DecideRebelRecruitment(bool accept)
        {
            var ending = PostReport.DecideRebelRecruitment(accept);
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
            if (!CanSave) throw new InvalidOperationException("회의 중에는 저장할 수 없습니다.");
            var data = SaveGameService.Capture(State, Organization, PoliceSuspicion, PoliceLife, MainCase, Endings, PlayerId);
            data.World = WorldStateSaveService.Capture(BuildSaveTargets());
            return data;
        }

        public static GameSession Load(SaveGameData data, GameSessionConfig config)
        {
            var session = new GameSession(config);
            SaveGameService.Restore(data, session.State, session.Organization, session.PoliceSuspicion, session.PoliceLife,
                session.MainCase, session.Endings, session.PlayerId, session.Logger);

            session.Rooms = HouseLayoutFactory.CreateStandardHouse();
            session.Occupancy = new RoomOccupancyTracker(session.Rooms);
            var targets = session.BuildSaveTargets();
            WorldStateSaveService.Restore(data?.World, targets, session.Logger);

            if (session.State.CurrentPhase == GamePhase.Investigation && targets.Contract != null)
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

            session.BuildNpcSystems(keepTransferLog: true);
            // 불러온 뒤 새로 만드는 이벤트 ID가 저장된 이벤트와 겹치지 않게 한다.
            session._actionCounter = session.State.EventLog.GetOrdered().Count;
            return session;
        }

        private WorldStateTargets BuildSaveTargets() => new WorldStateTargets
        {
            Clues = Clues,
            Manipulation = Manipulation,
            ManipulationOutcomes = PhysicalManipulation,
            Capacity = Capacity,
            Npcs = Npcs,
            Relationships = Relationships,
            InfoTransfer = InfoTransfer,
            Reports = Reports,
            MainCase = MainCase,
            FinalReport = FinalReport.Report,
            PostReport = PostReport,
            Occupancy = Occupancy,
            EventLog = State.EventLog,
            Contract = CurrentContract,
            Pursuit = Pursuit,
            Firearm = Firearm,
            OrganizationSupport = OrganizationSupport,
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
                    ContractOutcome.HandlerPoliceDeceased => ContractEndKind.HandlerPoliceDeceased,
                    _ => ContractEndKind.Unsolved
                };

                string next = null;
                if (kind == ContractEndKind.HandlerPoliceDeceased)
                {
                    var candidates = PoliceRoster.AllPoliceIds.Where(id => id != contract.HandlerPoliceId && PoliceLife.IsAlive(id)).ToList();
                    next = candidates.Count == 0 ? null : candidates[_random.Next(candidates.Count)];
                    Campaign.NextHandlerPoliceId = next;
                }
                PoliceSuspicion.ApplyContractEndEffects(PlayerId, contract.HandlerPoliceId, Campaign.HandlerStartingSuspicion, kind, next);

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
        }

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
            Planner = new NpcBehaviorPlanner(Npcs, Occupancy, Rooms, InfoTransfer, Relationships, Clues, Capacity, random: _random);
        }

        private GameActionResult GuardInvestigation(int cost)
        {
            if (IsGameOver) return GameActionResult.Fail(GameActionFailure.GameOver);
            if (State.CurrentPhase != GamePhase.Investigation || CurrentContract == null)
                return GameActionResult.Fail(GameActionFailure.WrongPhase);
            if (cost > 0 && State.IsTimeBlockEnded) return GameActionResult.Fail(GameActionFailure.TimeBlockEnded);
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
            State.TrySpendTime(slots);
            return new GameActionResult { TimeSpent = slots };
        }

        /// <summary>행동 효과 뒤에 쓴 칸만큼 NPC가 행동하고(화면 밖 포함), 엔딩을 검사한다.</summary>
        private GameActionResult AfterAction(GameActionResult result)
        {
            if (CurrentContract != null)
            {
                for (int i = 0; i < result.TimeSpent; i++)
                {
                    Campaign.NpcMinute += NpcMinutesPerTimeSlot;
                    Planner.ExecuteTurn($"{CurrentContract.ContractId}-m{Campaign.NpcMinute}", CurrentContract.ContractId, Campaign.NpcMinute);
                }
            }
            CheckEndings();
            return result;
        }

        private GameActionResult AfterMeetingAction()
        {
            CheckEndings();
            return GameActionResult.Ok();
        }

        private GameActionResult ApplyQuestionAction(QuestionActionResult action)
        {
            var guard = GuardMeeting();
            if (!guard.Success) return guard;
            if (action == null || action.Outcome != QuestionActionOutcome.Applied)
                return GameActionResult.Fail(GameActionFailure.InvalidInput);
            if (action.SuspicionDelta != null)
                ApplySuspicionDelta(action.SuspicionDelta);
            return AfterMeetingAction();
        }

        private void DiscoverMaterial(EvidenceMaterial material, GameActionResult result)
        {
            if (Clues.Exists(material.MaterialId))
                return;
            var definition = material.ToClueDefinition();
            definition.DisplayName = material.MaterialId;
            Clues.Discover(definition);
            result.NewClueIds.Add(material.MaterialId);
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
                var roll = _confirmation.Roll(handler, sameRoom, PoliceSuspicion.GetSuspicion(handler, PlayerId), 0, acceptedLies);
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
                if (_mainCaseClues.TryAcquire(code, MainCaseClueRoute.FoundInContract, context, MainCase, Clues) == MainCaseClueAcquireOutcome.Acquired)
                    result.NewMainCaseClues.Add(MainCaseClueAcquisitionService.GetClueName(code));
            }
            AdvanceMainCaseStages();
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
        }

        private List<ICardJudge> BuildJudges()
        {
            var lie = new LieDetectionService(Clues, _random);
            var contradictions = EvidenceMaterialGenerator.BuildContradictionIndex(Materials);
            var judges = new List<ICardJudge>();
            foreach (var npc in Npcs.GetByContract(CurrentContract.ContractId).Where(n => n.IsAlive))
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

        private void CloseMeeting()
        {
            if (Meeting != null && Meeting.IsActive)
                Meeting.EndSession();
            Meeting = null;
            MeetingResolver = null;
            MeetingFlow = null;
        }

        private void ExecuteEnding(GameEndingType ending)
        {
            if (Endings.TryExecute(ending))
                State.EventLog.TryRecord(new GameEvent($"ending:{ending}", GameEventType.EndingTriggered, CurrentContract?.ContractId, ending.ToString()));
        }

        /// <summary>상태 기반 엔딩(체포·경찰 전원 제거·조직 신뢰도 0)을 검사한다. 체포 판정은 진행 중 의뢰의 담당 경찰 기준이다.</summary>
        public void CheckEndings()
        {
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
