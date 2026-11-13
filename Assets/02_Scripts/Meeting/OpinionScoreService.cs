using System;
using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.NPC;

namespace CaseFileOmission.Meeting
{
    /// <summary>같은 시점에 한 관찰자가 받아들인 정보 하나.</summary>
    public readonly struct OpinionInput
    {
        public readonly string SuspectId;

        /// <summary>정보 기본 점수: 의심 증가 +1/+2/+3(약·보통·강), 중립 0, 의심 감소 -1/-2/-3.</summary>
        public readonly int BaseScore;

        /// <summary>참이면 '확인한 증거', 거짓이면 '회의에서 수용한 주장' 가중치를 쓴다.</summary>
        public readonly bool IsConfirmedEvidence;

        public OpinionInput(string suspectId, int baseScore, bool isConfirmedEvidence)
        {
            SuspectId = suspectId;
            BaseScore = Math.Max(-3, Math.Min(3, baseScore));
            IsConfirmedEvidence = isConfirmedEvidence;
        }
    }

    /// <summary>
    /// 06 2026-11-11 확정 NPC 개인 의견 점수(0~100, 0 의심 없음 · 50 중립 · 100 강한 의심). 관계 의심도와 별개다.
    /// 계산: 정보마다 기본 점수 × 요소 가중치 → × (4 − w)/3 → 같은 시점 변화량을 소수 그대로 합산 → 가장 가까운 정수로 반올림 →
    /// 기존 점수에 더한 뒤 0~100으로 한 번 제한. 공식 결론 투표는 지목 인물에 대한 점수가 50 초과면 찬성(50은 반대).
    /// 경찰용 가중치는 원문 미정이라 '검증된 증거 우선' 원칙만 반영한 TEST_TEMP 값(증거 3·주장 1·w 2)을 쓴다.
    /// </summary>
    public sealed class OpinionScoreService
    {
        public const float Neutral = 50f;
        public const float VoteThreshold = 50f;

        private readonly Dictionary<(string Observer, string Suspect), float> _scores = new Dictionary<(string, string), float>();

        public float Get(string observerId, string suspectId) =>
            _scores.TryGetValue((observerId, suspectId), out var v) ? v : Neutral;

        public bool VotesGuilty(string observerId, string suspectId) => Get(observerId, suspectId) > VoteThreshold;

        public IEnumerable<KeyValuePair<(string Observer, string Suspect), float>> All => _scores;

        /// <summary>저장/불러오기 전용.</summary>
        public void Restore(string observerId, string suspectId, float value) =>
            _scores[(observerId, suspectId)] = Math.Max(0f, Math.Min(100f, value));

        public void Clear() => _scores.Clear();

        /// <summary>(확인 증거, 수용 주장, 기존 의심 w) — 06 표. personality가 null이면 경찰(TEST_TEMP).</summary>
        public static (int Evidence, int Claim, int ExistingWeight) Weights(NpcPersonality? personality) => personality switch
        {
            NpcPersonality.Suspicious => (3, 1, 2),
            NpcPersonality.Greedy => (2, 1, 3),
            NpcPersonality.Timid => (2, 1, 3),
            NpcPersonality.FrequentLiar => (3, 1, 2),
            NpcPersonality.Secretive => (3, 1, 2),
            NpcPersonality.Guilty => (3, 2, 1),
            NpcPersonality.BlindlyTrusting => (2, 3, 1),
            NpcPersonality.Indifferent => (2, 1, 3),
            NpcPersonality.Jealous => (2, 2, 2),
            NpcPersonality.Dependent => (2, 3, 1),
            _ => (3, 1, 2)
        };

        /// <summary>같은 시점의 정보 묶음을 한 번에 반영한다. 용의자별로 합산·반올림·제한한다.</summary>
        public void ApplySimultaneous(string observerId, NpcPersonality? personality, IEnumerable<OpinionInput> inputs)
        {
            var (evidenceWeight, claimWeight, w) = Weights(personality);
            // 자기 자신에 대한 의심은 의견 점수로 다루지 않는다(자기 지목에는 찬성하지 않는다).
            foreach (var group in inputs.Where(i => !string.IsNullOrEmpty(i.SuspectId) && i.BaseScore != 0 && i.SuspectId != observerId).GroupBy(i => i.SuspectId))
            {
                double sum = 0;
                foreach (var input in group)
                {
                    int weighted = input.BaseScore * (input.IsConfirmedEvidence ? evidenceWeight : claimWeight);
                    sum += weighted * (4 - w) / 3.0;
                }
                int delta = (int)Math.Round(sum, MidpointRounding.AwayFromZero);
                float next = Math.Max(0f, Math.Min(100f, Get(observerId, group.Key) + delta));
                _scores[(observerId, group.Key)] = next;
            }
        }
    }
}
