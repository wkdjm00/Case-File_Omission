using System;
using System.Collections.Generic;
using CaseFileOmission.Core;

namespace CaseFileOmission.Police
{
    public sealed class ConfirmationRequestResult
    {
        public bool Requested;
        public float ProbabilityUsed;
    }

    /// <summary>
    /// 06·07페이지 확정: 담당 경찰과 플레이어가 같은 방에 있을 때만 판정한다. 기본 10%,
    /// 의심도 20%마다 +5%p, 수사중심형(A·B)은 해당 방에서 발견한 단서 1개당 +5%p,
    /// 직관중심형(E)은 거짓말이 수락된 횟수 2회당 +5%p. 최종 확률은 최대 50%.
    /// </summary>
    public sealed class ClueConfirmationRequestService
    {
        private const float BaseChance = 10f;
        private const float MaxChance = 50f;

        private readonly Random _random;

        public ClueConfirmationRequestService(Random random = null)
        {
            _random = random ?? new Random();
        }

        public ConfirmationRequestResult Roll(
            string policeId,
            bool sameRoomAsPlayer,
            float currentSuspicion,
            int cluesFoundByPoliceInThisRoom,
            int acceptedLiesCount)
        {
            if (!sameRoomAsPlayer)
                return new ConfirmationRequestResult { Requested = false, ProbabilityUsed = 0f };

            var type = PoliceRoster.JudgementTypeById[policeId];
            var modifiers = new List<float> { MathF.Floor(currentSuspicion / 20f) * 5f };

            if (type == PoliceJudgementType.Investigative)
                modifiers.Add(cluesFoundByPoliceInThisRoom * 5f);

            if (type == PoliceJudgementType.Intuitive)
                modifiers.Add((acceptedLiesCount / 2) * 5f);

            float finalChance = ProbabilityCalculator.ApplyModifiers(BaseChance, modifiers, 0f, MaxChance);
            bool requested = _random.NextDouble() * 100.0 < finalChance;

            return new ConfirmationRequestResult { Requested = requested, ProbabilityUsed = finalChance };
        }
    }
}
