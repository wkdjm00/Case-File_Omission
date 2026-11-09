using System;
using System.Collections.Generic;
using CaseFileOmission.Core;

namespace CaseFileOmission.NPC
{
    public enum SuspicionLevel
    {
        None,
        Weak,
        Strong
    }

    public sealed class InfoTransferResult
    {
        public InfoTransferOutcome Outcome;
        public float ProbabilityUsed;
    }

    /// <summary>
    /// 07페이지 2026-11-09 구현 명세 확정 표를 그대로 구현한다.
    /// "강하게/약하게 의심함"의 수치 경계는 문서에 없으므로, 그 분류(<see cref="SuspicionLevel"/>) 자체는
    /// 호출자가 실제 관계 수치를 보고 결정해서 넘긴다. 여기서는 분류가 정해졌을 때의 보정값만 확정 적용한다.
    /// </summary>
    public sealed class NpcInfoTransferService
    {
        private const float BaseChance = 50f;
        private const float MinChance = 5f;
        private const float MaxChance = 95f;

        private readonly Random _random;
        private readonly HashSet<(string Sender, string Receiver, string InfoId)> _transferLog =
            new HashSet<(string, string, string)>();

        public NpcInfoTransferService(Random random = null)
        {
            _random = random ?? new Random();
        }

        /// <summary>저장/불러오기 전용. 성공적으로 전달된 (발신자, 수신자, 정보) 기록.</summary>
        public IEnumerable<(string Sender, string Receiver, string InfoId)> TransferLog => _transferLog;

        /// <summary>같은 NPC 쌍에 이미 성공적으로 전달한 정보인지.</summary>
        public bool HasTransferred(string senderId, string receiverId, string infoId) =>
            _transferLog.Contains((senderId, receiverId, infoId));

        /// <summary>저장/불러오기 전용. 확률 판정 없이 "이미 전달함" 기록만 되살린다.</summary>
        public void RestoreTransfer(string senderId, string receiverId, string infoId) =>
            _transferLog.Add((senderId, receiverId, infoId));

        public InfoTransferResult TryTransfer(
            NpcState sender,
            NpcState receiver,
            string infoId,
            bool canContact,
            SuspicionLevel senderSuspicionTowardReceiver,
            bool hasConcreteBenefit,
            bool hasExposureRisk)
        {
            if (sender == null) throw new ArgumentNullException(nameof(sender));
            if (receiver == null) throw new ArgumentNullException(nameof(receiver));
            if (!sender.Knows(infoId))
                return new InfoTransferResult { Outcome = InfoTransferOutcome.NotAttempted, ProbabilityUsed = 0f };

            if (!canContact)
                return new InfoTransferResult { Outcome = InfoTransferOutcome.NotAttempted, ProbabilityUsed = 0f };

            var key = (sender.NpcId, receiver.NpcId, infoId);
            if (_transferLog.Contains(key))
                return new InfoTransferResult { Outcome = InfoTransferOutcome.AlreadyTransferred, ProbabilityUsed = 0f };

            var modifiers = new List<float>();

            switch (sender.Personality)
            {
                case NpcPersonality.Secretive: modifiers.Add(-20f); break;
                case NpcPersonality.Timid: modifiers.Add(-10f); break;
                case NpcPersonality.Suspicious: modifiers.Add(-10f); break;
                case NpcPersonality.Indifferent: modifiers.Add(-10f); break;
                case NpcPersonality.Guilty: modifiers.Add(10f); break;
                case NpcPersonality.BlindlyTrusting: modifiers.Add(15f); break;
            }

            if (sender.IsActualPerpetrator)
                modifiers.Add(-15f);

            switch (senderSuspicionTowardReceiver)
            {
                case SuspicionLevel.Strong: modifiers.Add(-15f); break;
                case SuspicionLevel.Weak: modifiers.Add(-5f); break;
            }

            if (hasConcreteBenefit) modifiers.Add(10f);
            if (hasExposureRisk) modifiers.Add(-15f);

            float finalChance = ProbabilityCalculator.ApplyModifiers(BaseChance, modifiers, MinChance, MaxChance);
            bool success = _random.NextDouble() * 100.0 < finalChance;

            // "이미 전달한 정보는 반복 전달하지 않는다"는 성공적으로 전달된 정보에 대한 규칙이다.
            // 실패한 시도는 이후 접촉 기회에서 다시 시도할 수 있도록 기록하지 않는다.
            if (success)
            {
                _transferLog.Add(key);

                // 04페이지 확정: "수신자가 그 정보를 믿는지는 별도 판정한다." 그 별도 판정 규칙은
                // 아직 문서에 없으므로 임의로 Believe/Suspect를 단정하지 않고 Withhold(판단 보류)로 둔다.
                receiver.LearnInfo(infoId, NpcJudgement.Withhold);
            }

            return new InfoTransferResult
            {
                Outcome = success ? InfoTransferOutcome.Succeeded : InfoTransferOutcome.Failed,
                ProbabilityUsed = finalChance
            };
        }
    }
}
