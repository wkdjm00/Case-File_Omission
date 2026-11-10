using System;
using CaseFileOmission.ContractContent;
using CaseFileOmission.Contracts;
using CaseFileOmission.Core;
using CaseFileOmission.Evidence;
using CaseFileOmission.Police;
using CaseFileOmission.MainCase;
using CaseFileOmission.Meeting;
using CaseFileOmission.NPC;
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
    }

    [Serializable]
    public sealed class ClueRecordDto
    {
        public string ClueId;
        public long DiscoveredAtTicks;
        public ClueWorldState WorldState;
        public string[] ConfirmedByPoliceIds;
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
    public sealed class MeetingSaveData
    {
        public bool HasSession;
        public string ContractId;
        public bool IsActive;
        public MeetingCounterDto[] Counters;
        public GameEventDto[] ProcessedActions;
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

        public string ReportedSummary;
        public string InitialExplanation;
        public bool DiffersFromTruth;
        public string DifferenceNote;

        public bool ConclusionFinalized;
        public string OfficiallyAccusedNpcId;
        public ContractOutcome Outcome;

        public OrganizationConditionType ConditionType;
        public string ConditionTargetId;
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
        public bool HasFirearm;
        public int FirearmLoaded;
        public int FirearmReserve;
        public string[] GrantedSupportClueIds;
        public int ContractCounter;
        public OrganizationConditionType PreviousConditionType;
        public float HandlerStartingSuspicion;
        public string NextHandlerPoliceId;
        public string SceneRoomId;
        public int NpcMinute;
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
    }

    /// <summary>
    /// 통합 명세서 §12의 세부 상태(단서 그래프·발견·소유·조작 이력, NPC 상태·지식·전달 기록,
    /// 회의 단계·연속 카운터, 보고서 진행·연결 단서·제출 여부)와 메인 사건 확장 상태를 담는다.
    /// 버전 2부터 진행 중인 의뢰(진실·조직 조건 등)와 의뢰 간 진행 상태(추적·탄약·지원 단서)를 함께 담는다.
    /// 각 구역은 대상 객체가 없을 때 비워 두며, 불러올 때도 빈 구역은 건너뛴다.
    /// </summary>
    [Serializable]
    public sealed class WorldSaveData
    {
        public const int CurrentVersion = 2;

        public int Version;
        public EvidenceSaveData Evidence;
        public NpcSaveData Npc;
        public MeetingSaveData Meeting;
        public ContractReportSaveData[] Reports;
        public MainCaseExtendedSaveData MainCase;
        public OccupantPositionDto[] Positions;
        public GameEventDto[] GlobalEvents;

        public ContractSaveData Contract;
        public CampaignSaveData Campaign;
    }
}
