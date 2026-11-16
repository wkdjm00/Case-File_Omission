using System;
using System.Collections.Generic;
using System.Linq;

namespace CaseFileOmission.Organization
{
    public enum OrgTruthStage
    {
        Systemic,
        Power,
        Experiment,
        Designer
    }

    /// <summary>06 roll_status.</summary>
    public enum OrgReportRollStatus
    {
        NotChecked,
        LowTrustWait,
        Success,
        Failed
    }

    /// <summary>06 delivery_status.</summary>
    public enum OrgReportDeliveryStatus
    {
        NotReady,
        Pending,
        Delivered
    }

    public sealed class OrgSupplementaryReportDefinition
    {
        public string Id;
        public OrgTruthStage Stage;
        public string Title;
        /// <summary>조건 A/B/C: 이 단서명을 플레이어가 보유.</summary>
        public string ConditionA, ConditionB, ConditionC;
        /// <summary>표시용 출처 자료명(원본 자료 또는 고정 설정 데이터 ID).</summary>
        public string[] SourceLabels;
        public string Body;
    }

    /// <summary>보고서별 진행 상태(저장 대상).</summary>
    public sealed class OrgReportState
    {
        public string Id;
        public OrgReportRollStatus RollStatus = OrgReportRollStatus.NotChecked;
        public OrgReportDeliveryStatus DeliveryStatus = OrgReportDeliveryStatus.NotReady;
        /// <summary>최초 적격 시점의 신뢰도 구간(eligibility_trust_band), 예: "40-69".</summary>
        public string EligibilityBand;
        public string FirstEligibleContractId;
        /// <summary>전달된 세계 시각(분). 전달 전이면 -1.</summary>
        public int DeliveredWorldMinute = -1;
        public bool Read;
    }

    /// <summary>판정에 필요한 게임 상태(호출자가 채운다).</summary>
    public sealed class OrgSupportContext
    {
        public float Trust;
        public string ContractId;
        public Func<string, bool> PlayerHolds = _ => false;
        public Func<OrgTruthStage, bool> StageUnlocked = _ => false;
        /// <summary>그 진실 단계가 열린 뒤 추가로 완료한 서로 다른 일반 의뢰 수(조건 D).</summary>
        public Func<OrgTruthStage, int> ContractsCompletedSinceUnlock = _ => 0;
        /// <summary>원본 자료가 캠페인 안에 실제로 존재하는지(조직이 원본에 접근할 수 있는지 포함).</summary>
        public Func<string, bool> SourceAvailable = _ => true;
        public bool FinalReportComplete;
    }

    /// <summary>
    /// 05·06 2026-11-13 확정 조직 보조 자료 지원.
    /// - 신뢰도 0: 지원 없음(게임 오버) / 1~20: 판정하지 않고 대기(LOW_TRUST_WAIT), 21 이상이 되면 다시 검사
    /// - 21~39: A+B+C+D 20% / 40~69: A+B+C 35% / 70~89: A+B 50% / 90~100: A 70%
    /// - 필수 조건: 진실 단계 해금, 원본 자료의 실제 존재·조직 접근 가능, 미지급·미판정, 설계자 자료는 최종 보고서 완성 전.
    ///   플레이어가 원본을 먼저 가져야 한다는 조건은 두지 않는다.
    /// - 조건 D: 해당 진실 단계 해금 뒤 서로 다른 일반 의뢰 2건 추가 완료(05·06 최종 정정).
    /// - 최초 적격 상태가 된 시점에 그때 신뢰도 구간 확률로 한 번만 판정하고, 실패하면 다시 굴리지 않는다.
    /// - 성공해 대기열에 들어간 자료는 이후 신뢰도가 바뀌어도 취소하지 않는다.
    /// 전달 시점(의뢰·조사·사건보드·회의 중에는 전달하지 않음)과 전화 1회는 호출자(GameSession)가 정한다.
    /// </summary>
    public sealed class OrgSupplementarySupportService
    {
        public const int ContractsRequiredForConditionD = 2;

        private readonly Dictionary<string, OrgReportState> _states = new Dictionary<string, OrgReportState>();
        private readonly Random _random;

        public OrgSupplementarySupportService(Random random = null)
        {
            _random = random ?? new Random();
            foreach (var def in OrgSupplementaryReports.All)
                _states[def.Id] = new OrgReportState { Id = def.Id };
        }

        public IReadOnlyCollection<OrgReportState> States => _states.Values;

        public OrgReportState GetState(string id) => _states.TryGetValue(id, out var s) ? s : null;

        public static OrgSupplementaryReportDefinition GetDefinition(string id) =>
            OrgSupplementaryReports.All.FirstOrDefault(d => d.Id == id);

        /// <summary>신뢰도 구간별 제공 확률(%). 0 또는 1~20이면 0.</summary>
        public static float ProbabilityFor(float trust) =>
            trust >= 90f ? 70f : trust >= 70f ? 50f : trust >= 40f ? 35f : trust >= 21f ? 20f : 0f;

        public static string BandFor(float trust) =>
            trust >= 90f ? "90-100" : trust >= 70f ? "70-89" : trust >= 40f ? "40-69" : trust >= 21f ? "21-39" : trust >= 1f ? "1-20" : "0";

        /// <summary>신뢰도 구간이 요구하는 조건(A, A+B, A+B+C, A+B+C+D)을 모두 만족하는지.</summary>
        public static bool ConditionsMet(OrgSupplementaryReportDefinition def, OrgSupportContext ctx)
        {
            bool a = ctx.PlayerHolds(def.ConditionA);
            bool b = ctx.PlayerHolds(def.ConditionB);
            bool c = ctx.PlayerHolds(def.ConditionC);
            bool d = ctx.ContractsCompletedSinceUnlock(def.Stage) >= ContractsRequiredForConditionD;
            float t = ctx.Trust;
            if (t >= 90f) return a;
            if (t >= 70f) return a && b;
            if (t >= 40f) return a && b && c;
            if (t >= 21f) return a && b && c && d;
            return false;
        }

        /// <summary>필수 조건(단계 해금·원본 존재·설계자 자료는 최종 보고서 전).</summary>
        public static bool BaseEligible(OrgSupplementaryReportDefinition def, OrgSupportContext ctx) =>
            ctx.StageUnlocked(def.Stage)
            && def.SourceLabels.All(ctx.SourceAvailable)
            && !(def.Stage == OrgTruthStage.Designer && ctx.FinalReportComplete);

        /// <summary>
        /// 의뢰 종료 직후(신뢰도 변화·신뢰도 0 검사 뒤) 호출한다. 아직 판정하지 않은 자료 중 이번에 처음 적격이 된 자료를 판정하고,
        /// 성공한 자료 ID를 돌려준다(전달 대기열에 들어감).
        /// </summary>
        public IReadOnlyList<string> Evaluate(OrgSupportContext ctx)
        {
            var succeeded = new List<string>();
            if (ctx.Trust <= 0f) return succeeded;

            foreach (var def in OrgSupplementaryReports.All)
            {
                var state = _states[def.Id];
                if (state.RollStatus != OrgReportRollStatus.NotChecked && state.RollStatus != OrgReportRollStatus.LowTrustWait) continue;
                if (!BaseEligible(def, ctx)) continue;

                if (ctx.Trust < 21f)
                {
                    state.RollStatus = OrgReportRollStatus.LowTrustWait;
                    continue;
                }
                if (!ConditionsMet(def, ctx)) continue;

                state.EligibilityBand = BandFor(ctx.Trust);
                state.FirstEligibleContractId = ctx.ContractId;
                bool success = _random.NextDouble() * 100.0 < ProbabilityFor(ctx.Trust);
                state.RollStatus = success ? OrgReportRollStatus.Success : OrgReportRollStatus.Failed;
                if (success)
                {
                    state.DeliveryStatus = OrgReportDeliveryStatus.Pending;
                    succeeded.Add(def.Id);
                }
            }
            return succeeded;
        }

        /// <summary>대기 중인 자료를 모두 전달한다(같은 경계의 여러 건은 한꺼번에). 전달한 ID를 돌려준다.</summary>
        public IReadOnlyList<string> DeliverPending(int worldMinute)
        {
            var delivered = new List<string>();
            foreach (var state in _states.Values.Where(s => s.DeliveryStatus == OrgReportDeliveryStatus.Pending).OrderBy(s => s.Id, StringComparer.Ordinal))
            {
                state.DeliveryStatus = OrgReportDeliveryStatus.Delivered;
                state.DeliveredWorldMinute = worldMinute;
                delivered.Add(state.Id);
            }
            return delivered;
        }

        public void MarkRead(string id)
        {
            if (_states.TryGetValue(id, out var s) && s.DeliveryStatus == OrgReportDeliveryStatus.Delivered) s.Read = true;
        }

        /// <summary>저장/불러오기 전용. 판정을 다시 실행하지 않고 상태만 되살린다. 모르는 ID면 false.</summary>
        public bool Restore(OrgReportState saved)
        {
            if (saved == null || string.IsNullOrEmpty(saved.Id) || !_states.ContainsKey(saved.Id)) return false;
            _states[saved.Id] = saved;
            return true;
        }
    }
}
