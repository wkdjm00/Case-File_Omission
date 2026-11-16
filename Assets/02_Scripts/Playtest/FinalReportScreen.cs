using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Game;
using CaseFileOmission.MainCase;
using UnityEngine;
using UnityEngine.UI;

namespace CaseFileOmission.Playtest
{
    /// <summary>
    /// UI-404 최종 진실 정리(03 「⭐ 중요 확정 사항 — 타자기 보고서 연출」, 09). 메인 사건 최종 보고서의 14개 빈칸에 단서 이름을 입력한다.
    /// 진행: 타자기가 한 글자씩 친다(<see cref="TypewriterText"/>) → 빈칸에서 멈춘다 → 핵심·함정 단어가 떠다닌다(<see cref="FloatingWords"/>) →
    /// 키보드로 단서 이름을 입력 → 맞은 단어는 사라지고, 문장에 필요한 단어를 모두 넣으면 단어들이 흐려진 뒤 사라지고
    /// 입력한 문구가 재배열되어 들어간 다음 타자기가 다음 빈칸까지 친다. 판정 규칙은 FinalReportState가 한다.
    /// 함정 단어 목록은 노션에 없어(미정) 확보한 메인 사건 단서 중 이번 칸의 정답이 아닌 이름을 임시로 쓴다(TEST_TEMP).
    /// 완성되면 책상 수화기가 울리고(수화기 질문), 최종 보고서는 세상에 공개되지 않는다.
    /// </summary>
    public sealed class FinalReportScreen
    {
        private const int MaxTrapWords = 5;

        private readonly PlaytestGame _g;
        private string _message = "";
        private int _shownLength;
        private readonly HashSet<int> _knownFilled = new HashSet<int>();
        private FloatingWords _words;
        private TypewriterText _typewriter;
        private object _reportIdentity;

        public FinalReportScreen(PlaytestGame g) => _g = g;

        private GameSession S => _g.Session;

        public void Open()
        {
            // 새 세션(다른 보고서)이면 출력 위치를 처음부터 다시 센다.
            if (!ReferenceEquals(_reportIdentity, S.FinalReport.Report))
            {
                _reportIdentity = S.FinalReport.Report;
                _shownLength = 0;
                _knownFilled.Clear();
                foreach (var b in S.FinalReport.Report.Blanks.Where(b => b.IsFilled)) _knownFilled.Add(b.Index);
            }
            Rebuild();
        }

        private void Rebuild()
        {
            _g.CloseWindow();
            var s = S;
            var body = Ui.Window(_g.WindowLayer, "UI-404 최종 진실 정리 — 최종 보고서", () => { _g.CloseWindow(); _g.RefreshHud(); }, out _);

            if (!s.FinalReport.CanWriteFinalReport)
            {
                var locked = Ui.Scroll(Ui.Stretch(Ui.Rect(body, "Locked")));
                Ui.Line(locked, "아직 최종 보고서를 쓸 수 없습니다.", 24);
                Ui.Line(locked, $"메인 사건 진행: {PlaytestGame.StageName(s.MainCase.CurrentStage)} — 1~4단계 진실을 모두 밝혀 '설계자의 진실' 단계에 도달해야 합니다.", 19, Ui.DimTextColor);
                Ui.Line(locked, "확보한 메인 사건 단서: " + (s.MainCase.AcquiredClueNames.Count == 0 ? "없음" : string.Join(", ", s.MainCase.AcquiredClueNames)), 19);
                Ui.Line(locked, "(테스트용 진입: F1 개발용 패널 → '메인 사건 단서 전부 지급')", 17, Ui.DimTextColor);
                return;
            }

            var report = s.FinalReport.Report;
            string full = report.GetTypedTextUntilCurrentStop();

            // 종이(타자기 출력) + 캐리지 표시
            var paperArea = Ui.Anchor(Ui.Rect(body, "PaperArea"), 0.03f, 0.2f, 0.97f, 0.97f);
            var carriageTrack = Ui.Anchor(Ui.Panel(paperArea, "CarriageTrack", new Color(0.2f, 0.2f, 0.22f)), 0, 0.975f, 1, 1);
            var carriage = Ui.Anchor(Ui.Panel(carriageTrack, "Carriage", new Color(0.85f, 0.75f, 0.45f)), 0, 0, 0, 1);
            carriage.offsetMin = new Vector2(-6, 0);
            carriage.offsetMax = new Vector2(6, 0);
            var paperRect = Ui.Anchor(Ui.Rect(paperArea, "Paper"), 0, 0, 1, 0.97f);
            var content = Ui.Scroll(paperRect, new Color(0.93f, 0.9f, 0.82f));
            var paperText = Ui.Line(content, "", 21, new Color(0.1f, 0.08f, 0.06f));
            paperText.supportRichText = false;
            var scroll = paperRect.GetComponent<ScrollRect>();

            _typewriter = paperText.gameObject.AddComponent<TypewriterText>();
            _typewriter.Target = paperText;
            _typewriter.Carriage = carriage;
            _typewriter.FullText = full;
            _typewriter.StartLength = Mathf.Min(_shownLength, full.Length);
            _typewriter.RevealedRanges = NewlyRevealedRanges(report);
            _typewriter.OnProgress = n =>
            {
                _shownLength = n;
                if (scroll != null) scroll.verticalNormalizedPosition = 0f;
            };
            foreach (var b in report.Blanks.Where(b => b.IsFilled)) _knownFilled.Add(b.Index);

            // 떠다니는 핵심·함정 단어(보고서 위 전체 영역, 클릭을 가로채지 않음)
            if (!report.IsComplete)
            {
                var area = Ui.Stretch(Ui.Rect(paperArea, "FloatingWords"));
                _words = area.gameObject.AddComponent<FloatingWords>();
                _words.Area = area;
                _words.Paper = paperRect;
                _words.PaperText = paperText;
                _words.Font = Ui.Font;
                _words.Build(WordsForCurrentStop(report), GameSession.StableSeed("ui404:" + report.CurrentGroupBlankIndices.FirstOrDefault()));
            }

            var bottom = Ui.Anchor(Ui.Rect(body, "Input"), 0, 0, 1, 0.19f);
            Ui.Vertical(bottom, 8, 8);
            int filled = report.Blanks.Count(b => b.IsFilled);
            Ui.Line(bottom, report.IsComplete ? "보고서가 완성되었습니다. 책상 위 수화기가 울립니다." : $"빈칸 {filled}/{report.Blanks.Count} — {_message}", 19, new Color(1f, 0.85f, 0.5f));
            if (report.IsComplete) return;
            var row = Ui.Row(bottom, 52);
            var input = Ui.Input(row, "단서 이름을 키보드로 입력 (Enter)", "", false, 52);
            void Submit()
            {
                string text = input.text;
                if (string.IsNullOrWhiteSpace(text)) return;
                var before = report.CurrentGroupBlankIndices.ToList();
                var outcome = s.WriteFinalReport(text);
                _message = OutcomeText(outcome);
                if (outcome == FillBlankOutcome.PartiallyFilled)
                {
                    // 맞은 핵심 단어만 떠다니는 영역에서 사라진다(빈칸 문장은 아직 미완성).
                    _words?.Remove(MainCaseClueNames.ResolveCanonical(text, ignoreWhitespace: true));
                    _words?.Remove(text.Trim());
                    input.text = "";
                    input.ActivateInputField();
                    return;
                }
                if (outcome == FillBlankOutcome.Filled)
                {
                    // 모든 힌트·함정 단어를 흐린 뒤 지우고, 다음 화면에서 입력한 문구를 재배열해 넣은 다음 계속 친다.
                    if (_words != null)
                    {
                        _words.transform.SetParent(_g.WindowLayer, true);
                        _words.FadeOutAndDestroy();
                        _words = null;
                    }
                    if (report.IsComplete) _g.Log("최종 보고서 완성 — 수화기 질문으로 이어진다.");
                    Rebuild();
                    _g.RefreshHud();
                    return;
                }
                input.text = "";
                Rebuild();
            }
            input.onEndEdit.AddListener(_ => { if (UnityEngine.InputSystem.Keyboard.current?.enterKey.wasPressedThisFrame == true) Submit(); });
            Ui.Button(row, "입력", Submit, true, 52, Ui.AccentColor).GetComponent<LayoutElement>().preferredWidth = 160;
            Ui.Button(row, "빨리 치기", () => _typewriter?.Skip(), true, 52).GetComponent<LayoutElement>().preferredWidth = 140;
            input.ActivateInputField();
        }

        /// <summary>이번에 처음 채워진 빈칸 문구가 본문 어디에서 시작하는지(재배열 연출 위치).</summary>
        private Dictionary<int, int> NewlyRevealedRanges(FinalReportState report)
        {
            var ranges = new Dictionary<int, int>();
            int offset = 0;
            foreach (var segment in FinalReportTemplate.Segments)
            {
                if (segment.Kind == ReportSegmentKind.Blank)
                {
                    if (!report.Blanks[segment.BlankIndex].IsFilled) break;
                    if (!_knownFilled.Contains(segment.BlankIndex) && segment.Text.Length > 0)
                        ranges[offset] = segment.Text.Length;
                }
                offset += segment.Text.Length;
            }
            return ranges;
        }

        /// <summary>지금 멈춘 빈칸의 핵심 단어(아직 입력하지 않은 정답 단서명)와 임시 함정 단어.</summary>
        private IEnumerable<string> WordsForCurrentStop(FinalReportState report)
        {
            var current = report.CurrentGroupBlankIndices.Select(i => report.Blanks[i]).ToList();
            var core = current.SelectMany(b => b.AcceptedClueNames.Where(n => !b.CollectedClueNames.Contains(n))).Distinct().ToList();
            // 진행 추적용 키(판본 구분 '#', 영문 출처 ID)는 단서 이름이 아니므로 함정 단어에서 뺀다.
            var traps = S.MainCase.AcquiredClueNames.Where(n => !core.Contains(n) && !n.Contains('#') && n.Any(ch => ch >= 0xAC00 && ch <= 0xD7A3))
                .OrderBy(n => GameSession.StableSeed("trap:" + n + current.FirstOrDefault()?.Index))
                .Take(MaxTrapWords);
            return core.Concat(traps).OrderBy(n => GameSession.StableSeed("order:" + n));
        }

        private static string OutcomeText(FillBlankOutcome o) => o switch
        {
            FillBlankOutcome.Filled => "맞았다 — 타자기가 다음 문장까지 친다.",
            FillBlankOutcome.PartiallyFilled => "이 칸에 들어갈 단서 중 하나다. 나머지도 입력하라.",
            FillBlankOutcome.AlreadyFilled => "이미 채운 칸이다.",
            FillBlankOutcome.AlreadyCollected => "이미 입력한 단서다.",
            FillBlankOutcome.IncorrectClueName => "이 칸에 맞지 않는 단서 이름이다.",
            FillBlankOutcome.ClueNotAcquired => "아직 확보하지 않은 단서다.",
            FillBlankOutcome.BlankNotConfigured => "이 칸의 정답이 설정되지 않았다(기획 데이터 필요).",
            _ => o.ToString()
        };
    }
}
