using System;
using System.Collections.Generic;
using System.Linq;

namespace CaseFileOmission.MainCase
{
    /// <summary>
    /// 03페이지 확정 5단계 진행을 관리한다. 각 단계는 해당 단계에 속한 단서를 모두 확보해야
    /// 다음 단계로 넘어간다. 설계자의 진실(5단계)은 필요한 단서가 없으므로(§확정) 최종 보고서
    /// 완성을 별도 조건으로 둔다 — 이 트래커는 1~4단계 전환만 자동으로 처리한다.
    /// </summary>
    public sealed class MainCaseProgressTracker
    {
        public MainCaseTruthStage CurrentStage { get; private set; } = MainCaseTruthStage.PersonalTruth;

        private readonly HashSet<string> _acquiredClueNames = new HashSet<string>();
        private readonly Dictionary<string, int> _acquiredAtCompletedContractCount = new Dictionary<string, int>();

        /// <summary>지금까지 종료된 의뢰 수. "번호 쪽지 확보 후 다른 사건 2건이 더 진행된 경우" 같은 조건 판정에 쓴다.</summary>
        public int CompletedContractCount { get; private set; }

        public void AcquireClue(string clueName)
        {
            if (string.IsNullOrEmpty(clueName))
                throw new ArgumentException("clueName은 비어 있을 수 없습니다.", nameof(clueName));

            if (_acquiredClueNames.Add(clueName))
                _acquiredAtCompletedContractCount[clueName] = CompletedContractCount;
        }

        /// <summary>의뢰가 종료될 때마다 한 번 호출한다.</summary>
        public void RecordContractCompleted() => CompletedContractCount++;

        /// <summary>해당 단서를 확보한 뒤 종료된 의뢰 수. 확보하지 않았으면 null.</summary>
        public int? ContractsCompletedSinceAcquired(string clueName) =>
            _acquiredAtCompletedContractCount.TryGetValue(clueName, out int at) ? CompletedContractCount - at : (int?)null;

        /// <summary>저장/불러오기 전용.</summary>
        public IReadOnlyDictionary<string, int> AcquiredAtCompletedContractCount => _acquiredAtCompletedContractCount;

        /// <summary>저장/불러오기 전용. 확보 시점을 함께 복원한다.</summary>
        public void RestoreAcquisition(string clueName, int acquiredAtCompletedContractCount)
        {
            _acquiredClueNames.Add(clueName);
            _acquiredAtCompletedContractCount[clueName] = acquiredAtCompletedContractCount;
        }

        /// <summary>저장/불러오기 전용.</summary>
        public void RestoreCompletedContractCount(int count) => CompletedContractCount = count;

        public bool HasAcquired(string clueName) => _acquiredClueNames.Contains(clueName);

        /// <summary>저장/불러오기 등 외부 조회용. 내부 HashSet을 직접 노출하지 않는다.</summary>
        public IReadOnlyCollection<string> AcquiredClueNames => _acquiredClueNames;

        private static IReadOnlyCollection<string> GetRequiredClues(MainCaseTruthStage stage) => stage switch
        {
            MainCaseTruthStage.PersonalTruth => MainCaseClueNames.PersonalTruthClues,
            MainCaseTruthStage.SystemicTruth => MainCaseClueNames.SystemicTruthClues,
            MainCaseTruthStage.PowerTruth => MainCaseClueNames.PowerTruthClues,
            MainCaseTruthStage.ExperimentTruth => MainCaseClueNames.ExperimentTruthClues,
            _ => Array.Empty<string>()
        };

        public bool CurrentStageRequirementsMet() =>
            GetRequiredClues(CurrentStage).All(clue => _acquiredClueNames.Contains(clue));

        /// <summary>
        /// 현재 단계의 필요한 단서를 모두 확보했으면 다음 단계로 넘어간다. 설계자의 진실(5단계)
        /// 이후에는 더 진행할 단계가 없으므로 그대로 둔다.
        /// </summary>
        public bool TryAdvanceStage()
        {
            if (CurrentStage == MainCaseTruthStage.DesignerTruth)
                return false;

            if (!CurrentStageRequirementsMet())
                return false;

            CurrentStage = (MainCaseTruthStage)((int)CurrentStage + 1);
            return true;
        }

        /// <summary>저장/불러오기 전용. 요구 단서 충족 여부를 다시 계산하지 않고 저장된 단계로 직접 복원한다.</summary>
        public void RestoreStage(MainCaseTruthStage stage) => CurrentStage = stage;
    }
}
