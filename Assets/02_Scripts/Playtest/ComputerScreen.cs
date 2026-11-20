using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.ContractContent;
using CaseFileOmission.Game;
using CaseFileOmission.MainCase;
using CaseFileOmission.NPC;
using UnityEngine;
using UnityEngine.UI;

namespace CaseFileOmission.Playtest
{
    /// <summary>
    /// 컴퓨터 UI(09): 메인 화면(상단 검색창 + 사건 목록) → 사건 선택 시 보고서 폴더 창·인물 정보 창 → 자료 내용 창(최대 2개 비교, 창마다 상단 검색).
    /// 인물 이름은 ID를 찾기 전 ????????. 인물 정보를 올바르게 특정하면 ID를 얻고, ID를 검색해도 해금된다.
    /// 문서 본문·사건명은 TEST_TEMP(정식 사건명·보고서명은 미정).
    /// </summary>
    public sealed class ComputerScreen
    {
        private sealed class Doc
        {
            public string Id, Title, Body;
        }

        private sealed class CaseEntry
        {
            public string Name;
            public List<Doc> Docs = new List<Doc>();
            public List<string> PersonIds = new List<string>();
        }

        private readonly PlaytestGame _g;
        private readonly HashSet<string> _unlockedPersonCodes = new HashSet<string>();
        private readonly Dictionary<string, (string occupation, string cause, string tool)> _personGuesses =
            new Dictionary<string, (string, string, string)>();
        private readonly List<string> _openDocIds = new List<string>();
        private int _selectedCase = -1;
        private string _selectedPerson;
        private string _message = "";

        // 04의 고정 선택지 ID/표시명을 사용해 실제 사건 데이터와 같은 값으로 비교한다.
        private const string NoDeathProfile = "생존 / 사망 원인 없음";
        private static readonly string[] CauseOptions = CaseCatalog.Methods
            .Where(m => new[] { "METHOD-01", "METHOD-02", "METHOD-03", "METHOD-05", "METHOD-06", "METHOD-07" }.Contains(m.Id))
            .Select(m => m.Name)
            .Concat(new[] { NoDeathProfile })
            .ToArray();
        private static readonly string[] ToolOptions = CaseCatalog.Tools.Select(t => t.Name).ToArray();
        private static readonly string[] OccupationOptions = CaseCatalog.Occupations.ToArray();

        public ComputerScreen(PlaytestGame g) => _g = g;

        private GameSession S => _g.Session;

        public void Open() => Rebuild();

        /// <summary>컴퓨터 화면만 도트 폰트(Ui.ComputerFont)로 그린다. 나머지 화면은 기본 폰트를 쓴다.</summary>
        private void Rebuild()
        {
            var normal = Ui.Font;
            if (Ui.ComputerFont != null) Ui.Font = Ui.ComputerFont;
            try { RebuildCore(); }
            finally { Ui.Font = normal; }
        }

        private void RebuildCore()
        {
            _g.CloseWindow();
            var body = Ui.Window(_g.WindowLayer, "컴퓨터", () => { _g.CloseWindow(); _g.RefreshHud(); }, out _);
            var cases = BuildCases();

            // 메인 화면: 상단 검색창 + 사건 목록
            var top = Ui.Anchor(Ui.Rect(body, "Search"), 0, 0.92f, 1, 1);
            Ui.Horizontal(top, 8);
            var search = Ui.Input(top, "ID 검색 (문서 ID·인물 ID)");
            Ui.Button(top, "검색", () => Search(search.text, cases)).GetComponent<LayoutElement>().preferredWidth = 160;

            var msg = Ui.Label(body, _message, 19, new Color(1f, 0.85f, 0.5f));
            Ui.Anchor(msg.rectTransform, 0, 0.87f, 1, 0.915f);

            var list = Ui.Scroll(Ui.Anchor(Ui.Rect(body, "Cases"), 0, 0, 0.17f, 0.86f));
            Ui.Line(list, "사건 목록", 21);
            for (int i = 0; i < cases.Count; i++)
            {
                int index = i;
                Ui.Button(list, cases[i].Name, () => { _selectedCase = index; _openDocIds.Clear(); _selectedPerson = null; _message = ""; Rebuild(); },
                    true, 56, i == _selectedCase ? Ui.AccentColor : (Color?)null);
            }

            if (_selectedCase < 0 || _selectedCase >= cases.Count) return;
            var selected = cases[_selectedCase];

            // 보고서 폴더 창
            var folder = Ui.Scroll(Ui.Anchor(Ui.Rect(body, "Folder"), 0.175f, 0.45f, 0.36f, 0.86f));
            Ui.Line(folder, "보고서 폴더", 21);
            foreach (var doc in selected.Docs)
            {
                var d = doc;
                Ui.Button(folder, d.Title, () => OpenDoc(d.Id), true, 50, _openDocIds.Contains(d.Id) ? Ui.AccentColor : (Color?)null);
            }

            // 인물 정보 창(자료 내용과 분리)
            BuildPersonWindow(Ui.Anchor(Ui.Panel(body, "Persons", Ui.SubPanelColor), 0.175f, 0f, 0.36f, 0.44f), selected);

            // 자료 내용 창: 최대 2개 좌우 비교, 창마다 상단 검색 + 본문
            for (int i = 0; i < 2; i++)
            {
                float minX = 0.365f + i * 0.318f;
                var win = Ui.Anchor(Ui.Panel(body, "DocWindow" + i, Ui.SubPanelColor), minX, 0, minX + 0.313f, 0.86f);
                if (i >= _openDocIds.Count)
                {
                    var empty = Ui.Label(win, i == 0 ? "보고서를 선택하면 자료 내용 창이 열립니다." : "두 번째 자료를 열면 나란히 비교합니다.", 19, Ui.DimTextColor, TextAnchor.MiddleCenter);
                    Ui.Stretch(empty.rectTransform, 20, 20, 20, 20);
                    continue;
                }
                int slot = i;
                var doc = FindDoc(cases, _openDocIds[i]);
                var wtop = Ui.Anchor(Ui.Rect(win, "Search"), 0, 0.92f, 1, 1);
                Ui.Horizontal(wtop, 6, 4);
                var wsearch = Ui.Input(wtop, "이 창에서 ID 검색");
                Ui.Button(wtop, "열기", () => { var found = FindDoc(cases, wsearch.text.Trim()); if (found != null) { _openDocIds[slot] = found.Id; Rebuild(); } else { _message = "그 ID의 자료가 없습니다."; Rebuild(); } })
                    .GetComponent<LayoutElement>().preferredWidth = 90;
                Ui.Button(wtop, "닫기", () => { _openDocIds.RemoveAt(slot); Rebuild(); }).GetComponent<LayoutElement>().preferredWidth = 90;
                var content = Ui.Scroll(Ui.Anchor(Ui.Rect(win, "Body"), 0, 0, 1, 0.91f));
                Ui.Line(content, doc != null ? doc.Body : "(자료 없음)", 20);
            }
        }

        private void BuildPersonWindow(RectTransform win, CaseEntry selected)
        {
            var content = Ui.Scroll(Ui.Stretch(Ui.Rect(win, "Scroll"), 2, 2, 2, 2));
            Ui.Line(content, "인물 정보", 21);
            if (selected.PersonIds.Count == 0) { Ui.Line(content, "(인물 자료 없음)", 18, Ui.DimTextColor); return; }

            var row = Ui.Row(content, 40);
            foreach (var id in selected.PersonIds)
            {
                string pid = id;
                Ui.Button(row, IsUnlocked(pid) ? PlaytestTexts.TempName(S, pid) : "????????", () => { _selectedPerson = pid; Rebuild(); },
                    true, 40, pid == _selectedPerson ? Ui.AccentColor : (Color?)null, 17);
            }
            if (_selectedPerson == null) return;

            // 왼쪽 이미지(임시 사각형) + 오른쪽 이름·상세. 레이아웃 그룹 안이라 세로로 쌓는다.
            var portrait = Ui.Panel(content, "Portrait", new Color(0.3f, 0.3f, 0.32f));
            var le = portrait.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 70;
            var ptext = Ui.Label(portrait, "[인물 사진 — 임시]", 16, Ui.DimTextColor, TextAnchor.MiddleCenter);
            Ui.Stretch(ptext.rectTransform);

            string person = _selectedPerson;
            Ui.Line(content, "이름: " + (IsUnlocked(person) ? $"{PlaytestTexts.TempName(S, person)} ({PlaytestTexts.PersonCode(person)})" : "????????"), 19);
            _personGuesses.TryGetValue(person, out var guess);
            ChoiceRow(content, "직업", OccupationOptions, guess.occupation, v => SetGuess(person, v, null, null));
            ChoiceRow(content, "사망 원인", CauseOptions, guess.cause, v => SetGuess(person, null, v, null));
            ChoiceRow(content, "사망 요인/도구", ToolOptions, guess.tool, v => SetGuess(person, null, null, v));
        }

        private void ChoiceRow(RectTransform parent, string label, string[] options, string current, System.Action<string> onPick)
        {
            Ui.Line(parent, $"{label}: {current ?? "선택 안 함"}", 17, Ui.DimTextColor);
            var row = Ui.Row(parent, 34);
            foreach (var o in options)
            {
                string v = o;
                Ui.Button(row, o, () => onPick(v), true, 34, o == current ? Ui.AccentColor : (Color?)null, 14);
            }
        }

        /// <summary>직업·사망 원인·도구를 실제 사건 데이터와 모두 일치시켜야 인물 ID를 해금한다.</summary>
        private void SetGuess(string personId, string occupation, string cause, string tool)
        {
            _personGuesses.TryGetValue(personId, out var g);
            g = (occupation ?? g.occupation, cause ?? g.cause, tool ?? g.tool);
            _personGuesses[personId] = g;

            var contract = S.CurrentContract ?? S.LastContract;
            if (contract != null && S.Npcs.Exists(personId)
                && !string.IsNullOrEmpty(g.occupation)
                && !string.IsNullOrEmpty(g.cause)
                && !string.IsNullOrEmpty(g.tool))
            {
                var npc = S.Npcs.Get(personId);
                bool occupationOk = string.Equals(g.occupation, npc.Occupation, System.StringComparison.Ordinal);
                bool isCaseVictim = personId == contract.Truth.VictimId
                    || contract.Truth.AdditionalVictimIds.Contains(personId);
                bool hasKnownDeathProfile = isCaseVictim
                    && contract.PrimaryCrimeType == Contracts.CrimeType.Murder
                    && !npc.IsAlive;

                bool causeOk;
                bool toolOk;
                if (hasKnownDeathProfile)
                {
                    var expectedCause = CaseCatalog.Methods.FirstOrDefault(m => m.Id == contract.Truth.MethodId)?.Name;
                    var expectedTool = CaseCatalog.Tools.FirstOrDefault(t => t.Id == contract.Truth.ToolId)?.Name;
                    causeOk = expectedCause != null && string.Equals(g.cause, expectedCause, System.StringComparison.Ordinal);
                    toolOk = expectedTool != null && string.Equals(g.tool, expectedTool, System.StringComparison.Ordinal);
                }
                else
                {
                    // 이 사건의 실제 진실에 사망 프로필이 정의되지 않은 인물에게 임의의 원인을 정답으로 부여하지 않는다.
                    causeOk = npc.IsAlive && string.Equals(g.cause, NoDeathProfile, System.StringComparison.Ordinal);
                    toolOk = npc.IsAlive && string.Equals(g.tool, CaseCatalog.Tools.First(t => t.Id == "TOOL-00").Name, System.StringComparison.Ordinal);
                }

                if (occupationOk && causeOk && toolOk
                    && _unlockedPersonCodes.Add(PlaytestTexts.PersonCode(personId)))
                    _message = $"인물 정보를 올바르게 특정했습니다. ID 획득: {PlaytestTexts.PersonCode(personId)}";
            }
            Rebuild();
        }

        private bool IsUnlocked(string personId) => _unlockedPersonCodes.Contains(PlaytestTexts.PersonCode(personId));

        private const string OrgDocPrefix = "ORGDOC-";

        private void OpenDoc(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || _openDocIds.Contains(id)) return;

            var read = S.ReadDocument(id);
            if (!read.Success)
            {
                _message = PlaytestTexts.Failure(read.Failure);
                Rebuild();
                return;
            }

            if (id.StartsWith(OrgDocPrefix))
                S.OrgSupplementary.MarkRead(id.Substring(OrgDocPrefix.Length));

            if (_openDocIds.Count >= 2) _openDocIds.RemoveAt(0);
            _openDocIds.Add(id);
            _message = read.TimeSpent > 0 ? "새 자료를 확인해 1시간이 경과했습니다." : "";

            // 첫 열람으로 세계 시각이 진행되었으면 NPC·경찰 상태와 HUD를 갱신하고 자동 저장한다.
            _g.AfterStateChange(animatePieces: false);
            if (S.IsGameOver) return;
            Rebuild();
        }

        private void Search(string query, List<CaseEntry> cases)
        {
            query = (query ?? "").Trim();
            if (query.Length == 0) return;
            var personId = cases.SelectMany(c => c.PersonIds).FirstOrDefault(p => PlaytestTexts.PersonCode(p) == query);
            if (personId != null)
            {
                _unlockedPersonCodes.Add(query);
                _message = $"인물 ID {query} 확인 — 이름이 표시됩니다.";
                _selectedPerson = personId;
                _selectedCase = cases.FindIndex(c => c.PersonIds.Contains(personId));
                Rebuild();
                return;
            }
            var doc = FindDoc(cases, query);
            if (doc != null)
            {
                _selectedCase = cases.FindIndex(c => c.Docs.Contains(doc));
                OpenDoc(doc.Id);
                return;
            }
            _message = $"'{query}'에 해당하는 자료가 없습니다.";
            Rebuild();
        }

        private static Doc FindDoc(List<CaseEntry> cases, string id) =>
            cases.SelectMany(c => c.Docs).FirstOrDefault(d => d.Id == id);

        /// <summary>메인 사건 관련 사건 목록. 진행 중인 의뢰의 경찰·조직 제공 자료도 컴퓨터에서 확인한다(09).</summary>
        private List<CaseEntry> BuildCases()
        {
            var cases = new List<CaseEntry>();
            var contract = S.CurrentContract;
            if (contract != null)
            {
                var entry = new CaseEntry { Name = $"진행 중 의뢰 — {PlaytestTexts.Crime(contract.PoliceCrimeType)} 사건(경찰 분류)" };
                string victim = PlaytestTexts.TempName(S, contract.Truth.VictimId);
                entry.Docs.Add(new Doc
                {
                    Id = "DOC-" + (GameSession.StableSeed(contract.ContractId) % 9000 + 1000),
                    Title = "경찰 제공 — 사건 개요",
                    Body = $"[경찰 제공 사건 개요]\n경찰 분류: {PlaytestTexts.Crime(contract.PoliceCrimeType)}\n장소: 주택 — 현장 {PlaytestTexts.Room(S.Campaign.SceneRoomId)}\n" +
                           $"피해자: {victim} ({PlaytestTexts.PersonCode(contract.Truth.VictimId)})\n담당 경찰: {PlaytestTexts.PoliceName(contract.HandlerPoliceId)}\n" +
                           $"사건 발생일: {contract.IncidentDate} · 인계: {PlaytestTexts.WorldClock(S, GameSession.InvestigationStartMinute(contract))}\n접수 분류: {ContractContent.CaseCatalog.Name(ContractContent.CaseCatalog.RepresentativeSubtypes[contract.PoliceCrimeType], contract.Truth.RepresentativeSubtypeId)}\n\n관련 인물 {PlaytestTexts.AllContractNpcIds(S).Count}명. 인물 신원은 인물 정보 창에서 확인."
                });
                entry.Docs.Add(new Doc
                {
                    Id = "ORG-" + (GameSession.StableSeed("org" + contract.ContractId) % 9000 + 1000),
                    Title = "조직 전달 — 의뢰 조건",
                    Body = "[조직 전달]\n" + PlaytestTexts.Condition(S, contract.OrgCondition)
                });
                entry.PersonIds.AddRange(PlaytestTexts.AllContractNpcIds(S));
                cases.Add(entry);
            }

            var main = new CaseEntry { Name = "사건 기록 #1 (메인 사건 — 사건명 미정)" };
            foreach (var name in S.MainCase.AcquiredClueNames.OrderBy(n => n))
            {
                main.Docs.Add(new Doc
                {
                    Id = "MC-" + (GameSession.StableSeed(name) % 9000 + 1000),
                    // 같은 문서명의 다른 판본(예: 참가자 운영 규칙 안내문 배포본·내부 상세본)은 문서 번호로 구분해 따로 보인다.
                    Title = MainCaseDocumentCatalog.DisplayTitle(name),
                    Body = string.IsNullOrEmpty(MainCaseDocumentCatalog.Header(name))
                        ? MainCaseDocumentBody(name)
                        : MainCaseDocumentCatalog.Header(name) + "\n────────\n" + MainCaseDocumentBody(name)
                });
            }
            cases.Add(main);

            // 06 조직 전달 자료 폴더: 보고서마다 따로 등록, 목록에는 제목·수신 시각·새 자료 표시·열람 여부만 보인다.
            // 진실 단계명·제공 확률·조건·판정 결과는 보이지 않는다. 전달된 자료는 계속 보관하고 다시 열 수 있다.
            var org = new CaseEntry { Name = "조직 전달 자료" };
            foreach (var state in S.DeliveredOrgReports)
            {
                var def = Organization.OrgSupplementarySupportService.GetDefinition(state.Id);
                if (def == null) continue;
                org.Docs.Add(new Doc
                {
                    Id = OrgDocPrefix + state.Id,
                    Title = $"{(state.Read ? "" : "[새 자료] ")}{def.Title} · 수신 {PlaytestTexts.WorldClock(S, state.DeliveredWorldMinute)}{(state.Read ? " · 열람함" : "")}",
                    Body = def.Body
                });
            }
            cases.Add(org);
            return cases;
        }

        /// <summary>메인 사건 문서 본문. 노션 원문을 확보한 문서는 원문을, 나머지는 자리표시 문구를 보여 준다.</summary>
        private string MainCaseDocumentBody(string name)
        {
            switch (name)
            {
                case MainCaseClueNames.BloodiedRebelReport: return EndingScripts.BloodiedRebelReport;
                case MainCaseClueNames.FamilyDeathReport: return EndingScripts.FamilyDeathReport;
                case MainCaseClueNames.FamilyNotificationRecord: return EndingScripts.FamilyNotificationRecord;
                case MainCaseClueNames.NieceDeathReport:
                    // 03 2026-11-13: 조카 사망 보고서를 열면 형제자매 가족 사망 보고서와 연락 담당 코드(경-84-07)를 비교할 수 있다.
                    return (MainCaseDocumentTexts.Bodies.TryGetValue(name, out var report)
                               ? report
                               : $"[{name}]\n문서 번호 내안-84-1014-27 · 국가보안국 특별조사과 · 실험 대상자 84-031") +
                           (S.MainCase.HasAcquired(MainCaseClueNames.FamilyDeathReport)
                               ? "\n\n[대조] 연락 담당 코드 경-84-07 — 「형제자매 가족 사망·사고 처리 보고서」의 연락 담당 코드와 같다."
                               : "");
                default:
                    return MainCaseDocumentTexts.Bodies.TryGetValue(name, out var original)
                        ? original
                        : $"[{name}]\n확보 시점: 완료 의뢰 {S.MainCase.ContractsCompletedSinceAcquired(name)}건 전\n\n(노션 원문이 아직 없는 문서 — TEST_TEMP)";
            }
        }

        // ───────── 서재 컴퓨터: 대상자 번호 파일 ─────────

        public void OpenPasswordPrompt()
        {
            var body = Ui.Window(_g.WindowLayer, "서재 컴퓨터 — 대상자 번호 파일", () => { _g.CloseWindow(); _g.RefreshHud(); }, out _, 0.3f, 0.3f, 0.7f, 0.7f);
            Ui.Vertical(body, 10, 16);
            Ui.Line(body, "고위 관계자 서신을 확보한 뒤 비밀번호를 입력하면 대상자 번호 파일을 열 수 있습니다.", 20, Ui.DimTextColor);
            var input = Ui.Input(body, "비밀번호");
            Ui.Button(body, "입력", () =>
            {
                var outcome = S.EnterSubjectFilePassword(input.text, roomHasComputer: true);
                _g.Toast(outcome == MainCaseClueAcquireOutcome.Acquired ? "대상자 번호 파일을 확보했습니다." : "열리지 않는다: " + outcome);
                _g.CloseWindow();
                _g.AfterStateChange();
            }, true, 48, Ui.AccentColor);
        }
    }
}
