using System;
using System.Collections.Generic;
using CaseFileOmission.Core;
using CaseFileOmission.Evidence;

namespace CaseFileOmission.Meeting
{
    public enum ContradictionLevel
    {
        None,
        Weak,
        Clear
    }

    public sealed class LieAttemptResult
    {
        public bool Success;
        public float ProbabilityUsed;
        public bool ForcedDetectionByConfirmedClue;
    }

    /// <summary>
    /// 06페이지 2026-11-09 구현 명세 확정. 배정된 경찰이 이미 내용을 확인한 단서에 대한 거짓말은
    /// 확률 계산 없이 100% 발각된다(06·07페이지 확정 규칙) — 이는 일반 확률 판정보다 우선한다.
    /// </summary>
    public sealed class LieDetectionService
    {
        private const float BaseChance = 50f;
        private const float MinChance = 5f;
        private const float MaxChance = 95f;

        private readonly ClueRepository _clueRepository;
        private readonly Random _random;

        public LieDetectionService(ClueRepository clueRepository, Random random = null)
        {
            _clueRepository = clueRepository ?? throw new ArgumentNullException(nameof(clueRepository));
            _random = random ?? new Random();
        }

        public LieAttemptResult AttemptLie(string clueId, string confrontingPoliceId, ContradictionLevel contradictionLevel, bool hasSupportingEvidence)
        {
            if (_clueRepository.Exists(clueId))
            {
                var record = _clueRepository.GetRecord(clueId);
                if (!string.IsNullOrEmpty(confrontingPoliceId) && record.ConfirmedByPoliceIds.Contains(confrontingPoliceId))
                {
                    return new LieAttemptResult { Success = false, ProbabilityUsed = 0f, ForcedDetectionByConfirmedClue = true };
                }
            }

            var modifiers = new List<float>();
            if (hasSupportingEvidence) modifiers.Add(15f);

            switch (contradictionLevel)
            {
                case ContradictionLevel.Weak: modifiers.Add(-15f); break;
                case ContradictionLevel.Clear: modifiers.Add(-30f); break;
            }

            float finalChance = ProbabilityCalculator.ApplyModifiers(BaseChance, modifiers, MinChance, MaxChance);
            bool success = _random.NextDouble() * 100.0 < finalChance;

            return new LieAttemptResult { Success = success, ProbabilityUsed = finalChance, ForcedDetectionByConfirmedClue = false };
        }
    }
}
