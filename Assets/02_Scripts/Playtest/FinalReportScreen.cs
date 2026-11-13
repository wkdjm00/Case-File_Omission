using System.Linq;
using CaseFileOmission.Game;
using CaseFileOmission.MainCase;
using UnityEngine;

namespace CaseFileOmission.Playtest
{
    /// <summary>
    /// UI-404 최종 진실 정리(03·09). 메인 사건 최종 보고서의 14개 빈칸에 단서 이름을 입력한다. 사건보드의 배치·연결 기능은 두지 않는다.
    /// 타자기 연출은 임시로 "지금까지 찍힌 문장" 표시 + 입력창 + 입력 버튼으로 대신한다(규칙은 FinalReportState가 판정).
    /// 완성되면 책상 수화기가 울리고(수화기 질문), 최종 보고서는 세상에 공개되지 않는다.
    /// </summary>
    public sealed class FinalReportScreen
    {
        private readonly PlaytestGame _g;
        private string _message = "";

        public FinalReportScreen(PlaytestGame g) => _g = g;

        private GameSession S => _g.Session;

        public void Open() => Rebuild();

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
            var paper = Ui.Scroll(Ui.Anchor(Ui.Rect(body, "Paper"), 0, 0.2f, 0.62f, 1), new Color(0.93f, 0.9f, 0.82f));
            Ui.Line(paper, report.GetTypedTextUntilCurrentStop() + (report.IsComplete ? "" : " ▌"), 21, new Color(0.1f, 0.08f, 0.06f));

            var blanks = Ui.Scroll(Ui.Anchor(Ui.Rect(body, "Blanks"), 0.63f, 0.2f, 1, 1));
            Ui.Line(blanks, $"빈칸 {report.Blanks.Count(b => b.IsFilled)}/{report.Blanks.Count}", 21);
            var current = report.CurrentGroupBlankIndices;
            foreach (var b in report.Blanks)
            {
                string state = b.IsFilled ? $"<color=#c33>{b.RevealedText}</color>" : current.Contains(b.Index) ? "▶ 지금 입력할 칸" : "□";
                Ui.Line(blanks, $"{b.Index:00}. {state}", 18);
            }
            Ui.Line(blanks, "확보한 단서: " + string.Join(", ", s.MainCase.AcquiredClueNames), 16, Ui.DimTextColor);

            var bottom = Ui.Anchor(Ui.Rect(body, "Input"), 0, 0, 1, 0.19f);
            Ui.Vertical(bottom, 8, 8);
            Ui.Line(bottom, report.IsComplete ? "보고서가 완성되었습니다. 책상 위 수화기가 울립니다." : _message, 19, new Color(1f, 0.85f, 0.5f));
            if (report.IsComplete) return;
            var row = Ui.Row(bottom, 52);
            var input = Ui.Input(row, "단서 이름을 입력 (예: 조카의 일기)", "", false, 52);
            Ui.Button(row, "입력", () =>
            {
                var outcome = s.WriteFinalReport(input.text);
                _message = OutcomeText(outcome);
                if (report.IsComplete) _g.Log("최종 보고서 완성 — 수화기 질문으로 이어진다.");
                Rebuild();
                _g.RefreshHud();
            }, true, 52, Ui.AccentColor).GetComponent<UnityEngine.UI.LayoutElement>().preferredWidth = 200;
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
