using System;
using System.Collections.Generic;
using CaseFileOmission.Core;

namespace CaseFileOmission.NPC
{
    public sealed class PersuasionResult
    {
        public bool Success;
        public float ProbabilityUsed;

        /// <summary>회유 성공 시 대상이 플레이어에게 공개한 정보 — 보유 정보 전부(아직 누구에게도 전달하지 않은 정보 포함).</summary>
        public readonly List<string> RevealedInfoIds = new List<string>();
    }

    /// <summary>
    /// 07페이지 2026-11-09 구현 명세 확정. 회유는 플레이어가 실제 가해자를 이미 알아낸 경우에만, 실제 가해자에게만 시도할 수 있다.
    /// 성공하면 사건이 끝날 때까지 협조하며, 자신이 아는 정보를 이전에 공유하지 않은 것까지 모두 플레이어에게 공개한다.
    /// 공개된 정보가 경찰이나 다른 NPC에게 자동으로 전달되지는 않는다.
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
            bool targetAlreadyTrustsPlayer)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            if (!playerKnowsActualCulprit)
                throw new InvalidOperationException("회유는 플레이어가 실제 가해자를 알아낸 뒤에만 시도할 수 있습니다.");
            if (!target.IsActualPerpetrator)
                throw new InvalidOperationException("회유 대상은 실제 가해자뿐입니다.");
            if (!target.IsAlive)
                throw new InvalidOperationException("사망한 인물은 회유할 수 없습니다.");

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
                result.RevealedInfoIds.AddRange(target.KnownInfoIds);
                result.RevealedInfoIds.Sort(StringComparer.Ordinal);
            }

            return result;
        }
    }
}
