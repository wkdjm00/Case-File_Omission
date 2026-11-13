using System;
using System.Linq;
using CaseFileOmission.Game;
using CaseFileOmission.Reports;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CaseFileOmission.Playtest
{
    /// <summary>보드 위 자료 카드 드래그·클릭. 놓은 위치를 화면에 알려 주기만 하고 판정은 하지 않는다.</summary>
    public sealed class BoardCardDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        public string ItemId;
        public Action<BoardCardDrag, PointerEventData> Dropped;
        public Action<BoardCardDrag> Clicked;
        private bool _dragging;

        public void OnBeginDrag(PointerEventData e)
        {
            _dragging = true;
            transform.SetAsLastSibling();
        }

        public void OnDrag(PointerEventData e)
        {
            var rt = (RectTransform)transform;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)rt.parent, e.position, e.pressEventCamera, out var local))
                rt.anchoredPosition = local;
        }

        public void OnEndDrag(PointerEventData e)
        {
            _dragging = false;
            Dropped?.Invoke(this, e);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (!_dragging && !e.dragging) Clicked?.Invoke(this);
        }
    }

    /// <summary>
    /// UI-003 사건보드(09 2026-11-13 계약). 코르크판(화면 전체가 아님) + 아래쪽 폐기 영역 + 중앙 메모.
    /// 자료를 끌어 옮기고, 아래로 끌어 버리고(보고서 원본 유지), 두 자료를 직접 골라 붉은 선으로 잇는다.
    /// 상태는 <see cref="CaseBoardState"/>(저장 대상)에 있고 이 화면은 그 상태를 그리기만 한다.
    /// </summary>
    public sealed class CaseBoardScreen
    {
        private readonly PlaytestGame _g;
        private RectTransform _boardArea, _discardZone;
        private string _selected, _linkFrom;
        private bool _linkMode;

        public CaseBoardScreen(PlaytestGame g) => _g = g;

        private GameSession S => _g.Session;

        public void Open() => Rebuild();

        private void Rebuild()
        {
            _g.CloseWindow();
            var s = S;
            var board = s.GetCaseBoard();
            var body = Ui.Window(_g.WindowLayer, "사건보드", () => { _g.CloseWindow(); _g.RefreshHud(); }, out _);
            if (board == null) { Ui.Line(body, "진행 중인 의뢰가 없습니다."); return; }

            // 코르크판과 아래쪽 폐기 영역
            _boardArea = Ui.Anchor(Ui.Panel(body, "Cork", new Color(0.62f, 0.45f, 0.28f)), 0, 0.17f, 0.74f, 1);
            _discardZone = Ui.Anchor(Ui.Panel(body, "DiscardZone", new Color(0.12f, 0.1f, 0.09f)), 0, 0, 0.74f, 0.15f);
            var dz = Ui.Label(_discardZone, "아래쪽 여백 — 자료를 여기로 끌어 내리면 보드에서 떼어 버립니다(보고서 원본은 남음)", 18, Ui.DimTextColor, TextAnchor.MiddleCenter);
            Ui.Stretch(dz.rectTransform);

            var lines = Ui.Stretch(Ui.Rect(_boardArea, "Lines"));
            var cards = Ui.Stretch(Ui.Rect(_boardArea, "Cards"));

            // 중앙 메모(연결선이 있을 때만)
            if (board.IsMemoVisible)
            {
                var memo = Ui.Anchor(Ui.Panel(_boardArea, "CenterMemo", new Color(0.98f, 0.93f, 0.55f)),
                    CaseBoardState.MemoMinX, CaseBoardState.MemoMinY, CaseBoardState.MemoMaxX, CaseBoardState.MemoMaxY);
                var t = Ui.Label(memo, "중앙 메모 — 내가 이은 관계\n" + string.Join("\n", board.BuildMemoLines(id => ShortTitle(id))), 15, new Color(0.15f, 0.12f, 0.1f));
                Ui.Stretch(t.rectTransform, 8, 6, 8, 6);
            }

            Canvas.ForceUpdateCanvases();
            var size = _boardArea.rect.size;

            foreach (var link in board.Links)
            {
                var a = board.Get(link.ItemA);
                var b = board.Get(link.ItemB);
                var pa = new Vector2((a.X - 0.5f) * size.x, (a.Y - 0.5f) * size.y);
                var pb = new Vector2((b.X - 0.5f) * size.x, (b.Y - 0.5f) * size.y);
                var line = Ui.Rect(lines, $"Link_{link.ItemA}_{link.ItemB}");
                line.anchorMin = line.anchorMax = new Vector2(0.5f, 0.5f);
                line.sizeDelta = new Vector2(Vector2.Distance(pa, pb), 5f);
                line.anchoredPosition = (pa + pb) * 0.5f;
                line.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(pb.y - pa.y, pb.x - pa.x) * Mathf.Rad2Deg);
                var img = line.gameObject.AddComponent<Image>();
                img.color = new Color(0.8f, 0.05f, 0.05f, 0.95f);
                var btn = line.gameObject.AddComponent<Button>();
                string la = link.ItemA, lb = link.ItemB;
                btn.onClick.AddListener(() => { board.Disconnect(la, lb); Rebuild(); });
            }

            foreach (var item in board.VisibleItems)
            {
                var card = Ui.Panel(cards, "Card_" + item.ItemId, item.BoardDecorationOnly ? new Color(0.9f, 0.88f, 0.8f) : CardColor(item.ItemId));
                card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
                card.sizeDelta = new Vector2(190, 92);
                card.anchoredPosition = new Vector2((item.X - 0.5f) * size.x, (item.Y - 0.5f) * size.y);
                card.localRotation = Quaternion.Euler(0, 0, item.RotationDegrees);
                if (item.ItemId == _selected || item.ItemId == _linkFrom)
                    card.gameObject.AddComponent<Outline>().effectColor = new Color(0.9f, 0.1f, 0.1f);
                var label = Ui.Label(card, item.BoardDecorationOnly ? "(관련 없는 메모지)" : ShortTitle(item.ItemId), 15, new Color(0.1f, 0.08f, 0.06f), TextAnchor.MiddleCenter);
                Ui.Stretch(label.rectTransform, 6, 4, 6, 4);

                var drag = card.gameObject.AddComponent<BoardCardDrag>();
                drag.ItemId = item.ItemId;
                drag.Dropped = OnDropped;
                drag.Clicked = OnClicked;
            }

            BuildSide(Ui.Anchor(Ui.Rect(body, "Side"), 0.75f, 0, 1, 1), board);
        }

        private void BuildSide(RectTransform side, CaseBoardState board)
        {
            var s = S;
            var content = Ui.Scroll(side);
            Ui.Button(content, _linkMode ? "연결 모드: 켜짐 (자료 두 개를 차례로 클릭)" : "연결 모드: 꺼짐", () => { _linkMode = !_linkMode; _linkFrom = null; Rebuild(); },
                true, 46, _linkMode ? Ui.AccentColor : (Color?)null, 17);
            Ui.Line(content, "붉은 선을 클릭하면 연결을 지웁니다. 연결은 내 추론일 뿐, 진실을 보증하지 않습니다.", 15, Ui.DimTextColor);

            if (_selected != null && board.Get(_selected) != null && !board.Get(_selected).BoardDecorationOnly)
            {
                Ui.Line(content, "선택한 자료", 19);
                Ui.Line(content, PlaytestTexts.MaterialTitle(s, _selected), 18);
                Ui.Line(content, PlaytestTexts.MaterialBody(s, _selected), 17);
                Ui.Line(content, "출처: " + PlaytestTexts.MaterialSource(s, _selected), 15, Ui.DimTextColor);
            }
            else if (_selected != null)
                Ui.Line(content, "관련 없는 메모지다. (장식 — 증거 아님)", 17, Ui.DimTextColor);

            Ui.Line(content, "보고서에 남아 있는 뗀 자료", 19);
            var removed = board.Items.Where(i => !i.OnBoard && !i.BoardDecorationOnly).ToList();
            if (removed.Count == 0) Ui.Line(content, "(없음)", 16, Ui.DimTextColor);
            foreach (var item in removed)
            {
                string id = item.ItemId;
                Ui.Button(content, "다시 붙이기: " + ShortTitle(id), () => { board.Restore(id); Rebuild(); }, true, 40, null, 15);
            }
        }

        private void OnClicked(BoardCardDrag card)
        {
            var board = S.GetCaseBoard();
            if (_linkMode)
            {
                if (_linkFrom == null) _linkFrom = card.ItemId;
                else
                {
                    if (!board.Connect(_linkFrom, card.ItemId)) _g.Toast("연결할 수 없습니다(같은 자료·이미 연결됨·장식 메모지).");
                    _linkFrom = null;
                }
            }
            _selected = card.ItemId;
            Rebuild();
        }

        private void OnDropped(BoardCardDrag card, PointerEventData e)
        {
            var board = S.GetCaseBoard();
            if (RectTransformUtility.RectangleContainsScreenPoint(_discardZone, e.position, e.pressEventCamera))
            {
                board.Discard(card.ItemId);
                if (_selected == card.ItemId) _selected = null;
            }
            else if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_boardArea, e.position, e.pressEventCamera, out var local))
            {
                var r = _boardArea.rect;
                board.Move(card.ItemId, (local.x - r.xMin) / r.width, (local.y - r.yMin) / r.height);
            }
            Rebuild();
        }

        private string ShortTitle(string id)
        {
            var t = PlaytestTexts.MaterialTitle(S, id);
            return t.Length > 28 ? t.Substring(0, 28) + "…" : t;
        }

        private Color CardColor(string id)
        {
            var m = PlaytestTexts.FindMaterial(S, id);
            if (m == null) return new Color(0.95f, 0.95f, 0.9f);
            return m.Format == Evidence.ClueFormat.Testimony ? new Color(1f, 0.95f, 0.55f) : new Color(0.85f, 0.9f, 0.95f);
        }
    }
}
