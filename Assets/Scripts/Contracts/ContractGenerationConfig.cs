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
        [Tooltip("요소 하나의 검증 실패 시 해당 요소 재생성 최대 횟수. 현재 1단계 구현은 후보 단위 재시도만 적용하며, " +
                 "요소 단위 부분 재생성 로직은 생성기가 필드별 재생성 훅을 갖춘 뒤 별도로 구현한다.")]
        public int MaxRetriesPerElement = 5;
        public int MaxAttemptsPerCandidate = 3;

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

            return config;
        }
    }
}
