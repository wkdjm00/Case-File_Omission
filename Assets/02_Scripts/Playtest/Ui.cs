using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CaseFileOmission.Playtest
{
    /// <summary>
    /// 임시 UI 부품(09 「테스트 빌드용 임시 UI 구현 기준」). 기본 패널·버튼·텍스트·입력창·스크롤만 쓴다.
    /// 외형(색·크기·글꼴)은 여기 한 곳에 모아 두어 최종 스킨으로 바꿀 때 화면 코드를 고치지 않게 한다.
    /// </summary>
    public static class Ui
    {
        public static Font Font;

        public static readonly Color PanelColor = new Color(0.09f, 0.10f, 0.11f, 0.94f);
        public static readonly Color SubPanelColor = new Color(0.16f, 0.17f, 0.19f, 0.96f);
        public static readonly Color ButtonColor = new Color(0.25f, 0.27f, 0.30f, 1f);
        public static readonly Color AccentColor = new Color(0.55f, 0.20f, 0.18f, 1f);
        public static readonly Color TextColor = new Color(0.93f, 0.92f, 0.86f, 1f);
        public static readonly Color DimTextColor = new Color(0.65f, 0.65f, 0.62f, 1f);

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Stretch(RectTransform rt, float left = 0, float bottom = 0, float right = 0, float top = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>앵커(0~1)로 화면 비율 영역에 놓는다.</summary>
        public static RectTransform Anchor(RectTransform rt, float minX, float minY, float maxX, float maxY)
        {
            rt.anchorMin = new Vector2(minX, minY);
            rt.anchorMax = new Vector2(maxX, maxY);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static RectTransform Panel(Transform parent, string name, Color? color = null)
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color ?? PanelColor;
            return rt;
        }

        public static Text Label(Transform parent, string text, int size = 22, Color? color = null, TextAnchor align = TextAnchor.UpperLeft)
        {
            var rt = Rect(parent, "Text");
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.fontSize = size;
            t.color = color ?? TextColor;
            t.alignment = align;
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>레이아웃 그룹 안에서 쓰는 줄 텍스트(높이는 내용에 맞춘다).</summary>
        public static Text Line(Transform parent, string text, int size = 22, Color? color = null)
        {
            var t = Label(parent, text, size, color);
            var fitter = t.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return t;
        }

        public static Button Button(Transform parent, string label, UnityAction onClick, bool interactable = true,
            float height = 44f, Color? color = null, int fontSize = 21)
        {
            var rt = Rect(parent, "Button:" + label);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color ?? ButtonColor;
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            button.interactable = interactable;
            if (onClick != null) button.onClick.AddListener(onClick);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;

            var text = Label(rt, label, fontSize, interactable ? TextColor : DimTextColor, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform, 8, 2, 8, 2);
            return button;
        }

        public static VerticalLayoutGroup Vertical(RectTransform rt, float spacing = 6f, int padding = 10, bool fitHeight = false)
        {
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(padding, padding, padding, padding);
            v.childControlHeight = true;
            v.childControlWidth = true;
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = true;
            if (fitHeight)
                rt.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return v;
        }

        public static HorizontalLayoutGroup Horizontal(RectTransform rt, float spacing = 6f, int padding = 0)
        {
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = new RectOffset(padding, padding, padding, padding);
            h.childControlHeight = true;
            h.childControlWidth = true;
            h.childForceExpandHeight = false;
            h.childForceExpandWidth = true;
            return h;
        }

        /// <summary>가로로 버튼을 늘어놓는 한 줄.</summary>
        public static RectTransform Row(Transform parent, float height = 44f)
        {
            var rt = Rect(parent, "Row");
            Horizontal(rt);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            return rt;
        }

        /// <summary>세로 스크롤 영역. 반환값은 내용을 넣을 content(세로 레이아웃).</summary>
        public static RectTransform Scroll(RectTransform area, Color? background = null)
        {
            var bg = area.gameObject.GetComponent<Image>() ?? area.gameObject.AddComponent<Image>();
            bg.color = background ?? SubPanelColor;
            var scroll = area.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.scrollSensitivity = 30f;

            var viewport = Stretch(Rect(area, "Viewport"));
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect(viewport, "Content");
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = content.offsetMax = Vector2.zero;
            Vertical(content, 6, 8, fitHeight: true);

            scroll.viewport = viewport;
            scroll.content = content;
            return content;
        }

        public static InputField Input(Transform parent, string placeholder, string value = "", bool multiLine = false, float height = 44f)
        {
            var rt = Rect(parent, "Input");
            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(0.05f, 0.05f, 0.06f, 1f);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;

            var text = Label(rt, "", 21);
            text.supportRichText = false;
            Stretch(text.rectTransform, 10, 6, 10, 6);
            var ph = Label(rt, placeholder, 21, DimTextColor);
            Stretch(ph.rectTransform, 10, 6, 10, 6);

            var input = rt.gameObject.AddComponent<InputField>();
            input.textComponent = text;
            input.placeholder = ph;
            input.lineType = multiLine ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            input.text = value ?? "";
            return input;
        }

        public static Toggle Toggle(Transform parent, string label, bool value, Action<bool> onChanged)
        {
            var rt = Rect(parent, "Toggle:" + label);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 36;
            le.preferredHeight = 36;

            var box = Rect(rt, "Box");
            box.anchorMin = new Vector2(0, 0.5f);
            box.anchorMax = new Vector2(0, 0.5f);
            box.sizeDelta = new Vector2(26, 26);
            box.anchoredPosition = new Vector2(15, 0);
            var boxImg = box.gameObject.AddComponent<Image>();
            boxImg.color = ButtonColor;
            var check = Rect(box, "Check");
            Stretch(check, 5, 5, 5, 5);
            var checkImg = check.gameObject.AddComponent<Image>();
            checkImg.color = AccentColor;

            var text = Label(rt, label, 20, TextColor, TextAnchor.MiddleLeft);
            Stretch(text.rectTransform, 38, 0, 0, 0);

            var toggle = rt.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = boxImg;
            toggle.graphic = checkImg;
            toggle.isOn = value;
            toggle.onValueChanged.AddListener(v => onChanged?.Invoke(v));
            return toggle;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }

        /// <summary>창 제목 + 닫기 버튼이 있는 전체 화면 패널. 반환값은 본문 영역.</summary>
        public static RectTransform Window(Transform parent, string title, UnityAction onClose, out RectTransform root,
            float minX = 0.03f, float minY = 0.04f, float maxX = 0.97f, float maxY = 0.96f)
        {
            root = Anchor(Panel(parent, "Window:" + title), minX, minY, maxX, maxY);
            var header = Anchor(Panel(root, "Header", SubPanelColor), 0, 1, 1, 1);
            header.offsetMin = new Vector2(0, -52);
            var titleText = Label(header, title, 26, TextColor, TextAnchor.MiddleLeft);
            Stretch(titleText.rectTransform, 16, 0, 160, 0);
            if (onClose != null)
            {
                var close = Button(header, "닫기", onClose, true, 40, AccentColor);
                var crt = (RectTransform)close.transform;
                crt.anchorMin = new Vector2(1, 0.5f);
                crt.anchorMax = new Vector2(1, 0.5f);
                crt.sizeDelta = new Vector2(130, 40);
                crt.anchoredPosition = new Vector2(-75, 0);
            }
            var body = Rect(root, "Body");
            Stretch(body, 10, 10, 10, 62);
            return body;
        }

        public static string Join(IEnumerable<string> lines) => string.Join("\n", lines);
    }
}
