using System;
using System.Collections.Generic;
using CaseFileOmission.Core;

namespace CaseFileOmission.Evidence
{
    public enum ManipulationOutcome
    {
        Succeeded,
        Failed,

        /// <summary>조작 가능 시점 전이라 판정하지 않았다(시도 기록도 남기지 않음).</summary>
        Locked,

        /// <summary>남은 조사 예산이 1시간보다 적어 시도하지 않았다(시간도 진행하지 않음).</summary>
        NotEnoughTime
    }

    public sealed class ManipulationAttemptRecord
    {
        public string ClueId;
        public ManipulationType Type;
        public string ActorId;
        public DateTime AttemptedAtUtc;
    }

    public sealed class ManipulationOutcomeResult
    {
        public ManipulationOutcome Outcome;
        public float ProbabilityUsed;

        /// <summary>성공했을 때만 채워진다. 실패 시 원본은 그대로 유지된다.</summary>
        public ManipulationRecord AppliedRecord;
    }

    /// <summary>
    /// 물리적 증거·기록 조작의 성공/실패 판정. 기본 성공 확률 70%는 확정값이다(사용자 확정 2026-11-10,
    /// 통합 구현 명세서 §4-5). 보정값(-15/-20/-10/+10)과 5~95% 범위는 통합 구현 명세서 §4-5의 값이다.
    /// 가해자 거짓 알리바이 생성 확률(거짓말 성격 70%)과는 무관한 별개 규칙이다.
    /// 구두 거짓말 판별(Meeting.LieDetectionService, 기본 50%)과도 별개의 메커니즘이며 서로 섞지 않는다.
    ///
    /// 조작 가능 시점(04 §5, 사용자 확정 2026-11-10): 메인 사건 최종 보고서(타자기) 완성 및 첫 회의 종료 이후.
    /// 그 판정은 <see cref="UnlockCondition"/>으로 주입한다. 조건이 거짓이면 판정 없이 Locked를 돌려준다.
    ///
    /// 성공하면 기존 <see cref="EvidenceManipulationService"/>의 왜곡/은폐 입력 처리를 그대로
    /// 호출해 실제로 적용한다. 실패하면 원본을 건드리지 않고 시도 기록만 남긴다
    /// ("왜곡 실패: 원본 유지, 조작 시도 기록" / "은폐 실패: 기존 단서 유지, 조작 시도 기록").
    /// </summary>
    public sealed class EvidenceManipulationOutcomeResolver
    {
        public const float BaseChance = 70f;
        public const float MinChance = 5f;
        public const float MaxChance = 95f;

        public const float SuspiciousTargetModifier = -15f;
        public const float WitnessedModifier = -20f;
        public const float AlreadyConfirmedModifier = -10f;
        public const float TargetUnawareModifier = 10f;

        /// <summary>조작 가능 시점 조건. null이면 시점 제한 없이 판정한다(단위 테스트·기존 호출 호환).</summary>
        public Func<bool> UnlockCondition;

        private readonly EvidenceManipulationService _manipulationService;
        private readonly Random _random;
        private readonly List<ManipulationAttemptRecord> _failedAttempts = new List<ManipulationAttemptRecord>();

        public IReadOnlyList<ManipulationAttemptRecord> FailedAttempts => _failedAttempts;

        /// <summary>저장/불러오기 전용.</summary>
        public void RestoreFailedAttempt(ManipulationAttemptRecord attempt)
        {
            if (attempt == null) throw new ArgumentNullException(nameof(attempt));
            _failedAttempts.Add(attempt);
        }

        public EvidenceManipulationOutcomeResolver(EvidenceManipulationService manipulationService, Random random = null)
        {
            _manipulationService = manipulationService ?? throw new ArgumentNullException(nameof(manipulationService));
            _random = random ?? new Random();
        }

        public ManipulationOutcomeResult Attempt(
            ManipulationType type,
            string clueId,
            string actorId,
            bool targetIsSuspicious,
            bool witnessedByOthers,
            bool targetAlreadyConfirmedClue,
            bool targetUnawareOfFacts,
            IReadOnlyList<string> actorHeldClueIds = null)
        {
            if (UnlockCondition != null && !UnlockCondition())
                return new ManipulationOutcomeResult { Outcome = ManipulationOutcome.Locked, ProbabilityUsed = 0f };

            var modifiers = new List<float>();
            if (targetIsSuspicious) modifiers.Add(SuspiciousTargetModifier);
            if (witnessedByOthers) modifiers.Add(WitnessedModifier);
            if (targetAlreadyConfirmedClue) modifiers.Add(AlreadyConfirmedModifier);
            if (targetUnawareOfFacts) modifiers.Add(TargetUnawareModifier);

            float finalChance = ProbabilityCalculator.ApplyModifiers(BaseChance, modifiers, MinChance, MaxChance);
            bool success = _random.NextDouble() * 100.0 < finalChance;

            if (!success)
            {
                _failedAttempts.Add(new ManipulationAttemptRecord
                {
                    ClueId = clueId,
                    Type = type,
                    ActorId = actorId,
                    AttemptedAtUtc = DateTime.UtcNow
                });

                return new ManipulationOutcomeResult { Outcome = ManipulationOutcome.Failed, ProbabilityUsed = finalChance };
            }

            ManipulationRecord applied = type == ManipulationType.Distort
                ? _manipulationService.Distort(clueId, actorId)
                : _manipulationService.Conceal(clueId, actorId, actorHeldClueIds);

            return new ManipulationOutcomeResult
            {
                Outcome = ManipulationOutcome.Succeeded,
                ProbabilityUsed = finalChance,
                AppliedRecord = applied
            };
        }
    }
}
