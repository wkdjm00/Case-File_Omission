using System.Collections.Generic;
using UnityEngine;

namespace CaseFileOmission.Contracts
{
    /// <summary>
    /// 의뢰 생성 고정 데이터. 06페이지 데이터 분류 기준에 따라 ScriptableObject로 관리한다.
    /// 수치는 04페이지 2026-11-06/11-09 확정 값을 기본값으로 사용하며, 기획 변경 시 이 에셋만 수정하면 된다.
    /// </summary>
    [CreateAssetMenu(fileName = "ContractGenerationConfig", menuName = "CaseFileOmission/Contracts/Generation Config")]
    public sealed class ContractGenerationConfig : ScriptableObject
    {
        [Header("대표 사건 유형 가중치 (04페이지 2026-11-06 확정)")]
        public List<WeightedEntry<CrimeType>> PrimaryCrimeWeights = new List<WeightedEntry<CrimeType>>();

        [Header("진행 중 부수 범죄 가중치 (대표 사건 가중치와 혼용 금지)")]
        public List<WeightedEntry<CrimeType>> SubCrimeWeights = new List<WeightedEntry<CrimeType>>();

        [Header("조직 조건 부여 확률 (05페이지 확정: 40%)")]
        [Range(0f, 1f)]
        public float OrganizationConditionChance = 0.4f;

        [Header("재시도 정책 (04페이지 2026-11-09 구현 명세 확정)")]
        [Tooltip("요소 하나(시간표·거짓 자료 등)의 검증 실패 시 해당 요소 재생성 최대 횟수.")]
        public int MaxRetriesPerElement = 5;
        [Tooltip("후보 하나의 전체 시도 횟수. 넘으면 후보를 폐기하고 새 후보를 만든다.")]
        public int MaxAttemptsPerCandidate = 3;
        [Tooltip("04 2026-11-12 최종 계약(사용자 확인 2026-11-20, 이전 10개를 대체): 의뢰 후보는 최대 3개까지 검증한다. 모두 실패하면 GENERATION_FAILED.")]
        public int MaxCandidates = 3;

        [Header("임시값 — 기획 미정")]
        [Tooltip("은폐 방식이 붙을 확률. 원문은 '은폐 없음 허용'까지만 정했다.")]
        [Range(0f, 1f)]
        public float CoverUpChance = 0.5f;
        [Tooltip("의뢰가 시작되는 조사 구간(계약 데이터 설정). 사건 경위 시간표의 발견 시각과 연결하지 않는다(사용자 확정 2026-11-10 C5).")]
        public CaseFileOmission.Core.TimeBlock DefaultStartTimeBlock = CaseFileOmission.Core.TimeBlock.Morning;

        /// <summary>테스트/에디터 자산 생성에 공용으로 사용하는 확정 기본값.</summary>
        public static ContractGenerationConfig CreateDefault()
        {
            var config = CreateInstance<ContractGenerationConfig>();

            config.PrimaryCrimeWeights = new List<WeightedEntry<CrimeType>>
            {
                new WeightedEntry<CrimeType>(CrimeType.Murder, 30f),
                new WeightedEntry<CrimeType>(CrimeType.Missing, 30f),
                new WeightedEntry<CrimeType>(CrimeType.AssaultInjury, 20f),
                new WeightedEntry<CrimeType>(CrimeType.TheftRobbery, 12f),
                new WeightedEntry<CrimeType>(CrimeType.Blackmail, 8f),
            };

            config.SubCrimeWeights = new List<WeightedEntry<CrimeType>>
            {
                new WeightedEntry<CrimeType>(CrimeType.TheftRobbery, 30f),
                new WeightedEntry<CrimeType>(CrimeType.Blackmail, 30f),
                new WeightedEntry<CrimeType>(CrimeType.AssaultInjury, 20f),
                new WeightedEntry<CrimeType>(CrimeType.Murder, 10f),
                new WeightedEntry<CrimeType>(CrimeType.Missing, 10f),
            };

            config.OrganizationConditionChance = 0.4f;
            config.MaxRetriesPerElement = 5;
            config.MaxAttemptsPerCandidate = 3;
            config.MaxCandidates = 3;
            config.CoverUpChance = 0.5f;

            return config;
        }
    }
}
