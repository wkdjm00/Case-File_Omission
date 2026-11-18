using System;
using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Contracts;
using CaseFileOmission.Core;
using CaseFileOmission.Endings;
using CaseFileOmission.Game;
using CaseFileOmission.MainCase;
using CaseFileOmission.Police;
using CaseFileOmission.SaveLoad;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CaseFileOmission.Playtest
{
    /// <summary>
    /// P0 테스트 플레이 진행기. 게임 규칙은 전부 <see cref="GameSession"/>이 판정하고, 이 클래스는 화면 전환·입력·표시만 맡는다.
    /// 흐름: 타이틀 → UI-003 책상(전화로 의뢰 수락·컴퓨터·보고서·사건보드·타자기) → 3D 현장 조사 → 회의(손패 5장 선택·카드게임)
    /// → 결과 화면 → 다음 의뢰, 또는 게임 오버·엔딩.
    /// </summary>
    public sealed class PlaytestGame : MonoBehaviour
    {
        public enum View { Title, Desk, Board, Meeting, Ending }

        [Header("보유 자산(씬 빌더가 연결)")]
        public Font UiFont;
        public BoardView Board;
        public DeskView Desk;
        public Camera MainCamera;
        [Tooltip("현장 HUD·상태 아이콘·접근 안내 임시 이미지")]
        public FieldArt Art = new FieldArt();

        [Header("테스트")]
        [Tooltip("TEST_TEMP: 같은 시드면 같은 의뢰(고정 테스트 사건)가 생성된다.")]
        public int DefaultSeed = 7;

        public GameSession Session { get; private set; }
        public View Current { get; private set; } = View.Title;
        public RectTransform WindowLayer { get; private set; }

        private RectTransform _hudLayer, _modalLayer, _toastLayer, _debugLayer, _worldLayer, _dialogueLayer;
        private FieldHud _field;
        private bool _menuOpen;
        private readonly List<GameObject> _hiddenDuringDialogue = new List<GameObject>();
        private PhoneController _phone;
        private readonly List<string> _log = new List<string>();
        private Text _toastText;
        private float _toastUntil;
        private int _seed;
        private bool _debugVisible;
        private Vector3 _boardCamPos = new Vector3(-3f, 26f, -14f);
        private Quaternion _boardCamRot = Quaternion.Euler(58f, 0f, 0f);
        private int _passSlots = 1;

        // 결과 화면용: 의뢰 시작 시점 값
        private float _trustAtStart, _handlerSuspicionAtStart;
        private string _resultShownFor;

        private ComputerScreen _computer;
        private ReportScreen _report;
        private CaseBoardScreen _caseBoard;
        private MeetingScreen _meeting;
        private FinalReportScreen _finalReport;

        public static string SavePath => System.IO.Path.Combine(Application.persistentDataPath, "p0_playtest_save.json");

        private void Awake()
        {
            Ui.Font = UiFont != null ? UiFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (MainCamera == null) MainCamera = Camera.main;
            BuildCanvas();

            Board.LabelFont = Ui.Font;
            Desk.LabelFont = Ui.Font;
            Board.Build();
            Desk.Build(Board);
            _phone = gameObject.AddComponent<PhoneController>();
            if (Desk.Telephone != null) _phone.Bind(Desk.Telephone, MainCamera);

            _computer = new ComputerScreen(this);
            _report = new ReportScreen(this);
            _caseBoard = new CaseBoardScreen(this);
            _meeting = new MeetingScreen(this);
            _finalReport = new FinalReportScreen(this);
        }

        private void Start() => ShowTitle();

        // ───────────────────────── 캔버스 ─────────────────────────

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("PlaytestCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var root = (RectTransform)canvasGo.transform;
            _worldLayer = Ui.Stretch(Ui.Rect(root, "WorldMarks"));
            _hudLayer = Ui.Stretch(Ui.Rect(root, "HUD"));
            WindowLayer = Ui.Stretch(Ui.Rect(root, "Windows"));
            _modalLayer = Ui.Stretch(Ui.Rect(root, "Modal"));
            _dialogueLayer = Ui.Stretch(Ui.Rect(root, "Dialogue"));
            _field = new FieldHud(Art, _worldLayer, _dialogueLayer);
            _debugLayer = Ui.Stretch(Ui.Rect(root, "Debug"));
            _toastLayer = Ui.Stretch(Ui.Rect(root, "Toast"));

            // 현장 HUD 중앙 위 의심도 목록(09)을 가리지 않게 그 아래에 띄운다.
            var toast = Ui.Anchor(Ui.Panel(_toastLayer, "Toast", new Color(0, 0, 0, 0.8f)), 0.3f, 0.67f, 0.7f, 0.76f);
            _toastText = Ui.Label(toast, "", 22, Ui.TextColor, TextAnchor.MiddleCenter);
            Ui.Stretch(_toastText.rectTransform, 12, 4, 12, 4);
            _toastLayer.gameObject.SetActive(false);

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem));
                es.AddComponent<InputSystemUIInputModule>();
            }
        }

        public void Toast(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            _toastText.text = message;
            _toastLayer.gameObject.SetActive(true);
            _toastUntil = Time.time + 3.5f;
            Log(message);
        }

        public void Log(string message)
        {
            _log.Add(message);
            if (_log.Count > 9) _log.RemoveAt(0);
        }

        // ───────────────────────── 화면 전환 ─────────────────────────

        private void ShowTitle()
        {
            Current = View.Title;
            ClearLayers();
            MainCamera.transform.SetPositionAndRotation(_boardCamPos, _boardCamRot);
            var body = Ui.Window(_hudLayer, "사건기록: 누락 — P0 테스트 플레이", null, out _, 0.3f, 0.25f, 0.7f, 0.75f);
            Ui.Vertical(body, 12, 16);
            Ui.Line(body, "임시 UI·임시 배치로 의뢰 1건의 전체 흐름을 테스트하는 빌드입니다.\n같은 시드는 같은 의뢰(고정 테스트 사건)를 만듭니다.", 21, Ui.DimTextColor);
            var seed = Ui.Input(body, "시드(정수)", DefaultSeed.ToString());
            Ui.Button(body, "새 테스트 세션 시작", () =>
            {
                _seed = int.TryParse(seed.text, out int v) ? v : DefaultSeed;
                Session = new GameSession(new GameSessionConfig { Random = new System.Random(_seed) });
                Session.StartNewGame();
                Session.CompletePrologue();
                _resultShownFor = null;
                Log($"새 세션 시작(시드 {_seed}). 조카의 일기·사망 관련 보고서 확보.");
                GoDesk();
            }, true, 56, Ui.AccentColor);
            Ui.Button(body, "고정 테스트 사건 TEST_TEMP_CASE_001 (폭행 신고 → 실종)", () =>
            {
                _seed = ContractContent.TestFixtureCase001.Seed;
                Session = new GameSession(new GameSessionConfig { Random = new System.Random(_seed) });
                Session.StartNewGame();
                Session.CompletePrologue();
                Session.OfferFixture(ContractContent.TestFixtureCase001.Build(), Police.PoliceRoster.PoliceC);
                _resultShownFor = null;
                Log("고정 테스트 사건 TEST_TEMP_CASE_001 준비 — 전화를 받아 수락하세요.");
                GoDesk();
            }, true, 50);
            Ui.Button(body, "불러오기", LoadGame, System.IO.File.Exists(SavePath));
            Ui.Line(body, "조작(현장): WASD 이동 · 마우스로 카메라 회전(Alt 누르는 동안 커서) · 휠로 거리 · 가까이 가서 E로 조사·대화 · Tab 조사 메뉴 / 책상: 물건 클릭 · F1 개발용 패널 · F5 저장 · F9 불러오기", 18, Ui.DimTextColor);
        }

        public void GoDesk()
        {
            CloseWindow();
            _field.CloseDialogue();
            _field.HideWorldUi();
            Current = View.Desk;
            MainCamera.transform.SetPositionAndRotation(Desk.CameraPose.position, Desk.CameraPose.rotation);
            RefreshHud();
        }

        public void GoBoard()
        {
            if (Session.State.CurrentPhase != GamePhase.Investigation) { Toast("진행 중인 조사가 없습니다. 전화로 의뢰를 받으세요."); return; }
            CloseWindow();
            Current = View.Board;
            Board.SyncPieces(Session, animate: false);
            Board.SyncObjects(Session);
            // 탐사 카메라는 캐릭터 뒤쪽 45도에서 시작해, 마우스(오른쪽 드래그)로 돌린다. 들어올 때는 그 자리로 바로 놓는다.
            var me = Board.PlayerPiece(Session);
            if (me != null)
            {
                var fwd = me.forward; fwd.y = 0;
                _camYaw = fwd.sqrMagnitude < 0.01f ? 0f : Quaternion.LookRotation(fwd).eulerAngles.y;
                CameraPose(me, out _boardCamPos, out _boardCamRot);
            }
            MainCamera.transform.SetPositionAndRotation(_boardCamPos, _boardCamRot);
            Board.HighlightRooms(Session);
            Board.ShowExchanges(Session);
            RefreshHud();
        }

        public void GoMeeting()
        {
            CloseWindow();
            Current = View.Meeting;
            RefreshHud();
            _meeting.Open();
        }

        public void CloseWindow()
        {
            Ui.Clear(WindowLayer);
        }

        private void OpenWindow(Action open)
        {
            CloseWindow();
            open();
        }

        private void ClearLayers()
        {
            Ui.Clear(_hudLayer);
            Ui.Clear(WindowLayer);
            Ui.Clear(_modalLayer);
            _field.CloseDialogue();
            _field.HideWorldUi();
        }

        /// <summary>모든 상태 변경 뒤 호출. 엔딩·의뢰 종료를 먼저 확인하고 HUD를 다시 그린다.</summary>
        public void AfterStateChange(bool animatePieces = true)
        {
            if (Session == null) return;
            if (Session.Endings.ExecutedEnding.HasValue && Current != View.Ending && ShouldShowEnding())
            {
                ShowEnding();
                return;
            }

            if (Session.LastContract != null && Session.CurrentContract == null && _resultShownFor != Session.LastContract.ContractId)
            {
                _resultShownFor = Session.LastContract.ContractId;
                Current = View.Desk;
                MainCamera.transform.SetPositionAndRotation(Desk.CameraPose.position, Desk.CameraPose.rotation);
                ShowResult();
                RefreshHud();
                return;
            }

            if (Current == View.Board)
            {
                Board.SyncPieces(Session, animatePieces);
                Board.SyncObjects(Session);
                Board.HighlightRooms(Session);
                Board.ShowExchanges(Session);
            }
            if (Current == View.Meeting && Session.State.CurrentPhase == GamePhase.Investigation)
                GoBoard();
            RefreshHud();
        }

        private GameEndingType? _endingShown;

        private bool ShouldShowEnding() => Session.Endings.ExecutedEnding != _endingShown;

        // ───────────────────────── HUD ─────────────────────────

        public void RefreshHud()
        {
            Ui.Clear(_hudLayer);
            Ui.Clear(_modalLayer);
            _hiddenDuringDialogue.Clear();
            if (Session == null) return;

            // 현장은 09 HUD 배치(신뢰도·의심도·시각·잔여시간)가 위쪽 막대를 대신한다.
            if (Current != View.Board) BuildTopBar();
            if (Current == View.Desk) BuildDeskHud();
            if (Current == View.Board) BuildBoardHud();
            BuildLogPanel();
            if (_debugVisible) BuildDebugPanel(); else Ui.Clear(_debugLayer);
        }

        private void BuildTopBar()
        {
            var bar = Ui.Anchor(Ui.Panel(_hudLayer, "TopBar"), 0, 0.955f, 1, 1);
            var s = Session;
            string contract = s.CurrentContract != null
                ? $"의뢰: {PlaytestTexts.Crime(s.CurrentContract.PoliceCrimeType)} 사건 · {PlaytestTexts.WorldClock(s, s.WorldMinute)} ({PlaytestTexts.TimeBlock(s.State.CurrentTimeBlock)} {s.State.CurrentDay}일차) · 남은 조사 {s.State.RemainingTimeSlots * 15 / 60f:0.##}시간"
                : "의뢰 없음";
            var text = Ui.Label(bar, $"단계: {PlaytestTexts.Phase(s)}   |   {contract}   |   조직 신뢰도 {s.Organization.Trust:0}   |   메인 사건: {StageName(s.MainCase.CurrentStage)}", 21, Ui.TextColor, TextAnchor.MiddleLeft);
            Ui.Stretch(text.rectTransform, 16, 0, 16, 0);
        }

        public static string StageName(MainCaseTruthStage stage) => stage switch
        {
            MainCaseTruthStage.PersonalTruth => "1. 개인적 진실",
            MainCaseTruthStage.SystemicTruth => "2. 체계의 진실",
            MainCaseTruthStage.PowerTruth => "3. 권력의 진실",
            MainCaseTruthStage.ExperimentTruth => "4. 실험의 진실",
            MainCaseTruthStage.DesignerTruth => "5. 설계자의 진실",
            _ => stage.ToString()
        };

        private void BuildDeskHud()
        {
            var panel = Ui.Anchor(Ui.Panel(_hudLayer, "DeskPanel"), 0.005f, 0.30f, 0.20f, 0.95f);
            Ui.Vertical(panel, 8, 12);
            Ui.Line(panel, "UI-003 메인 책상", 24);
            Ui.Line(panel, "책상 위 물건을 클릭하세요.", 18, Ui.DimTextColor);
            bool ringing = Session.State.CurrentPhase == GamePhase.Idle && !Session.IsGameOver || Session.PostReport.PhoneStage == PhoneCallStage.AwaitingAnswer;
            Ui.Button(panel, ringing ? "전화기 (벨이 울린다!)" : "전화기", () => Interact(DeskObjectKind.Telephone), true, 44, ringing ? Ui.AccentColor : (Color?)null);
            Ui.Button(panel, "컴퓨터", () => Interact(DeskObjectKind.Computer));
            Ui.Button(panel, "의뢰 보고서", () => Interact(DeskObjectKind.Report), Session.CurrentContract != null);
            Ui.Button(panel, "사건보드", () => Interact(DeskObjectKind.CaseBoard), Session.CurrentContract != null);
            Ui.Button(panel, "타자기 — 최종 보고서(UI-404)", () => Interact(DeskObjectKind.Typewriter));
            Ui.Button(panel, RebelVisitDue ? "사무실을 나선다" : "현장으로 이동", () => Interact(DeskObjectKind.ToScene), Session.State.CurrentPhase == GamePhase.Investigation || RebelVisitDue);
            Ui.Line(panel, " ", 8);
            Ui.Button(panel, "저장 (F5)", SaveGame, Session.CanSave);
            Ui.Button(panel, "불러오기 (F9)", LoadGame, System.IO.File.Exists(SavePath));
            Ui.Button(panel, "타이틀로", ShowTitle);
        }

        private void BuildBoardHud()
        {
            var s = Session;
            _field.BuildHud(_hudLayer, s);
            if (s.State.IsTimeBlockEnded)
                BuildBlockEndedModal();

            // 조사 메뉴: HUD 배치(09)를 가리지 않게 평소엔 접어 두고 Tab·버튼으로 연다. 엿듣기·관찰·회유·시간 보내기·조사 종료·조작·책상 이동.
            var toggle = Ui.Button(_hudLayer, _menuOpen ? "조사 메뉴 닫기 [Tab]" : "조사 메뉴 [Tab]", () => { _menuOpen = !_menuOpen; RefreshHud(); }, true, 44, Ui.AccentColor);
            Ui.Anchor((RectTransform)toggle.transform, 0.86f, 0.205f, 0.995f, 0.25f);
            _hiddenDuringDialogue.Add(toggle.gameObject);
            if (!_menuOpen) return;

            var panel = Ui.Anchor(Ui.Panel(_hudLayer, "BoardMenu"), 0.73f, 0.26f, 0.995f, 0.65f);
            var content = Ui.Scroll(Ui.Stretch(Ui.Rect(panel, "Scroll"), 4, 4, 4, 4), Ui.PanelColor);

            string room = s.PlayerRoom;
            Ui.Line(content, $"현재 위치: {PlaytestTexts.Room(room)}", 24);
            Ui.Line(content, $"사건 현장: {PlaytestTexts.Room(s.Campaign.SceneRoomId)} (붉은 표식) · {PlaytestTexts.Crime(s.CurrentContract.PoliceCrimeType)} 사건", 18, Ui.DimTextColor);
            Ui.Line(content, $"{PlaytestTexts.TimeBlock(s.State.CurrentTimeBlock)} {s.State.CurrentDay}일차 · 시간대 끝 {PlaytestTexts.WorldClock(s, s.BlockEndMinute)} · 메인 사건 {StageName(s.MainCase.CurrentStage)}", 18, Ui.DimTextColor);

            // 이동은 WASD로 한다(사용자 지정). 방을 옮겨도 시간은 들지 않는다(06: 이동 0시간).
            Ui.Line(content, "— 이동: WASD (0분) · 카메라: 마우스(메뉴를 닫으면) —", 18, Ui.DimTextColor);

            Ui.Line(content, "— 이 방의 조사 대상 (가까이 가서 E · 각 1시간) —", 18, Ui.DimTextColor);
            foreach (var obj in s.GetInvestigationObjects().Where(o => o.RoomId == room))
                Ui.Line(content, (s.InvestigatedObjectIds.Contains(obj.ObjectId) ? "✓ " : "· ") + obj.Name, 18);
            int talking = s.CurrentOverhearableExchanges().Count;
            Ui.Button(content, talking > 0 ? $"엿듣기 30분 — 지금 이 방에서 오가는 대화 {talking}건" : "엿듣기 — 지금 이 방에서 오가는 대화가 없다", () => Act(s.Overhear()),
                talking > 0 && !s.State.IsTimeBlockEnded && s.State.RemainingTimeSlots >= GameSession.OverhearCost);
            if (Rooms.HouseLayoutFactory.RoomHasComputer(room))
                Ui.Button(content, "이 방 컴퓨터: 대상자 번호 파일 비밀번호", () => OpenWindow(() => _computer.OpenPasswordPrompt()), true);

            Ui.Line(content, "— 같은 방의 인물 —", 18, Ui.DimTextColor);
            bool anyone = false;
            foreach (var npc in s.Npcs.GetByContract(s.CurrentContract.ContractId).Where(n => s.Occupancy.HasPosition(n.NpcId) && s.Occupancy.GetRoom(n.NpcId) == room))
            {
                anyone = true;
                string label = $"{PlaytestTexts.TempName(s, npc.NpcId)} · {PlaytestTexts.PublicRole(s, npc)} · {PlaytestTexts.Personality(npc.Personality)}{(npc.IsAlive ? "" : " (사망)")}";
                Ui.Line(content, label, 19);
                var row = Ui.Row(content);
                string id = npc.NpcId;
                Ui.Button(row, "관찰 30분", () => ObserveNpc(id), npc.IsAlive && !s.State.IsTimeBlockEnded, 44, null, 17);
                Ui.Button(row, "회유 1시간", () => Persuade(id), npc.IsAlive && !s.State.IsTimeBlockEnded && !s.Campaign.PersuasionAttempted, 44, null, 17);
            }
            string handler = s.HandlerPoliceId;
            if (handler != null && s.Occupancy.HasPosition(handler) && s.Occupancy.GetRoom(handler) == room)
            {
                anyone = true;
                Ui.Line(content, $"{PlaytestTexts.PoliceName(handler)} · 담당 경찰", 19);
                Ui.Button(content, "경찰에게 의뢰 포기를 알린다 (조직 신뢰도 -2)", ConfirmAbandon, true, 44, Ui.AccentColor);
            }
            if (!anyone) Ui.Line(content, "(없음)", 18, Ui.DimTextColor);

            Ui.Line(content, "— 시간 —", 18, Ui.DimTextColor);
            _passSlots = Mathf.Clamp(_passSlots, 1, Mathf.Max(1, s.State.RemainingTimeSlots));
            var passRow = Ui.Row(content);
            Ui.Button(passRow, "−", () => { _passSlots--; RefreshHud(); });
            Ui.Button(passRow, $"{_passSlots * GameStateController.SlotMinutes}분", null, false);
            Ui.Button(passRow, "+", () => { _passSlots++; RefreshHud(); });
            Ui.Button(content, $"시간 보내기 ({_passSlots * GameStateController.SlotMinutes}분 머무르기)", () => Act(s.PassTime(_passSlots)), !s.State.IsTimeBlockEnded && s.State.RemainingTimeSlots > 0);
            Ui.Button(content, "조사 종료 — 시간대 끝까지 진행", () => Act(s.EndInvestigationEarly()), !s.State.IsTimeBlockEnded);

            Ui.Line(content, "— 물리적 증거·기록 조작 —", 18, Ui.DimTextColor);
            Ui.Button(content, s.IsPhysicalManipulationUnlocked ? "조작할 단서 고르기 (1시간)" : "잠김: 최종 보고서 완성 + 이번 의뢰 첫 회의 이후",
                () => OpenWindow(() => _report.OpenManipulation()), s.IsPhysicalManipulationUnlocked && !s.State.IsTimeBlockEnded);

            Ui.Line(content, " ", 6);
            Ui.Button(content, "책상으로 (컴퓨터·보고서·사건보드)", GoDesk, true, 48, Ui.AccentColor);
        }

        private void BuildBlockEndedModal()
        {
            var modal = Ui.Anchor(Ui.Panel(_modalLayer, "BlockEnded"), 0.36f, 0.35f, 0.64f, 0.65f);
            Ui.Vertical(modal, 12, 18);
            Ui.Line(modal, "이번 조사 시간대가 끝났습니다.", 26);
            Ui.Line(modal, "회의를 열거나(증거가 부족해도 가능) 다음 조사 시간대로 넘어갑니다.", 19, Ui.DimTextColor);
            Ui.Button(modal, "회의 열기 — 손패 5장 고르기", GoMeeting, true, 52, Ui.AccentColor);
            Ui.Button(modal, "회의 없이 다음 시간대로", () => Act(Session.AdvanceToNextTimeBlock()), true, 48);
            Ui.Button(modal, "책상으로(보고서 정리)", GoDesk);
        }

        private void BuildLogPanel()
        {
            var panel = Ui.Anchor(Ui.Panel(_hudLayer, "Log", new Color(0, 0, 0, 0.6f)), 0.6f, 0.0f, 1f, 0.2f);
            _hiddenDuringDialogue.Add(panel.gameObject);
            var t = Ui.Label(panel, "기록\n" + string.Join("\n", _log.AsEnumerable().Reverse().Take(7)), 17, Ui.DimTextColor);
            Ui.Stretch(t.rectTransform, 10, 6, 10, 6);
        }

        // ───────────────────────── 조사 행동 ─────────────────────────

        public void Act(GameActionResult result, string successNote = null)
        {
            if (!result.Success) { Toast(PlaytestTexts.Failure(result.Failure)); AfterStateChange(); return; }
            ReportResult(result, successNote);
            AfterStateChange();
        }

        private void ReportResult(GameActionResult result, string successNote)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(successNote)) parts.Add(successNote);
            if (result.NewClueIds.Count > 0)
                parts.Add("새 자료: " + string.Join(", ", result.NewClueIds.Select(id => PlaytestTexts.MaterialTitle(Session, id))));
            if (result.ConfirmedByPoliceClueIds.Count > 0)
                parts.Add($"담당 경찰이 자료 {result.ConfirmedByPoliceClueIds.Count}건을 확인했다(소유는 그대로).");
            if (result.NewMainCaseClues.Count > 0)
                parts.Add("메인 사건 단서: " + string.Join(", ", result.NewMainCaseClues));
            if (result.TimeSpent > 0 && parts.Count == 0) parts.Add($"{result.TimeSpent}칸 사용. 새로 얻은 것은 없다.");
            if (parts.Count > 0) Toast(string.Join("  /  ", parts));
        }

        private void InvestigateObject(string objectId)
        {
            var result = Session.InvestigateObject(objectId);
            Act(result, result.Success && result.NewClueIds.Count == 0 && result.NewMainCaseClues.Count == 0
                ? $"{ContractContent.CaseCatalog.ObjectName(objectId)}: 특별한 것은 없다." : null);
        }

        private void ObserveNpc(string npcId)
        {
            var result = Session.Observe(npcId);
            if (!result.Success) { Act(result); return; }
            var o = Session.LastObservation;
            string held = o.HeldClueIds.Count == 0 ? "들고 있는 물건은 없다" : "들고 있는 것: " + string.Join(", ", o.HeldClueIds.Select(id => PlaytestTexts.MaterialTitle(Session, id)));
            string next = o.ScheduledRoom != null && o.ScheduledRoom != Session.Occupancy.GetRoom(npcId) ? $" 곧 {PlaytestTexts.Room(o.ScheduledRoom)} 쪽으로 갈 것 같다." : "";
            Act(result, $"{PlaytestTexts.TempName(Session, npcId)}을(를) 지켜봤다. {held}.{next}");
        }

        /// <summary>회유(07): 의뢰 보고서의 '인물 정보' 문단을 내용·증거 모두 통과해 실제 가해자를 식별한 뒤, 그 인물에게 의뢰당 1회.</summary>
        private void Persuade(string npcId)
        {
            var s = Session;
            var result = s.Persuade(npcId);
            if (!result.Success && result.Failure == GameActionFailure.InvalidInput)
            {
                Toast(s.Campaign.CulpritIdentified
                    ? "이 인물은 회유할 대상이 아니다(회유는 실제 가해자에게만, 의뢰당 1회)."
                    : "회유하려면 먼저 의뢰 보고서의 '인물 정보' 문단을 근거와 함께 완성해 가해자를 특정해야 합니다.");
                return;
            }
            Act(result, result.Success ? (s.LastPersuasion != null && s.LastPersuasion.Success ? "회유 성공 — 아는 정보를 털어놓는다." : "회유 실패.") : null);
        }

        private void ConfirmAbandon()
        {
            var modal = Ui.Anchor(Ui.Panel(_modalLayer, "Abandon"), 0.38f, 0.4f, 0.62f, 0.6f);
            Ui.Vertical(modal, 10, 16);
            Ui.Line(modal, "의뢰를 포기할까요? 조직 신뢰도가 2 내려갑니다.", 22);
            Ui.Button(modal, "포기한다", () => Act(Session.AbandonContract()), true, 48, Ui.AccentColor);
            Ui.Button(modal, "취소", RefreshHud);
        }

        // ───────────────────────── 책상 상호작용 ─────────────────────────

        public void Interact(DeskObjectKind kind)
        {
            if (_phone.InCall || Session == null) return;
            switch (kind)
            {
                case DeskObjectKind.Telephone: StartPhoneCall(); break;
                case DeskObjectKind.Computer: OpenWindow(_computer.Open); break;
                case DeskObjectKind.Report:
                    if (Session.CurrentContract == null) Toast("진행 중인 의뢰가 없습니다.");
                    else OpenWindow(_report.Open);
                    break;
                case DeskObjectKind.CaseBoard:
                    if (Session.CurrentContract == null) Toast("진행 중인 의뢰가 없습니다.");
                    else OpenWindow(_caseBoard.Open);
                    break;
                case DeskObjectKind.Typewriter: OpenWindow(_finalReport.Open); break;
                case DeskObjectKind.ToScene:
                    if (RebelVisitDue) ShowRebelVisit();
                    else GoBoard();
                    break;
            }
        }

        private void StartPhoneCall()
        {
            var s = Session;
            CloseWindow();
            Ui.Clear(_hudLayer);
            var steps = new List<CallStep>();

            if (s.PostReport.PhoneStage == PhoneCallStage.AwaitingAnswer)
            {
                // 04 2026-11-13 완성본: 얼굴·신원 표시 없이 질문과 선택지 두 개만 보인다.
                foreach (var para in Paragraphs(EndingScripts.PhoneQuestion, 3))
                    steps.Add(CallStep.Say(PhoneVoice, para));
                steps.Add(CallStep.Choose("", new[] { $"[{EndingScripts.ListenChoice}]", $"[{EndingScripts.RefuseChoice}]" }, i =>
                {
                    var ending = s.AnswerPhoneQuestion(i == 0);
                    if (ending == null)
                    {
                        foreach (var para in Paragraphs(EndingScripts.DesignerInfo, 2))
                            _phone.Append(CallStep.Say(PhoneVoice, para));
                    }
                    else
                    {
                        foreach (var para in Paragraphs(EndingScripts.RefusalLine, 3))
                            _phone.Append(CallStep.Say(PhoneVoice, para));
                    }
                }));
            }
            else if (s.State.CurrentPhase == GamePhase.Idle)
            {
                // 06: 조직 보조 자료가 전달된 경계마다 전화 1회로 알린다.
                if (s.ConsumeOrgDeliveryCall())
                    steps.Add(CallStep.Say("조직", GameSession.OrgDeliveryPhoneLine));
                var offer = s.OfferContract();
                if (offer == null)
                {
                    steps.Add(CallStep.Say("조직", "지금은 맡길 의뢰가 없다. (의뢰 생성 실패: 검증을 통과한 후보 없음)"));
                }
                else
                {
                    var c = offer.Contract;
                    steps.Add(CallStep.Say("조직", $"새 의뢰다. {PlaytestTexts.Crime(c.PoliceCrimeType)} 사건, 장소는 한 주택이다."));
                    steps.Add(CallStep.Say("조직", "조건: " + PlaytestTexts.Condition(s, c.OrgCondition)));
                    steps.Add(CallStep.Choose("의뢰를 받을까?", new[] { "수락한다.", "다시 생각해 보겠다." }, i =>
                    {
                        if (i != 0) return;
                        float trust = s.Organization.Trust;
                        var result = s.AcceptContract();
                        if (!result.Success) { _phone.Append(CallStep.Say("조직", PlaytestTexts.Failure(result.Failure))); return; }
                        _trustAtStart = trust;
                        _handlerSuspicionAtStart = s.PoliceSuspicion.GetSuspicion(s.HandlerPoliceId, s.PlayerId);
                        _phone.Append(CallStep.Say("조직", $"담당 경찰은 {PlaytestTexts.PoliceName(s.HandlerPoliceId)}다. 현장에서 상황을 설명해 줄 거다."),
                            CallStep.Say("조직", "경찰이 넘긴 사건 개요는 컴퓨터에 올려 두었다."));
                        Log("의뢰 수락: " + PlaytestTexts.Crime(c.PoliceCrimeType));
                    }));
                }
            }
            else
            {
                var c = s.CurrentContract;
                steps.Add(CallStep.Say("조직", c != null ? "조건을 잊지 마라. " + PlaytestTexts.Condition(s, c.OrgCondition) : "용건이 뭐지?"));
                steps.Add(CallStep.Me("알고 있다."));
            }

            _phone.StartCall(steps, () => AfterStateChange());
        }

        private const string PhoneVoice = "수화기 너머의 목소리";

        /// <summary>설계자 정보를 들은 뒤 사무실을 나가려는 순간 반란 세력이 찾아온다(04 완성본 2·3절).</summary>
        private bool RebelVisitDue => Session.PostReport.RebelStage == RebelRecruitmentStage.Offered;

        /// <summary>원문 문단을 n개씩 묶어 화면 한 장에 보인다(문장은 바꾸지 않는다).</summary>
        private static IEnumerable<string> Paragraphs(string text, int perPage)
        {
            var paras = text.Replace("\r", "").Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < paras.Length; i += perPage)
                yield return string.Join("\n\n", paras.Skip(i).Take(perPage));
        }

        /// <summary>
        /// 반란 세력 방문(04 완성본 3절): 노크 → 연락책 대화 → [반란 세력에 합류한다]/[영입을 거절한다]. 선택지는 한 번만 표시한다.
        /// </summary>
        private void ShowRebelVisit()
        {
            CloseWindow();
            var pages = new List<(string, string)> { ("", "(사무실을 나서려는 순간, 문을 두드리는 소리가 난다.)") };
            foreach (var (speaker, line) in EndingScripts.ContactDialogue)
                pages.Add((speaker == "나" ? "플레이어" : "반란 세력 연락책", line));
            PlayStory(pages, () =>
            {
                var modal = Ui.Anchor(Ui.Panel(_modalLayer, "RebelChoice"), 0.32f, 0.38f, 0.68f, 0.62f);
                Ui.Vertical(modal, 12, 18);
                Ui.Button(modal, $"[{EndingScripts.JoinChoice}]", () => { Ui.Clear(_modalLayer); Session.DecideRebelRecruitment(true); AfterStateChange(); }, true, 52, Ui.AccentColor);
                Ui.Button(modal, $"[{EndingScripts.DeclineChoice}]", () =>
                {
                    Ui.Clear(_modalLayer);
                    PlayStory(new List<(string, string)> { ("반란 세력 연락책", EndingScripts.ContactDeclineLine), ("", "(연락책이 자리를 떠난다.)") },
                        () => { Session.DecideRebelRecruitment(false); AfterStateChange(); });
                }, true, 52);
            });
        }

        /// <summary>대사·연출 문장을 한 장씩 넘겨 보여 준다. 마지막 장에서 onDone을 부른다.</summary>
        private void PlayStory(List<(string speaker, string text)> pages, Action onDone, int index = 0)
        {
            Ui.Clear(_modalLayer);
            if (index >= pages.Count) { onDone?.Invoke(); return; }
            var (speaker, text) = pages[index];
            var modal = Ui.Anchor(Ui.Panel(_modalLayer, "Story", new Color(0.03f, 0.03f, 0.04f, 0.96f)), 0.15f, 0.12f, 0.85f, 0.88f);
            Ui.Vertical(modal, 12, 26);
            if (!string.IsNullOrEmpty(speaker)) Ui.Line(modal, speaker, 22, Ui.AccentColor);
            var area = Ui.Rect(modal, "Text");
            area.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            var content = Ui.Scroll(area, new Color(0, 0, 0, 0));
            Ui.Line(content, text, 23);
            Ui.Button(modal, index + 1 < pages.Count ? "다음" : "계속", () => PlayStory(pages, onDone, index + 1), true, 50);
        }

        private static List<(string, string)> Monologue(string speaker, string text, int perPage) =>
            Paragraphs(text, perPage).Select(p => (speaker, p)).ToList();

        // ───────────────────────── 결과·엔딩 ─────────────────────────

        private void ShowResult()
        {
            var s = Session;
            var c = s.LastContract;
            var body = Ui.Window(WindowLayer, "의뢰 결과", () => { CloseWindow(); RefreshHud(); }, out _, 0.25f, 0.15f, 0.75f, 0.85f);
            var content = Ui.Scroll(Ui.Stretch(Ui.Rect(body, "Scroll")));
            string outcome = c.OfficialConclusion.Outcome switch
            {
                ContractOutcome.Resolved => "정상 해결 — 공식 결론 확정",
                ContractOutcome.Unsolved => "미제 (실패가 아님)",
                ContractOutcome.HandlerPoliceDeceased => "담당 경찰 사망으로 종료(미제 처리)",
                _ => c.OfficialConclusion.Outcome.ToString()
            };
            Ui.Line(content, $"사건: {PlaytestTexts.Crime(c.PoliceCrimeType)}", 22);
            Ui.Line(content, $"결과: {outcome}", 22);
            string accused = c.OfficialConclusion.OfficiallyAccusedNpcId;
            Ui.Line(content, $"공식 지목: {(accused == null ? "없음" : PlaytestTexts.TempName(s, accused))}", 22);
            Ui.Line(content, c.OrgCondition.HasCondition
                ? $"조직 조건: {PlaytestTexts.Condition(s, c.OrgCondition)} → {(c.OrgCondition.Achieved == true ? "달성 (+5)" : "미달성 (-5)")}"
                : "조직 조건: 없음 (신뢰도 변화 없음)", 20);
            Ui.Line(content, $"조직 신뢰도: {_trustAtStart:0} → {s.Organization.Trust:0}", 22);
            if (c.HandlerPoliceId != null)
                Ui.Line(content, $"담당 경찰({PlaytestTexts.PoliceName(c.HandlerPoliceId)}) 의심도: {_handlerSuspicionAtStart:0} → {s.PoliceSuspicion.GetSuspicion(c.HandlerPoliceId, s.PlayerId):0}", 22);
            Ui.Line(content, "※ 실제 진실과 공식 결론은 별개입니다. 실제 진실은 개발용 패널(F1)에서만 확인합니다.", 18, Ui.DimTextColor);
            Ui.Button(content, "책상으로", () => { CloseWindow(); RefreshHud(); }, true, 50, Ui.AccentColor);
        }

        private void ShowEnding()
        {
            var ending = Session.Endings.ExecutedEnding.Value;
            _endingShown = ending;
            var sequence = EndingSequence(ending);
            if (sequence != null)
            {
                if (Session.IsGameOver) { Current = View.Ending; ClearLayers(); }
                PlayStory(sequence, ShowEndingCard);
                return;
            }
            ShowEndingCard();
        }

        /// <summary>엔딩 연출(04 완성본 4~6절). 독백은 원문 전체를 재생하고, 장면 지시는 괄호 안 연출 문장으로 대신한다(임시 연출).</summary>
        private static List<(string, string)> EndingSequence(GameEndingType ending)
        {
            switch (ending)
            {
                case GameEndingType.RebelRecruitAccepted:
                {
                    var pages = new List<(string, string)>
                    {
                        ("", "(연락책이 플레이어의 선택을 확인하고 사무실을 떠난다.)"),
                        ("", "(플레이어가 남겨 둔 기록과 보고서를 잠시 비춘다.)")
                    };
                    pages.AddRange(Monologue("플레이어", EndingScripts.RebelMonologue, 4));
                    pages.Add(("", "(사무실과 조사 환경으로 돌아온다.)"));
                    return pages;
                }
                case GameEndingType.BossHiddenEnding:
                {
                    var pages = new List<(string, string)>
                    {
                        ("", "(화면이 어두워진다. 플레이어는 최종 설계자와 마주한다.)"),
                        ("", "(설계자는 플레이어가 자신에게 도달했다는 사실을 알아차린다. 두 사람이 서로를 마주하고, 플레이어가 행동을 결심한다.)"),
                        ("", "(암전.)"),
                        ("", "(짧은 정적 뒤, 설계자는 더는 움직이지 않고 플레이어는 그 자리에 남아 있다.)")
                    };
                    pages.AddRange(Monologue("보스", EndingScripts.BossMonologue, 5));
                    pages.Add(("", "(플레이어의 사무실로 돌아온다.)"));
                    return pages;
                }
                case GameEndingType.PhoneCallRejected:
                    return new List<(string, string)>
                    {
                        ("", "(통화가 끊기고 연결 종료음이 들린다. 전화기를 내려놓으려는 순간 문밖에서 접근하는 소리가 난다.)"),
                        ("", "(문 쪽으로 돌아보는 순간 화면이 암전된다.)"),
                        ("", "(짧은 충돌음. 전화기가 책상 아래로 떨어진다.)"),
                        ("", "(바닥에 놓인 수화기에서 통화 연결음만 반복된다.)")
                    };
                default:
                    return null;
            }
        }

        private void ShowEndingCard()
        {
            var ending = Session.Endings.ExecutedEnding.Value;
            bool gameOver = Session.IsGameOver;
            Current = gameOver ? View.Ending : Current;
            if (gameOver) ClearLayers();
            var body = Ui.Window(_modalLayer, gameOver ? "게임 오버" : "엔딩", null, out _, 0.25f, 0.25f, 0.75f, 0.75f);
            Ui.Vertical(body, 14, 20);
            Ui.Line(body, EndingName(ending), 30);
            Ui.Line(body, gameOver ? "모든 행동이 막혔습니다. (게임 종료)" : "이 엔딩 뒤에도 의뢰 플레이는 계속됩니다.", 21, Ui.DimTextColor);
            if (gameOver) Ui.Button(body, "타이틀로", ShowTitle, true, 52, Ui.AccentColor);
            else Ui.Button(body, "계속", () => { Ui.Clear(_modalLayer); AfterStateChange(); }, true, 52, Ui.AccentColor);
        }

        public static string EndingName(GameEndingType e) => e switch
        {
            GameEndingType.Arrest => "체포 엔딩 — 담당 경찰 의심도 100",
            GameEndingType.OrganizationTrustZero => "조직 신뢰도 0 — 조직이 정보를 넘기고 플레이어는 살해된다",
            GameEndingType.PoliceAllEliminated => "경찰 전원 제거 엔딩",
            GameEndingType.Immigration => "이민 엔딩",
            GameEndingType.RebelRecruitAccepted => $"「{EndingScripts.RebelRecruitEndingName}」",
            GameEndingType.BossHiddenEnding => $"「{EndingScripts.PartOfTheWaterName}」",
            GameEndingType.PhoneCallRejected => $"「{EndingScripts.PriceOfSilenceName}」",
            _ => e.ToString()
        };

        // ───────────────────────── 저장 ─────────────────────────

        private void SaveGame()
        {
            if (Session == null || !Session.CanSave) { Toast("지금은 저장할 수 없습니다(회의 중)."); return; }
            try
            {
                System.IO.File.WriteAllText(SavePath, SaveGameService.ToJson(Session.CaptureSave()));
                Toast("저장했습니다.");
            }
            catch (Exception e)
            {
                Toast("저장 실패: " + e.Message);
            }
            RefreshHud();
        }

        private void LoadGame()
        {
            try
            {
                var data = SaveGameService.FromJson(System.IO.File.ReadAllText(SavePath));
                Session = GameSession.Load(data, new GameSessionConfig { Random = new System.Random(_seed + Environment.TickCount) });
                _resultShownFor = Session.LastContract?.ContractId;
                _endingShown = Session.Endings.ExecutedEnding;
                Toast(Session.Logger.Entries.Count > 0 ? $"불러왔습니다(복구 기록 {Session.Logger.Entries.Count}건 — F1에서 확인)." : "불러왔습니다.");
                if (Session.State.CurrentPhase == GamePhase.Investigation) GoBoard(); else GoDesk();
            }
            catch (Exception e)
            {
                Toast("불러오기 실패(손상된 파일일 수 있음): " + e.Message);
            }
        }

        // ───────────────────────── 개발용 패널 ─────────────────────────

        private void BuildDebugPanel()
        {
            Ui.Clear(_debugLayer);
            var s = Session;
            var panel = Ui.Anchor(Ui.Panel(_debugLayer, "Debug", new Color(0.15f, 0.02f, 0.02f, 0.92f)), 0.62f, 0.22f, 0.995f, 0.95f);
            var content = Ui.Scroll(Ui.Stretch(Ui.Rect(panel, "Scroll"), 4, 4, 4, 4), new Color(0, 0, 0, 0));
            Ui.Line(content, "개발용 패널 (플레이어 비노출 정보) — F1", 22);
            var c = s.CurrentContract ?? s.LastContract;
            if (c != null)
            {
                Ui.Line(content, $"[실제 진실] 유형 {PlaytestTexts.Crime(c.PrimaryCrimeType)}(경찰 분류 {PlaytestTexts.Crime(c.PoliceCrimeType)}) · 세부 {c.Truth.ActualSubtypeId} · 가해자 {PlaytestTexts.TempName(s, c.Truth.PerpetratorId)} / 피해자 {PlaytestTexts.TempName(s, c.Truth.VictimId)} / 방법 {c.Truth.Method} / 도구 {c.Truth.Tool} / 동기 {c.Truth.PrimaryMotive} / 은폐 {c.Truth.CoverUp}", 17);
                Ui.Line(content, $"[공식 결론] 지목 {c.OfficialConclusion.OfficiallyAccusedNpcId ?? "-"} · {c.OfficialConclusion.Outcome} · 확정 {c.OfficialConclusion.IsFinalized}", 17);
                if (c.PlannedMainCaseClue != null)
                    Ui.Line(content, $"[메인 단서 배치] {c.PlannedMainCaseClue} · 위치 {(c.PlannedClueObjectId != null ? PlaytestTexts.Room(ContractContent.CaseCatalog.ObjectRoom(c.PlannedClueObjectId)) + " " + ContractContent.CaseCatalog.ObjectName(c.PlannedClueObjectId) : "사건 현장")}" +
                                     (c.DocumentHolderNpcId != null ? $" · 보유자 {PlaytestTexts.TempName(s, c.DocumentHolderNpcId)}({c.DocumentHolderTag})" : "") +
                                     $" · 사건일 {c.IncidentDate} · 추가 피해자 {c.Truth.AdditionalVictimIds.Count}명", 17);
                foreach (var npc in s.Npcs.All.Where(n => n.ContractId == c.ContractId))
                    Ui.Line(content, $"  {PlaytestTexts.TempName(s, npc.NpcId)}: 실제 {npc.ActualRole} · 지식 {npc.KnownInfoIds.Count} · 믿음 {npc.Beliefs.Count} · 플레이어 의심 {s.Relationships.GetSuspicion(npc.NpcId, s.PlayerId):0} · 위치 {(s.Occupancy.HasPosition(npc.NpcId) ? PlaytestTexts.Room(s.Occupancy.GetRoom(npc.NpcId)) : "-")}", 16);
            }
            Ui.Line(content, "[경찰 의심도] " + string.Join(", ", PoliceRoster.AllPoliceIds.Select(p => $"{p[^1]}:{s.PoliceSuspicion.GetSuspicion(p, s.PlayerId):0}{(s.PoliceLife.IsAlive(p) ? "" : "†")}")) + $" · 추적 {s.Pursuit.Stage}", 17);
            if (c != null && s.CurrentContract != null)
                Ui.Line(content, "[개인 의견 → 실제 가해자] " + string.Join(", ", s.Opinions.All.Where(o => o.Key.Suspect == c.Truth.PerpetratorId)
                    .Select(o => $"{PlaytestTexts.TempName(s, o.Key.Observer)} {o.Value:0}")) + $" · 가해자 식별 {(s.Campaign.CulpritIdentified ? "완료" : "미완")}", 16);
            Ui.Line(content, $"[조직] 신뢰도 {s.Organization.Trust:0} · 메인 단서 {s.MainCase.AcquiredClueNames.Count}개 · 완료 의뢰 {s.MainCase.CompletedContractCount}", 17);
            Ui.Line(content, "[이벤트 로그 최근] " + string.Join(" | ", s.State.EventLog.GetOrdered().Reverse().Take(6).Select(e => e.EventId)), 15, Ui.DimTextColor);
            foreach (var e in s.Logger.Entries.Reverse().Take(5))
                Ui.Line(content, $"[GameLogger] {e.ErrorCode}: {e.Cause}", 15, new Color(1f, 0.6f, 0.5f));

            Ui.Line(content, "TEST_TEMP 진입점 (확정 해금 조건 아님)", 18, new Color(1f, 0.7f, 0.4f));
            Ui.Button(content, "조직 신뢰도 0으로 → 게임 오버 경로", () => { s.Organization.SetTrust(0); s.CheckEndings(); AfterStateChange(); });
            Ui.Button(content, "담당 경찰 의심도 100 → 체포 경로", () =>
            {
                if (s.HandlerPoliceId == null) { Toast("담당 경찰이 없습니다(의뢰 중에만)."); return; }
                s.PoliceSuspicion.SetSuspicion(s.HandlerPoliceId, s.PlayerId, 100f);
                s.CheckEndings();
                AfterStateChange();
            });
            Ui.Button(content, "메인 사건 단서 전부 지급 → 최종 보고서(UI-404) 진입", () =>
            {
                foreach (var f in typeof(MainCaseClueNames).GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(string)))
                    s.MainCase.AcquireClue((string)f.GetRawConstantValue());
                while (s.MainCase.TryAdvanceStage()) { }
                Toast("메인 사건 단서를 모두 지급했습니다: " + StageName(s.MainCase.CurrentStage));
                AfterStateChange();
            });
        }

        // ───────────────────────── 입력 ─────────────────────────

        private void Update()
        {
            if (_toastLayer.gameObject.activeSelf && Time.time > _toastUntil) _toastLayer.gameObject.SetActive(false);
            if (Session == null) return;

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.f1Key.wasPressedThisFrame) { _debugVisible = !_debugVisible; RefreshHud(); }
                if (kb.f5Key.wasPressedThisFrame) SaveGame();
                if (kb.f9Key.wasPressedThisFrame && System.IO.File.Exists(SavePath)) LoadGame();
            }

            Desk.SetRinging(Current == View.Desk && !_phone.InCall && (Session.State.CurrentPhase == GamePhase.Idle && !Session.IsGameOver
                                                     || Session.PostReport.PhoneStage == PhoneCallStage.AwaitingAnswer));
            Desk.SetLabelsVisible(!_phone.InCall);

            bool blocked = _phone.InCall || WindowLayer.childCount > 0 || _modalLayer.childCount > 0 || _field.DialogueOpen;
            // 마우스 시점(사용자 지정): 현장에서 창·대화·조사 메뉴가 없으면 커서를 잠그고 마우스 움직임으로 카메라를 돌린다.
            // 커서가 필요한 화면(창·대화창·모달·조사 메뉴)이나 Alt를 누르는 동안은 커서를 풀어 UI를 클릭할 수 있게 한다.
            bool mouseLook = Current == View.Board && !blocked && !_menuOpen && !(kb != null && (kb.leftAltKey.isPressed || kb.rightAltKey.isPressed));
            SetMouseLook(mouseLook);

            if (Current == View.Board)
            {
                UpdateFieldMarks(blocked);
                // 대화창이 덮는 화면 아래쪽 HUD(기록·조사 메뉴 버튼)는 대화 중에 숨긴다.
                foreach (var go in _hiddenDuringDialogue) if (go != null) go.SetActive(!_field.DialogueOpen);
                if (!_phone.InCall) UpdateBoardCamera(mouseLook);
                // 대화창의 대사는 클릭뿐 아니라 E로도 넘긴다(사용자 지정). 대화를 연 E와 같은 프레임에는 넘기지 않는다.
                if (_field.DialogueOpen && kb != null && kb.eKey.wasPressedThisFrame && Time.frameCount != _dialogueOpenedFrame)
                    _field.TryAdvance();
            }
            if (blocked) return;

            if (Current == View.Board)
            {
                if (kb != null && kb.tabKey.wasPressedThisFrame) { _menuOpen = !_menuOpen; RefreshHud(); }
                UpdatePlayerMovement(kb);
                if (kb != null && kb.eKey.wasPressedThisFrame) InteractNearest();
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            if (!Physics.Raycast(MainCamera.ScreenPointToRay(mouse.position.ReadValue()), out var hit, 500f)) return;

            if (Current == View.Desk)
            {
                var marker = hit.collider.GetComponentInParent<DeskObjectMarker>();
                if (marker != null) Interact(marker.Kind);
            }
        }

        // ───────────────────────── 현장 이동(WASD)·카메라(마우스) ─────────────────────────

        private float _camDistance = 4.5f;
        private float _camYaw;
        private const float CamPitch = 45f; // 위아래 고정(사용자 지정)
        private const float MouseLookSensitivity = 0.2f; // 도/픽셀
        private const float PlayerRadius = 0.25f;

        /// <summary>
        /// WASD 이동(사용자 지정): 카메라가 보는 방향 기준으로 걷고, 걷는 방향을 바라본다. 벽·계단은 막는다(인물 말은 통과).
        /// 다른 방 안쪽으로 들어서면 그 방으로 이동 판정을 한다(06: 이동 0시간). 판정이 거절되면 원래 자리로 되돌린다.
        /// </summary>
        private void UpdatePlayerMovement(Keyboard kb)
        {
            var s = Session;
            if (kb == null || s.State.CurrentPhase != GamePhase.Investigation || s.State.IsTimeBlockEnded) return;
            var input = new Vector2((kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0), (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0));
            if (input == Vector2.zero) return;
            var me = Board.PlayerPiece(s);
            if (me == null) return;
            me.GetComponent<PieceView>()?.Stop();

            var yaw = Quaternion.Euler(0, _camYaw, 0);
            var dir = (yaw * Vector3.forward * input.y + yaw * Vector3.right * input.x).normalized;
            var from = me.position;
            var step = dir * Rooms.TempLayoutV1.PlayerMoveAnimationSpeed * Time.deltaTime;
            var to = from + step;
            if (IsBlocked(to))
            {
                // 벽에 비스듬히 부딪히면 벽을 따라 미끄러진다.
                var alongX = from + new Vector3(step.x, 0, 0);
                var alongZ = from + new Vector3(0, 0, step.z);
                if (!IsBlocked(alongX)) to = alongX;
                else if (!IsBlocked(alongZ)) to = alongZ;
                else return;
            }
            me.position = to;
            me.rotation = Quaternion.Slerp(me.rotation, Quaternion.LookRotation(dir), 1f - Mathf.Exp(-14f * Time.deltaTime));

            string room = BoardView.RoomStrictlyAt(to);
            if (room == null || room == s.PlayerRoom) return;
            var r = s.MovePlayer(room);
            if (!r.Success)
            {
                me.position = from;
                Toast(PlaytestTexts.Failure(r.Failure));
                return;
            }
            AfterStateChange();
        }

        /// <summary>그 자리에 서면 벽·계단 같은 고정 충돌체와 겹치는지. 바닥·인물 말·책상 물건은 무시한다.</summary>
        private static bool IsBlocked(Vector3 p)
        {
            foreach (var c in Physics.OverlapCapsule(p + Vector3.up * 0.35f, p + Vector3.up * 0.9f, PlayerRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c.GetComponent<RoomFloorMarker>() != null || c.GetComponentInParent<PieceView>() != null) continue;
                if (c.GetComponentInParent<DeskObjectMarker>() != null) continue;
                return true;
            }
            return false;
        }

        /// <summary>카메라 자세: 캐릭터 머리 쪽을 중심으로 yaw·pitch 궤도, 휠로 정한 거리.</summary>
        private void CameraPose(Transform me, out Vector3 position, out Quaternion rotation)
        {
            var focus = me.position + Vector3.up * 0.6f;
            rotation = Quaternion.Euler(CamPitch, _camYaw, 0);
            position = focus - rotation * Vector3.forward * _camDistance;
        }

        private bool _mouseLook;

        private void SetMouseLook(bool on)
        {
            if (on == _mouseLook) return;
            _mouseLook = on;
            Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !on;
        }

        private void OnDisable() => SetMouseLook(false);

        /// <summary>
        /// 탐사 카메라(사용자 지정): 캐릭터를 중심으로 마우스를 좌우로 움직이면 좌우로만 돈다(커서 잠금 상태).
        /// 위아래 각도는 45도로 고정한다. 휠로 거리를 조절한다. 시작은 캐릭터 뒤쪽.
        /// </summary>
        private void UpdateBoardCamera(bool allowInput)
        {
            var me = Board.PlayerPiece(Session);
            if (me == null) return;
            var mouse = Mouse.current;
            if (mouse != null && allowInput)
            {
                _camYaw += mouse.delta.ReadValue().x * MouseLookSensitivity;
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    _camDistance = Mathf.Clamp(_camDistance - Mathf.Sign(scroll) * 0.6f, 2.5f, 12f);
            }
            CameraPose(me, out var pos, out var rot);
            var t = MainCamera.transform;
            float k = 1f - Mathf.Exp(-16f * Time.deltaTime);
            t.position = Vector3.Lerp(t.position, pos, k);
            t.rotation = Quaternion.LookRotation(me.position + Vector3.up * 0.6f - t.position);
            _boardCamPos = t.position;
            _boardCamRot = t.rotation;
        }

        // ───────────────────────── 접근 안내·상호작용 ─────────────────────────

        private readonly List<FieldHud.WorldMark> _nameMarks = new List<FieldHud.WorldMark>();
        private readonly List<Vector3> _talkIcons = new List<Vector3>();

        /// <summary>
        /// 09 §2·§4: 가까이 간 NPC·경찰·조사 대상의 이름 띠, 지금 E로 할 수 있는 행동 안내, 다른 NPC와 대화 중인 NPC 머리 위 대화 아이콘.
        /// 세 표시의 조건(거리·가능 행동·상태)은 따로 판정한다.
        /// </summary>
        private void UpdateFieldMarks(bool blocked)
        {
            var s = Session;
            _nameMarks.Clear();
            _talkIcons.Clear();
            if (blocked || s.State.CurrentPhase != GamePhase.Investigation)
            {
                _field.HideWorldUi();
                return;
            }

            foreach (var n in Board.NearbyTargets(s, FieldHud.NamePlateRange, includeDead: true))
                _nameMarks.Add(new FieldHud.WorldMark { Position = n.Position, Name = MarkName(n), IsPerson = n.Kind != BoardView.InteractableKind.Object });

            FieldHud.WorldMark? prompt = null;
            bool talk = false;
            string caption = null;
            if (!s.State.IsTimeBlockEnded)
            {
                var kind = Board.FindInteractable(s, out var id);
                switch (kind)
                {
                    case BoardView.InteractableKind.Object:
                        prompt = new FieldHud.WorldMark { Position = Board.ObjectPosition(id).Value, IsPerson = false };
                        caption = s.InvestigatedObjectIds.Contains(id) ? "조사 · 1시간 (조사함)" : "조사 · 1시간";
                        break;
                    case BoardView.InteractableKind.Npc:
                        prompt = new FieldHud.WorldMark { Position = Board.PiecePosition(id).Value, IsPerson = true };
                        talk = true;
                        caption = "대화 · 30분";
                        break;
                    case BoardView.InteractableKind.Police:
                        prompt = new FieldHud.WorldMark { Position = Board.PiecePosition(id).Value, IsPerson = true };
                        talk = true;
                        caption = "대화";
                        break;
                }
            }

            var mePos = Board.PiecePosition(s.PlayerId);
            foreach (var id in Board.TalkingIds)
            {
                var p = Board.PiecePosition(id);
                if (p.HasValue && mePos.HasValue && Vector3.Distance(p.Value, mePos.Value) <= FieldHud.StatusIconRange)
                    _talkIcons.Add(p.Value);
            }

            _field.UpdateWorldUi(MainCamera, _nameMarks, prompt, talk, caption, _talkIcons);
        }

        private string MarkName(BoardView.Nearby n) => n.Kind switch
        {
            BoardView.InteractableKind.Object => ContractContent.CaseCatalog.ObjectName(n.Id),
            BoardView.InteractableKind.Police => PlaytestTexts.PoliceName(n.Id) + (n.Id == Session.HandlerPoliceId ? " · 담당" : ""),
            _ => Session.Npcs.Get(n.Id).IsAlive
                ? PlaytestTexts.TempName(Session, n.Id)
                : $"{PlaytestTexts.TempName(Session, n.Id)} · {PlaytestTexts.PublicRole(Session, Session.Npcs.Get(n.Id))}"
        };

        /// <summary>가까이 있는 대상과 상호작용(E): 조사 오브젝트는 조사(1시간), NPC는 대화(30분), 담당 경찰은 대화.</summary>
        private int _dialogueOpenedFrame = -1;

        private void InteractNearest()
        {
            _dialogueOpenedFrame = Time.frameCount;
            if (Session.State.CurrentPhase != GamePhase.Investigation || Session.State.IsTimeBlockEnded) return;
            switch (Board.FindInteractable(Session, out var id))
            {
                case BoardView.InteractableKind.Object: InvestigateObject(id); break;
                case BoardView.InteractableKind.Npc: TalkDialogue(id); break;
                case BoardView.InteractableKind.Police: PoliceDialogue(id); break;
            }
        }

        /// <summary>
        /// NPC와 대화(30분, 06). 09 §3 대화창에 얻은 증언을 한 장씩 보이고, 끝나면 선택지(관찰·회유·대화 종료)를 보인다.
        /// 선택지 문구·구성은 09에서 별도 시각 설계 범위라 이미 있는 행동만 묶었다(TEST_TEMP).
        /// </summary>
        private void TalkDialogue(string npcId)
        {
            var s = Session;
            var result = s.TalkTo(npcId);
            if (!result.Success) { Act(result); return; }
            string name = PlaytestTexts.TempName(s, npcId);
            var lines = result.NewClueIds.Select(id => PlaytestTexts.MaterialBody(s, id)).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            if (lines.Count == 0) lines.Add("\"더 할 말은 없어요.\"");
            ReportResult(result, null);
            AfterStateChange();
            if (Session.State.CurrentPhase != GamePhase.Investigation || Current != View.Board) return;

            var choices = new List<(string, Action)>();
            bool canAct = !s.State.IsTimeBlockEnded;
            if (canAct && s.State.RemainingTimeSlots >= GameSession.TalkCost) choices.Add(("지켜본다 (관찰 30분)", () => ObserveNpc(npcId)));
            if (canAct && !s.Campaign.PersuasionAttempted) choices.Add(("회유한다 (1시간)", () => Persuade(npcId)));
            choices.Add(("대화를 마친다", null));
            _field.ShowDialogue(name, lines, choices);
        }

        /// <summary>
        /// 담당 경찰과 대화. 경찰 대화의 규칙·대사가 기획에 없어 시간을 쓰지 않고 정보도 주지 않는다(TEST_TEMP).
        /// 기존 행동인 의뢰 포기 알림만 선택지로 둔다.
        /// </summary>
        private void PoliceDialogue(string policeId)
        {
            var lines = new List<string> { "\"현장은 우리가 넘긴 개요 그대로요. 필요한 건 직접 확인하시오.\"" };
            var choices = new List<(string, Action)>
            {
                ("의뢰 포기를 알린다 (조직 신뢰도 -2)", ConfirmAbandon),
                ("대화를 마친다", null)
            };
            _field.ShowDialogue(PlaytestTexts.PoliceName(policeId), lines, choices);
        }
    }
}
