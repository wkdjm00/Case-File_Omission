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

    /// <summary>전달하는 정보와 전달자의 관계(07 2026-11-12 §3 실제 역할 보정 조건).</summary>
    public enum InfoSenderRelation
    {
        None,

        /// <summary>본인의 범행·가해자 정체를 드러내거나 그 판단을 유의미하게 뒷받침하는 정보.</summary>
        IncriminatesSender,

        /// <summary>본인이 직접 겪은 사건 사실(생존 피해자).</summary>
        OwnExperience,

        /// <summary>본인이 직접 목격한 사건 사실.</summary>
        DirectlyWitnessed
    }

    /// <summary>
    /// 07 「2026-11-12 — NPC 정보 전달 확률 보정값 최종 확정」을 구현한다(2026-11-09·11-10 표보다 우선).
    /// 기본 50%, 성격 1개 → 역할 1개 → 전달자→수신자 의심 → 독립 상황 보정 순으로 합산 후 5~95%.
    /// </summary>
    public sealed class NpcInfoTransferService
    {
        /// <summary>
        /// 정보와 전달자의 관계 판정. 게임 진행 계층이 사건 데이터로 정한다. 없으면 기본 판정:
        /// 실제 가해자는 자기 범행 관련으로, 그 밖에는 전달자가 직접 목격한 사건 ID와 같은 정보면 목격(생존 피해자는 직접 경험)으로 본다.
        /// </summary>
        public Func<NpcState, string, InfoSenderRelation> RelationOf;

        private const float BaseChance = 50f;
        private const float MinChance = 5f;
        private const float MaxChance = 95f;

        /// <summary>
        /// 07 2026-11-10 확정: NPC 사이 의심 0–9 보정 없음, 10–34 약한 의심(-5%p), 35–100 강한 의심(-15%p). 둘은 중복 적용하지 않는다.
        /// </summary>
        public static SuspicionLevel ClassifySuspicion(float suspicion)
        {
            if (suspicion >= 35f) return SuspicionLevel.Strong;
            if (suspicion >= 10f) return SuspicionLevel.Weak;
            return SuspicionLevel.None;
        }

        private static InfoSenderRelation DefaultRelation(NpcState sender, string infoId)
        {
            if (sender.IsActualPerpetrator) return InfoSenderRelation.IncriminatesSender;
            if (sender.WitnessedEventIds.Contains(infoId))
                return sender.ActualRole == NpcActualRole.Victim ? InfoSenderRelation.OwnExperience : InfoSenderRelation.DirectlyWitnessed;
            return InfoSenderRelation.None;
        }

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
            bool hasExposureRisk,
            bool receiverIsTrustedBySender = false)
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

            // §2 공식 성격 보정(하나만). 무관심·욕심(성격 자체)은 0, 맹목적으로 믿는은 등록된 신뢰 대상에게만.
            // 신뢰 관계 데이터가 없으면 모든 상대를 신뢰 대상으로 처리하지 않는다(receiverIsTrustedBySender 기본 false).
            switch (sender.Personality)
            {
                case NpcPersonality.Suspicious: modifiers.Add(-10f); break;
                case NpcPersonality.Timid: modifiers.Add(-10f); break;
                case NpcPersonality.FrequentLiar: modifiers.Add(-10f); break;
                case NpcPersonality.Secretive: modifiers.Add(-20f); break;
                case NpcPersonality.Guilty: modifiers.Add(10f); break;
                case NpcPersonality.BlindlyTrusting: if (receiverIsTrustedBySender) modifiers.Add(15f); break;
                case NpcPersonality.Jealous: modifiers.Add(-5f); break;
                case NpcPersonality.Dependent: modifiers.Add(receiverIsTrustedBySender ? 15f : -5f); break;
            }

            // §3 실제 역할 보정(조건에 맞는 하나만). 가해자는 목격자 보정을 추가로 받지 않는다.
            var relation = RelationOf?.Invoke(sender, infoId) ?? DefaultRelation(sender, infoId);
            bool roleExposure = false;
            if (sender.IsActualPerpetrator)
            {
                if (relation == InfoSenderRelation.IncriminatesSender) { modifiers.Add(-15f); roleExposure = true; }
            }
            else if (relation == InfoSenderRelation.OwnExperience || relation == InfoSenderRelation.DirectlyWitnessed)
                modifiers.Add(10f); // 생존 피해자의 직접 경험 / 직접 목격자의 목격 사실

            switch (senderSuspicionTowardReceiver)
            {
                case SuspicionLevel.Strong: modifiers.Add(-15f); break;
                case SuspicionLevel.Weak: modifiers.Add(-5f); break;
            }

            // §5 상황 보정. 역할 보정에 이미 반영한 자기 범행 노출 위험은 같은 이유로 다시 계산하지 않는다.
            if (hasConcreteBenefit) modifiers.Add(10f);
            if (hasExposureRisk && !roleExposure) modifiers.Add(-15f);

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
