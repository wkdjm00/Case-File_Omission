using System;

namespace CaseFileOmission.MainCase
{
    /// <summary>
    /// 메인 사건 진행 상태 → 최종 보고서 → 수화기 질문을 연결한다.
    /// - 최종 보고서는 1~4단계 진실을 모두 밝혀 설계자의 진실 단계에 도달했을 때만 작성할 수 있다
    ///   (노션: "플레이어가 메인 사건의 진실 5단계를 모두 밝혀 최종 보고서를 완성한다", 5단계는 보고서 이후 공개).
    /// - 정답 단서는 플레이어가 확보한 단서여야 한다.
    /// - 14칸이 모두 채워지면 수화기 질문(PostReportBranching.TriggerPhoneCall)으로 이어진다.
    /// </summary>
    public sealed class MainCaseFinalReportCoordinator
    {
        private readonly MainCaseProgressTracker _tracker;
        private readonly PostReportBranching _branching;

        public FinalReportState Report { get; }

        public MainCaseFinalReportCoordinator(
            MainCaseProgressTracker tracker,
            PostReportBranching branching,
            FinalReportAnswerConfig config)
        {
            _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
            _branching = branching ?? throw new ArgumentNullException(nameof(branching));

            Report = new FinalReportState(config, _tracker.HasAcquired);
            Report.Completed += OnReportCompleted;
        }

        public bool CanWriteFinalReport => _tracker.CurrentStage == MainCaseTruthStage.DesignerTruth;

        public FillBlankOutcome SubmitInput(string committedText, bool isComposing = false)
        {
            if (!CanWriteFinalReport)
                return FillBlankOutcome.ReportLocked;

            return Report.SubmitInput(committedText, isComposing);
        }

        private void OnReportCompleted()
        {
            if (_branching.PhoneStage == PhoneCallStage.NotReached)
                _branching.TriggerPhoneCall();
        }
    }
}
