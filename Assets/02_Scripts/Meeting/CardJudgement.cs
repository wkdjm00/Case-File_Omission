using System;
using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Evidence;
using CaseFileOmission.NPC;

namespace CaseFileOmission.Meeting
{
    /// <summary>판단 결과의 근거. 화면 표시용 문구가 아니라 디버그·로그·테스트용 코드다.</summary>
    public enum CardJudgementReason
    {
        /// <summary>사망한 인물은 회의에 참여하지 않는다(§5-1 확정).</summary>
        NotParticipatingDeceased,

        /// <summary>배정된 경찰이 이미 내용을 확인한 단서에 대한 거짓말 — 100% 발각(06 §21 확정).</summary>
        ForcedDetectionJudgeKnowsClueContent,

        /// <summary>거짓말 판정(기본 50%, 모순·증거 보정, 5~95%)에서 발각됨(06 2026-11-09 확정).</summary>
        LieDetectedByRoll,

        /// <summary>거짓말 판정에서 발각되지 않음. 판정자가 사실로 믿었다는 뜻은 아니다(확정).</summary>
        LieNotDetectedByRoll,

        /// <summary>판정자가 실제로 보유한 정보와 카드가 모순된다(모순은 보유 정보로만 판정 — 확정).</summary>
        ContradictsJudgeKnowledge,

        /// <summary>제시자에 대한 의심도가 100 — 적극적으로 검증한다(06 §10 확정).</summary>
        MaximumSuspicionActiveVerification,

        /// <summary>임시 정책이 의문 제기로 판단했다(원본 기획에 수치 기준 없음).</summary>
        ProvisionalPolicyRaisedQuestion,

        /// <summary>임시 정책이 수용으로 판단했다(원본 기획에 수치 기준 없음).</summary>
        ProvisionalPolicyAccepted
    }

    /// <summary>카드 하나를 판정하는 데 필요한 입력. 실제 참·거짓은 판정자가 아니라 시스템만 안다.</summary>
    public sealed class CardJudgementInput
    {
        public string PresenterId;
        public string ClueId;

        /// <summary>제시자가 카드를 거짓으로 바꾸어 말했는지(06 §20-4 "진실대로 말할지, 거짓으로 바꾸어 말할지").</summary>
        public bool PresenterIsLying;

        /// <summary>주장을 뒷받침하는 증거가 함께 제시되었는지(거짓말 판정 +15%p).</summary>
        public bool HasSupportingEvidence;
    }

    public sealed class CardJudgement
    {
        public string JudgeId;
        public bool Participated = true;

        /// <summary>06 §20-4: 수용과 의문 제기는 동시에 표현될 수 있으므로 별도 플래그로 둔다.</summary>
        public bool Accepts;
        public bool RaisesQuestion;

        public bool LieDetected;
        public float LieProbabilityUsed;
        public ContradictionLevel Contradiction;
        public float SuspicionTowardPresenter;

        /// <summary>판단이 확정 규칙이 아니라 교체 가능한 임시 정책으로 내려졌는지.</summary>
        public bool DecidedByProvisionalPolicy;

        public readonly List<CardJudgementReason> Reasons = new List<CardJudgementReason>();

        public static CardJudgement NotParticipating(string judgeId)
        {
            var j = new CardJudgement { JudgeId = judgeId, Participated = false };
            j.Reasons.Add(CardJudgementReason.NotParticipatingDeceased);
            return j;
        }
    }

    public interface ICardJudge
    {
        string JudgeId { get; }
        CardJudgement Judge(CardJudgementInput input);
    }

    /// <summary>
    /// 발각되지 않은 카드에 대해 수용할지 의문을 제기할지 정하는 정책. 원본 기획은 "의심도에 따라 받아들이거나
    /// 의문을 제기하거나 교차 검증한다", "명백한 모순이면 의심도가 낮아도 의문 제기 가능", "의심도가 높다고 무조건
    /// 반박하지 않는다"까지만 정했고 수치 경계는 없다. 따라서 이 판단은 교체 가능한 정책 객체로 분리한다.
    /// </summary>
    public interface ICardQuestionPolicy
    {
        /// <summary>true면 원본 기획에 근거한 확정 정책이 아니다.</summary>
        bool IsProvisional { get; }

        bool ShouldRaiseQuestion(float suspicionTowardPresenter, ContradictionLevel contradiction);
    }

    /// <summary>
    /// 임시 기본 정책(확정 아님). 명확한 모순 → 의문, 약한 모순 → 의심도 단계 '높음'(35) 이상일 때 의문,
    /// 모순 없음 → 수용. 의심도 단계 경계(35)는 06 §10 단계표 "높음: 의문 제기와 검증 가능성이 커지는 상태"를 따랐다.
    /// </summary>
    public sealed class ProvisionalCardQuestionPolicy : ICardQuestionPolicy
    {
        public float WeakContradictionQuestionThreshold = 35f;

        public bool IsProvisional => true;

        public bool ShouldRaiseQuestion(float suspicionTowardPresenter, ContradictionLevel contradiction) => contradiction switch
        {
            ContradictionLevel.Clear => true,
            ContradictionLevel.Weak => suspicionTowardPresenter >= WeakContradictionQuestionThreshold,
            _ => false
        };
    }

    /// <summary>
    /// 카드(단서)와 그 카드를 반박하는 정보의 연결. "모순은 NPC가 실제로 보유한 정보와 검증 가능한 증거만으로
    /// 판정한다"(확정)를 구현하기 위해, 판정자가 보유한 정보 ID 중 이 목록에 있는 것만 모순으로 센다.
    /// 정보 ID는 단서 ID 또는 NPC 정보 ID와 같은 ID 공간을 쓴다.
    /// </summary>
    public sealed class CardContradictionIndex
    {
        private readonly Dictionary<string, Dictionary<string, ContradictionLevel>> _rebuttalsByClue =
            new Dictionary<string, Dictionary<string, ContradictionLevel>>();

        public void AddRebuttal(string clueId, string rebuttingInfoId, ContradictionLevel level)
        {
            if (string.IsNullOrEmpty(clueId)) throw new ArgumentException("clueId는 비어 있을 수 없습니다.", nameof(clueId));
            if (string.IsNullOrEmpty(rebuttingInfoId)) throw new ArgumentException("rebuttingInfoId는 비어 있을 수 없습니다.", nameof(rebuttingInfoId));
            if (level == ContradictionLevel.None) return;

            if (!_rebuttalsByClue.TryGetValue(clueId, out var map))
            {
                map = new Dictionary<string, ContradictionLevel>();
                _rebuttalsByClue[clueId] = map;
            }

            if (!map.TryGetValue(rebuttingInfoId, out var existing) || existing < level)
                map[rebuttingInfoId] = level;
        }

        public IReadOnlyCollection<string> GetRebuttingInfoIds(string clueId) =>
            _rebuttalsByClue.TryGetValue(clueId, out var map) ? map.Keys.ToList() : (IReadOnlyCollection<string>)Array.Empty<string>();

        /// <summary>판정자가 아는 정보 중 이 카드를 반박하는 것의 최대 강도.</summary>
        public ContradictionLevel Evaluate(string clueId, Func<string, bool> judgeKnows)
        {
            if (string.IsNullOrEmpty(clueId) || judgeKnows == null || !_rebuttalsByClue.TryGetValue(clueId, out var map))
                return ContradictionLevel.None;

            var level = ContradictionLevel.None;
            foreach (var pair in map)
            {
                if (pair.Value > level && judgeKnows(pair.Key))
                    level = pair.Value;
            }
            return level;
        }
    }

    /// <summary>
    /// NPC·경찰 판정자가 공유하는 판단 순서. 확정 규칙(강제 발각, 거짓말 확률 판정, 의심도 100 적극 검증)을 먼저
    /// 적용하고, 남은 "수용 vs 의문" 판단만 정책 객체에 맡긴다.
    /// </summary>
    public static class CardJudgementPipeline
    {
        public const float MaximumSuspicion = 100f;

        public static CardJudgement Run(
            string judgeId,
            CardJudgementInput input,
            float suspicionTowardPresenter,
            ContradictionLevel contradiction,
            LieDetectionService lieDetection,
            string confrontingPoliceId,
            ICardQuestionPolicy policy)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var judgement = new CardJudgement
            {
                JudgeId = judgeId,
                Contradiction = contradiction,
                SuspicionTowardPresenter = suspicionTowardPresenter
            };

            if (contradiction != ContradictionLevel.None)
                judgement.Reasons.Add(CardJudgementReason.ContradictsJudgeKnowledge);

            if (input.PresenterIsLying)
            {
                var lie = lieDetection.AttemptLie(input.ClueId, confrontingPoliceId, contradiction, input.HasSupportingEvidence);
                judgement.LieProbabilityUsed = lie.ProbabilityUsed;

                if (lie.ForcedDetectionByConfirmedClue)
                {
                    judgement.LieDetected = true;
                    judgement.Reasons.Add(CardJudgementReason.ForcedDetectionJudgeKnowsClueContent);
                    return judgement;
                }

                if (!lie.Success)
                {
                    judgement.LieDetected = true;
                    judgement.Reasons.Add(CardJudgementReason.LieDetectedByRoll);
                    return judgement;
                }

                judgement.Reasons.Add(CardJudgementReason.LieNotDetectedByRoll);
            }

            if (suspicionTowardPresenter >= MaximumSuspicion)
            {
                judgement.RaisesQuestion = true;
                judgement.Reasons.Add(CardJudgementReason.MaximumSuspicionActiveVerification);
                return judgement;
            }

            var activePolicy = policy ?? new ProvisionalCardQuestionPolicy();
            judgement.DecidedByProvisionalPolicy = activePolicy.IsProvisional;

            if (activePolicy.ShouldRaiseQuestion(suspicionTowardPresenter, contradiction))
            {
                judgement.RaisesQuestion = true;
                judgement.Reasons.Add(CardJudgementReason.ProvisionalPolicyRaisedQuestion);
            }
            else
            {
                judgement.Accepts = true;
                judgement.Reasons.Add(CardJudgementReason.ProvisionalPolicyAccepted);
            }

            return judgement;
        }
    }

    /// <summary>
    /// 일반 NPC 판정자. NPC가 실제로 아는 정보(KnownInfoIds)와 그 NPC의 제시자에 대한 의심도만 사용한다 —
    /// 실제 진실이나 다른 인물의 지식은 보지 않는다(진실/NPC 지식 분리 원칙).
    /// </summary>
    public sealed class NpcCardJudge : ICardJudge
    {
        private readonly NpcState _npc;
        private readonly NpcRelationshipStore _relationships;
        private readonly LieDetectionService _lieDetection;
        private readonly CardContradictionIndex _contradictions;
        private readonly ICardQuestionPolicy _policy;

        public NpcCardJudge(
            NpcState npc,
            NpcRelationshipStore relationships,
            LieDetectionService lieDetection,
            CardContradictionIndex contradictions,
            ICardQuestionPolicy policy = null)
        {
            _npc = npc ?? throw new ArgumentNullException(nameof(npc));
            _relationships = relationships ?? throw new ArgumentNullException(nameof(relationships));
            _lieDetection = lieDetection ?? throw new ArgumentNullException(nameof(lieDetection));
            _contradictions = contradictions ?? new CardContradictionIndex();
            _policy = policy;
        }

        public string JudgeId => _npc.NpcId;

        public CardJudgement Judge(CardJudgementInput input)
        {
            if (!_npc.IsAlive)
                return CardJudgement.NotParticipating(JudgeId);

            var contradiction = _contradictions.Evaluate(input.ClueId, _npc.Knows);
            float suspicion = _relationships.GetSuspicion(_npc.NpcId, input.PresenterId);

            // NPC는 "배정된 경찰"이 아니므로 확인 단서 강제 발각 규칙의 대상이 아니다.
            return CardJudgementPipeline.Run(JudgeId, input, suspicion, contradiction, _lieDetection, null, _policy);
        }
    }
}
