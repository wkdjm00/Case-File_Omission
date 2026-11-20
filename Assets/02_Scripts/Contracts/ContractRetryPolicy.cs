using System;
using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Core;

namespace CaseFileOmission.Contracts
{
    public sealed class ContractGenerationFailedException : Exception
    {
        public ContractGenerationFailedException(string message) : base(message)
        {
        }
    }

    /// <summary>생성 결과. 모든 후보가 실패해도 예외 대신 실패 결과를 돌려줄 때 쓴다.</summary>
    public sealed class ContractGenerationResult
    {
        public ContractInstance Contract;
        public int CandidatesUsed;
        public int ValidationAttempts;
        public readonly List<ContractValidationErrorCode> LastErrors = new List<ContractValidationErrorCode>();
        public bool Success => Contract != null;
    }

    /// <summary>
    /// 의뢰 생성 재시도(04페이지 최신 사용자 확정 2026-11-20):
    /// - 의뢰 후보는 최대 3개까지 생성한다.
    /// - 후보 하나는 검증을 최대 3회 시도한다.
    /// - 3회 모두 실패하면 그 후보를 폐기하고 다음 후보를 만든다.
    /// - 같은 후보를 재시도할 때 이미 확정된 실제 진실과 사건 핵심 설정은 바꾸지 않는다.
    ///   재시도에서는 인물·시간표·단서 등 해당 단계 이후의 데이터만 다시 만든다.
    /// - 3개 후보가 모두 실패하면 조건을 완화하지 않고 GENERATION_FAILED를 반환한다.
    /// </summary>
    public sealed class ContractRetryPolicy
    {
        private readonly ContractGenerator _generator;
        private readonly IContractValidator _validator;
        private readonly int _maxAttemptsPerCandidate;
        private readonly int _maxCandidates;
        private readonly GameLogger _logger;

        public ContractRetryPolicy(
            ContractGenerator generator,
            IContractValidator validator,
            int maxAttemptsPerCandidate = 3,
            int maxCandidates = 3,
            GameLogger logger = null)
        {
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            if (maxAttemptsPerCandidate < 1) throw new ArgumentOutOfRangeException(nameof(maxAttemptsPerCandidate));
            if (maxCandidates < 1) throw new ArgumentOutOfRangeException(nameof(maxCandidates));
            _maxAttemptsPerCandidate = maxAttemptsPerCandidate;
            _maxCandidates = maxCandidates;
            _logger = logger;
        }

        /// <summary>폐기된 후보 수. 진단용.</summary>
        public int DiscardedCandidates { get; private set; }

        public ContractInstance GenerateValidCandidate(
            Func<string> contractIdFactory,
            OrganizationConditionType previousConditionType = OrganizationConditionType.None) =>
            GenerateValidCandidate(contractIdFactory, c => _validator.Validate(c), previousConditionType);

        /// <summary>실패하면 <see cref="ContractGenerationFailedException"/>을 던진다.</summary>
        public ContractInstance GenerateValidCandidate(
            Func<string> contractIdFactory,
            Func<ContractInstance, ContractValidationResult> buildAndValidate,
            OrganizationConditionType previousConditionType = OrganizationConditionType.None)
        {
            var result = TryGenerate(contractIdFactory, buildAndValidate, previousConditionType);
            if (result.Success)
                return result.Contract;

            throw new ContractGenerationFailedException(
                $"후보 {_maxCandidates}개를 각각 {_maxAttemptsPerCandidate}회 시도했지만 검증을 통과하지 못했습니다: {Describe(result.LastErrors)}");
        }

        /// <summary>
        /// buildAndValidate는 한 번의 시도에서 후보 위에 인물·시간표·단서 등을 만들고 전체 검증 결과를 돌려준다.
        /// 실패해도 예외를 던지지 않고 실패 결과(Contract == null)를 돌려준다.
        /// </summary>
        public ContractGenerationResult TryGenerate(
            Func<string> contractIdFactory,
            Func<ContractInstance, ContractValidationResult> buildAndValidate,
            OrganizationConditionType previousConditionType = OrganizationConditionType.None)
        {
            if (contractIdFactory == null) throw new ArgumentNullException(nameof(contractIdFactory));
            if (buildAndValidate == null) throw new ArgumentNullException(nameof(buildAndValidate));

            var outcome = new ContractGenerationResult();

            for (int candidateIndex = 0; candidateIndex < _maxCandidates; candidateIndex++)
            {
                outcome.CandidatesUsed++;
                var candidate = _generator.CreateCandidate(contractIdFactory(), previousConditionType);

                for (int attempt = 1; attempt <= _maxAttemptsPerCandidate; attempt++)
                {
                    outcome.ValidationAttempts++;
                    var result = buildAndValidate(candidate);
                    if (result.IsValid)
                    {
                        outcome.Contract = candidate;
                        return outcome;
                    }

                    outcome.LastErrors.Clear();
                    outcome.LastErrors.AddRange(result.Errors.Distinct());
                }

                DiscardedCandidates++;
                _logger?.LogError("CONTRACT_CANDIDATE_DISCARDED", "Contracts", GamePhase.Idle,
                    $"후보가 {_maxAttemptsPerCandidate}회 시도 후에도 검증을 통과하지 못해 폐기했습니다: {Describe(outcome.LastErrors)}",
                    recoverable: true, contractId: candidate.ContractId);
            }

            _logger?.LogError("CONTRACT_GENERATION_FAILED", "Contracts", GamePhase.Idle,
                $"후보 {_maxCandidates}개가 모두 폐기되었습니다: {Describe(outcome.LastErrors)}", recoverable: true);
            return outcome;
        }

        private static string Describe(List<ContractValidationErrorCode> errors) =>
            errors == null || errors.Count == 0 ? "(오류 코드 없음)" : string.Join(", ", errors);
    }
}
