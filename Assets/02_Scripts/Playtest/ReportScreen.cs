using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Evidence;
using CaseFileOmission.Game;
using CaseFileOmission.Reports;
using UnityEngine;
using UnityEngine.UI;

namespace CaseFileOmission.Playtest
{
    /// <summary>
    /// 의뢰 보고서(04 V-13·「무작위」 2026-11-12 확정). 이 의뢰에서 확보한 자료를 모두 보관·열람하고(사건보드에서 버려도 남는다),
    /// 공개된 문단마다 핵심 문장을 자연어로 써서 제출한다. 시스템이 내용 정확성과 획득 증거 충분성을 따로 검증하고,
    /// 둘 다 통과해야 완료·후속 조사 방향 공개. 수동 증거 연결 UI·단서 이름 입력은 없다. 메인 사건 타자기 보고서(UI-404)와 다른 화면이다.
    /// </summary>
    public sealed class ReportScreen
    {
        private readonly PlaytestGame _g;
        private string _selectedClue;
        private readonly Dictionary<int, string> _drafts = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _feedback = new Dictionary<int, string>();

        public ReportScreen(PlaytestGame g) => _g = g;

        private GameSession S => _g.Session;

        public void Open() => Rebuild();

        private void Rebuild()
        {
            _g.CloseWindow();
            var s = S;
            var report = s.Report;
            var body = Ui.Window(_g.WindowLayer, "의뢰 보고서", () => { _g.CloseWindow(); _g.RefreshHud(); }, out _);
            if (report == null) { Ui.Line(body, "진행 중인 의뢰가 없습니다."); return; }
            s.RefreshReportUnlocks();

            // 왼쪽: 보관 자료 목록과 상세
            var list = Ui.Scroll(Ui.Anchor(Ui.Rect(body, "Materials"), 0, 0.42f, 0.33f, 1));
            Ui.Line(list, "보관 자료 (이 의뢰에서 확보한 것)", 21);
            var clues = s.Clues.GetAllClueIds(s.CurrentContract.ContractId);
            if (clues.Count == 0) Ui.Line(list, "(아직 없음 — 현장에서 조사·대화·엿듣기로 얻습니다)", 18, Ui.DimTextColor);
            foreach (var id in clues)
            {
                string cid = id;
                Ui.Button(list, PlaytestTexts.MaterialTitle(s, cid), () => { _selectedClue = cid; Rebuild(); }, true, 46,
                    cid == _selectedClue ? Ui.AccentColor : (Color?)null, 17);
            }

            var detail = Ui.Scroll(Ui.Anchor(Ui.Rect(body, "Detail"), 0, 0, 0.33f, 0.41f));
            if (_selectedClue != null && s.Clues.Exists(_selectedClue))
            {
                var record = s.Clues.GetRecord(_selectedClue);
                Ui.Line(detail, PlaytestTexts.MaterialTitle(s, _selectedClue), 21);
                Ui.Line(detail, PlaytestTexts.MaterialBody(s, _selectedClue), 19);
                Ui.Line(detail, "출처: " + PlaytestTexts.MaterialSource(s, _selectedClue), 17, Ui.DimTextColor);
                if (record.WorldState != ClueWorldState.HeldByPlayer) Ui.Line(detail, "현장에 내려놓음(보고서 기록은 유지)", 17, Ui.DimTextColor);
                if (record.ConfirmedByPoliceIds.Count > 0) Ui.Line(detail, "담당 경찰이 내용을 확인한 자료", 17, new Color(1f, 0.7f, 0.4f));
            }
            else Ui.Line(detail, "자료를 선택하면 내용이 보입니다.", 18, Ui.DimTextColor);

            // 오른쪽: 문단(보고서 목표)
            var paragraphs = Ui.Scroll(Ui.Anchor(Ui.Rect(body, "Paragraphs"), 0.34f, 0, 1, 1));
            Ui.Line(paragraphs, "각 문단의 질문에 대한 핵심 문장을 직접 써서 제출하세요. 확보한 자료와 맞아야 완료됩니다(재제출 제한 없음).", 17, Ui.DimTextColor);
            var held = new HashSet<string>(clues);
            foreach (var target in s.GetReportTargets())
            {
                var slot = report.GetOrAddSlot(target.Index);
                if (!slot.Unlocked)
                {
                    Ui.Line(paragraphs, $"문단 {target.Index + 1} — 잠김 (관련 자료를 더 찾거나 앞 문단을 완성하면 열립니다)", 19, Ui.DimTextColor);
                    continue;
                }

                Ui.Line(paragraphs, $"문단 {target.Index + 1} · {target.Category}", 22);
                Ui.Line(paragraphs, target.CommonText, 18);
                // 조건부 문장: 이 문단의 증거 묶음에 들어 있는 자료 중 실제로 보유한 것만 보여 준다.
                foreach (var id in target.EvidenceGroups.SelectMany(g => g).Distinct().Where(held.Contains))
                    Ui.Line(paragraphs, "· " + PlaytestTexts.MaterialBody(s, id), 16, new Color(0.8f, 0.85f, 0.75f));
                Ui.Line(paragraphs, "질문: " + target.Question, 19, new Color(1f, 0.9f, 0.6f));

                if (slot.Status == ReportSlotStatus.Completed)
                {
                    Ui.Line(paragraphs, "✓ " + slot.LastAnswer, 19, new Color(0.6f, 1f, 0.6f));
                    Ui.Line(paragraphs, NarrativeReportJudge.FeedbackCorrectSupported, 16, Ui.DimTextColor);
                    Ui.Line(paragraphs, "다음 조사 방향: " + target.FollowUpHint, 17, new Color(0.7f, 0.85f, 1f));
                    continue;
                }

                int index = target.Index;
                _drafts.TryGetValue(index, out var draft);
                var input = Ui.Input(paragraphs, "핵심 문장을 쓰세요", draft ?? slot.LastAnswer ?? "", true, 70);
                input.onValueChanged.AddListener(v => _drafts[index] = v);
                Ui.Button(paragraphs, "제출", () =>
                {
                    var status = s.SubmitReportAnswer(index, input.text);
                    _feedback[index] = NarrativeReportJudge.Feedback(status);
                    _drafts.Remove(index);
                    _g.Log($"보고서 문단 {index + 1} 제출 → {StatusName(status)}");
                    Rebuild();
                    _g.RefreshHud();
                }, true, 44, Ui.AccentColor);
                if (_feedback.TryGetValue(index, out var fb) || slot.Status != ReportSlotStatus.Unanswered)
                    Ui.Line(paragraphs, $"[{StatusName(slot.Status)}] {fb ?? NarrativeReportJudge.Feedback(slot.Status)}", 17, new Color(1f, 0.8f, 0.5f));
            }
        }

        public static string StatusName(ReportSlotStatus s) => s switch
        {
            ReportSlotStatus.Completed => "완료",
            ReportSlotStatus.CorrectUnsupported => "근거 부족",
            ReportSlotStatus.Partial => "일부만 맞음",
            ReportSlotStatus.Incorrect => "오답",
            ReportSlotStatus.NeedsClarification => "의미 불명확",
            ReportSlotStatus.AnswerPending => "판정 보류",
            _ => "미작성"
        };

        // ───────── 물리적 증거·기록 조작 ─────────

        public void OpenManipulation()
        {
            var s = S;
            var body = Ui.Window(_g.WindowLayer, "물리적 증거·기록 조작", () => { _g.CloseWindow(); _g.RefreshHud(); }, out _, 0.25f, 0.15f, 0.75f, 0.85f);
            var list = Ui.Scroll(Ui.Stretch(Ui.Rect(body, "List")));
            Ui.Line(list, "기본 성공 70%. 성공·실패 모두 발각되지 않고 흔적도 없다. 시도 1시간.", 18, Ui.DimTextColor);
            var held = s.Clues.GetHeldByPlayerClueIds(s.CurrentContract.ContractId);
            foreach (var id in held)
            {
                string cid = id;
                Ui.Line(list, PlaytestTexts.MaterialTitle(s, cid), 19);
                var row = Ui.Row(list, 40);
                Ui.Button(row, "왜곡", () => Attempt(cid, ManipulationType.Distort));
                Ui.Button(row, "은폐", () => Attempt(cid, ManipulationType.Conceal), held.Count >= 2);
            }
        }

        private void Attempt(string clueId, ManipulationType type)
        {
            var outcome = S.AttemptPhysicalManipulation(clueId, type);
            _g.Toast("조작 결과: " + outcome.Outcome);
            _g.CloseWindow();
            _g.AfterStateChange();
        }
    }
}
