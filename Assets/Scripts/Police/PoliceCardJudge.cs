using System;
using System.Linq;
using CaseFileOmission.Evidence;
using CaseFileOmission.Meeting;

namespace CaseFileOmission.Police
{
    /// <summary>
    /// 담당 경찰 판정자. 경찰이 아는 정보는 (1) 단서 확인 요청으로 내용을 확인한 단서와 (2) 직접 보유한 단서뿐이다
    /// (06 §21: 확인한 단서의 종류와 내용을 의뢰 종료까지 기억). 제시자에 대한 의심도는 PoliceSuspicionService 값을 쓴다.
    ///
    /// 판단 유형(A·B 수사/C 통제/D 권위/E 직관)별 수용·의문 차이는 원본 기획에 서술만 있고 수치 기준이 없다.
    /// 그래서 유형별 정책을 생성자에서 주입받으며, 주입하지 않으면 모든 유형이 같은 임시 정책을 쓴다.
    /// </summary>
    public sealed class PoliceCardJudge : ICardJudge
    {
        private readonly string _policeId;
        private readonly PoliceSuspicionService _suspicion;
        private readonly PoliceLifeStateService _lifeState;
        private readonly ClueRepository _clues;
        private readonly EvidenceCapacityManager _capacity;
        private readonly LieDetectionService _lieDetection;
        private readonly CardContradictionIndex _contradictions;
        private readonly ICardQuestionPolicy _policy;

        public PoliceCardJudge(
            string policeId,
            PoliceSuspicionService suspicion,
            PoliceLifeStateService lifeState,
            ClueRepository clues,
            EvidenceCapacityManager capacity,
            LieDetectionService lieDetection,
            CardContradictionIndex contradictions,
            ICardQuestionPolicy policy = null)
        {
            if (!PoliceRoster.AllPoliceIds.Contains(policeId))
                throw new ArgumentException($"알 수 없는 경찰 ID '{policeId}'입니다.", nameof(policeId));

            _policeId = policeId;
            _suspicion = suspicion ?? throw new ArgumentNullException(nameof(suspicion));
            _lifeState = lifeState ?? throw new ArgumentNullException(nameof(lifeState));
            _clues = clues ?? throw new ArgumentNullException(nameof(clues));
            _capacity = capacity;
            _lieDetection = lieDetection ?? throw new ArgumentNullException(nameof(lieDetection));
            _contradictions = contradictions ?? new CardContradictionIndex();
            _policy = policy;
        }

        public string JudgeId => _policeId;

        public PoliceJudgementType JudgementType => PoliceRoster.JudgementTypeById[_policeId];

        /// <summary>경찰이 실제로 아는 정보인지. 확인한 단서 또는 직접 보유한 단서만 해당한다.</summary>
        public bool Knows(string infoId)
        {
            if (string.IsNullOrEmpty(infoId))
                return false;

            if (_clues.Exists(infoId) && _clues.GetRecord(infoId).ConfirmedByPoliceIds.Contains(_policeId))
                return true;

            return _capacity != null && _capacity.GetHeldClues(_policeId).Contains(infoId);
        }

        public CardJudgement Judge(CardJudgementInput input)
        {
            if (!_lifeState.IsAlive(_policeId))
                return CardJudgement.NotParticipating(JudgeId);

            var contradiction = _contradictions.Evaluate(input.ClueId, Knows);
            float suspicion = _suspicion.GetSuspicion(_policeId, input.PresenterId);

            // 경찰 ID를 넘기면 LieDetectionService가 "확인한 단서에 대한 거짓말 100% 발각"을 먼저 적용한다.
            return CardJudgementPipeline.Run(JudgeId, input, suspicion, contradiction, _lieDetection, _policeId, _policy);
        }
    }
}
