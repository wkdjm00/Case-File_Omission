using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Core;

namespace CaseFileOmission.Police
{
    public enum ContractEndKind
    {
        Resolved,
        Unsolved,
        HandlerPoliceDeceased
    }

    /// <summary>
    /// 경찰별 플레이어 의심도(방향성 값, 경찰→플레이어)와 의뢰 종료 처리 효과를 관리한다.
    /// 06페이지 20·21절 확정 규칙만 구현한다. 조직 신뢰도·NPC 간 관계 의심도와는 완전히 분리된 값이다.
    /// </summary>
    public sealed class PoliceSuspicionService
    {
        private readonly DirectionalValueStore _suspicionTowardPlayer = new DirectionalValueStore(0f);

        public float GetSuspicion(string policeId, string playerId) => _suspicionTowardPlayer.Get(policeId, playerId);

        /// <summary>저장/불러오기 전용 직접 대입. 게임플레이 중에는 ApplyBatch만 사용한다.</summary>
        public void SetSuspicion(string policeId, string playerId, float value) =>
            _suspicionTowardPlayer.Set(policeId, playerId, ProbabilityCalculator.Clamp(value, 0f, 100f));

        /// <summary>
        /// 한 번의 처리에서 발생한 모든 증감 효과를 감소 먼저, 증가 다음 순서로 합산한 뒤 한 번만
        /// 0~100으로 보정한다(06페이지 확정 처리 순서). 이벤트 판정은 이 최종값을 기준으로 해야 한다.
        /// </summary>
        public float ApplyBatch(string policeId, string playerId, IEnumerable<float> deltas)
        {
            float decreaseSum = 0f;
            float increaseSum = 0f;
            foreach (var delta in deltas)
            {
                if (delta < 0f) decreaseSum += delta;
                else increaseSum += delta;
            }

            float current = _suspicionTowardPlayer.Get(policeId, playerId);
            float result = ProbabilityCalculator.Clamp(current + decreaseSum + increaseSum, 0f, 100f);
            _suspicionTowardPlayer.Set(policeId, playerId, result);
            return result;
        }

        /// <summary>체포 엔딩 경로: 경찰 중 누구라도 플레이어 의심도가 100에 도달하면 체포 조건을 만족한다.</summary>
        public bool AnyPoliceReachedArrestThreshold(string playerId)
        {
            return PoliceRoster.AllPoliceIds.Any(policeId => GetSuspicion(policeId, playerId) >= 100f);
        }

        /// <summary>
        /// 의뢰 종료 처리. startingHandlerSuspicion은 해당 의뢰 시작 당시 담당 경찰의 의심도여야 한다
        /// (이 서비스는 의뢰 생명주기를 별도로 추적하지 않으므로 호출자가 제공해야 한다).
        /// </summary>
        public void ApplyContractEndEffects(
            string playerId,
            string handlerPoliceId,
            float startingHandlerSuspicion,
            ContractEndKind kind,
            string nextHandlerPoliceId = null)
        {
            switch (kind)
            {
                case ContractEndKind.Unsolved:
                    // 미제 사건은 의심도 회복을 적용하지 않는다(확정). 상승 효과는 별도 이벤트(차례 넘기기 등)로
                    // 이미 적용되었어야 하며, 여기서 추가로 적용하지 않는다.
                    return;

                case ContractEndKind.Resolved:
                {
                    float handlerCurrent = GetSuspicion(handlerPoliceId, playerId);
                    float decrease = handlerCurrent < 55f ? -10f : -5f;
                    float handlerFinal = ApplyBatch(handlerPoliceId, playerId, new[] { decrease });

                    if (handlerFinal < startingHandlerSuspicion)
                    {
                        foreach (var otherId in PoliceRoster.AllPoliceIds)
                        {
                            if (otherId == handlerPoliceId) continue;
                            ApplyBatch(otherId, playerId, new[] { -5f });
                        }
                    }

                    return;
                }

                case ContractEndKind.HandlerPoliceDeceased:
                {
                    foreach (var policeId in PoliceRoster.AllPoliceIds)
                        ApplyBatch(policeId, playerId, new[] { 5f });

                    if (!string.IsNullOrEmpty(nextHandlerPoliceId))
                        ApplyBatch(nextHandlerPoliceId, playerId, new[] { 10f }); // 기본 +5에 추가 +10 = 총 +15

                    return;
                }
            }
        }
    }
}
