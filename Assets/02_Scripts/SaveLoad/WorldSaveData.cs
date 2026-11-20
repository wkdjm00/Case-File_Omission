using System;
using CaseFileOmission.ContractContent;
using CaseFileOmission.Contracts;
using CaseFileOmission.Core;
using CaseFileOmission.Evidence;
using CaseFileOmission.Police;
using CaseFileOmission.MainCase;
using CaseFileOmission.Meeting;
using CaseFileOmission.NPC;
using CaseFileOmission.Organization;
using CaseFileOmission.Reports;

namespace CaseFileOmission.SaveLoad
{
    // UnityEngine.JsonUtility는 Dictionary·튜플·DateTime·nullable을 직렬화하지 못한다.
    // 그래서 런타임의 Dictionary 기반 상태를 키-값 배열 DTO로 펼치고, 시각은 ticks(long)로 저장한다.

    [Serializable]
    public sealed class ClueDefinitionDto
    {
        public string ClueId;
        public string ContractId;
        public ClueFormat Format;
        public ClueTruthStatus TruthStatus;
        public string DisplayName;
        public string DisplayedContent;
        public string RelatedTruthNote;
        public string SourceDescription;
        public string AcquisitionCondition;
        public bool IdentifiesPerpetrator;
        public bool IsDirectPerpetratorEvidence;
    }

    [Serializable]
    public sealed class ClueRecordDto
    {
        public string ClueId;
        public long DiscoveredAtTicks;
        public ClueWorldState WorldState;
        public string[] ConfirmedByPoliceIds;
        public ClueAccessState Access;
        public ConcealmentResult Concealment;
        public bool ConcealmentDetected;
    }

    [Serializable]
    public sealed class ManipulationRecordDto
    {
        public string OriginalClueId;
        public ManipulationType Type;
        public string ActorId;
        public string DisplayedClueId;
        public long AppliedAtTicks;
    }

    [Serializable]
    public sealed class ManipulationAttemptDto
    {
        public string ClueId;
        public ManipulationType Type;
        public string ActorId;
        public long AttemptedAtTicks;
    }

    [Serializable]
    public sealed class HolderHoldingDto
    {
        public string HolderId;
        public HolderType HolderType;
        public string[] ClueIds;
    }

    [Serializable]
    public sealed class EvidenceSaveData
    {
        public ClueDefinitionDto[] Definitions;
        public ClueRecordDto[] Records;
        public ManipulationRecordDto[] ManipulationHistory;
        public ManipulationAttemptDto[] FailedManipulationAttempts;
        public HolderHoldingDto[] Holdings;
    }

    [Serializable]
    public sealed class NpcKnowledgeDto
    {
        public string InfoId;
        public bool HasJudgement;
        public NpcJudgement Judgement;
    }

    [Serializable]
    public sealed class NpcStateDto
    {
        public string NpcId;
        public string ContractId;
        public NpcActualRole ActualRole;
        public NpcPersonality Personality;
        public string Occupation;
        public string DisplayName;
        public bool IsAlive;
        public bool IsCooperatingWithPlayer;
        public NpcKnowledgeDto[] Knowledge;

        /// <summary>실제로 목격한 사건 ID(버전 2부터).</summary>
        public string[] WitnessedEventIds;
    }

    [Serializable]
    public sealed class DirectionalValueDto
    {
        public string SourceId;
        public string TargetId;
        public float Value;
    }

    [Serializable]
    public sealed class InfoTransferDto
    {
        public string SenderId;
        public string ReceiverId;
        public string InfoId;
    }

    [Serializable]
    public sealed class NpcSaveData
    {
        public NpcStateDto[] Npcs;
        public DirectionalValueDto[] Suspicion;
        public DirectionalValueDto[] Trust;
        public InfoTransferDto[] Transfers;
        public NpcBehaviorMovementState MovementState;
    }

    [Serializable]
    public sealed class GameEventDto
    {
        public string EventId;
        public GameEventType Type;
        public string ContractId;
        public string Payload;
        public long OccurredAtTicks;
    }

    [Serializable]
    public sealed class MeetingCounterDto
    {
        public MeetingCounterKind Kind;
        public string ObserverId;
        public string SuspectId;
        public int Count;
    }

    [Serializable]
    public sealed class MeetingParticipantPresentedDto
    {
        public string PresenterId;
        public string MaterialId;
    }

    [Serializable]
    public sealed class MeetingSaveData
    {
        public bool HasSession;
        public string ContractId;
        public bool IsActive;
        public MeetingCounterDto[] Counters;
        public GameEventDto[] ProcessedActions;
        public MeetingFlowSnapshot FlowSnapshot;
        public MeetingCardResolverSnapshot ResolverSnapshot;
        public MeetingParticipantPresentedDto[] ParticipantPresented;
    }

    [Serializable]
    public sealed class ReportSectionDto
    {
        public ReportSection Section;
        public string Content;
        public string[] LinkedClueIds;
        public string ClaimedNpcId;
    }

    [Serializable]
    public sealed class ContractReportSaveData
    {
        public string ContractId;
        public bool IsSubmitted;
        public ReportSectionDto[] Sections;
        public ReportSlotDto[] Slots;
    }

    [Serializable]
    public sealed class StageUnlockDto
    {
        public string Stage;
        public int CompletedAt;
    }

    [Serializable]
    public sealed class OrgReportDto
    {
        public string Id;
        public OrgReportRollStatus RollStatus;
        public OrgReportDeliveryStatus DeliveryStatus;
        public string EligibilityBand;
        public string FirstEligibleContractId;
        public int DeliveredWorldMinute = -1;
        public bool Read;
    }

    [Serializable]
    public sealed class PublicRoleDto
    {
        public string NpcId;
        public string Role;
    }

    [Serializable]
    public sealed class OpinionDto
    {
        public string Observer;
        public string Suspect;
        public float Value;
    }

    [Serializable]
    public sealed class ReportSlotDto
    {
        public int Index;
        public bool Unlocked;
        public ReportSlotStatus Status;
        public string LastAnswer;
    }

    [Serializable]
    public sealed class CaseBoardItemDto
    {
        public string ItemId;
        public bool BoardDecorationOnly;
        public float X, Y, RotationDegrees;
        public bool OnBoard;
    }

    [Serializable]
    public sealed class CaseBoardLinkDto
    {
        public string ItemA, ItemB;
    }

    /// <summary>의뢰 하나의 사건보드 배치·폐기·연결선(09 2026-11-13). 중앙 메모는 연결선에서 다시 계산한다.</summary>
    [Serializable]
    public sealed class CaseBoardSaveData
    {
        public string ContractId;
        public int Seed;
        public int LayoutVersion;
        public CaseBoardItemDto[] Items;
        public CaseBoardLinkDto[] Links;
    }

    [Serializable]
    public sealed class MainCaseClueAcquisitionDto
    {
        public string ClueName;
        public int AcquiredAtCompletedContractCount;
    }

    [Serializable]
    public sealed class FinalReportBlankDto
    {
        public int Index;
        public bool IsFilled;
        public string[] CollectedClueNames;
    }

    [Serializable]
    public sealed class MainCaseExtendedSaveData
    {
        public int CompletedContractCount;
        public MainCaseClueAcquisitionDto[] Acquisitions;
        public bool HasFinalReport;
        public FinalReportBlankDto[] FinalReportBlanks;
        public bool HasPostReport;
        public PhoneCallStage PhoneStage;
        public RebelRecruitmentStage RebelStage;
    }

    [Serializable]
    public sealed class OccupantPositionDto
    {
        public string OccupantId;
        public string RoomId;
    }

    [Serializable]
    public sealed class SubCrimeLinkDto
    {
        public CrimeType Type;
        public string LinkReason;
        public string[] LinkedNpcIds;
        public string EventId;
    }

    /// <summary>진행 중인 의뢰 하나. 실제 진실/외부 상태/공식 결론/조직 조건을 분리된 구역 그대로 저장한다.</summary>
    [Serializable]
    public sealed class ContractSaveData
    {
        public bool HasContract;
        public string ContractId;
        public CrimeType PrimaryCrimeType;
        public LocationCategory LocationCategory;
        public StubLocation Location;
        public TimeBlock StartTimeBlock;
        public string HandlerPoliceId;
        public string IncidentDate;
        public string FixtureId;
        public string PlayerStartRoomId;
        public bool HasInvestigationStartOverride;
        public int InvestigationStartMinuteOverride;
        public bool HasRepresentativeCrimeType;
        public CrimeType RepresentativeCrimeType;
        public string[] PoliceKnownFacts;
        public string[] PoliceUnknownFacts;
        public string PoliceClassificationBasis;
        public int IncidentClockMinute;
        public int HandoverDayOffset;
        public SubCrimeLinkDto[] SubCrimes;

        public string PerpetratorId;
        public string VictimId;
        public string PrimaryMotive;
        public string SecondaryMotive;
        public string Method;
        public string Tool;
        public string RelationshipType;
        public CoverUpMethod CoverUp;
        public string[] TimelineEvents;
        public string MethodId;
        public string ToolId;
        public string MotiveId;
        public string RelationshipId;
        public string RepresentativeSubtypeId;
        public string ActualSubtypeId;
        public bool VictimDeparted;
        public bool BodyHidden;
        public bool DelayedDeath;
        public string[] AdditionalVictimIds;
        public bool VictimsAreCouple;
        public string PlannedMainCaseClue;
        public string PlannedClueObjectId;
        public string DocumentHolderNpcId;
        public string DocumentHolderTag;

        public string ReportedSummary;
        public string InitialExplanation;
        public bool DiffersFromTruth;
        public string DifferenceNote;

        public bool ConclusionFinalized;
        public string OfficiallyAccusedNpcId;
        public ContractOutcome Outcome;

        public OrganizationConditionType ConditionType;
        public string ConditionTargetId;
        public string[] ConditionTargetIds;
        public bool ConditionRequiresDelivery;
        public bool ConditionJudged;
        public bool ConditionAchieved;
        public bool ConditionLocked;
    }

    /// <summary>의뢰를 넘나드는 진행 상태: 추적 단계, 총기 탄약, 조직 지원 단서 지급 기록, 의뢰 생성·종료 처리에 쓰는 값.</summary>
    [Serializable]
    public sealed class CampaignSaveData
    {
        public PursuitStage PursuitStage;
        public string PursuitTriggerId;
        public bool HasFirearm;
        public int FirearmLoaded;
        public int FirearmReserve;
        public string[] GrantedSupportClueIds;
        public int ContractCounter;
        public OrganizationConditionType PreviousConditionType;
        public float HandlerStartingSuspicion;
        public string NextHandlerPoliceId;
        public bool PendingHandlerDeathBonus;
        public int CompletedContractCount;
        public StageUnlockDto[] StageUnlocks;
        public string[] GeneratedMainDocuments;
        public bool PendingOrgDeliveryCall;
        public OrgReportDto[] OrgReports;
        public string SceneRoomId;
        public int NpcMinute;
        public int BlockStartMinute;
        public string[] HandlerQueue;
        public string LastHandlerPoliceId;
        public bool PersuasionAttempted;
        public string[] PoliceInspectedObjectIds;
        public string[] PhysicallyDistortedClueIds;
        public string PlayerAccusedCaseId;
        public string[] StolenMaterialIds;
        public TheftIncident[] TheftIncidents;
        public string RunId;
        public int SaveSequence;
        public bool CulpritIdentified;
        public OpinionDto[] Opinions;
        public PublicRoleDto[] PublicRoles;
        public int MeetingsEndedInContract;
        public string RebelContactNpcId;
        public string RebelMemoRoomId;
        public EvidenceMaterialDto[] Materials;
    }

    [Serializable]
    public sealed class EvidenceMaterialDto
    {
        public string MaterialId;
        public string ContractId;
        public ClueFormat Format;
        public ClueTruthStatus TruthStatus;
        public string SourceNpcId;
        public string RoomId;
        public MaterialAcquisition Acquisition;
        public string AboutEventId;
        public int AboutMinute;
        public string AboutRoomId;
        public string[] StatedInfoIds;
        public string SubjectNpcId;
        public string[] RebuttedByMaterialIds;
        public string FalsehoodReason;
        public bool IdentifiesPerpetrator;
        public bool IsDirectPerpetratorEvidence;
        public string ObjectId;
        public string RequiresClueId;
        public string Title;
        public string Content;
    }

    /// <summary>
    /// 통합 명세서 §12의 세부 상태(단서 그래프·발견·소유·조작 이력, NPC 상태·지식·전달 기록,
    /// 회의 단계·연속 카운터, 보고서 진행·연결 단서·제출 여부)와 메인 사건 확장 상태를 담는다.
    /// 버전 2부터 진행 중인 의뢰(진실·조직 조건 등)와 의뢰 간 진행 상태(추적·탄약·지원 단서)를 함께 담는다.
    /// 각 구역은 대상 객체가 없을 때 비워 두며, 불러올 때도 빈 구역은 건너뛴다.
    /// </summary>
    /// <summary>
    /// 06 「단서·절도 행동 규칙 대조」 부수사건: 직접 목격하거나 관련 NPC가 부재를 발견한 시점에 생긴다. 같은 행위·같은 물건이면 하나만 만들고,
    /// 이후 목격·발견은 NPC별 인지로 덧붙인다(자동 전파 없음). 부재 발견은 '없어졌다'만 알 뿐 범인을 확정하지 않는다.
    /// </summary>
    [Serializable]
    public sealed class TheftIncident
    {
        public string IncidentId;
        public string MaterialId;
        public string RoomId;
        public int Minute;
        public bool IsBody;
        /// <summary>훔치는 장면을 직접 본 인물(NPC·경찰). 범인이 플레이어임을 안다.</summary>
        public System.Collections.Generic.List<string> WitnessIds = new System.Collections.Generic.List<string>();
        /// <summary>나중에 물건이 없어진 것을 발견한 NPC(범인은 모른다).</summary>
        public System.Collections.Generic.List<string> AbsenceNoticedIds = new System.Collections.Generic.List<string>();
    }

    [Serializable]
    public sealed class CardQuestionDto
    {
        public string QuestionId;
        public string JudgeId;
        public string PresenterId;
        public string ClueId;
        public bool PresenterIsLying;
        public CardQuestionState State;
        public int AdditionalVerificationsUsed;
        public int DeferCount;
    }

    [Serializable]
    public sealed class WorldSaveData
    {
        public const int CurrentVersion = 5;

        public int Version;
        public EvidenceSaveData Evidence;
        public NpcSaveData Npc;
        public MeetingSaveData Meeting;
        public ContractReportSaveData[] Reports;

        /// <summary>버전 2에 추가된 선택 구역. 없던 예전 저장 파일은 빈 보드로 시작한다.</summary>
        public CaseBoardSaveData[] CaseBoards;

        /// <summary>다음 회의로 넘긴 의문(선택 구역 — 없던 예전 파일은 빈 목록).</summary>
        public CardQuestionDto[] CarriedOverQuestions;
        public MainCaseExtendedSaveData MainCase;
        public OccupantPositionDto[] Positions;
        public GameEventDto[] GlobalEvents;

        public ContractSaveData Contract;
        public CampaignSaveData Campaign;
    }
}
