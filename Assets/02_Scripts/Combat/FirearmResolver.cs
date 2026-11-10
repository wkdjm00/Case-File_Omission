using System;
using System.Collections.Generic;
using CaseFileOmission.Core;

namespace CaseFileOmission.Combat
{
    /// <summary>06페이지 2026-11-09 확정 명중 확률 계산과 발사 처리.</summary>
    public sealed class FirearmResolver
    {
        private const float BaseHitChance = 70f;
        private const float MinHitChance = 10f;
        private const float MaxHitChance = 95f;

        private readonly Random _random;

        public FirearmResolver(Random random = null)
        {
            _random = random ?? new Random();
        }

        public float CalculateHitChance(bool isCloseRange, bool hasUnstableAim, bool targetMovingFast, bool pathPartiallyBlocked)
        {
            var modifiers = new List<float>();
            if (isCloseRange) modifiers.Add(15f);
            if (hasUnstableAim) modifiers.Add(-20f);
            if (targetMovingFast) modifiers.Add(-15f);
            if (pathPartiallyBlocked) modifiers.Add(-30f);

            return ProbabilityCalculator.ApplyModifiers(BaseHitChance, modifiers, MinHitChance, MaxHitChance);
        }

        public ShotEvent Fire(
            FirearmState firearm,
            string shooterId,
            string targetId,
            bool isCloseRange,
            bool hasUnstableAim,
            bool targetMovingFast,
            bool pathPartiallyBlocked,
            string roomId = null,
            GameEventLog eventLog = null,
            string contractId = null,
            string eventId = null)
        {
            firearm.Fire(); // 탄약 없으면 FirearmState가 예외를 던진다.

            float hitChance = CalculateHitChance(isCloseRange, hasUnstableAim, targetMovingFast, pathPartiallyBlocked);
            bool hit = _random.NextDouble() * 100.0 < hitChance;

            var shot = new ShotEvent
            {
                EventId = eventId ?? Guid.NewGuid().ToString("N"),
                ShooterId = shooterId,
                TargetId = targetId,
                Hit = hit,
                RoomId = roomId,
                OccurredAtUtc = DateTime.UtcNow
            };

            // 사격 이벤트 자체만 기록한다. 목격·흔적·정보 전달은 별도 이벤트로 이어진다.
            eventLog?.TryRecord(new GameEvent(shot.EventId, GameEventType.ShotFired, contractId,
                $"shooter={shooterId};target={targetId};hit={hit};room={roomId}"));

            return shot;
        }
    }
}
