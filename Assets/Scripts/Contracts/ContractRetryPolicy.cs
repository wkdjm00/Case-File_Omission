using System;

namespace CaseFileOmission.Contracts
{
    public sealed class ContractGenerationFailedException : Exception
    {
        public ContractGenerationFailedException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// 04페이지 2026-11-09 구현 명세: 의뢰 전체 생성은 후보당 최대 3회 시도하고,
    /// 세 번째 시도까지 검증을 통과하지 못하면 후보 전체를 폐기한다.
    /// 요소 단위 최대 5회 재시도(<see cref="ContractGenerationConfig.MaxRetriesPerElement"/>)는
    /// 생성기가 필드별 부분 재생성을 지원해야 적용 가능하므로, 1단계 구현에는 포함하지 않는다.
    /// </summary>
    public sealed class ContractRetryPolicy
    {
        private readonly ContractGenerator _generator;
        private readonly IContractValidator _validator;
        private readonly int _maxAttemptsPerCandidate;

        public ContractRetryPolicy(ContractGenerator generator, IContractValidator validator, int maxAttemptsPerCandidate = 3)
        {
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _maxAttemptsPerCandidate = maxAttemptsPerCandidate;
        }

        public ContractInstance GenerateValidCandidate(Func<string> contractIdFactory)
        {
            for (int attempt = 1; attempt <= _maxAttemptsPerCandidate; attempt++)
            {
                var candidate = _generator.CreateCandidate(contractIdFactory());
                var result = _validator.Validate(candidate);
                if (result.IsValid)
                    return candidate;
            }

            throw new ContractGenerationFailedException(
                $"후보 생성이 {_maxAttemptsPerCandidate}회 시도 후에도 검증을 통과하지 못해 폐기되었습니다.");
        }
    }
}
