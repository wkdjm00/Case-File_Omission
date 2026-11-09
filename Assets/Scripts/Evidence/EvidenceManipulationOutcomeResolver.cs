using System;
using System.Collections.Generic;
using CaseFileOmission.Core;

namespace CaseFileOmission.Evidence
{
    public enum ManipulationOutcome
    {
        Succeeded,
        Failed
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
    /// 통합 구현 명세서 §4-5 확정 기본값(기본 70%, 여러 보정)을 사용하는 물리적 증거 조작의
    /// 성공/실패 판정. 구두 거짓말 판별(Meeting.LieDetectionService, 기본 50%)과는
    /// 완전히 별개의 메커니즘이며 서로 섞지 않는다(§4-5 확정 지침).
    ///
    /// 성공하면 기존 <see cref="EvidenceManipulationService"/>의 왜곡/은폐 입력 처리를 그대로
    /// 호출해 실제로 적용한다. 실패하면 원본을 건드리지 않고 시도 기록만 남긴다
    /// ("왜곡 실패: 원본 유지, 조작 시도 기록" / "은폐 실패: 기존 단서 유지, 조작 시도 기록").
    /// </summary>
    public sealed class EvidenceManipulationOutcomeResolver
    {
        private const float BaseChance = 70f;
        private const float MinChance = 5f;
        private const float MaxChance = 95f;

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
            var modifiers = new List<float>();
            if (targetIsSuspicious) modifiers.Add(-15f);
            if (witnessedByOthers) modifiers.Add(-20f);
            if (targetAlreadyConfirmedClue) modifiers.Add(-10f);
            if (targetUnawareOfFacts) modifiers.Add(10f);

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
