using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CaseFileOmission.MainCase
{
    public sealed class FinalReportBlank
    {
        public int Index { get; }
        public int GroupId { get; }

        /// <summary>노션 원문 그대로의 빈칸 문구(빨간 배경 표시 대상). 임의로 바꾸지 않는다.</summary>
        public string RevealedText { get; }

        public bool IsDetermined { get; internal set; }
        public BlankMatchMode MatchMode { get; internal set; } = BlankMatchMode.AnyOf;
        public string Notes { get; internal set; }

        internal readonly List<string> AcceptedNames = new List<string>();
        internal readonly HashSet<string> CollectedNames = new HashSet<string>();

        public IReadOnlyList<string> AcceptedClueNames => AcceptedNames;
        public IReadOnlyCollection<string> CollectedClueNames => CollectedNames;

        /// <summary>정답 단서명 중 첫 번째(단일 정답 칸 호환용). 미설정이면 null.</summary>
        public string RequiredClueName => AcceptedNames.Count > 0 ? AcceptedNames[0] : null;

        public bool IsFilled { get; internal set; }

        public FinalReportBlank(int index, int groupId, string revealedText)
        {
            Index = index;
            GroupId = groupId;
            RevealedText = revealedText;
        }
    }

    public enum FillBlankOutcome
    {
        Filled,
        PartiallyFilled,
        AlreadyFilled,
        AlreadyCollected,
        IncorrectClueName,
        BlankNotConfigured,
        ClueNotAcquired,
        IgnoredWhileComposing,
        ReportLocked
    }

    /// <summary>
    /// 노션 "최종 보고서 원문 및 타자기 연출"의 14개 빈칸과 진행 방식을 구현한다.
    /// - 빈칸 문구와 보고서 본문은 FinalReportTemplate(원문 그대로)에서 가져온다.
    /// - 정답 단서명 매핑은 FinalReportAnswerConfig 데이터에서 읽는다. 미결정 칸은 입력을 받지 않는다.
    /// - 타자기는 같은 문장의 빈칸 묶음에서 멈추고, 묶음이 모두 채워지면 다음 빈칸까지 이어서 출력한다.
    /// - 오답·미보유 단서·조합 중 입력은 상태를 바꾸지 않는다.
    /// </summary>
    public sealed class FinalReportState
    {
        private readonly List<FinalReportBlank> _blanks = new List<FinalReportBlank>();
        private readonly Func<string, bool> _isClueAcquired;
        private readonly bool _ignoreWhitespace;
        private bool _completionRaised;

        public IReadOnlyList<FinalReportBlank> Blanks => _blanks;

        public bool IsComplete => _blanks.All(b => b.IsFilled);

        /// <summary>14칸이 모두 채워지는 순간 한 번만 발생한다(저장 데이터 복원 시에는 발생하지 않는다).</summary>
        public event Action Completed;

        /// <summary>매핑 없이 생성한다. 각 칸은 ConfigureBlankAnswer로 직접 설정하기 전까지 미설정 상태다.</summary>
        public FinalReportState()
        {
            foreach (var segment in FinalReportTemplate.Blanks)
                _blanks.Add(new FinalReportBlank(segment.BlankIndex, segment.GroupId, segment.Text));
        }

        /// <summary>
        /// 데이터 자산의 매핑을 적용해 생성한다. isClueAcquired를 넘기면 플레이어가 확보하지 않은 단서명은
        /// 정답이어도 받아들이지 않는다(노션: "확보한 단서들을 바탕으로 빈칸을 하나씩 완성한다").
        /// </summary>
        public FinalReportState(FinalReportAnswerConfig config, Func<string, bool> isClueAcquired = null)
            : this()
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            _ignoreWhitespace = config.IgnoreWhitespaceInComparison;
            _isClueAcquired = isClueAcquired;

            foreach (var entry in config.Entries)
            {
                if (entry.BlankIndex < 0 || entry.BlankIndex >= _blanks.Count)
                    continue;

                var blank = _blanks[entry.BlankIndex];
                blank.Notes = entry.Notes;
                if (!entry.IsDetermined || entry.AcceptedClueNames == null || entry.AcceptedClueNames.Count == 0)
                    continue;

                blank.IsDetermined = true;
                blank.MatchMode = entry.MatchMode;
                blank.AcceptedNames.AddRange(entry.AcceptedClueNames);
            }
        }

        public void ConfigureBlankAnswer(int index, string requiredClueName)
        {
            var blank = _blanks[index];
            blank.AcceptedNames.Clear();
            blank.CollectedNames.Clear();
            blank.MatchMode = BlankMatchMode.AnyOf;

            if (string.IsNullOrEmpty(requiredClueName))
            {
                blank.IsDetermined = false;
                return;
            }

            blank.AcceptedNames.Add(requiredClueName);
            blank.IsDetermined = true;
        }

        public IReadOnlyList<int> UndeterminedBlankIndices =>
            _blanks.Where(b => !b.IsDetermined).Select(b => b.Index).ToList();

        /// <summary>
        /// 지정한 칸에 단서명을 입력한다. 오답이면 아무 상태도 바뀌지 않는다.
        /// 노션 "최종 보고서 원문 및 타자기 연출" 2026-11-10 입력 판정 규칙: 한 단서가 여러 빈칸에 필요하면 한 번 입력한 결과를
        /// 관련된 모든 빈칸(뒤쪽 문장 포함)의 판정에 반영한다. 단일 단서 칸은 즉시 완성되고, 복수 단서 칸(7·13번 등)은 필요한
        /// 단서가 모두 입력될 때까지 기다린다. 이미 입력한 단서를 다시 입력하도록 요구하지 않는다.
        /// </summary>
        public FillBlankOutcome TryFillBlank(int index, string typedClueName)
        {
            var blank = _blanks[index];

            if (blank.IsFilled)
                return FillBlankOutcome.AlreadyFilled;

            if (!blank.IsDetermined || blank.AcceptedNames.Count == 0)
                return FillBlankOutcome.BlankNotConfigured;

            string typed = Canonical(typedClueName);
            string matched = blank.AcceptedNames.FirstOrDefault(accepted => Canonical(accepted) == typed);

            if (matched == null)
                return FillBlankOutcome.IncorrectClueName;

            if (_isClueAcquired != null && !_isClueAcquired(matched))
                return FillBlankOutcome.ClueNotAcquired;

            if (blank.CollectedNames.Contains(matched))
                return FillBlankOutcome.AlreadyCollected;

            ApplyEnteredClue(typed);
            RaiseCompletedIfNeeded();
            return blank.IsFilled ? FillBlankOutcome.Filled : FillBlankOutcome.PartiallyFilled;
        }

        /// <summary>지금까지 입력되어 하나 이상의 칸에 반영된 단서명(정식 표기).</summary>
        public IReadOnlyCollection<string> EnteredClueNames =>
            new HashSet<string>(_blanks.SelectMany(b => b.CollectedNames));

        private string Canonical(string name) => MainCaseClueNames.ResolveCanonical(name, _ignoreWhitespace);

        /// <summary>입력된 단서를 그 단서를 정답으로 갖는 모든 미완성 칸에 반영하고, 조건을 채운 칸을 완성한다.</summary>
        private void ApplyEnteredClue(string canonicalName)
        {
            foreach (var other in _blanks)
            {
                if (other.IsFilled || !other.IsDetermined)
                    continue;

                string accepted = other.AcceptedNames.FirstOrDefault(a => Canonical(a) == canonicalName);
                if (accepted == null)
                    continue;

                other.CollectedNames.Add(accepted);
                bool satisfied = other.MatchMode == BlankMatchMode.AnyOf
                    || other.AcceptedNames.All(other.CollectedNames.Contains);
                if (satisfied)
                    other.IsFilled = true;
            }
        }

        /// <summary>타자기가 멈춰 있는 현재 문장의 빈칸 묶음. 모두 채워졌으면 빈 목록.</summary>
        public IReadOnlyList<int> CurrentGroupBlankIndices
        {
            get
            {
                var firstOpen = _blanks.FirstOrDefault(b => !b.IsFilled);
                if (firstOpen == null)
                    return Array.Empty<int>();

                return _blanks.Where(b => b.GroupId == firstOpen.GroupId && !b.IsFilled).Select(b => b.Index).ToList();
            }
        }

        /// <summary>
        /// 키보드 입력 확정값을 현재 문장의 빈칸 묶음에 적용한다. IME가 아직 조합 중이면(isComposing)
        /// 확정되지 않은 글자이므로 아무것도 하지 않는다 — 한글 조합 중 Enter/확정 처리 충돌 방지.
        /// </summary>
        public FillBlankOutcome SubmitInput(string committedText, bool isComposing = false)
        {
            if (isComposing)
                return FillBlankOutcome.IgnoredWhileComposing;

            var group = CurrentGroupBlankIndices;
            if (group.Count == 0)
                return FillBlankOutcome.AlreadyFilled;

            var outcome = FillBlankOutcome.IncorrectClueName;
            bool allNotConfigured = true;

            foreach (int index in group)
            {
                var result = TryFillBlank(index, committedText);
                if (result == FillBlankOutcome.Filled || result == FillBlankOutcome.PartiallyFilled)
                    return result;

                if (result != FillBlankOutcome.BlankNotConfigured)
                    allNotConfigured = false;

                if (result == FillBlankOutcome.ClueNotAcquired || result == FillBlankOutcome.AlreadyCollected)
                    outcome = result;
            }

            return allNotConfigured ? FillBlankOutcome.BlankNotConfigured : outcome;
        }

        /// <summary>
        /// 타자기가 지금까지 출력해야 할 본문. 채워진 칸은 원문 문구로 드러나고, 첫 번째 미완성 칸에서 멈춘다.
        /// </summary>
        public string GetTypedTextUntilCurrentStop()
        {
            var sb = new StringBuilder();
            foreach (var segment in FinalReportTemplate.Segments)
            {
                if (segment.Kind == ReportSegmentKind.Text)
                {
                    sb.Append(segment.Text);
                    continue;
                }

                if (!_blanks[segment.BlankIndex].IsFilled)
                    break;

                sb.Append(segment.Text);
            }

            return sb.ToString();
        }

        /// <summary>모든 칸이 채워졌을 때의 완성 문장. 아직 미완성이면 null.</summary>
        public string GetCompletedText() => IsComplete ? GetTypedTextUntilCurrentStop() : null;

        private void RaiseCompletedIfNeeded()
        {
            if (_completionRaised || !IsComplete)
                return;

            _completionRaised = true;
            Completed?.Invoke();
        }

        /// <summary>저장/불러오기 전용. 이벤트를 발생시키지 않고 진행 상태만 복원한다.</summary>
        public void RestoreProgress(int index, bool isFilled, IEnumerable<string> collectedClueNames)
        {
            var blank = _blanks[index];
            blank.CollectedNames.Clear();
            if (collectedClueNames != null)
            {
                foreach (var name in collectedClueNames)
                    blank.CollectedNames.Add(name);
            }

            blank.IsFilled = isFilled;
            if (IsComplete)
                _completionRaised = true;
        }
    }
}
