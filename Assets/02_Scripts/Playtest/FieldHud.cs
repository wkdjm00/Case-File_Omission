using System;
using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Core;
using CaseFileOmission.Game;
using CaseFileOmission.Police;
using UnityEngine;
using UnityEngine.UI;

namespace CaseFileOmission.Playtest
{
    /// <summary>
    /// 3D 사건 현장 화면에 쓰는 임시 UI 이미지(사용자가 만든 임시 시안 — 최종 자산 아님). 씬 빌더가 연결한다.
    /// </summary>
    [Serializable]
    public sealed class FieldArt
    {
        [Tooltip("NPC 머리 위 상태 아이콘: !, ?, 대화, 땀방울, 눈, 소리")]
        public Texture2D StatusExclaim, StatusQuestion, StatusTalk, StatusSweat, StatusEye, StatusSound;
        [Tooltip("접근 안내: E+대화 / E+조사")]
        public Texture2D PromptTalk, PromptInvestigate;
        [Tooltip("이름 띠: 인물 / 물건")]
        public Texture2D PersonNamePlate, ItemNamePlate;
        [Tooltip("의심도 목록 초상(TEST_TEMP: 인물 코드 해시로 고른다)")]
        public Texture2D[] Portraits = Array.Empty<Texture2D>();

        [Header("의뢰 보고서(임시 시안)")]
        public Texture2D ReportCover, ReportListPaper, ReportItem, ReportItemSelected, ReportDetail, ReportPhoto, ReportWriting;
        [Tooltip("단서 이미지(02_Textures/Clue). 자료 이름에 이미지 이름이 들어 있으면 그 이미지, 없으면 자료 종류별 대표 이미지(TEST_TEMP)")]
        public Texture2D[] ClueImages = Array.Empty<Texture2D>();

        /// <summary>자료에 붙일 단서 이미지. 이름이 겹치는 이미지 → 종류별 대표 이미지 순으로 고른다(TEST_TEMP).</summary>
        public Texture2D ClueImageFor(string title, Evidence.ClueFormat format)
        {
            if (ClueImages == null || ClueImages.Length == 0) return null;
            if (!string.IsNullOrEmpty(title))
            {
                var hit = ClueImages.Where(t => t != null && title.Contains(t.name.Replace('_', ' ')))
                    .OrderByDescending(t => t.name.Length).FirstOrDefault();
                if (hit != null) return hit;
            }
            string fallback = format switch
            {
                Evidence.ClueFormat.PhysicalObject => "내용물이 든 증거 봉투",
                Evidence.ClueFormat.Document => "서류철",
                _ => "휴대용 녹음기"
            };
            return ClueImages.FirstOrDefault(t => t != null && t.name == fallback);
        }
    }

    /// <summary>
    /// 09 「2026-11-16 — 3D 사건 현장 게임보드 HUD 및 NPC 상태 표시」의 임시 구현.
    /// HUD(왼쪽 조직 신뢰도 원형 게이지·중앙 인물별 의심도 목록·오른쪽 연도/월일/현재 시각·하단 잔여시간),
    /// NPC 머리 위 상태 아이콘, 접근 시 이름·상호작용 안내, 화면 아래 NPC 대화창을 그린다.
    /// 표시만 하며 게임 상태는 바꾸지 않는다(선택지 동작은 호출자가 넘긴다).
    /// </summary>
    public sealed class FieldHud
    {
        /// <summary>이름 표시 거리. 09 §4에서 미확정 — TEST_TEMP.</summary>
        public const float NamePlateRange = 3f;
        /// <summary>상태 아이콘 표시 거리. 09 §2 "초기 테스트용 제안값 5 m(미확정)".</summary>
        public const float StatusIconRange = 5f;

        private static readonly Color PaperColor = new Color(0.86f, 0.80f, 0.66f, 0.95f);
        private static readonly Color InkColor = new Color(0.16f, 0.12f, 0.10f, 1f);

        private readonly FieldArt _art;
        private readonly RectTransform _worldLayer, _dialogueLayer;
        private readonly List<RectTransform> _plates = new List<RectTransform>();
        private readonly List<RawImage> _icons = new List<RawImage>();
        private RectTransform _prompt;
        private RawImage _promptImage;
        private Text _promptCaption;

        private static Sprite _ring, _disc;
        private static Texture2D _gradient, _hourglass;

        public FieldHud(FieldArt art, RectTransform worldLayer, RectTransform dialogueLayer)
        {
            _art = art ?? new FieldArt();
            _worldLayer = worldLayer;
            _dialogueLayer = dialogueLayer;
        }

        public bool DialogueOpen => _dialogueLayer.childCount > 0;

        private Action _advance;

        /// <summary>대사가 보이는 중이면 다음으로 넘긴다(E 키). 선택지 단계에서는 아무것도 하지 않는다.</summary>
        public bool TryAdvance()
        {
            if (!DialogueOpen || _advance == null) return false;
            _advance();
            return true;
        }

        // ───────────────────────── HUD ─────────────────────────

        /// <summary>현장 HUD를 그린다. 값은 모두 실제 게임 데이터에서 읽는다.</summary>
        public void BuildHud(RectTransform parent, GameSession s)
        {
            BuildTrust(parent, s);
            BuildSuspicionList(parent, s);
            BuildClock(parent, s);
            BuildRemaining(parent, s);
        }

        /// <summary>왼쪽 세로형 종이 패널: 고정 조직 문양 + 바깥 원형 테두리가 시계 방향으로 채워지는 신뢰도 게이지.</summary>
        private void BuildTrust(RectTransform parent, GameSession s)
        {
            var panel = Ui.Anchor(Ui.Panel(parent, "OrgTrust", PaperColor), 0.006f, 0.50f, 0.096f, 0.94f);
            var title = Ui.Label(panel, "조직 신뢰도", 22, InkColor, TextAnchor.MiddleCenter);
            Ui.Anchor(title.rectTransform, 0, 0.84f, 1, 0.98f);

            var gauge = Ui.Rect(panel, "Gauge");
            gauge.anchorMin = gauge.anchorMax = new Vector2(0.5f, 0.52f);
            gauge.sizeDelta = new Vector2(140, 140);
            var back = gauge.gameObject.AddComponent<Image>();
            back.sprite = Ring;
            back.color = new Color(0.35f, 0.30f, 0.26f, 0.35f);
            var fill = Ui.Stretch(Ui.Rect(gauge, "Fill")).gameObject.AddComponent<Image>();
            fill.sprite = Ring;
            fill.color = new Color(0.55f, 0.12f, 0.10f, 1f);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Radial360;
            fill.fillOrigin = (int)Image.Origin360.Top;
            fill.fillClockwise = true;
            fill.fillAmount = Mathf.Clamp01(s.Organization.Trust / 100f);

            // 조직 문양: 정식 문양이 없어 임시 원판 + 글자(TEST_TEMP). 게이지와 달리 값에 따라 바뀌지 않는다.
            var emblem = Ui.Rect(gauge, "Emblem");
            emblem.anchorMin = emblem.anchorMax = new Vector2(0.5f, 0.5f);
            emblem.sizeDelta = new Vector2(96, 96);
            var disc = emblem.gameObject.AddComponent<Image>();
            disc.sprite = Disc;
            disc.color = new Color(0.20f, 0.15f, 0.12f, 1f);
            var mark = Ui.Label(emblem, "◆", 44, PaperColor, TextAnchor.MiddleCenter);
            Ui.Stretch(mark.rectTransform);

            var value = Ui.Label(panel, $"{s.Organization.Trust:0} / 100", 22, InkColor, TextAnchor.MiddleCenter);
            Ui.Anchor(value.rectTransform, 0, 0.06f, 1, 0.22f);
        }

        /// <summary>중앙 「의심도」 목록: 인물별 한 줄(왼쪽 얼굴 초상, 오른쪽 가로 게이지와 수치). 약 3명이 보이고 목록 전체를 위아래로 드래그해 스크롤한다.</summary>
        private void BuildSuspicionList(RectTransform parent, GameSession s)
        {
            var panel = Ui.Anchor(Ui.Panel(parent, "Suspicion", new Color(0.08f, 0.07f, 0.06f, 0.82f)), 0.37f, 0.785f, 0.63f, 0.99f);
            var title = Ui.Label(panel, "의심도", 22, Ui.TextColor, TextAnchor.MiddleCenter);
            Ui.Anchor(title.rectTransform, 0, 0.83f, 1, 1);
            var area = Ui.Anchor(Ui.Rect(panel, "List"), 0.02f, 0.03f, 0.98f, 0.83f);
            var content = Ui.Scroll(area, new Color(0, 0, 0, 0));
            var scroll = area.GetComponent<ScrollRect>();
            scroll.movementType = ScrollRect.MovementType.Clamped;
            content.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(4, 4, 2, 2);
            content.GetComponent<VerticalLayoutGroup>().spacing = 3;

            foreach (var (id, value) in SuspicionRows(s))
                SuspicionRow(content, id, value);
        }

        /// <summary>
        /// 목록에 올릴 인물: 이 의뢰의 살아 있는 NPC(그 NPC가 플레이어에게 갖는 관계 의심도)와 담당 경찰(경찰 의심도).
        /// 행 순서는 인물 코드 순(내부 ID 순서가 역할을 드러내지 않게).
        /// </summary>
        private static IEnumerable<(string id, float value)> SuspicionRows(GameSession s)
        {
            if (s.CurrentContract == null) yield break;
            string handler = s.HandlerPoliceId;
            if (handler != null && s.PoliceLife.IsAlive(handler))
                yield return (handler, s.PoliceSuspicion.GetSuspicion(handler, s.PlayerId));
            foreach (var id in PlaytestTexts.AllContractNpcIds(s))
            {
                var npc = s.Npcs.Get(id);
                if (npc.IsAlive) yield return (id, s.Relationships.GetSuspicion(id, s.PlayerId));
            }
        }

        private void SuspicionRow(RectTransform content, string id, float value)
        {
            var row = Ui.Rect(content, "Row:" + id);
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 50;

            var portrait = Ui.Rect(row, "Portrait");
            portrait.anchorMin = new Vector2(0, 0.5f);
            portrait.anchorMax = new Vector2(0, 0.5f);
            portrait.pivot = new Vector2(0, 0.5f);
            portrait.sizeDelta = new Vector2(46, 46);
            portrait.anchoredPosition = new Vector2(2, 0);
            var img = portrait.gameObject.AddComponent<RawImage>();
            img.texture = Portrait(id);
            img.raycastTarget = false;
            if (img.texture == null) img.color = new Color(0.4f, 0.4f, 0.4f);

            var bar = Ui.Panel(row, "Bar", new Color(0.22f, 0.20f, 0.18f, 1f));
            bar.anchorMin = new Vector2(0, 0.5f);
            bar.anchorMax = new Vector2(1, 0.5f);
            bar.offsetMin = new Vector2(58, -9);
            bar.offsetMax = new Vector2(-62, 9);
            bar.GetComponent<Image>().raycastTarget = false;
            var fill = Ui.Panel(bar, "Fill", Color.Lerp(new Color(0.75f, 0.65f, 0.35f), new Color(0.75f, 0.12f, 0.10f), value / 100f));
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(Mathf.Clamp01(value / 100f), 1);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            fill.GetComponent<Image>().raycastTarget = false;

            var text = Ui.Label(row, $"{value:0}", 22, Ui.TextColor, TextAnchor.MiddleRight);
            text.rectTransform.anchorMin = new Vector2(1, 0);
            text.rectTransform.anchorMax = new Vector2(1, 1);
            text.rectTransform.pivot = new Vector2(1, 0.5f);
            text.rectTransform.sizeDelta = new Vector2(56, 0);
            text.rectTransform.anchoredPosition = new Vector2(-4, 0);
        }

        private Texture2D Portrait(string id)
        {
            var list = _art.Portraits;
            if (list == null || list.Length == 0) return null;
            return list[GameSession.StableSeed("portrait:" + id) % list.Length];
        }

        /// <summary>오른쪽 세로 영역: 위 모래시계+작은 연도, 가운데 월/일, 아래 현재 시각.</summary>
        private static void BuildClock(RectTransform parent, GameSession s)
        {
            var date = WorldDate(s);
            var panel = Ui.Anchor(Ui.Panel(parent, "Clock", PaperColor), 0.895f, 0.66f, 0.994f, 0.94f);

            var icon = Ui.Rect(panel, "Hourglass");
            icon.anchorMin = icon.anchorMax = new Vector2(0.28f, 0.86f);
            icon.sizeDelta = new Vector2(30, 42);
            var raw = icon.gameObject.AddComponent<RawImage>();
            raw.texture = Hourglass;
            raw.color = InkColor;
            raw.raycastTarget = false;
            var year = Ui.Label(panel, date.Year.ToString(), 22, InkColor, TextAnchor.MiddleLeft);
            Ui.Anchor(year.rectTransform, 0.46f, 0.76f, 1, 0.96f);

            var day = Ui.Label(panel, $"{date.Month}월 {date.Day}일", 33, InkColor, TextAnchor.MiddleCenter);
            Ui.Anchor(day.rectTransform, 0, 0.40f, 1, 0.72f);

            var time = Ui.Label(panel, date.ToString("HH:mm"), 33, new Color(0.45f, 0.10f, 0.08f), TextAnchor.MiddleCenter);
            Ui.Anchor(time.rectTransform, 0, 0.06f, 1, 0.38f);
        }

        /// <summary>하단 가운데: 잔여시간(이번 조사 시간대의 남은 조사 예산). 현재 시각과 별도 영역.</summary>
        private static void BuildRemaining(RectTransform parent, GameSession s)
        {
            int minutes = s.State.RemainingTimeSlots * GameStateController.SlotMinutes;
            var panel = Ui.Anchor(Ui.Panel(parent, "Remaining", new Color(0.08f, 0.07f, 0.06f, 0.82f)), 0.42f, 0.008f, 0.58f, 0.06f);
            var t = Ui.Label(panel, $"잔여시간  {minutes / 60}시간 {minutes % 60:00}분", 22, Ui.TextColor, TextAnchor.MiddleCenter);
            Ui.Stretch(t.rectTransform);
        }

        private static DateTime WorldDate(GameSession s)
        {
            var contract = s.CurrentContract ?? s.LastContract;
            var date = DateTime.TryParse(contract?.IncidentDate, out var d) ? d : new DateTime(1984, 10, 1);
            return date.AddMinutes(s.WorldMinute);
        }

        // ───────────────────────── 화면 위 표시(매 프레임) ─────────────────────────

        public struct WorldMark
        {
            public Vector3 Position;
            public string Name;
            public bool IsPerson;
        }

        /// <summary>
        /// 근접 이름 표시(09 §4)·상호작용 안내·NPC 머리 위 상태 아이콘(09 §2)을 화면 좌표로 옮긴다.
        /// prompt가 null이면 상호작용 안내를 숨긴다. 세 표시는 서로 다른 조건으로 켜고 끈다.
        /// </summary>
        public void UpdateWorldUi(Camera cam, IReadOnlyList<WorldMark> names, WorldMark? prompt, bool promptIsTalk, string promptCaption,
            IReadOnlyList<(Vector3 pos, Texture2D tex)> statusIcons)
        {
            for (int i = 0; i < names.Count; i++)
            {
                var plate = Plate(i);
                bool shown = Place(cam, plate, names[i].Position + Vector3.up * (names[i].IsPerson ? 1.25f : 0.65f));
                plate.gameObject.SetActive(shown);
                if (!shown) continue;
                plate.GetComponent<RawImage>().texture = names[i].IsPerson ? _art.PersonNamePlate : _art.ItemNamePlate;
                plate.GetComponentInChildren<Text>().text = names[i].Name;
            }
            for (int i = names.Count; i < _plates.Count; i++) _plates[i].gameObject.SetActive(false);

            EnsurePrompt();
            bool promptShown = prompt.HasValue && Place(cam, _prompt, prompt.Value.Position + Vector3.up * (prompt.Value.IsPerson ? 1.25f : 0.65f));
            _prompt.gameObject.SetActive(promptShown);
            if (promptShown)
            {
                _prompt.anchoredPosition += new Vector2(0, -58);
                _promptImage.texture = promptIsTalk ? _art.PromptTalk : _art.PromptInvestigate;
                _promptCaption.text = promptCaption;
            }

            for (int i = 0; i < statusIcons.Count; i++)
            {
                var icon = Icon(i);
                bool shown = statusIcons[i].tex != null && Place(cam, (RectTransform)icon.transform, statusIcons[i].pos + Vector3.up * 1.6f);
                icon.gameObject.SetActive(shown);
                icon.texture = statusIcons[i].tex;
            }
            for (int i = statusIcons.Count; i < _icons.Count; i++) _icons[i].gameObject.SetActive(false);
        }

        public void HideWorldUi()
        {
            foreach (var p in _plates) p.gameObject.SetActive(false);
            foreach (var i in _icons) i.gameObject.SetActive(false);
            if (_prompt != null) _prompt.gameObject.SetActive(false);
        }

        private bool Place(Camera cam, RectTransform rt, Vector3 world)
        {
            var screen = cam.WorldToScreenPoint(world);
            if (screen.z <= 0.05f) return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_worldLayer, screen, null, out var local)) return false;
            rt.anchoredPosition = local;
            return true;
        }

        private RectTransform Plate(int index)
        {
            while (_plates.Count <= index)
            {
                var rt = Ui.Rect(_worldLayer, "NamePlate");
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(240, 54);
                var raw = rt.gameObject.AddComponent<RawImage>();
                raw.uvRect = new Rect(0.02f, 0.38f, 0.96f, 0.26f); // 시안의 붓 자국 띠 부분만
                raw.raycastTarget = false;
                var label = Ui.Label(rt, "", 22, Color.white, TextAnchor.MiddleCenter);
                Ui.Stretch(label.rectTransform, 16, 0, 16, 0);
                _plates.Add(rt);
            }
            return _plates[index];
        }

        private void EnsurePrompt()
        {
            if (_prompt != null) return;
            _prompt = Ui.Rect(_worldLayer, "InteractionPrompt");
            _prompt.anchorMin = _prompt.anchorMax = new Vector2(0.5f, 0.5f);
            // 잘라 쓰는 띠(원본의 92%×48%)의 가로세로 비율(약 2.9:1)을 그대로 지킨다 — 찌그러지지 않게.
            _prompt.sizeDelta = new Vector2(224, 78);
            _promptImage = _prompt.gameObject.AddComponent<RawImage>();
            _promptImage.uvRect = new Rect(0.04f, 0.27f, 0.92f, 0.48f); // E 키 + 아이콘 띠 부분만
            _promptImage.raycastTarget = false;
            _promptCaption = Ui.Label(_prompt, "", 22, Ui.TextColor, TextAnchor.UpperCenter);
            _promptCaption.rectTransform.anchorMin = new Vector2(-0.5f, 0);
            _promptCaption.rectTransform.anchorMax = new Vector2(1.5f, 0);
            _promptCaption.rectTransform.pivot = new Vector2(0.5f, 1);
            _promptCaption.rectTransform.sizeDelta = new Vector2(0, 30);
            _promptCaption.rectTransform.anchoredPosition = new Vector2(0, -2);
            _promptCaption.gameObject.AddComponent<Outline>().effectColor = Color.black;
        }

        private RawImage Icon(int index)
        {
            while (_icons.Count <= index)
            {
                var rt = Ui.Rect(_worldLayer, "StatusIcon");
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(56, 56);
                var raw = rt.gameObject.AddComponent<RawImage>();
                raw.raycastTarget = false;
                _icons.Add(raw);
            }
            return _icons[index];
        }

        // ───────────────────────── NPC 대화창 ─────────────────────────

        /// <summary>
        /// 09 §3 NPC 대화창: 화면 아래 어두운 그라데이션에 NPC 이름과 현재 대화 내용을 보이고, 클릭으로 넘긴다.
        /// 대화 내용이 끝나면 문구를 지우고 선택지를 보인다. 선택지를 고르면 창을 닫고 그 동작을 실행한다.
        /// </summary>
        public void ShowDialogue(string speaker, IList<string> lines, IList<(string label, Action act)> choices) =>
            ShowPages(lines.Select(l => (speaker, l)).ToList(), choices, speaker);

        /// <summary>
        /// 여러 화자의 대사를 한 장씩 보인다(회의 반응 등). 말하는 사람 이름 옆에 상태 표시(!·?)를 붙일 수 있다.
        /// 마지막 장 뒤에 선택지가 있으면 선택지를, 없으면 창을 닫는다.
        /// </summary>
        public void ShowPages(IList<(string speaker, string line)> pages, IList<(string label, Action act)> choices, string choiceSpeaker = "", int index = 0)
        {
            Ui.Clear(_dialogueLayer);
            _advance = null;
            var box = Ui.Anchor(Ui.Rect(_dialogueLayer, "Dialogue"), 0, 0, 1, 0.34f);
            var bg = box.gameObject.AddComponent<RawImage>();
            bg.texture = Gradient;
            bg.color = Color.white;

            string speaker = index < pages.Count ? pages[index].speaker : choiceSpeaker;
            var name = Ui.Label(box, speaker, 30, new Color(0.95f, 0.78f, 0.45f), TextAnchor.LowerLeft);
            Ui.Anchor(name.rectTransform, 0.16f, 0.66f, 0.84f, 0.86f);

            if (index < pages.Count)
            {
                var text = Ui.Label(box, pages[index].line, 26, Ui.TextColor, TextAnchor.UpperLeft);
                Ui.Anchor(text.rectTransform, 0.16f, 0.08f, 0.84f, 0.62f);
                var hint = Ui.Label(box, "클릭 또는 E로 계속 ▼", 18, Ui.DimTextColor, TextAnchor.LowerRight);
                Ui.Anchor(hint.rectTransform, 0.6f, 0.03f, 0.84f, 0.12f);
                // 대사는 자동으로 넘기지 않는다 — 클릭이나 E로만 진행한다.
                _advance = () => ShowPages(pages, choices, choiceSpeaker, index + 1);
                var click = box.gameObject.AddComponent<Button>();
                click.transition = Selectable.Transition.None;
                click.onClick.AddListener(() => _advance?.Invoke());
                return;
            }

            if (choices == null || choices.Count == 0) { CloseDialogue(); return; }
            var list = Ui.Anchor(Ui.Rect(box, "Choices"), 0.30f, 0.06f, 0.70f, 0.64f);
            Ui.Vertical(list, 6, 4);
            foreach (var (label, act) in choices)
            {
                var a = act;
                Ui.Button(list, label, () => { Ui.Clear(_dialogueLayer); a?.Invoke(); }, true, 44, new Color(0.12f, 0.11f, 0.10f, 0.92f), 22);
            }
        }

        public void CloseDialogue()
        {
            _advance = null;
            Ui.Clear(_dialogueLayer);
        }

        // ───────────────────────── 임시 도형 텍스처 ─────────────────────────

        private static Sprite Ring => _ring != null ? _ring : _ring = CircleSprite(0.70f);
        private static Sprite Disc => _disc != null ? _disc : _disc = CircleSprite(0f);

        private static Sprite CircleSprite(float inner)
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01((1f - r) * n * 0.5f) * Mathf.Clamp01((r - inner) * n * 0.5f);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }

        /// <summary>위로 갈수록 투명해지는 검은 그라데이션(대화창 바탕).</summary>
        private static Texture2D Gradient
        {
            get
            {
                if (_gradient != null) return _gradient;
                const int h = 64;
                _gradient = new Texture2D(1, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < h; y++)
                    _gradient.SetPixel(0, y, new Color(0, 0, 0, Mathf.Lerp(0.92f, 0f, Mathf.Pow(y / (h - 1f), 1.4f))));
                _gradient.Apply();
                return _gradient;
            }
        }

        /// <summary>임시 모래시계 아이콘(위아래 삼각형). 정식 아이콘이 생기면 교체한다.</summary>
        private static Texture2D Hourglass
        {
            get
            {
                if (_hourglass != null) return _hourglass;
                const int w = 30, h = 42;
                _hourglass = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool frame = y < 3 || y >= h - 3;
                    float t = Mathf.Abs(y - (h - 1) * 0.5f) / ((h - 1) * 0.5f); // 가운데 0 → 위아래 1
                    float half = 2f + t * (w * 0.5f - 4f);
                    bool glass = Mathf.Abs(x - (w - 1) * 0.5f) <= half && y >= 3 && y < h - 3;
                    _hourglass.SetPixel(x, y, frame || glass ? Color.white : new Color(1, 1, 1, 0));
                }
                _hourglass.Apply();
                return _hourglass;
            }
        }
    }
}
