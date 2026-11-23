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

        // 종이 위 글자색(임시 시안 이미지가 밝은 종이라 어두운 잉크색을 쓴다).
        private static readonly Color Ink = new Color(0.16f, 0.12f, 0.09f);
        private static readonly Color InkDim = new Color(0.38f, 0.32f, 0.26f);
        private static readonly Color InkRed = new Color(0.55f, 0.12f, 0.08f);
        private static readonly Color InkGreen = new Color(0.12f, 0.40f, 0.16f);
        private static readonly Color InkBlue = new Color(0.12f, 0.24f, 0.45f);

        private static RectTransform Sheet(Transform parent, string name, Texture2D tex, float minX, float minY, float maxX, float maxY, Color fallback)
        {
            var rt = Ui.Anchor(Ui.Rect(parent, name), minX, minY, maxX, maxY);
            if (tex != null) rt.gameObject.AddComponent<RawImage>().texture = tex;
            else rt.gameObject.AddComponent<Image>().color = fallback;
            return rt;
        }

        /// <summary>
        /// 임시 시안 이미지로 꾸민 보고서(사용자 제작 `UI/Report` 이미지 — 최종 자산 아님): 어두운 커버 위에 왼쪽은 단서 목록 종이(선택/미선택 띠)와
        /// 사진(단서 이미지)·설명 노트, 오른쪽은 보고서 작성 종이. 기능(자료 보관·문단 제출·검증)은 이전 임시 UI와 같다.
        /// </summary>
        private void Rebuild()
        {
            _g.CloseWindow();
            var s = S;
            var art = _g.Art ?? new FieldArt();
            var report = s.Report;

            var root = Ui.Stretch(Ui.Rect(_g.WindowLayer, "Window:의뢰 보고서"));
            var dim = root.gameObject.AddComponent<Image>();
            dim.color = new Color(0, 0, 0, 0.7f);
            var book = Sheet(root, "Cover", art.ReportCover, 0.03f, 0.03f, 0.97f, 0.97f, Ui.PanelColor);
            var close = Ui.Button(root, "닫기", () => { _g.CloseWindow(); _g.RefreshHud(); }, true, 40, Ui.AccentColor);
            Ui.Anchor((RectTransform)close.transform, 0.88f, 0.92f, 0.97f, 0.97f);
            // 사용자 지정 2026-11-20: 단서 조작·버리기는 보고서에서 한다(임시 버튼 — 배치는 나중에 받은 이미지대로).
            if (S.CurrentContract != null && S.State.CurrentPhase == Core.GamePhase.Investigation)
            {
                var manage = Ui.Button(root, "단서 조작·버리기", () => { _g.CloseWindow(); _g.OpenWindow(OpenManipulation); }, true, 40);
                Ui.Anchor((RectTransform)manage.transform, 0.72f, 0.92f, 0.87f, 0.97f);
            }
            var title = Ui.Label(root, "의뢰 보고서", 30, new Color(0.92f, 0.86f, 0.72f), TextAnchor.MiddleLeft);
            Ui.Anchor(title.rectTransform, 0.06f, 0.92f, 0.5f, 0.97f);
            if (report == null) { Ui.Line(book, "진행 중인 의뢰가 없습니다."); return; }
            s.RefreshReportUnlocks();

            // 왼쪽 위: 단서 목록 종이 + 선택/미선택 띠
            var listPaper = Sheet(book, "ClueList", art.ReportListPaper, 0.04f, 0.47f, 0.47f, 0.92f, new Color(0.85f, 0.8f, 0.68f));
            var list = Ui.Scroll(Ui.Anchor(Ui.Rect(listPaper, "Scroll"), 0.06f, 0.06f, 0.94f, 0.94f), new Color(0, 0, 0, 0));
            Ui.Line(list, "보관 자료 (이 의뢰에서 확보한 것)", 22, Ink);
            var clues = s.Clues.GetAllClueIds(s.CurrentContract.ContractId);
            if (clues.Count == 0) Ui.Line(list, "(아직 없음 — 현장에서 조사·대화·엿듣기로 얻습니다)", 18, InkDim);
            foreach (var id in clues)
            {
                string cid = id;
                ClueStrip(list, art, PlaytestTexts.MaterialTitle(s, cid), cid == _selectedClue, () => { _selectedClue = cid; Rebuild(); });
            }

            // 왼쪽 아래: 사진(단서 이미지) + 설명 노트
            var photo = Sheet(book, "CluePhoto", art.ReportPhoto, 0.04f, 0.05f, 0.19f, 0.45f, new Color(0.85f, 0.8f, 0.68f));
            var notebook = Sheet(book, "ClueDetail", art.ReportDetail, 0.19f, 0.04f, 0.48f, 0.46f, new Color(0.85f, 0.8f, 0.68f));
            var detail = Ui.Scroll(Ui.Anchor(Ui.Rect(notebook, "Scroll"), 0.08f, 0.08f, 0.92f, 0.92f), new Color(0, 0, 0, 0));
            if (_selectedClue != null && s.Clues.Exists(_selectedClue))
            {
                var record = s.Clues.GetRecord(_selectedClue);
                var m = PlaytestTexts.FindMaterial(s, _selectedClue);
                var tex = art.ClueImageFor(m?.Title ?? PlaytestTexts.MaterialTitle(s, _selectedClue), s.Clues.GetDefinition(_selectedClue).Format);
                if (tex != null)
                {
                    // 사진 틀의 검은 칸 안에 단서 이미지를 넣는다.
                    var pic = Ui.Anchor(Ui.Rect(photo, "Image"), 0.17f, 0.30f, 0.83f, 0.82f);
                    var raw = pic.gameObject.AddComponent<RawImage>();
                    raw.texture = tex;
                    pic.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                    pic.GetComponent<AspectRatioFitter>().aspectRatio = (float)tex.width / tex.height;
                }
                Ui.Line(detail, PlaytestTexts.MaterialTitle(s, _selectedClue), 21, Ink);
                Ui.Line(detail, PlaytestTexts.MaterialBody(s, _selectedClue), 18, Ink);
                Ui.Line(detail, "출처: " + PlaytestTexts.MaterialSource(s, _selectedClue), 16, InkDim);
                if (record.WorldState != ClueWorldState.HeldByPlayer) Ui.Line(detail, "현장에 내려놓음(보고서 기록은 유지)", 16, InkDim);
                if (record.ConfirmedByPoliceIds.Count > 0) Ui.Line(detail, "담당 경찰이 내용을 확인한 자료", 16, InkRed);
            }
            else Ui.Line(detail, "목록에서 자료를 고르면 사진과 내용이 보입니다.", 18, InkDim);

            // 오른쪽: 보고서 작성 종이(문단·질문·핵심 문장 제출)
            var writing = Sheet(book, "Writing", art.ReportWriting, 0.52f, 0.04f, 0.96f, 0.92f, new Color(0.85f, 0.8f, 0.68f));
            var paragraphs = Ui.Scroll(Ui.Anchor(Ui.Rect(writing, "Scroll"), 0.08f, 0.05f, 0.92f, 0.95f), new Color(0, 0, 0, 0));
            Ui.Line(paragraphs, "각 문단의 질문에 대한 핵심 문장을 직접 써서 제출하세요. 확보한 자료와 맞아야 완료됩니다(재제출 제한 없음).", 17, InkDim);
            var held = new HashSet<string>(clues);
            foreach (var target in s.GetReportTargets())
            {
                var slot = report.GetOrAddSlot(target.Index);
                if (!slot.Unlocked)
                {
                    Ui.Line(paragraphs, $"문단 {target.Index + 1} — 잠김 (관련 자료를 더 찾거나 앞 문단을 완성하면 열립니다)", 19, InkDim);
                    continue;
                }

                Ui.Line(paragraphs, $"문단 {target.Index + 1} · {target.Category}", 23, Ink);
                Ui.Line(paragraphs, target.CommonText, 18, Ink);
                // 조건부 문장: 이 문단의 증거 묶음에 들어 있는 자료 중 실제로 보유한 것만 보여 준다.
                foreach (var id in target.EvidenceGroups.SelectMany(g => g).Distinct().Where(held.Contains))
                    Ui.Line(paragraphs, "· " + PlaytestTexts.MaterialBody(s, id), 16, InkDim);
                Ui.Line(paragraphs, "질문: " + target.Question, 19, InkRed);

                if (slot.Status == ReportSlotStatus.Completed)
                {
                    Ui.Line(paragraphs, "✓ " + slot.LastAnswer, 19, InkGreen);
                    Ui.Line(paragraphs, NarrativeReportJudge.FeedbackCorrectSupported, 16, InkDim);
                    Ui.Line(paragraphs, "다음 조사 방향: " + target.FollowUpHint, 17, InkBlue);
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
                    Ui.Line(paragraphs, $"[{StatusName(slot.Status)}] {fb ?? NarrativeReportJudge.Feedback(slot.Status)}", 17, InkRed);
            }
        }

        /// <summary>단서 목록의 한 줄: 선택/미선택 종이 띠 이미지 위에 자료 이름.</summary>
        private static void ClueStrip(Transform parent, FieldArt art, string label, bool selected, UnityEngine.Events.UnityAction onClick)
        {
            var rt = Ui.Rect(parent, "Clue:" + label);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 46;
            var tex = selected ? art.ReportItemSelected : art.ReportItem;
            Graphic g;
            if (tex != null) g = rt.gameObject.AddComponent<RawImage>();
            else g = rt.gameObject.AddComponent<Image>();
            if (g is RawImage raw) raw.texture = tex;
            else g.color = selected ? new Color(0.8f, 0.7f, 0.5f) : new Color(0.9f, 0.86f, 0.76f);
            if (selected && tex == null) g.color = new Color(0.8f, 0.7f, 0.5f);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = g;
            button.onClick.AddListener(onClick);
            var text = Ui.Label(rt, (selected ? "▶ " : "") + label, 18, selected ? InkRed : Ink, TextAnchor.MiddleLeft);
            Ui.Stretch(text.rectTransform, 18, 2, 12, 2);
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
            var body = Ui.Window(_g.WindowLayer, "단서 조작·버리기", () => { _g.CloseWindow(); _g.RefreshHud(); }, out _, 0.25f, 0.15f, 0.75f, 0.85f);
            var list = Ui.Scroll(Ui.Stretch(Ui.Rect(body, "List")));
            var held = s.Clues.GetHeldByPlayerClueIds(s.CurrentContract.ContractId);
            bool canAct = !s.State.IsTimeBlockEnded;

            // 06 §8 단서 버리기(15분, 지금 있는 방에 남음, 보고서 기록 유지). 다시 줍기는 현장에서 E.
            Ui.Line(list, "— 단서 버리기 (15분 · 지금 있는 방에 남는다 · 경찰 앞이면 발각) —", 18, Ui.DimTextColor);
            if (held.Count == 0) Ui.Line(list, "(가진 단서가 없다)", 18, Ui.DimTextColor);
            foreach (var id in held)
            {
                string cid = id;
                Ui.Button(list, "버린다 — " + PlaytestTexts.MaterialTitle(s, cid), () =>
                {
                    var r = s.DropClue(cid);
                    _g.Toast(r.Success ? "단서를 내려놓았다(보고서 기록은 남는다)." : "버릴 수 없다: " + r.Failure);
                    _g.CloseWindow();
                    _g.AfterStateChange();
                }, canAct, 40, null, 16);
            }

            Ui.Line(list, " ", 8);
            Ui.Line(list, "— 물리적 증거·기록 조작 —", 18, Ui.DimTextColor);
            if (!s.IsPhysicalManipulationUnlocked) { Ui.Line(list, "잠김: 최종 보고서 완성 + 이번 의뢰 첫 회의 이후", 18, Ui.DimTextColor); return; }
            Ui.Line(list, "보유한 물리적 증거만 대상. 성공 70% 고정. 성공·실패 모두 발각되지 않는다. 시도 1시간.", 18, Ui.DimTextColor);
            var physical = held.Where(id => s.Clues.GetDefinition(id).Format == ClueFormat.PhysicalObject).ToList();
            if (physical.Count == 0) Ui.Line(list, "(보유한 물리적 증거가 없다)", 18, Ui.DimTextColor);
            foreach (var id in physical)
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
