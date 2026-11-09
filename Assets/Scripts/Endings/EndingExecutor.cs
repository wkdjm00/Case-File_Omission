using System.Collections.Generic;
using CaseFileOmission.Police;

namespace CaseFileOmission.Endings
{
    /// <summary>
    /// §11-1 확정: "엔딩은 한 번만 실행한다", "엔딩이 시작되면... 사건 종료 처리를 중단한다",
    /// "이미 시작된 엔딩을 다른 조건이 다시 덮어쓰지 않는다". 기존 GameEndEvaluator(조건 판정)는
    /// 그대로 유지하고, 이 클래스가 "실행을 1회로 잠그는" 책임만 추가로 진다.
    /// </summary>
    public sealed class EndingExecutor
    {
        private readonly GameEndEvaluator _evaluator;

        public bool HasExecuted { get; private set; }
        public GameEndingType? ExecutedEnding { get; private set; }

        /// <summary>게임 종료 엔딩이 실행되어 일반 루프를 멈춰야 하면 true. 메인 서사 엔딩은 계속 진행 가능하다.</summary>
        public bool ShouldStopGameLoop =>
            HasExecuted && ExecutedEnding.HasValue && !GameEndingClassifier.ContinuesAfterEnding(ExecutedEnding.Value);

        public EndingExecutor(GameEndEvaluator evaluator)
        {
            _evaluator = evaluator;
        }

        /// <summary>
        /// 대화/선택 분기(반란군 영입, 이민, 수화기 질문 거절)처럼 상태만으로 유도할 수 없는 엔딩은
        /// 호출자가 결정한 GameEndingType을 직접 넘긴다. 이미 실행된 뒤라면 아무 효과가 없다
        /// ("다른 조건이 다시 덮어쓰지 않는다").
        /// </summary>
        public bool TryExecute(GameEndingType ending)
        {
            if (HasExecuted)
                return false;

            if (ending == GameEndingType.None)
                return false;

            HasExecuted = true;
            ExecutedEnding = ending;
            return true;
        }

        /// <summary>
        /// 상태 기반 자동 판정(체포/경찰전원제거/조직신뢰도0)을 평가하고, 결과가 있으면 즉시 실행한다.
        /// 우선순위는 GameEndEvaluator가 이미 담당한다(체포 최우선).
        /// </summary>
        public bool TryEvaluateAndExecute(string playerId, PoliceLifeStateService policeLifeState)
        {
            if (HasExecuted)
                return false;

            var ending = _evaluator.EvaluateAutomaticEndings(playerId, policeLifeState);
            return ending != GameEndingType.None && TryExecute(ending);
        }

        public bool TryEvaluateAndExecute(string playerId, IReadOnlyCollection<string> deceasedPoliceIds)
        {
            if (HasExecuted)
                return false;

            var ending = _evaluator.EvaluateAutomaticEndings(playerId, deceasedPoliceIds);
            return ending != GameEndingType.None && TryExecute(ending);
        }
    }
}
