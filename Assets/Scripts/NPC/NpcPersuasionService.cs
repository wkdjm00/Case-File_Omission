using System;
using System.Collections.Generic;
using CaseFileOmission.Core;

namespace CaseFileOmission.NPC
{
    public sealed class PersuasionResult
    {
        public bool Success;
        public float ProbabilityUsed;

        /// <summary>회유 성공 시, 대상이 아직 다른 누구에게도 전달하지 않은 보유 정보. 성공이 아니면 비어 있다.</summary>
        public readonly List<string> RevealedInfoIds = new List<string>();
    }

    /// <summary>
    /// 07페이지 2026-11-09 구현 명세 확정. 회유는 플레이어가 실제 가해자를 이미 알아낸 경우에만 시도할 수 있다.
    /// </summary>
    public sealed class NpcPersuasionService
    {
        private const float BaseChance = 50f;
        private const float MinChance = 5f;
        private const float MaxChance = 95f;

        private readonly Random _random;

        public NpcPersuasionService(Random random = null)
        {
            _random = random ?? new Random();
        }

        public PersuasionResult TryPersuade(
            NpcState target,
            bool playerKnowsActualCulprit,
            bool hasStrongSupportingEvidence,
            bool hasUnfavorableDirectEvidence,
            bool targetAlreadyTrustsPlayer,
            HashSet<string> infoAlreadySharedWithOthers)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            if (!playerKnowsActualCulprit)
                throw new InvalidOperationException("회유는 플레이어가 실제 가해자를 알아낸 뒤에만 시도할 수 있습니다.");

            var modifiers = new List<float>();
            if (hasStrongSupportingEvidence) modifiers.Add(20f);
            if (hasUnfavorableDirectEvidence) modifiers.Add(-15f);
            if (targetAlreadyTrustsPlayer) modifiers.Add(10f);

            float finalChance = ProbabilityCalculator.ApplyModifiers(BaseChance, modifiers, MinChance, MaxChance);
            bool success = _random.NextDouble() * 100.0 < finalChance;

            var result = new PersuasionResult { Success = success, ProbabilityUsed = finalChance };

            if (success)
            {
                target.MarkCooperating();
                infoAlreadySharedWithOthers ??= new HashSet<string>();
                foreach (var infoId in target.KnownInfoIds)
                {
                    if (!infoAlreadySharedWithOthers.Contains(infoId))
                        result.RevealedInfoIds.Add(infoId);
                }
            }

            return result;
        }
    }
}
