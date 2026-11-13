using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace CaseFileOmission.UI
{
    /// <summary>
    /// 전화 통화 UI.
    /// - 상대(발신자) 대사: 수화기 옆 화면 위치에 음파 + 글자. 글자가 하나 나올 때마다 음파가 출렁인다.
    /// - 플레이어 대사·선택지: 화면 아래 어두운 그라데이션 위.
    /// 화면 구성은 실행 시 코드로 만든다(프리팹에는 이 컴포넌트와 폰트만 있으면 됨).
    /// </summary>
    public sealed class PhoneCallUI : MonoBehaviour
    {
        [Header("글자")]
        [Tooltip("한글 글자체(갈무리11). TMP 폰트 에셋이 없으면 실행 시 이 폰트로 동적 에셋을 만든다.")]
        [SerializeField] private Font sourceFont;
        [SerializeField] private TMP_FontAsset fontAsset;
        [SerializeField] private Color textColor = new Color(0.95f, 0.96f, 0.86f, 1f);
        [SerializeField] private Color glowColor = new Color(0.85f, 1f, 0.85f, 0.45f);
        [Tooltip("상대 대사 글자마다 붙는 어두운 녹색 번짐(밝은 배경에서도 글자가 또렷하게)")]
        [SerializeField] private Color callerUnderlayColor = new Color(0.05f, 0.10f, 0.06f, 0.5f);
        [SerializeField] private float callerFontSize = 34f;
        [SerializeField] private float playerFontSize = 32f;
        [Tooltip("초당 글자 수")]
        [SerializeField] private float charactersPerSecond = 18f;

        [Header("상대 대사 위치")]
        [Tooltip("상대 대사를 붙일 3D 위치(수화기의 듣는 쪽). 비우면 화면 위쪽 가운데.")]
        [SerializeField] private Transform callerAnchor;
        [Tooltip("붙인 위치(듣는 쪽)에서 대사 묶음까지의 화면상 오프셋(px, 1920x1080 기준). x: 글자 왼쪽 끝, y: 음파 기준선 높이.")]
        [SerializeField] private Vector2 callerScreenOffset = new Vector2(140f, -32f);
        [Tooltip("대사 한 줄 최대 폭(px). 넘으면 참고 이미지처럼 두 줄로 접힌다.")]
        [SerializeField] private float callerWidth = 640f;
        [SerializeField] private string callerLabel = "발신자";

        [Header("음파 (참고 이미지 비율: 글자 폭의 40% 지점에서 시작해 글자 오른쪽 끝을 살짝 넘김)")]
        [SerializeField, Range(16, 160)] private int waveBars = 90;
        [SerializeField] private float waveWidth = 360f;
        [Tooltip("가장 높이 솟은 막대의 전체 높이(px, 기준선 위아래 합)")]
        [SerializeField] private float waveMaxHeight = 120f;
        [Tooltip("글자 왼쪽 끝에서 음파가 시작하는 위치(px). 수화기 듣는 쪽 끝에서 음파가 뻗어 나오게 맞춘 값.")]
        [SerializeField] private float waveOffsetX = 0f;
        [Tooltip("음파 기준선에서 첫 줄 글자 윗선까지 간격(px)")]
        [SerializeField] private float waveToTextGap = 45f;

        [Header("아래 그라데이션")]
        [SerializeField] private float bottomGradientHeight = 460f;
        [SerializeField, Range(0f, 1f)] private float bottomGradientAlpha = 0.9f;
        [Tooltip("아래에서부터 이 비율까지는 진한 채로 두고, 그 위로 서서히 투명해진다")]
        [SerializeField, Range(0f, 0.9f)] private float bottomGradientSolid = 0.45f;

        [Header("상대 대사 뒤 번짐")]
        [Tooltip("대사 뒤에 깔리는 탁한 녹색 번짐")]
        [SerializeField] private Color callerHazeColor = new Color(0.22f, 0.30f, 0.22f, 0.45f);
        [Tooltip("음파·글자가 각각 차지한 영역보다 번짐이 바깥으로 더 퍼지는 폭(px)")]
        [SerializeField] private Vector2 callerHazePadding = new Vector2(340f, 220f);
        [Tooltip("글자 번짐만 양옆으로 더 늘리는 폭(px)")]
        [SerializeField] private float textHazeExtraWidth = 500f;
        [Tooltip("음파 번짐만 양옆으로 더 늘리는 폭(px)")]
        [SerializeField] private float waveHazeExtraWidth = 200f;

        [Header("넘기기")]
        [Tooltip("대사를 다 보여 준 뒤 자동으로 넘어가는 시간(초). 0이면 클릭해야 넘어간다.")]
        [SerializeField] private float autoAdvanceSeconds = 0f;

        private const float RefWidth = 1920f, RefHeight = 1080f;
        private const float WaveDeadZone = 0.05f;

        /// <summary>묶음 윗변에서 음파 기준선까지(px)</summary>
        private float WaveCenterFromTop => waveMaxHeight * 0.5f + 4f;

        private Canvas _canvas;
        private RectTransform _canvasRect;
        private RectTransform _callerGroup;
        private TextMeshProUGUI _callerText;
        private RectTransform[] _bars;
        private RectTransform _baseline;
        private RectTransform _playerGroup;
        private TextMeshProUGUI _playerText;
        private RectTransform _choiceList;
        private readonly List<GameObject> _choiceButtons = new List<GameObject>();
        private Texture2D _dashTexture, _gradientTexture, _hazeTexture;
        private Material _textMaterial, _callerMaterial;
        private RectTransform _waveHaze, _textHaze;

        private WaveformModel _wave;
        private TypewriterSequence _typing;
        private TextMeshProUGUI _typingTarget;
        private int _typingPrefix;
        private Action _onLineDone;
        private float _idleTimer;
        private bool _choosing;
        private int _lineStartFrame;

        public bool IsShowingLine => _typing != null;

        private void Awake()
        {
            BuildUi();
            HideAll();
        }

        private void OnDestroy()
        {
            if (_dashTexture != null) Destroy(_dashTexture);
            if (_gradientTexture != null) Destroy(_gradientTexture);
            if (_hazeTexture != null) Destroy(_hazeTexture);
            if (_textMaterial != null) Destroy(_textMaterial);
            if (_callerMaterial != null) Destroy(_callerMaterial);
        }

        // ── 공개 API ─────────────────────────────────────────────

        /// <summary>상대 대사를 수화기 옆에 글자 하나씩 보여 준다. 다 보여 주고 넘기면 onDone.</summary>
        public void ShowCallerLine(string text, Action onDone, string label = null)
        {
            _playerGroup.gameObject.SetActive(false);
            _callerGroup.gameObject.SetActive(true);
            string name = string.IsNullOrEmpty(label) ? callerLabel : label;
            string prefix = string.IsNullOrEmpty(name) ? string.Empty : name + ": ";
            StartLine(_callerText, prefix, text, onDone);
            FitHazeToCallerText();
        }

        /// <summary>플레이어 대사를 화면 아래 그라데이션 위에 보여 준다.</summary>
        public void ShowPlayerLine(string text, Action onDone)
        {
            _callerGroup.gameObject.SetActive(false);
            _playerGroup.gameObject.SetActive(true);
            _playerText.gameObject.SetActive(true);
            ClearChoices();
            StartLine(_playerText, string.Empty, text, onDone);
        }

        /// <summary>선택지를 화면 아래에 띄우고 마우스로 고르면 onChosen(번호). prompt가 있으면 목록 맨 위에 질문 줄을 둔다.</summary>
        public void ShowChoices(IList<string> choices, Action<int> onChosen, string prompt = null)
        {
            _typing = null;
            _callerGroup.gameObject.SetActive(false);
            _playerGroup.gameObject.SetActive(true);
            _playerText.gameObject.SetActive(false);
            ClearChoices();
            _choosing = true;
            if (!string.IsNullOrEmpty(prompt)) _choiceButtons.Add(CreatePromptRow(prompt));
            for (int i = 0; i < choices.Count; i++)
            {
                int index = i;
                _choiceButtons.Add(CreateChoiceButton(choices[i], () =>
                {
                    if (!_choosing) return;
                    _choosing = false;
                    ClearChoices();
                    onChosen?.Invoke(index);
                }));
            }
        }

        public void HideAll()
        {
            _typing = null;
            _choosing = false;
            ClearChoices();
            if (_callerGroup != null) _callerGroup.gameObject.SetActive(false);
            if (_playerGroup != null) _playerGroup.gameObject.SetActive(false);
        }

        // ── 진행 ────────────────────────────────────────────────

        private void StartLine(TextMeshProUGUI target, string prefix, string text, Action onDone)
        {
            _choosing = false;
            _typingTarget = target;
            _typingPrefix = TypewriterSequence.SplitTextElements(prefix).Length;
            _typing = new TypewriterSequence(text, charactersPerSecond);
            _onLineDone = onDone;
            _idleTimer = 0f;
            _lineStartFrame = Time.frameCount;   // 선택지를 누른 같은 프레임의 클릭으로 바로 넘어가지 않게
            target.text = prefix + text;
            target.maxVisibleCharacters = _typingPrefix;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_typing != null)
            {
                int before = _typing.VisibleCount;
                int revealed = _typing.Advance(dt);
                for (int k = 0; k < revealed; k++)
                    _wave.Pulse(_typing.IsVoiced(before + k) ? 1f : 0.15f);
                _typingTarget.maxVisibleCharacters = _typingPrefix + _typing.VisibleCount;

                bool clicked = Time.frameCount > _lineStartFrame && ClickedThisFrame();
                if (!_typing.IsComplete)
                {
                    if (clicked) _typing.Complete();
                }
                else
                {
                    _idleTimer += dt;
                    if (clicked || (autoAdvanceSeconds > 0f && _idleTimer >= autoAdvanceSeconds)) FinishLine();
                }
            }

            _wave.Update(dt);
            UpdateWaveBars();
            UpdateCallerPosition();
        }

        /// <summary>글자 번짐을 이번 대사 전체(다 나왔을 때 크기)에 맞춘다.</summary>
        private void FitHazeToCallerText()
        {
            if (_textHaze == null) return;
            float width = _callerText.rectTransform.rect.width;
            Vector2 textSize = _callerText.GetPreferredValues(_callerText.text, width, 0f);
            float w = Mathf.Min(textSize.x, width);
            float top = WaveCenterFromTop + waveToTextGap;
            _textHaze.anchoredPosition = new Vector2(w * 0.5f, -(top + textSize.y * 0.5f));
            _textHaze.sizeDelta = new Vector2(w + textHazeExtraWidth * 2f, textSize.y) + callerHazePadding * 2f;
        }

        /// <summary>대사 묶음 안의 번짐 하나(가운데 기준으로 놓음)</summary>
        private RectTransform NewHaze(string name)
        {
            var rect = NewRect(name, _callerGroup);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            var image = rect.gameObject.AddComponent<RawImage>();
            image.texture = _hazeTexture;
            image.color = callerHazeColor;
            image.raycastTarget = false;
            return rect;
        }

        private void FinishLine()
        {
            _typingTarget.maxVisibleCharacters = int.MaxValue;
            _typing = null;
            var done = _onLineDone;
            _onLineDone = null;
            done?.Invoke();
        }

        private static bool ClickedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }

        private void UpdateWaveBars()
        {
            if (_bars == null || !_callerGroup.gameObject.activeSelf) return;
            // 화면이 작아 캔버스가 줄어도 실제 화면에서 최소 1픽셀 굵기는 남게(안 그러면 가는 선이 사라졌다 나타났다 함)
            float minPx = 1f / Mathf.Max(_canvas.scaleFactor, 0.01f);
            float lineWidth = Mathf.Max(2f, minPx);   // 참고 이미지처럼 가늘게(실제 화면 1픽셀 이상)
            _baseline.sizeDelta = new Vector2(_baseline.sizeDelta.x, lineWidth);
            for (int i = 0; i < _bars.Length; i++)
            {
                // 잦아드는 작은 출렁임은 무시하고, 솟을 때만 기준선을 가로지르는 가는 세로선을 그린다.
                float a = Mathf.Clamp01((_wave.GetAmplitude(i) - WaveDeadZone) / (1f - WaveDeadZone));
                var raw = _bars[i].GetComponent<RawImage>();
                raw.enabled = a > 0f;
                if (a > 0f) _bars[i].sizeDelta = new Vector2(lineWidth, lineWidth + a * waveMaxHeight);
            }
        }

        private void UpdateCallerPosition()
        {
            if (!_callerGroup.gameObject.activeSelf) return;
            Vector2 pos = new Vector2(RefWidth * 0.5f, RefHeight * 0.72f);
            Camera cam = Camera.main;
            if (callerAnchor != null && cam != null)
            {
                Vector3 sp = cam.WorldToScreenPoint(callerAnchor.position);
                if (sp.z > 0f)
                {
                    Vector2 scale = new Vector2(_canvasRect.rect.width / Screen.width, _canvasRect.rect.height / Screen.height);
                    // 글자 왼쪽 끝이 듣는 쪽 오른편에, 음파 기준선이 듣는 쪽 높이에 오도록 묶음 왼쪽 위 모서리를 역산
                    pos = new Vector2(sp.x * scale.x, sp.y * scale.y) + callerScreenOffset + new Vector2(0f, WaveCenterFromTop);
                }
            }
            // 화면 밖으로 나가지 않게
            Vector2 size = _callerGroup.sizeDelta;
            float w = _canvasRect.rect.width, hgt = _canvasRect.rect.height;
            pos.x = Mathf.Clamp(pos.x, 20f, w - size.x - 20f);
            pos.y = Mathf.Clamp(pos.y, size.y + 20f, hgt - 20f);
            _callerGroup.anchoredPosition = pos;
        }

        // ── 화면 구성 ───────────────────────────────────────────

        private void BuildUi()
        {
            if (fontAsset == null && sourceFont != null)
            {
                fontAsset = TMP_FontAsset.CreateFontAsset(sourceFont);
                if (fontAsset != null) fontAsset.isMultiAtlasTexturesEnabled = true;
            }

            var canvasGo = new GameObject("PhoneCallCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 50;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
            scaler.matchWidthOrHeight = 0.5f;
            _canvasRect = canvasGo.GetComponent<RectTransform>();

            EnsureEventSystem();
            _dashTexture = MakeDashTexture();
            _gradientTexture = MakeGradientTexture(bottomGradientSolid);
            _hazeTexture = MakeHazeTexture();

            // 상대 대사 묶음(왼쪽 아래 기준으로 위치를 옮김)
            _callerGroup = NewRect("CallerLine", _canvasRect);
            _callerGroup.anchorMin = _callerGroup.anchorMax = Vector2.zero;
            _callerGroup.pivot = new Vector2(0f, 1f);
            // 글자가 커서 긴 대사는 참고 이미지처럼 두 줄로 접힌다
            _callerGroup.sizeDelta = new Vector2(Mathf.Max(waveOffsetX + waveWidth, callerWidth), WaveCenterFromTop + waveToTextGap + callerFontSize * 3.4f);

            // 뒤에 깔리는 탁한 녹색 번짐 두 개(음파 뒤, 글자 뒤). 먼저 만들어야 맨 뒤에 그려진다.
            // 음파 번짐은 고정, 글자 번짐은 대사마다 글자 영역에 맞춘다(FitHazeToCallerText).
            _waveHaze = NewHaze("WaveHaze");
            _waveHaze.anchoredPosition = new Vector2(waveOffsetX + waveWidth * 0.5f, -WaveCenterFromTop);
            _waveHaze.sizeDelta = new Vector2(waveWidth + waveHazeExtraWidth * 2f, waveMaxHeight) + callerHazePadding * 2f;
            _textHaze = NewHaze("TextHaze");

            var wave = NewRect("Waveform", _callerGroup);
            wave.anchorMin = wave.anchorMax = new Vector2(0f, 1f);
            wave.pivot = new Vector2(0f, 0.5f);
            wave.sizeDelta = new Vector2(waveWidth, waveMaxHeight + 8f);
            wave.anchoredPosition = new Vector2(waveOffsetX, -WaveCenterFromTop);
            _wave = new WaveformModel(waveBars);

            // 기준선: 처음부터 늘 보이는 가로 점선(쉴 때도 음파 자리가 보이게)
            _baseline = NewRect("Baseline", wave);
            _baseline.anchorMin = new Vector2(0f, 0.5f);
            _baseline.anchorMax = new Vector2(1f, 0.5f);
            _baseline.pivot = new Vector2(0.5f, 0.5f);
            _baseline.sizeDelta = new Vector2(0f, 2f);
            var baseImage = _baseline.gameObject.AddComponent<RawImage>();
            baseImage.texture = _dashTexture;
            baseImage.uvRect = new Rect(0f, 0f, waveWidth / 5f, 1f);   // 5px마다 점 하나(화면이 작으면 옅은 실선으로 뭉개져 사라지지 않음)
            baseImage.color = textColor;
            baseImage.raycastTarget = false;

            _bars = new RectTransform[waveBars];
            // 막대 간격을 정수 px로 맞춰 점선·세로선이 고르게 보이게(소수 간격이면 점이 뭉치거나 굵기가 들쭉날쭉)
            float step = Mathf.Max(3f, Mathf.Round(waveWidth / waveBars));
            for (int i = 0; i < waveBars; i++)
            {
                var bar = NewRect("Bar" + i, wave);
                bar.anchorMin = bar.anchorMax = new Vector2(0f, 0.5f);
                bar.pivot = new Vector2(0.5f, 0.5f);
                bar.anchoredPosition = new Vector2(step * (i + 0.5f), 0f);
                bar.sizeDelta = new Vector2(2f, 2f);
                var raw = bar.gameObject.AddComponent<RawImage>();
                raw.color = textColor;   // 텍스처 없음 = 단색 세로선
                raw.enabled = false;
                raw.raycastTarget = false;
                _bars[i] = bar;
            }

            _callerText = NewText("CallerText", _callerGroup, callerFontSize, TextAlignmentOptions.TopLeft);
            var ct = _callerText.rectTransform;
            ct.anchorMin = new Vector2(0f, 0f);
            ct.anchorMax = new Vector2(1f, 1f);
            ct.offsetMin = new Vector2(0f, 0f);
            ct.anchorMax = new Vector2(0f, 1f);
            ct.offsetMax = new Vector2(callerWidth, -(WaveCenterFromTop + waveToTextGap));
            // 상대 대사만 글자마다 녹색으로 번지게(밑깔림을 넓고 흐리게)
            if (_textMaterial != null)
            {
                _callerMaterial = new Material(_textMaterial);
                _callerMaterial.EnableKeyword("UNDERLAY_ON");
                _callerMaterial.SetColor("_UnderlayColor", callerUnderlayColor);
                _callerMaterial.SetFloat("_UnderlayOffsetX", 0f);
                _callerMaterial.SetFloat("_UnderlayOffsetY", 0f);
                _callerMaterial.SetFloat("_UnderlayDilate", 0.2f);
                _callerMaterial.SetFloat("_UnderlaySoftness", 1f);
                _callerText.fontSharedMaterial = _callerMaterial;
            }

            // 플레이어 대사·선택지(화면 아래 그라데이션)
            _playerGroup = NewRect("PlayerArea", _canvasRect);
            _playerGroup.anchorMin = new Vector2(0f, 0f);
            _playerGroup.anchorMax = new Vector2(1f, 0f);
            _playerGroup.pivot = new Vector2(0.5f, 0f);
            _playerGroup.sizeDelta = new Vector2(0f, bottomGradientHeight);
            var grad = _playerGroup.gameObject.AddComponent<RawImage>();
            grad.texture = _gradientTexture;
            grad.color = new Color(0f, 0f, 0f, bottomGradientAlpha);
            grad.raycastTarget = false;

            _playerText = NewText("PlayerText", _playerGroup, playerFontSize, TextAlignmentOptions.Bottom);
            var pt = _playerText.rectTransform;
            pt.anchorMin = new Vector2(0.15f, 0f);
            pt.anchorMax = new Vector2(0.85f, 1f);
            pt.offsetMin = new Vector2(0f, 70f);
            pt.offsetMax = new Vector2(0f, -40f);

            _choiceList = NewRect("Choices", _playerGroup);
            // 참고 이미지처럼 화면 왼쪽 아래에 촘촘히 쌓인 목록
            _choiceList.anchorMin = new Vector2(0.08f, 0f);
            _choiceList.anchorMax = new Vector2(0.6f, 1f);
            _choiceList.offsetMin = new Vector2(0f, 50f);
            _choiceList.offsetMax = new Vector2(0f, -60f);
            var layout = _choiceList.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.LowerLeft;
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        private GameObject CreateChoiceButton(string label, Action onClick)
        {
            var rect = NewRect("Choice", _choiceList);
            // 배경 상자 없이 글자만. 투명 이미지는 클릭 영역용.
            var hit = rect.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            var size = rect.gameObject.AddComponent<LayoutElement>();
            size.minHeight = playerFontSize + 10f;

            var text = NewText("Label", rect, playerFontSize, TextAlignmentOptions.Left);
            text.text = "> " + label;
            text.raycastTarget = false;
            var tr = text.rectTransform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = Vector2.zero;
            tr.offsetMax = Vector2.zero;

            // 마우스를 올리면 글자가 밝아진다
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = text;
            var colors = button.colors;
            colors.normalColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.6f, 0.6f, 0.6f, 1f);
            colors.selectedColor = colors.normalColor;
            colors.fadeDuration = 0.06f;
            button.colors = colors;
            button.onClick.AddListener(() => onClick());
            return rect.gameObject;
        }

        /// <summary>선택지 위의 질문 줄(클릭 안 됨, '>' 없음)</summary>
        private GameObject CreatePromptRow(string prompt)
        {
            var rect = NewRect("Prompt", _choiceList);
            var size = rect.gameObject.AddComponent<LayoutElement>();
            size.minHeight = playerFontSize + 18f;
            var text = NewText("Label", rect, playerFontSize, TextAlignmentOptions.Left);
            text.text = prompt;
            var tr = text.rectTransform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = tr.offsetMax = Vector2.zero;
            return rect.gameObject;
        }

        private void ClearChoices()
        {
            foreach (var go in _choiceButtons) if (go != null) Destroy(go);
            _choiceButtons.Clear();
        }

        private TextMeshProUGUI NewText(string name, RectTransform parent, float size, TextAlignmentOptions align)
        {
            var rect = NewRect(name, parent);
            var t = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (fontAsset != null) t.font = fontAsset;
            t.fontSize = size;
            t.color = textColor;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
            // 은은한 빛번짐(첫 참고 이미지처럼 연한 흰빛)
            if (_textMaterial == null && t.fontSharedMaterial != null)
            {
                _textMaterial = new Material(t.fontSharedMaterial);
                _textMaterial.EnableKeyword("GLOW_ON");
                _textMaterial.SetColor("_GlowColor", glowColor);
                // 넓게 퍼지면 글자가 뿌옇게 뭉개지므로 글자 가까이에만 옅게
                _textMaterial.SetFloat("_GlowOuter", 0.2f);
                _textMaterial.SetFloat("_GlowPower", 1f);
            }
            if (_textMaterial != null) t.fontSharedMaterial = _textMaterial;
            return t;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<InputSystemUIInputModule>();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
        }


        /// <summary>켜짐·꺼짐 두 칸 — 가로로 반복해 기준선 점선을 그린다. 부드럽게 걸러서 작게 보여도 끊겨 사라지지 않는다.</summary>
        private static Texture2D MakeDashTexture()
        {
            var tex = new Texture2D(2, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "PhoneCallDash" };
            tex.SetPixels32(new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 110) });
            tex.Apply();
            return tex;
        }

        /// <summary>아래가 진하고 위로 갈수록 투명해지는 세로 그라데이션. 아래 solid 비율까지는 진한 채로 유지한다.</summary>
        private static Texture2D MakeGradientTexture(float solid)
        {
            const int h = 128;
            var tex = new Texture2D(1, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "PhoneCallGradient" };
            for (int y = 0; y < h; y++)
            {
                float t = y / (h - 1f);
                float a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(solid, 1f, t));
                tex.SetPixel(0, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            return tex;
        }

        /// <summary>가운데가 진하고 가장자리로 갈수록 부드럽게 사라지는 타원 번짐</summary>
        private static Texture2D MakeHazeTexture()
        {
            const int w = 128, h = 64;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "PhoneCallHaze" };
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = (x + 0.5f) / w * 2f - 1f;
                    float dy = (y + 0.5f) / h * 2f - 1f;
                    // 초타원(지수 3): 원보다 살짝 네모나지만, 가장자리를 아주 넓게 흐려 모서리가 거의 드러나지 않게
                    float r = Mathf.Pow(Mathf.Pow(Mathf.Abs(dx), 3f) + Mathf.Pow(Mathf.Abs(dy), 3f), 1f / 3f);
                    float a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(r));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Pow(a, 1.8f)));
                }
            tex.Apply();
            return tex;
        }
    }
}
