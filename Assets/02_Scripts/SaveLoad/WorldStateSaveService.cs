using System;
using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Combat;
using CaseFileOmission.ContractContent;
using CaseFileOmission.Contracts;
using CaseFileOmission.Core;
using CaseFileOmission.Evidence;
using CaseFileOmission.Organization;
using CaseFileOmission.Police;
using CaseFileOmission.MainCase;
using CaseFileOmission.Meeting;
using CaseFileOmission.NPC;
using CaseFileOmission.Reports;
using CaseFileOmission.Rooms;

namespace CaseFileOmission.SaveLoad
{
    /// <summary>
    /// 저장·복원 대상 런타임 객체 묶음. 없는 시스템은 null로 두면 해당 구역을 건너뛴다.
    /// 복원은 "새로 생성된" 객체에 하는 것을 전제로 한다(같은 객체에 두 번 복원하면 중복 항목은 무시된다).
    /// </summary>
    public sealed class WorldStateTargets
    {
        public ClueRepository Clues;
        public EvidenceManipulationService Manipulation;
        public EvidenceManipulationOutcomeResolver ManipulationOutcomes;
        public EvidenceCapacityManager Capacity;

        public NpcRegistry Npcs;
        public NpcRelationshipStore Relationships;
        public NpcInfoTransferService InfoTransfer;

        public MeetingSession Meeting;

        /// <summary>의뢰 보고서 목록. 복원 시 같은 ContractId가 없으면 새 보고서를 만들어 추가한다.</summary>
        public List<ContractReportState> Reports;

        /// <summary>NPC·경찰 개인 의견 점수(06). 현재 의뢰의 회의 투표에 쓴다.</summary>
        public OpinionScoreService Opinions;

        /// <summary>의뢰별 사건보드. 복원 시 같은 ContractId가 없으면 새로 만들어 추가한다.</summary>
        public List<CaseBoardState> CaseBoards;

        public MainCaseProgressTracker MainCase;
        public FinalReportState FinalReport;
        public PostReportBranching PostReport;

        public RoomOccupancyTracker Occupancy;
        public GameEventLog EventLog;

        /// <summary>진행 중인 의뢰. 복원하면 저장된 의뢰로 새 객체를 만들어 이 필드에 넣는다(없던 의뢰면 null).</summary>
        public ContractInstance Contract;

        public PolicePursuitController Pursuit;
        public FirearmState Firearm;
        public OrganizationSupportClueService OrganizationSupport;

        /// <summary>의뢰 생성·종료 처리에 쓰는 의뢰 간 값.</summary>
        public CampaignProgress Campaign;

        /// <summary>현재 의뢰에서 조사·대화로 얻을 수 있는 자료(아직 얻지 않은 것 포함). 복원하면 이 목록에 채운다.</summary>
        public List<EvidenceMaterial> Materials;
    }

    /// <summary>의뢰를 넘나드는 진행 값. 게임 진행 계층이 소유하고 저장 시스템이 그대로 저장한다.</summary>
    public sealed class CampaignProgress
    {
        /// <summary>지금까지 만든 의뢰 수(의뢰 ID 생성용).</summary>
        public int ContractCounter;

        /// <summary>직전 의뢰의 조직 조건 — 같은 조건이 바로 반복되지 않게 할 때 쓴다(05 확정).</summary>
        public OrganizationConditionType PreviousConditionType = OrganizationConditionType.None;

        /// <summary>현재 의뢰 시작 당시 담당 경찰의 의심도 — 정상 해결 시 다른 경찰 감소 여부 판정에 쓴다(06 확정).</summary>
        public float HandlerStartingSuspicion;

        /// <summary>담당 경찰 사망으로 끝난 뒤 정해진 다음 담당 경찰(+15 대상). 없으면 null.</summary>
        public string NextHandlerPoliceId;

        /// <summary>현재 의뢰의 사건 현장 방.</summary>
        public string SceneRoomId;

        /// <summary>세계 시각(사건 발생일 00:00 기준 분). NPC 15분 턴과 일정이 이 시각을 쓴다.</summary>
        public int NpcMinute;

        /// <summary>지금 조사 시간대가 시작된 세계 시각(06:00 또는 18:00). 시간대 끝 = 이 값 + 12시간.</summary>
        public int BlockStartMinute;

        /// <summary>07 2026-11-13: 담당 경찰 대기열(캠페인 시작 시 섞고, 소진 전에는 중복 배정하지 않는다).</summary>
        public List<string> HandlerQueue = new List<string>();

        /// <summary>직전 의뢰의 담당 경찰(새 대기열의 연속 배정 회피용).</summary>
        public string LastHandlerPoliceId;

        /// <summary>이번 의뢰에서 회유를 시도했는지(07: 의뢰당 가해자 1명에게 1회).</summary>
        public bool PersuasionAttempted;

        /// <summary>플레이어가 실제 가해자를 정답으로 식별한 상태가 검증되었는지(의뢰 보고서 판정 correct_supported).</summary>
        public bool CulpritIdentified;

        /// <summary>NPC별 공개 역할(07 public_role, 값은 Game.PublicRole 이름). 없으면 '사건 관련 인물'.</summary>
        public Dictionary<string, string> PublicRoles = new Dictionary<string, string>();

        /// <summary>지금 의뢰에서 끝난 회의 수. 물리적 증거·기록 조작 가능 시점("첫 회의 종료 이후") 판정에 쓴다.</summary>
        public int MeetingsEndedInContract;

        /// <summary>지금 의뢰의 반란 세력 연락책 NPC(실제 역할과 별개 표시). 없으면 null.</summary>
        public string RebelContactNpcId;

        /// <summary>'반란 세력 내부 회람 메모'가 놓인 방. 확보했거나 이번 의뢰에 없으면 null.</summary>
        public string RebelMemoRoomId;
    }

    /// <summary>
    /// Dictionary 기반 세부 상태의 저장·복원. 잘못된 참조(없는 단서·NPC·방, 정의되지 않은 enum 값,
    /// 범위를 벗어난 인덱스)는 건너뛰고 GameLogger에 남긴다 — 한 항목의 오류가 다른 상태를 망가뜨리지 않는다.
    /// 복원은 확률 판정·이벤트·조작을 다시 실행하지 않고 결과 상태만 되살린다(중복 실행 방지).
    /// </summary>
    public static class WorldStateSaveService
    {
        private const string SystemName = "SaveLoad";

        public static WorldSaveData Capture(WorldStateTargets t)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));

            return new WorldSaveData
            {
                Version = WorldSaveData.CurrentVersion,
                Evidence = CaptureEvidence(t),
                Npc = CaptureNpc(t),
                Meeting = CaptureMeeting(t.Meeting),
                Reports = t.Reports?.Select(CaptureReport).ToArray() ?? Array.Empty<ContractReportSaveData>(),
                CaseBoards = t.CaseBoards?.Select(CaptureCaseBoard).ToArray() ?? Array.Empty<CaseBoardSaveData>(),
                MainCase = CaptureMainCase(t),
                Positions = t.Occupancy?.AllPositions
                    .Select(p => new OccupantPositionDto { OccupantId = p.Key, RoomId = p.Value })
                    .ToArray() ?? Array.Empty<OccupantPositionDto>(),
                GlobalEvents = t.EventLog?.GetOrdered().Select(ToDto).ToArray() ?? Array.Empty<GameEventDto>(),
                Contract = CaptureContract(t.Contract),
                Campaign = CaptureCampaign(t)
            };
        }

        public static void Restore(WorldSaveData data, WorldStateTargets t, GameLogger logger = null)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));

            if (data == null || data.Version <= 0)
            {
                // 세부 상태가 없는 예전 저장 파일: 대상 객체는 새로 생성된 초기 상태 그대로 둔다.
                logger?.LogError("SAVE_WORLD_MISSING", SystemName, GamePhase.Idle,
                    "세부 월드 상태가 없는 저장 데이터입니다. 초기 상태로 시작합니다.", recoverable: true);
                return;
            }

            if (data.Version > WorldSaveData.CurrentVersion)
            {
                logger?.LogError("SAVE_WORLD_VERSION_UNSUPPORTED", SystemName, GamePhase.Idle,
                    $"지원하지 않는 저장 버전 {data.Version}입니다.", recoverable: true);
                return;
            }

            // 단서 정의가 먼저 있어야 NPC·회의·보고서의 단서 참조를 검증할 수 있다.
            RestoreEvidence(data.Evidence, t, logger);
            RestoreNpc(data.Npc, t, logger);
            RestoreMeeting(data.Meeting, t.Meeting, logger);
            RestoreReports(data.Reports, t, logger);
            RestoreCaseBoards(data.CaseBoards, t, logger);
            RestoreMainCase(data.MainCase, t, logger);
            RestorePositions(data.Positions, t.Occupancy, logger);
            RestoreEvents(data.GlobalEvents, t.EventLog, logger);
            RestoreContract(data.Contract, t, logger);
            RestoreCampaign(data.Campaign, t, logger);
        }

        // ───────────────────────── Contract / Campaign ─────────────────────────

        private static ContractSaveData CaptureContract(ContractInstance c)
        {
            if (c == null)
                return new ContractSaveData { HasContract = false };

            return new ContractSaveData
            {
                HasContract = true,
                ContractId = c.ContractId,
                PrimaryCrimeType = c.PrimaryCrimeType,
                LocationCategory = c.LocationCategory,
                Location = c.Location,
                StartTimeBlock = c.StartTimeBlock,
                HandlerPoliceId = c.HandlerPoliceId,
                IncidentDate = c.IncidentDate,
                FixtureId = c.FixtureId,
                PlayerStartRoomId = c.PlayerStartRoomId,
                HasInvestigationStartOverride = c.InvestigationStartMinuteOverride >= 0,
                InvestigationStartMinuteOverride = c.InvestigationStartMinuteOverride,
                HasRepresentativeCrimeType = c.RepresentativeCrimeType.HasValue,
                RepresentativeCrimeType = c.RepresentativeCrimeType ?? c.PrimaryCrimeType,
                PoliceKnownFacts = c.PoliceKnownFacts.ToArray(),
                PoliceUnknownFacts = c.PoliceUnknownFacts.ToArray(),
                PoliceClassificationBasis = c.PoliceClassificationBasis,
                IncidentClockMinute = c.IncidentClockMinute,
                HandoverDayOffset = c.HandoverDayOffset,
                SubCrimes = c.SubCrimeLinks.Select(l => new SubCrimeLinkDto
                {
                    Type = l.Type,
                    LinkReason = l.LinkReason,
                    LinkedNpcIds = l.LinkedNpcIds.ToArray(),
                    EventId = l.EventId
                }).ToArray(),
                PerpetratorId = c.Truth.PerpetratorId,
                VictimId = c.Truth.VictimId,
                PrimaryMotive = c.Truth.PrimaryMotive,
                SecondaryMotive = c.Truth.SecondaryMotive,
                Method = c.Truth.Method,
                Tool = c.Truth.Tool,
                RelationshipType = c.Truth.RelationshipType,
                CoverUp = c.Truth.CoverUp,
                TimelineEvents = c.Truth.TimelineEvents.ToArray(),
                MethodId = c.Truth.MethodId,
                ToolId = c.Truth.ToolId,
                MotiveId = c.Truth.MotiveId,
                RelationshipId = c.Truth.RelationshipId,
                RepresentativeSubtypeId = c.Truth.RepresentativeSubtypeId,
                ActualSubtypeId = c.Truth.ActualSubtypeId,
                VictimDeparted = c.Truth.VictimDeparted,
                BodyHidden = c.Truth.BodyHidden,
                DelayedDeath = c.Truth.DelayedDeath,
                ReportedSummary = c.ExternalState.ReportedSummary,
                InitialExplanation = c.ExternalState.InitialExplanation,
                DiffersFromTruth = c.ExternalState.DiffersFromTruth,
                DifferenceNote = c.ExternalState.DifferenceNote,
                ConclusionFinalized = c.OfficialConclusion.IsFinalized,
                OfficiallyAccusedNpcId = c.OfficialConclusion.OfficiallyAccusedNpcId,
                Outcome = c.OfficialConclusion.Outcome,
                ConditionType = c.OrgCondition.Type,
                ConditionTargetId = c.OrgCondition.TargetId,
                ConditionRequiresDelivery = c.OrgCondition.RequiresDeliveryToOrganization,
                ConditionJudged = c.OrgCondition.Achieved.HasValue,
                ConditionAchieved = c.OrgCondition.Achieved == true,
                ConditionLocked = c.OrgCondition.IsLocked
            };
        }

        private static void RestoreContract(ContractSaveData d, WorldStateTargets t, GameLogger logger)
        {
            if (d == null || !d.HasContract)
                return;

            if (string.IsNullOrEmpty(d.ContractId))
            {
                Log(logger, "SAVE_INVALID_CONTRACT", "ID가 없는 의뢰를 건너뜁니다.");
                return;
            }

            if (!IsDefined(d.PrimaryCrimeType) || !IsDefined(d.LocationCategory) || !IsDefined(d.Location)
                || !IsDefined(d.StartTimeBlock) || !IsDefined(d.CoverUp) || !IsDefined(d.Outcome) || !IsDefined(d.ConditionType))
            {
                Log(logger, "SAVE_INVALID_ENUM", "의뢰의 유형·장소·결과 값이 올바르지 않아 의뢰를 복원하지 않습니다.", contractId: d.ContractId);
                return;
            }

            var c = new ContractInstance(d.ContractId)
            {
                PrimaryCrimeType = d.PrimaryCrimeType,
                LocationCategory = d.LocationCategory,
                Location = d.Location,
                StartTimeBlock = d.StartTimeBlock,
                HandlerPoliceId = d.HandlerPoliceId,
                IncidentDate = string.IsNullOrEmpty(d.IncidentDate) ? "1984-10-01" : d.IncidentDate,
                IncidentClockMinute = Math.Max(0, d.IncidentClockMinute),
                HandoverDayOffset = Math.Max(1, d.HandoverDayOffset)
            };

            if (!string.IsNullOrEmpty(d.HandlerPoliceId) && !PoliceRoster.AllPoliceIds.Contains(d.HandlerPoliceId))
            {
                Log(logger, "SAVE_UNKNOWN_POLICE_ID", "알 수 없는 담당 경찰 ID를 비웁니다.", contractId: d.ContractId);
                c.HandlerPoliceId = null;
            }

            foreach (var s in d.SubCrimes ?? Array.Empty<SubCrimeLinkDto>())
            {
                if (s == null || !IsDefined(s.Type))
                {
                    Log(logger, "SAVE_INVALID_ENUM", "알 수 없는 부수 범죄를 건너뜁니다.", contractId: d.ContractId);
                    continue;
                }
                c.AddSubCrime(s.Type, s.LinkReason, s.LinkedNpcIds ?? Array.Empty<string>());
                c.SubCrimeLinks[c.SubCrimeLinks.Count - 1].EventId = s.EventId;
            }

            if (d.HasRepresentativeCrimeType && IsDefined(d.RepresentativeCrimeType)) c.RepresentativeCrimeType = d.RepresentativeCrimeType;
            c.PoliceKnownFacts.AddRange(d.PoliceKnownFacts ?? Array.Empty<string>());
            c.PoliceUnknownFacts.AddRange(d.PoliceUnknownFacts ?? Array.Empty<string>());
            c.PoliceClassificationBasis = d.PoliceClassificationBasis;
            c.FixtureId = d.FixtureId;
            c.PlayerStartRoomId = d.PlayerStartRoomId;
            c.InvestigationStartMinuteOverride = d.HasInvestigationStartOverride ? Math.Max(0, d.InvestigationStartMinuteOverride) : -1;
            c.Truth.PerpetratorId = d.PerpetratorId;
            c.Truth.VictimId = d.VictimId;
            c.Truth.PrimaryMotive = d.PrimaryMotive;
            c.Truth.SecondaryMotive = d.SecondaryMotive;
            c.Truth.Method = d.Method;
            c.Truth.Tool = d.Tool;
            c.Truth.RelationshipType = d.RelationshipType;
            c.Truth.CoverUp = d.CoverUp;
            c.Truth.MethodId = d.MethodId;
            c.Truth.ToolId = d.ToolId;
            c.Truth.MotiveId = d.MotiveId;
            c.Truth.RelationshipId = d.RelationshipId;
            c.Truth.RepresentativeSubtypeId = d.RepresentativeSubtypeId;
            c.Truth.ActualSubtypeId = d.ActualSubtypeId;
            c.Truth.VictimDeparted = d.VictimDeparted;
            c.Truth.BodyHidden = d.BodyHidden;
            c.Truth.DelayedDeath = d.DelayedDeath;
            c.Truth.TimelineEvents.AddRange(d.TimelineEvents ?? Array.Empty<string>());

            c.ExternalState.ReportedSummary = d.ReportedSummary;
            c.ExternalState.InitialExplanation = d.InitialExplanation;
            c.ExternalState.DiffersFromTruth = d.DiffersFromTruth;
            c.ExternalState.DifferenceNote = d.DifferenceNote;

            c.OfficialConclusion.IsFinalized = d.ConclusionFinalized;
            c.OfficialConclusion.OfficiallyAccusedNpcId = d.OfficiallyAccusedNpcId;
            c.OfficialConclusion.Outcome = d.Outcome;

            c.OrgCondition.Type = d.ConditionType;
            c.OrgCondition.TargetId = d.ConditionTargetId;
            c.OrgCondition.RequiresDeliveryToOrganization = d.ConditionRequiresDelivery;
            c.OrgCondition.Achieved = d.ConditionJudged ? d.ConditionAchieved : (bool?)null;
            if (d.ConditionLocked)
                c.OrgCondition.Lock();

            t.Contract = c;
        }

        private static CampaignSaveData CaptureCampaign(WorldStateTargets t)
        {
            return new CampaignSaveData
            {
                PursuitStage = t.Pursuit?.Stage ?? PursuitStage.NotActive,
                HasFirearm = t.Firearm != null,
                FirearmLoaded = t.Firearm?.CurrentAmmo ?? 0,
                FirearmReserve = t.Firearm?.ReserveAmmo ?? 0,
                GrantedSupportClueIds = t.OrganizationSupport?.GrantedClueIds.ToArray() ?? Array.Empty<string>(),
                ContractCounter = t.Campaign?.ContractCounter ?? 0,
                PreviousConditionType = t.Campaign?.PreviousConditionType ?? OrganizationConditionType.None,
                HandlerStartingSuspicion = t.Campaign?.HandlerStartingSuspicion ?? 0f,
                NextHandlerPoliceId = t.Campaign?.NextHandlerPoliceId,
                SceneRoomId = t.Campaign?.SceneRoomId,
                NpcMinute = t.Campaign?.NpcMinute ?? 0,
                BlockStartMinute = t.Campaign?.BlockStartMinute ?? 0,
                HandlerQueue = t.Campaign?.HandlerQueue.ToArray() ?? Array.Empty<string>(),
                LastHandlerPoliceId = t.Campaign?.LastHandlerPoliceId,
                PersuasionAttempted = t.Campaign?.PersuasionAttempted ?? false,
                CulpritIdentified = t.Campaign?.CulpritIdentified ?? false,
                PublicRoles = t.Campaign?.PublicRoles.Select(p => new PublicRoleDto { NpcId = p.Key, Role = p.Value }).ToArray() ?? Array.Empty<PublicRoleDto>(),
                Opinions = t.Opinions?.All.Select(o => new OpinionDto { Observer = o.Key.Observer, Suspect = o.Key.Suspect, Value = o.Value }).ToArray() ?? Array.Empty<OpinionDto>(),
                MeetingsEndedInContract = t.Campaign?.MeetingsEndedInContract ?? 0,
                RebelContactNpcId = t.Campaign?.RebelContactNpcId,
                RebelMemoRoomId = t.Campaign?.RebelMemoRoomId,
                Materials = t.Materials?.Select(m => new EvidenceMaterialDto
                {
                    MaterialId = m.MaterialId,
                    ContractId = m.ContractId,
                    Format = m.Format,
                    TruthStatus = m.TruthStatus,
                    SourceNpcId = m.SourceNpcId,
                    RoomId = m.RoomId,
                    Acquisition = m.Acquisition,
                    AboutEventId = m.AboutEventId,
                    AboutMinute = m.AboutMinute,
                    AboutRoomId = m.AboutRoomId,
                    StatedInfoIds = m.StatedInfoIds.ToArray(),
                    SubjectNpcId = m.SubjectNpcId,
                    RebuttedByMaterialIds = m.RebuttedByMaterialIds.ToArray(),
                    FalsehoodReason = m.FalsehoodReason,
                    IdentifiesPerpetrator = m.IdentifiesPerpetrator,
                    ObjectId = m.ObjectId,
                    RequiresClueId = m.RequiresClueId,
                    Title = m.Title,
                    Content = m.Content
                }).ToArray() ?? Array.Empty<EvidenceMaterialDto>()
            };
        }

        private static void RestoreCampaign(CampaignSaveData d, WorldStateTargets t, GameLogger logger)
        {
            if (d == null)
                return;

            if (t.Pursuit != null)
            {
                if (IsDefined(d.PursuitStage))
                    t.Pursuit.RestoreStage(d.PursuitStage);
                else
                    Log(logger, "SAVE_INVALID_ENUM", "추적 단계 값이 올바르지 않아 비활성으로 둡니다.");
            }

            if (t.Firearm != null && d.HasFirearm)
                t.Firearm.RestoreAmmo(d.FirearmLoaded, d.FirearmReserve);

            if (t.OrganizationSupport != null)
            {
                foreach (var id in d.GrantedSupportClueIds ?? Array.Empty<string>())
                    t.OrganizationSupport.RestoreGranted(id);
            }

            if (t.Campaign != null)
            {
                t.Campaign.ContractCounter = Math.Max(0, d.ContractCounter);
                t.Campaign.PreviousConditionType = IsDefined(d.PreviousConditionType) ? d.PreviousConditionType : OrganizationConditionType.None;
                t.Campaign.HandlerStartingSuspicion = float.IsNaN(d.HandlerStartingSuspicion) ? 0f : d.HandlerStartingSuspicion;
                t.Campaign.NextHandlerPoliceId = PoliceRoster.AllPoliceIds.Contains(d.NextHandlerPoliceId) ? d.NextHandlerPoliceId : null;
                t.Campaign.SceneRoomId = d.SceneRoomId;
                t.Campaign.NpcMinute = Math.Max(0, d.NpcMinute);
                t.Campaign.BlockStartMinute = Math.Max(0, d.BlockStartMinute);
                t.Campaign.HandlerQueue = (d.HandlerQueue ?? Array.Empty<string>()).Where(id => PoliceRoster.AllPoliceIds.Contains(id)).ToList();
                t.Campaign.LastHandlerPoliceId = PoliceRoster.AllPoliceIds.Contains(d.LastHandlerPoliceId) ? d.LastHandlerPoliceId : null;
                t.Campaign.PersuasionAttempted = d.PersuasionAttempted;
                t.Campaign.CulpritIdentified = d.CulpritIdentified;
                foreach (var r in d.PublicRoles ?? Array.Empty<PublicRoleDto>())
                {
                    if (r == null || string.IsNullOrEmpty(r.NpcId) || string.IsNullOrEmpty(r.Role)) continue;
                    if (!NpcKnown(t, r.NpcId)) { Log(logger, "SAVE_UNKNOWN_NPC_ID", "공개 역할의 NPC가 없어 건너뜁니다.", npcId: r.NpcId); continue; }
                    t.Campaign.PublicRoles[r.NpcId] = r.Role;
                }
                foreach (var o in d.Opinions ?? Array.Empty<OpinionDto>())
                {
                    if (o == null || string.IsNullOrEmpty(o.Observer) || string.IsNullOrEmpty(o.Suspect) || float.IsNaN(o.Value)) continue;
                    t.Opinions?.Restore(o.Observer, o.Suspect, o.Value);
                }
                t.Campaign.MeetingsEndedInContract = Math.Max(0, d.MeetingsEndedInContract);
                t.Campaign.RebelContactNpcId = NpcKnown(t, d.RebelContactNpcId) ? d.RebelContactNpcId : null;
                if (string.IsNullOrEmpty(d.RebelMemoRoomId) || t.Occupancy == null || t.Occupancy.HasRoom(d.RebelMemoRoomId))
                    t.Campaign.RebelMemoRoomId = string.IsNullOrEmpty(d.RebelMemoRoomId) ? null : d.RebelMemoRoomId;
                else
                    Log(logger, "SAVE_UNKNOWN_ROOM_ID", $"메모가 놓인 방 '{d.RebelMemoRoomId}'가 없어 건너뜁니다.");
            }

            if (t.Materials != null)
            {
                foreach (var m in d.Materials ?? Array.Empty<EvidenceMaterialDto>())
                {
                    if (m == null || string.IsNullOrEmpty(m.MaterialId) || !IsDefined(m.Format) || !IsDefined(m.TruthStatus) || !IsDefined(m.Acquisition))
                    {
                        Log(logger, "SAVE_INVALID_MATERIAL", "잘못된 조사 자료를 건너뜁니다.", clueId: m?.MaterialId);
                        continue;
                    }

                    var material = new EvidenceMaterial
                    {
                        MaterialId = m.MaterialId,
                        ContractId = m.ContractId,
                        Format = m.Format,
                        TruthStatus = m.TruthStatus,
                        SourceNpcId = m.SourceNpcId,
                        RoomId = m.RoomId,
                        Acquisition = m.Acquisition,
                        AboutEventId = m.AboutEventId,
                        AboutMinute = m.AboutMinute,
                        AboutRoomId = m.AboutRoomId,
                        SubjectNpcId = m.SubjectNpcId,
                        FalsehoodReason = m.FalsehoodReason,
                        IdentifiesPerpetrator = m.IdentifiesPerpetrator,
                        ObjectId = m.ObjectId,
                        RequiresClueId = m.RequiresClueId,
                        Title = m.Title,
                        Content = m.Content
                    };
                    material.StatedInfoIds.AddRange(m.StatedInfoIds ?? Array.Empty<string>());
                    material.RebuttedByMaterialIds.AddRange(m.RebuttedByMaterialIds ?? Array.Empty<string>());
                    t.Materials.Add(material);
                }
            }
        }

        // ───────────────────────── Evidence ─────────────────────────

        private static EvidenceSaveData CaptureEvidence(WorldStateTargets t)
        {
            var data = new EvidenceSaveData
            {
                Definitions = Array.Empty<ClueDefinitionDto>(),
                Records = Array.Empty<ClueRecordDto>(),
                ManipulationHistory = Array.Empty<ManipulationRecordDto>(),
                FailedManipulationAttempts = Array.Empty<ManipulationAttemptDto>(),
                Holdings = Array.Empty<HolderHoldingDto>()
            };

            if (t.Clues != null)
            {
                data.Definitions = t.Clues.AllDefinitions.Select(d => new ClueDefinitionDto
                {
                    ClueId = d.ClueId,
                    ContractId = d.ContractId,
                    Format = d.Format,
                    TruthStatus = d.TruthStatus,
                    DisplayName = d.DisplayName,
                    DisplayedContent = d.DisplayedContent,
                    RelatedTruthNote = d.RelatedTruthNote,
                    SourceDescription = d.SourceDescription,
                    AcquisitionCondition = d.AcquisitionCondition,
                    IdentifiesPerpetrator = d.IdentifiesPerpetrator
                }).ToArray();

                data.Records = t.Clues.AllRecords.Select(r => new ClueRecordDto
                {
                    ClueId = r.ClueId,
                    DiscoveredAtTicks = r.DiscoveredAtUtc.Ticks,
                    WorldState = r.WorldState,
                    ConfirmedByPoliceIds = r.ConfirmedByPoliceIds.ToArray()
                }).ToArray();
            }

            if (t.Manipulation != null)
            {
                data.ManipulationHistory = t.Manipulation.History.Select(m => new ManipulationRecordDto
                {
                    OriginalClueId = m.OriginalClueId,
                    Type = m.Type,
                    ActorId = m.ActorId,
                    DisplayedClueId = m.DisplayedClueId,
                    AppliedAtTicks = m.AppliedAtUtc.Ticks
                }).ToArray();
            }

            if (t.ManipulationOutcomes != null)
            {
                data.FailedManipulationAttempts = t.ManipulationOutcomes.FailedAttempts.Select(a => new ManipulationAttemptDto
                {
                    ClueId = a.ClueId,
                    Type = a.Type,
                    ActorId = a.ActorId,
                    AttemptedAtTicks = a.AttemptedAtUtc.Ticks
                }).ToArray();
            }

            if (t.Capacity != null)
            {
                data.Holdings = t.Capacity.AllHoldings.Select(h => new HolderHoldingDto
                {
                    HolderId = h.Key,
                    HolderType = InferHolderType(h.Key, t.Npcs),
                    ClueIds = h.Value.ToArray()
                }).ToArray();
            }

            return data;
        }

        /// <summary>보유 한도 판정에 쓰는 보유자 종류. NPC 저장소에 있으면 NPC, 아니면 경찰로 본다.</summary>
        private static HolderType InferHolderType(string holderId, NpcRegistry npcs) =>
            npcs != null && npcs.Exists(holderId) ? HolderType.Npc : HolderType.Police;

        private static void RestoreEvidence(EvidenceSaveData data, WorldStateTargets t, GameLogger logger)
        {
            if (data == null)
                return;

            if (t.Clues != null)
            {
                var recordsById = new Dictionary<string, ClueRecordDto>();
                foreach (var r in data.Records ?? Array.Empty<ClueRecordDto>())
                {
                    if (r == null || string.IsNullOrEmpty(r.ClueId) || recordsById.ContainsKey(r.ClueId))
                    {
                        Log(logger, "SAVE_INVALID_CLUE_RECORD", "비어 있거나 중복된 단서 기록을 건너뜁니다.", clueId: r?.ClueId);
                        continue;
                    }
                    recordsById[r.ClueId] = r;
                }

                foreach (var d in data.Definitions ?? Array.Empty<ClueDefinitionDto>())
                {
                    if (d == null || string.IsNullOrEmpty(d.ClueId) || string.IsNullOrEmpty(d.ContractId))
                    {
                        Log(logger, "SAVE_INVALID_CLUE_DEFINITION", "ID가 없는 단서 정의를 건너뜁니다.", clueId: d?.ClueId);
                        continue;
                    }

                    if (!IsDefined(d.Format) || !IsDefined(d.TruthStatus))
                    {
                        Log(logger, "SAVE_INVALID_ENUM", "단서 형태/진위 값이 올바르지 않아 건너뜁니다.", clueId: d.ClueId);
                        continue;
                    }

                    var definition = new ClueDefinition(d.ClueId, d.ContractId)
                    {
                        Format = d.Format,
                        TruthStatus = d.TruthStatus,
                        DisplayName = d.DisplayName,
                        DisplayedContent = d.DisplayedContent,
                        RelatedTruthNote = d.RelatedTruthNote,
                        SourceDescription = d.SourceDescription,
                        AcquisitionCondition = d.AcquisitionCondition,
                        IdentifiesPerpetrator = d.IdentifiesPerpetrator
                    };

                    ClueRecord record;
                    if (recordsById.TryGetValue(d.ClueId, out var r))
                    {
                        record = new ClueRecord(d.ClueId, ToUtc(r.DiscoveredAtTicks, logger, d.ClueId));
                        if (IsDefined(r.WorldState))
                            record.RestoreWorldState(r.WorldState);
                        else
                            Log(logger, "SAVE_INVALID_ENUM", "단서 소지 상태가 올바르지 않아 플레이어 보유로 둡니다.", clueId: d.ClueId);

                        foreach (var policeId in r.ConfirmedByPoliceIds ?? Array.Empty<string>())
                            record.MarkConfirmedByPolice(policeId);
                    }
                    else
                    {
                        // 정의만 있고 기록이 없으면 발견 기록은 남아야 하므로(06 확정: 기록은 사라지지 않음) 새 기록으로 복구한다.
                        Log(logger, "SAVE_CLUE_RECORD_MISSING", "단서 기록이 없어 기본 기록으로 복구합니다.", clueId: d.ClueId);
                        record = new ClueRecord(d.ClueId, DateTime.MinValue);
                    }

                    if (!t.Clues.RestoreEntry(definition, record))
                        Log(logger, "SAVE_DUPLICATE_CLUE", "이미 존재하는 단서 정의는 덮어쓰지 않습니다.", clueId: d.ClueId);
                }

                foreach (var orphan in recordsById.Keys.Where(id => !t.Clues.Exists(id)))
                    Log(logger, "SAVE_UNKNOWN_CLUE_ID", "정의가 없는 단서 기록을 건너뜁니다.", clueId: orphan);
            }

            if (t.Manipulation != null)
            {
                foreach (var m in data.ManipulationHistory ?? Array.Empty<ManipulationRecordDto>())
                {
                    if (m == null || !ClueKnown(t, m.OriginalClueId) || !ClueKnown(t, m.DisplayedClueId) || !IsDefined(m.Type))
                    {
                        Log(logger, "SAVE_INVALID_MANIPULATION", "참조가 잘못된 조작 이력을 건너뜁니다.", clueId: m?.OriginalClueId);
                        continue;
                    }

                    t.Manipulation.RestoreHistory(new ManipulationRecord(
                        m.OriginalClueId, m.Type, m.ActorId, m.DisplayedClueId, ToUtc(m.AppliedAtTicks, logger, m.OriginalClueId)));
                }
            }

            if (t.ManipulationOutcomes != null)
            {
                foreach (var a in data.FailedManipulationAttempts ?? Array.Empty<ManipulationAttemptDto>())
                {
                    if (a == null || !ClueKnown(t, a.ClueId) || !IsDefined(a.Type))
                    {
                        Log(logger, "SAVE_INVALID_MANIPULATION", "참조가 잘못된 조작 실패 기록을 건너뜁니다.", clueId: a?.ClueId);
                        continue;
                    }

                    t.ManipulationOutcomes.RestoreFailedAttempt(new ManipulationAttemptRecord
                    {
                        ClueId = a.ClueId,
                        Type = a.Type,
                        ActorId = a.ActorId,
                        AttemptedAtUtc = ToUtc(a.AttemptedAtTicks, logger, a.ClueId)
                    });
                }
            }

            if (t.Capacity != null)
            {
                foreach (var h in data.Holdings ?? Array.Empty<HolderHoldingDto>())
                {
                    if (h == null || string.IsNullOrEmpty(h.HolderId) || !IsDefined(h.HolderType))
                    {
                        Log(logger, "SAVE_INVALID_HOLDING", "보유자 정보가 잘못된 항목을 건너뜁니다.");
                        continue;
                    }

                    var valid = new List<string>();
                    foreach (var clueId in h.ClueIds ?? Array.Empty<string>())
                    {
                        if (ClueKnown(t, clueId))
                            valid.Add(clueId);
                        else
                            Log(logger, "SAVE_UNKNOWN_CLUE_ID", $"보유자 '{h.HolderId}'의 알 수 없는 단서를 건너뜁니다.", clueId: clueId);
                    }

                    int accepted = t.Capacity.RestoreHoldings(h.HolderId, h.HolderType, valid);
                    if (accepted < valid.Distinct().Count())
                        Log(logger, "SAVE_HOLDING_OVER_LIMIT", $"보유자 '{h.HolderId}'의 저장값이 보유 한도를 넘어 한도까지만 복원합니다.");
                }
            }
        }

        /// <summary>단서 저장소가 없으면 참조 검증을 할 수 없으므로 통과시킨다.</summary>
        private static bool ClueKnown(WorldStateTargets t, string clueId) =>
            !string.IsNullOrEmpty(clueId) && (t.Clues == null || t.Clues.Exists(clueId));

        // ───────────────────────── NPC ─────────────────────────

        private static NpcSaveData CaptureNpc(WorldStateTargets t)
        {
            var data = new NpcSaveData
            {
                Npcs = Array.Empty<NpcStateDto>(),
                Suspicion = Array.Empty<DirectionalValueDto>(),
                Trust = Array.Empty<DirectionalValueDto>(),
                Transfers = Array.Empty<InfoTransferDto>()
            };

            if (t.Npcs != null)
            {
                data.Npcs = t.Npcs.All.Select(n => new NpcStateDto
                {
                    NpcId = n.NpcId,
                    ContractId = n.ContractId,
                    ActualRole = n.ActualRole,
                    Personality = n.Personality,
                    Occupation = n.Occupation,
                    DisplayName = n.DisplayName,
                    IsAlive = n.IsAlive,
                    IsCooperatingWithPlayer = n.IsCooperatingWithPlayer,
                    Knowledge = n.KnownInfoIds.Select(info => new NpcKnowledgeDto
                    {
                        InfoId = info,
                        HasJudgement = n.Beliefs.TryGetValue(info, out _),
                        Judgement = n.Beliefs.TryGetValue(info, out var j) ? j : NpcJudgement.Withhold
                    }).ToArray(),
                    WitnessedEventIds = n.WitnessedEventIds.ToArray()
                }).ToArray();
            }

            if (t.Relationships != null)
            {
                data.Suspicion = t.Relationships.SuspicionEntries.Select(ToDto).ToArray();
                data.Trust = t.Relationships.TrustEntries.Select(ToDto).ToArray();
            }

            if (t.InfoTransfer != null)
            {
                data.Transfers = t.InfoTransfer.TransferLog
                    .Select(x => new InfoTransferDto { SenderId = x.Sender, ReceiverId = x.Receiver, InfoId = x.InfoId })
                    .ToArray();
            }

            return data;
        }

        private static DirectionalValueDto ToDto((string Source, string Target, float Value) e) =>
            new DirectionalValueDto { SourceId = e.Source, TargetId = e.Target, Value = e.Value };

        private static void RestoreNpc(NpcSaveData data, WorldStateTargets t, GameLogger logger)
        {
            if (data == null)
                return;

            if (t.Npcs != null)
            {
                foreach (var dto in data.Npcs ?? Array.Empty<NpcStateDto>())
                {
                    if (dto == null || string.IsNullOrEmpty(dto.NpcId))
                    {
                        Log(logger, "SAVE_INVALID_NPC", "ID가 없는 NPC 상태를 건너뜁니다.");
                        continue;
                    }

                    if (!IsDefined(dto.ActualRole) || !IsDefined(dto.Personality))
                    {
                        Log(logger, "SAVE_INVALID_ENUM", "NPC 역할/성격 값이 올바르지 않아 건너뜁니다.", npcId: dto.NpcId);
                        continue;
                    }

                    NpcState npc;
                    if (t.Npcs.Exists(dto.NpcId))
                    {
                        npc = t.Npcs.Get(dto.NpcId);
                        npc.ActualRole = dto.ActualRole;
                        npc.Personality = dto.Personality;
                    }
                    else
                    {
                        npc = new NpcState(dto.NpcId, dto.ContractId, dto.ActualRole, dto.Personality);
                        t.Npcs.Add(npc);
                    }

                    npc.Occupation = dto.Occupation;
                    npc.DisplayName = dto.DisplayName;
                    if (!dto.IsAlive) npc.MarkDeceased();
                    if (dto.IsCooperatingWithPlayer) npc.MarkCooperating();
                    foreach (var eventId in dto.WitnessedEventIds ?? Array.Empty<string>())
                        npc.MarkWitnessed(eventId);

                    foreach (var k in dto.Knowledge ?? Array.Empty<NpcKnowledgeDto>())
                    {
                        if (k == null || string.IsNullOrEmpty(k.InfoId))
                            continue;

                        npc.KnownInfoIds.Add(k.InfoId);
                        if (k.HasJudgement && IsDefined(k.Judgement))
                            npc.Beliefs[k.InfoId] = k.Judgement;
                    }
                }
            }

            if (t.Relationships != null)
            {
                foreach (var e in data.Suspicion ?? Array.Empty<DirectionalValueDto>())
                {
                    if (NpcPairValid(e, t, logger))
                        t.Relationships.SetSuspicion(e.SourceId, e.TargetId, e.Value);
                }

                foreach (var e in data.Trust ?? Array.Empty<DirectionalValueDto>())
                {
                    if (NpcPairValid(e, t, logger))
                        t.Relationships.SetTrust(e.SourceId, e.TargetId, e.Value);
                }
            }

            if (t.InfoTransfer != null)
            {
                foreach (var x in data.Transfers ?? Array.Empty<InfoTransferDto>())
                {
                    if (x == null || string.IsNullOrEmpty(x.InfoId) || !NpcKnown(t, x.SenderId) || !NpcKnown(t, x.ReceiverId))
                    {
                        Log(logger, "SAVE_UNKNOWN_NPC_ID", "참조가 잘못된 정보 전달 기록을 건너뜁니다.", npcId: x?.SenderId);
                        continue;
                    }

                    t.InfoTransfer.RestoreTransfer(x.SenderId, x.ReceiverId, x.InfoId);
                }
            }
        }

        private static bool NpcKnown(WorldStateTargets t, string npcId) =>
            !string.IsNullOrEmpty(npcId) && (t.Npcs == null || t.Npcs.Exists(npcId));

        private static bool NpcPairValid(DirectionalValueDto e, WorldStateTargets t, GameLogger logger)
        {
            if (e != null && NpcKnown(t, e.SourceId) && NpcKnown(t, e.TargetId) && !float.IsNaN(e.Value) && !float.IsInfinity(e.Value))
                return true;

            Log(logger, "SAVE_UNKNOWN_NPC_ID", "참조가 잘못된 NPC 관계 값을 건너뜁니다.", npcId: e?.SourceId);
            return false;
        }

        // ───────────────────────── Meeting ─────────────────────────

        private static MeetingSaveData CaptureMeeting(MeetingSession session)
        {
            if (session == null)
                return new MeetingSaveData { HasSession = false, Counters = Array.Empty<MeetingCounterDto>(), ProcessedActions = Array.Empty<GameEventDto>() };

            return new MeetingSaveData
            {
                HasSession = true,
                ContractId = session.ContractId,
                IsActive = session.IsActive,
                Counters = session.CaptureCounters().Select(c => new MeetingCounterDto
                {
                    Kind = c.Kind,
                    ObserverId = c.ObserverId,
                    SuspectId = c.SuspectId,
                    Count = c.Count
                }).ToArray(),
                ProcessedActions = session.ProcessedActions.Select(ToDto).ToArray()
            };
        }

        private static void RestoreMeeting(MeetingSaveData data, MeetingSession session, GameLogger logger)
        {
            if (data == null || !data.HasSession)
                return;

            if (session == null)
            {
                Log(logger, "SAVE_MEETING_TARGET_MISSING", "저장된 회의 세션을 받을 대상이 없어 건너뜁니다.", contractId: data.ContractId);
                return;
            }

            if (session.ContractId != data.ContractId)
            {
                Log(logger, "SAVE_MEETING_CONTRACT_MISMATCH",
                    $"저장된 회의({data.ContractId})와 대상 회의({session.ContractId})의 의뢰가 달라 건너뜁니다.", contractId: data.ContractId);
                return;
            }

            foreach (var c in data.Counters ?? Array.Empty<MeetingCounterDto>())
            {
                if (c == null || !IsDefined(c.Kind) || string.IsNullOrEmpty(c.SuspectId) || c.Count < 0
                    || (c.Kind != MeetingCounterKind.TurnSkip && string.IsNullOrEmpty(c.ObserverId)))
                {
                    Log(logger, "SAVE_INVALID_MEETING_COUNTER", "잘못된 회의 연속 카운터를 건너뜁니다.", contractId: data.ContractId);
                    continue;
                }

                session.RestoreCounter(new MeetingCounterSnapshot(c.Kind, c.ObserverId, c.SuspectId, c.Count));
            }

            foreach (var e in data.ProcessedActions ?? Array.Empty<GameEventDto>())
            {
                var gameEvent = FromDto(e, logger);
                if (gameEvent != null)
                    session.RestoreProcessedAction(gameEvent);
            }

            if (!data.IsActive && session.IsActive)
                session.EndSession();
        }

        // ───────────────────────── Case boards ─────────────────────────

        private static CaseBoardSaveData CaptureCaseBoard(CaseBoardState board) => new CaseBoardSaveData
        {
            ContractId = board.ContractId,
            Seed = board.Seed,
            LayoutVersion = board.AppliedLayoutVersion,
            Items = board.Items.Select(i => new CaseBoardItemDto
            {
                ItemId = i.ItemId,
                BoardDecorationOnly = i.BoardDecorationOnly,
                X = i.X,
                Y = i.Y,
                RotationDegrees = i.RotationDegrees,
                OnBoard = i.OnBoard
            }).ToArray(),
            Links = board.Links.Select(l => new CaseBoardLinkDto { ItemA = l.ItemA, ItemB = l.ItemB }).ToArray()
        };

        /// <summary>단서가 아닌(장식이 아닌데 단서 저장소에 없는) 항목과 그 항목을 가리키는 연결선은 건너뛴다.</summary>
        private static void RestoreCaseBoards(CaseBoardSaveData[] boards, WorldStateTargets t, GameLogger logger)
        {
            if (boards == null || t.CaseBoards == null)
                return;

            foreach (var dto in boards)
            {
                if (dto == null || string.IsNullOrEmpty(dto.ContractId))
                {
                    Log(logger, "SAVE_INVALID_CASE_BOARD", "의뢰 ID가 없는 사건보드를 건너뜁니다.");
                    continue;
                }
                if (t.CaseBoards.Any(b => b.ContractId == dto.ContractId))
                    continue;

                var board = new CaseBoardState(dto.ContractId, dto.Seed) { AppliedLayoutVersion = dto.LayoutVersion };
                foreach (var item in dto.Items ?? Array.Empty<CaseBoardItemDto>())
                {
                    if (item == null || string.IsNullOrEmpty(item.ItemId) || board.Get(item.ItemId) != null)
                        continue;
                    if (!item.BoardDecorationOnly && t.Clues != null && !t.Clues.Exists(item.ItemId))
                    {
                        Log(logger, "SAVE_UNKNOWN_CLUE_ID", "사건보드 항목의 자료가 없어 건너뜁니다.", contractId: dto.ContractId, clueId: item.ItemId);
                        continue;
                    }
                    board.Items.Add(new CaseBoardItem
                    {
                        ItemId = item.ItemId,
                        BoardDecorationOnly = item.BoardDecorationOnly,
                        X = item.X,
                        Y = item.Y,
                        RotationDegrees = item.RotationDegrees,
                        OnBoard = item.OnBoard
                    });
                }
                foreach (var link in dto.Links ?? Array.Empty<CaseBoardLinkDto>())
                {
                    if (link == null || !board.Connect(link.ItemA, link.ItemB))
                        Log(logger, "SAVE_INVALID_CASE_BOARD_LINK", "사건보드 연결선을 복원하지 못해 건너뜁니다.", contractId: dto.ContractId);
                }
                t.CaseBoards.Add(board);
            }
        }

        // ───────────────────────── Reports ─────────────────────────

        private static ContractReportSaveData CaptureReport(ContractReportState report) => new ContractReportSaveData
        {
            ContractId = report.ContractId,
            IsSubmitted = report.IsSubmitted,
            Sections = ((ReportSection[])Enum.GetValues(typeof(ReportSection))).Select(section =>
            {
                var entry = report.GetSection(section);
                return new ReportSectionDto
                {
                    Section = section,
                    Content = entry.Content,
                    LinkedClueIds = entry.LinkedClueIds.ToArray(),
                    ClaimedNpcId = entry.ClaimedNpcId
                };
            }).ToArray(),
            Slots = report.Slots.Select(s => new ReportSlotDto { Index = s.Index, Unlocked = s.Unlocked, Status = s.Status, LastAnswer = s.LastAnswer }).ToArray()
        };

        private static void RestoreReports(ContractReportSaveData[] reports, WorldStateTargets t, GameLogger logger)
        {
            if (reports == null || t.Reports == null)
                return;

            foreach (var dto in reports)
            {
                if (dto == null || string.IsNullOrEmpty(dto.ContractId))
                {
                    Log(logger, "SAVE_INVALID_REPORT", "의뢰 ID가 없는 보고서를 건너뜁니다.");
                    continue;
                }

                var report = t.Reports.FirstOrDefault(r => r.ContractId == dto.ContractId);
                if (report == null)
                {
                    report = new ContractReportState(dto.ContractId);
                    t.Reports.Add(report);
                }

                foreach (var slot in dto.Slots ?? Array.Empty<ReportSlotDto>())
                {
                    if (slot == null || !IsDefined(slot.Status)) continue;
                    var restored = report.GetOrAddSlot(slot.Index);
                    restored.Unlocked = slot.Unlocked;
                    restored.Status = slot.Status;
                    restored.LastAnswer = slot.LastAnswer;
                }

                foreach (var s in dto.Sections ?? Array.Empty<ReportSectionDto>())
                {
                    if (s == null || !IsDefined(s.Section))
                    {
                        Log(logger, "SAVE_INVALID_ENUM", "알 수 없는 보고서 항목을 건너뜁니다.", contractId: dto.ContractId);
                        continue;
                    }

                    report.SetContent(s.Section, s.Content);

                    if (!string.IsNullOrEmpty(s.ClaimedNpcId) && !NpcKnown(t, s.ClaimedNpcId))
                        Log(logger, "SAVE_UNKNOWN_NPC_ID", "보고서가 지목한 NPC가 없어 지목을 비웁니다.", contractId: dto.ContractId, npcId: s.ClaimedNpcId);
                    else
                        report.SetClaimedNpcId(s.Section, s.ClaimedNpcId);

                    foreach (var clueId in s.LinkedClueIds ?? Array.Empty<string>())
                    {
                        if (ClueKnown(t, clueId))
                            report.LinkClue(s.Section, clueId);
                        else
                            Log(logger, "SAVE_UNKNOWN_CLUE_ID", "보고서에 연결된 알 수 없는 단서를 건너뜁니다.", contractId: dto.ContractId, clueId: clueId);
                    }
                }

                // 제출 상태는 다시 "제출 처리"를 실행하지 않고 표시만 복원한다(보상·판정 중복 방지).
                if (dto.IsSubmitted && !report.IsSubmitted)
                    report.MarkSubmitted();
            }
        }

        // ───────────────────────── MainCase ─────────────────────────

        private static MainCaseExtendedSaveData CaptureMainCase(WorldStateTargets t)
        {
            var data = new MainCaseExtendedSaveData
            {
                Acquisitions = Array.Empty<MainCaseClueAcquisitionDto>(),
                FinalReportBlanks = Array.Empty<FinalReportBlankDto>()
            };

            if (t.MainCase != null)
            {
                data.CompletedContractCount = t.MainCase.CompletedContractCount;
                data.Acquisitions = t.MainCase.AcquiredAtCompletedContractCount
                    .Select(a => new MainCaseClueAcquisitionDto { ClueName = a.Key, AcquiredAtCompletedContractCount = a.Value })
                    .ToArray();
            }

            if (t.FinalReport != null)
            {
                data.HasFinalReport = true;
                data.FinalReportBlanks = t.FinalReport.Blanks.Select(b => new FinalReportBlankDto
                {
                    Index = b.Index,
                    IsFilled = b.IsFilled,
                    CollectedClueNames = b.CollectedClueNames.ToArray()
                }).ToArray();
            }

            if (t.PostReport != null)
            {
                data.HasPostReport = true;
                data.PhoneStage = t.PostReport.PhoneStage;
                data.RebelStage = t.PostReport.RebelStage;
            }

            return data;
        }

        private static void RestoreMainCase(MainCaseExtendedSaveData data, WorldStateTargets t, GameLogger logger)
        {
            if (data == null)
                return;

            if (t.MainCase != null)
            {
                if (data.CompletedContractCount < 0)
                    Log(logger, "SAVE_INVALID_VALUE", "음수인 종료 의뢰 수를 0으로 둡니다.");
                t.MainCase.RestoreCompletedContractCount(Math.Max(0, data.CompletedContractCount));

                foreach (var a in data.Acquisitions ?? Array.Empty<MainCaseClueAcquisitionDto>())
                {
                    if (a == null || string.IsNullOrEmpty(a.ClueName))
                        continue;

                    int at = Math.Min(Math.Max(0, a.AcquiredAtCompletedContractCount), t.MainCase.CompletedContractCount);
                    t.MainCase.RestoreAcquisition(a.ClueName, at);
                }
            }

            if (data.HasFinalReport && t.FinalReport != null)
            {
                foreach (var b in data.FinalReportBlanks ?? Array.Empty<FinalReportBlankDto>())
                {
                    if (b == null || b.Index < 0 || b.Index >= t.FinalReport.Blanks.Count)
                    {
                        Log(logger, "SAVE_INVALID_REPORT_BLANK", $"범위를 벗어난 최종 보고서 빈칸({b?.Index})을 건너뜁니다.");
                        continue;
                    }

                    t.FinalReport.RestoreProgress(b.Index, b.IsFilled, b.CollectedClueNames);
                }
            }

            if (data.HasPostReport && t.PostReport != null)
            {
                if (IsDefined(data.PhoneStage) && IsDefined(data.RebelStage))
                    t.PostReport.RestoreStages(data.PhoneStage, data.RebelStage);
                else
                    Log(logger, "SAVE_INVALID_ENUM", "보고서 이후 분기 단계 값이 올바르지 않아 초기 상태로 둡니다.");
            }
        }

        // ───────────────────────── Positions / Events ─────────────────────────

        private static void RestorePositions(OccupantPositionDto[] positions, RoomOccupancyTracker occupancy, GameLogger logger)
        {
            if (positions == null || occupancy == null)
                return;

            foreach (var p in positions)
            {
                if (p == null || string.IsNullOrEmpty(p.OccupantId) || string.IsNullOrEmpty(p.RoomId))
                    continue;

                try
                {
                    occupancy.PlaceAt(p.OccupantId, p.RoomId);
                }
                catch (KeyNotFoundException)
                {
                    Log(logger, "SAVE_UNKNOWN_ROOM_ID", $"'{p.OccupantId}'의 저장 위치 '{p.RoomId}'가 없는 방이라 건너뜁니다.", npcId: p.OccupantId);
                }
            }
        }

        private static void RestoreEvents(GameEventDto[] events, GameEventLog log, GameLogger logger)
        {
            if (events == null || log == null)
                return;

            foreach (var e in events)
            {
                var gameEvent = FromDto(e, logger);
                if (gameEvent != null)
                    log.TryRecord(gameEvent);
            }
        }

        private static GameEventDto ToDto(GameEvent e) => new GameEventDto
        {
            EventId = e.EventId,
            Type = e.Type,
            ContractId = e.ContractId,
            Payload = e.Payload,
            OccurredAtTicks = e.OccurredAtUtc.Ticks
        };

        private static GameEvent FromDto(GameEventDto e, GameLogger logger)
        {
            if (e == null || string.IsNullOrEmpty(e.EventId) || !IsDefined(e.Type))
            {
                Log(logger, "SAVE_INVALID_EVENT", "ID 또는 종류가 잘못된 이벤트 기록을 건너뜁니다.");
                return null;
            }

            return new GameEvent(e.EventId, e.Type, e.ContractId, e.Payload, ToUtc(e.OccurredAtTicks, logger, null));
        }

        // ───────────────────────── helpers ─────────────────────────

        private static bool IsDefined<TEnum>(TEnum value) where TEnum : struct, Enum =>
            Enum.IsDefined(typeof(TEnum), value);

        private static DateTime ToUtc(long ticks, GameLogger logger, string clueId)
        {
            if (ticks >= DateTime.MinValue.Ticks && ticks <= DateTime.MaxValue.Ticks)
                return new DateTime(ticks, DateTimeKind.Utc);

            Log(logger, "SAVE_INVALID_TIMESTAMP", "잘못된 시각 값을 최솟값으로 둡니다.", clueId: clueId);
            return DateTime.MinValue;
        }

        private static void Log(GameLogger logger, string code, string cause,
            string contractId = null, string npcId = null, string clueId = null) =>
            logger?.LogError(code, SystemName, GamePhase.Idle, cause, recoverable: true, contractId, npcId, clueId);
    }
}
