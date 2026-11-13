using System.Collections.Generic;
using System.Linq;
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

        // TEST_TEMP: 인물 정보 선택지(정식 목록 미정). 정답은 실제 진실의 방법·도구 문자열이다.
        private static readonly string[] CauseOptions = { "둔기로 가격(임시)", "독극물", "추락", "질식", "해당 없음(생존)" };
        private static readonly string[] ToolOptions = { "둔기(임시)", "약물", "밧줄", "없음" };
        private static readonly string[] OccupationOptions = { "미상", "가정부", "사업가", "의사", "교사" };

        public ComputerScreen(PlaytestGame g) => _g = g;

        private GameSession S => _g.Session;

        public void Open() => Rebuild();

        private void Rebuild()
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

        /// <summary>사망 원인·도구를 실제 진실과 맞게 특정하면 그 인물의 ID를 얻는다(09 "올바르게 특정하면 ID 조각을 얻는다").</summary>
        private void SetGuess(string personId, string occupation, string cause, string tool)
        {
            _personGuesses.TryGetValue(personId, out var g);
            g = (occupation ?? g.occupation, cause ?? g.cause, tool ?? g.tool);
            _personGuesses[personId] = g;

            var contract = S.CurrentContract ?? S.LastContract;
            if (contract != null && S.Npcs.Exists(personId) && g.cause != null && g.tool != null)
            {
                bool isDead = personId == contract.Truth.VictimId && contract.PrimaryCrimeType == Contracts.CrimeType.Murder;
                bool causeOk = isDead ? g.cause == contract.Truth.Method : g.cause == "해당 없음(생존)";
                bool toolOk = isDead ? g.tool == contract.Truth.Tool : g.tool == "없음";
                if (causeOk && toolOk && _unlockedPersonCodes.Add(PlaytestTexts.PersonCode(personId)))
                    _message = $"인물 정보를 올바르게 특정했습니다. ID 획득: {PlaytestTexts.PersonCode(personId)}";
            }
            Rebuild();
        }

        private bool IsUnlocked(string personId) => _unlockedPersonCodes.Contains(PlaytestTexts.PersonCode(personId));

        private void OpenDoc(string id)
        {
            if (_openDocIds.Contains(id)) return;
            if (_openDocIds.Count >= 2) _openDocIds.RemoveAt(0);
            _openDocIds.Add(id);
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
                    Title = name,
                    Body = $"[{name}]\n확보 시점: 완료 의뢰 {S.MainCase.ContractsCompletedSinceAcquired(name)}건 전\n\n(원문은 「03 — 메인 사건」 문서 원문으로 교체 — TEST_TEMP)"
                });
            }
            cases.Add(main);
            return cases;
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
