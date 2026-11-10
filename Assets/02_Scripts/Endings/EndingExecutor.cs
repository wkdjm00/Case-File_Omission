using System.Collections.Generic;
using CaseFileOmission.Police;

namespace CaseFileOmission.Endings
{
    /// <summary>
    /// §11-1 확정: "엔딩은 한 번만 실행한다", "이미 시작된 엔딩을 다른 조건이 다시 덮어쓰지 않는다".
    /// 10 확정: 메인 서사 엔딩(반란군 영입·보스 히든) 이후에도 의뢰 반복 진행은 계속된다.
    /// 그래서 두 종류를 따로 잠근다.
    /// - 메인 서사 엔딩: 한 번만 실행된다. 실행된 뒤에도 게임은 계속되고, 이후의 게임 종료 엔딩은 막지 않는다
    ///   (메인 엔딩 뒤 체포·신뢰도 0이 가능한지는 원문에 명시가 없어 "반복 진행이 계속된다"를 근거로 한 해석).
    /// - 게임 종료 엔딩: 한 번 실행되면 잠기며, 이후 어떤 엔딩도 실행되지 않는다.
    /// </summary>
    public sealed class EndingExecutor
    {
        private readonly GameEndEvaluator _evaluator;

        /// <summary>실행된 게임 종료 엔딩. 없으면 null.</summary>
        public GameEndingType? GameOverEnding { get; private set; }

        /// <summary>실행된 메인 서사 엔딩. 없으면 null.</summary>
        public GameEndingType? MainNarrativeEnding { get; private set; }

        /// <summary>어떤 엔딩이든 실행되었는지.</summary>
        public bool HasExecuted => GameOverEnding.HasValue || MainNarrativeEnding.HasValue;

        /// <summary>대표 엔딩: 게임 종료 엔딩이 있으면 그것, 없으면 메인 서사 엔딩.</summary>
        public GameEndingType? ExecutedEnding => GameOverEnding ?? MainNarrativeEnding;

        public bool IsGameOver => GameOverEnding.HasValue;

        /// <summary>게임 종료 엔딩이 실행되어 일반 루프를 멈춰야 하면 true. 메인 서사 엔딩은 계속 진행 가능하다.</summary>
        public bool ShouldStopGameLoop => IsGameOver;

        public EndingExecutor(GameEndEvaluator evaluator)
        {
            _evaluator = evaluator;
        }

        /// <summary>
        /// 대화/선택 분기(반란군 영입, 이민, 수화기 질문 거절)처럼 상태만으로 유도할 수 없는 엔딩은
        /// 호출자가 결정한 GameEndingType을 직접 넘긴다. 게임 종료 뒤에는 아무 효과가 없다.
        /// </summary>
        public bool TryExecute(GameEndingType ending)
        {
            if (ending == GameEndingType.None || IsGameOver)
                return false;

            if (GameEndingClassifier.ContinuesAfterEnding(ending))
            {
                if (MainNarrativeEnding.HasValue)
                    return false;
                MainNarrativeEnding = ending;
                return true;
            }

            GameOverEnding = ending;
            return true;
        }

        /// <summary>
        /// 상태 기반 자동 판정(체포/경찰전원제거/조직신뢰도0)을 평가하고, 결과가 있으면 즉시 실행한다.
        /// 우선순위는 GameEndEvaluator가 담당한다(체포 최우선).
        /// </summary>
        public bool TryEvaluateAndExecute(string playerId, PoliceLifeStateService policeLifeState, string handlerPoliceId = null)
        {
            if (IsGameOver)
                return false;

            var ending = _evaluator.EvaluateAutomaticEndings(playerId, policeLifeState, handlerPoliceId);
            return ending != GameEndingType.None && TryExecute(ending);
        }

        public bool TryEvaluateAndExecute(string playerId, IReadOnlyCollection<string> deceasedPoliceIds, string handlerPoliceId = null)
        {
            if (IsGameOver)
                return false;

            var ending = _evaluator.EvaluateAutomaticEndings(playerId, deceasedPoliceIds, handlerPoliceId);
            return ending != GameEndingType.None && TryExecute(ending);
        }
    }
}
