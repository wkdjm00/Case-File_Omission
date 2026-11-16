using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CaseFileOmission.Playtest
{
    /// <summary>
    /// UI-404 떠다니는 단어(03 진행 방식 3·4·6·7). 핵심 단어와 함정 단어가 보고서 주변을 서로 다른 방향·속도로 천천히 떠다니고,
    /// 단어 자체는 선명하다. 단어가 보고서 글자에 가까이 가면 보고서 문장이 흐려지고 일그러지며, 멀어지면 다시 선명해진다.
    /// 문장에 필요한 단어를 모두 입력하면 모든 단어가 흐려진 뒤 사라진다(FadeOutAndDestroy).
    /// 함정 단어 목록은 노션에 없어(미정) 호출자가 임시 목록을 넘긴다.
    /// </summary>
    public sealed class FloatingWords : MonoBehaviour
    {
        public RectTransform Area;
        public RectTransform Paper;
        public Text PaperText;
        public Font Font;

        private sealed class Word
        {
            public RectTransform Rect;
            public Text Text;
            public Vector2 Velocity;
            public string Value;
        }

        private readonly List<Word> _words = new List<Word>();
        private Color _paperBase;
        private Vector2 _paperBasePos;
        private bool _fading;

        public void Build(IEnumerable<string> words, int seed)
        {
            var random = new System.Random(seed);
            _paperBase = PaperText != null ? PaperText.color : Color.black;
            _paperBasePos = PaperText != null ? PaperText.rectTransform.anchoredPosition : Vector2.zero;
            var size = Area.rect.size;
            foreach (var value in words)
            {
                var go = new GameObject("Word:" + value, typeof(RectTransform));
                go.transform.SetParent(Area, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(320, 34);
                rt.anchoredPosition = new Vector2((float)(random.NextDouble() - 0.5) * size.x * 0.9f, (float)(random.NextDouble() - 0.5) * size.y * 0.9f);
                var text = go.AddComponent<Text>();
                text.font = Font;
                text.fontSize = 22;
                text.alignment = TextAnchor.MiddleCenter;
                text.color = new Color(0.75f, 0.12f, 0.1f);
                text.raycastTarget = false;
                text.text = value;
                float angle = (float)(random.NextDouble() * Mathf.PI * 2);
                float speed = 12f + (float)random.NextDouble() * 26f;
                _words.Add(new Word { Rect = rt, Text = text, Value = value, Velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed });
            }
        }

        private void Update()
        {
            if (Area == null) return;
            var half = Area.rect.size * 0.5f;
            float nearest = float.MaxValue;
            foreach (var w in _words)
            {
                var p = w.Rect.anchoredPosition + w.Velocity * Time.deltaTime;
                if (Mathf.Abs(p.x) > half.x - 80) w.Velocity.x = -w.Velocity.x;
                if (Mathf.Abs(p.y) > half.y - 20) w.Velocity.y = -w.Velocity.y;
                w.Rect.anchoredPosition = new Vector2(Mathf.Clamp(p.x, -half.x + 80, half.x - 80), Mathf.Clamp(p.y, -half.y + 20, half.y - 20));
                // 이미 친 글자 영역(본문 Text의 크기는 친 줄만큼 늘어난다)까지의 거리. 종이 빈 곳을 떠다닐 때는 흐려지지 않는다.
                var target = PaperText != null ? PaperText.rectTransform : Paper;
                if (target != null) nearest = Mathf.Min(nearest, DistanceToRect(w.Rect.position, target));
            }

            if (PaperText == null || _fading) return;
            // 가까울수록 보고서 문장이 흐려지고 살짝 떨린다(멀어지면 원래대로).
            float t = _words.Count == 0 ? 0f : Mathf.Clamp01(1f - nearest / 40f);
            var c = _paperBase;
            c.a = Mathf.Lerp(_paperBase.a, 0.4f, t);
            PaperText.color = c;
            PaperText.rectTransform.anchoredPosition = _paperBasePos + (t > 0.01f ? Random.insideUnitCircle * 2.5f * t : Vector2.zero);
        }

        private static float DistanceToRect(Vector3 worldPoint, RectTransform rect)
        {
            var local = (Vector2)rect.InverseTransformPoint(worldPoint);
            var r = rect.rect;
            float dx = Mathf.Max(r.xMin - local.x, 0, local.x - r.xMax);
            float dy = Mathf.Max(r.yMin - local.y, 0, local.y - r.yMax);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>맞힌 핵심 단어를 떠다니는 영역에서 없앤다.</summary>
        public void Remove(string value)
        {
            var w = _words.Find(x => x.Value == value);
            if (w == null) return;
            Destroy(w.Rect.gameObject);
            _words.Remove(w);
        }

        /// <summary>모든 힌트·함정 단어를 점차 흐린 뒤 지운다.</summary>
        public void FadeOutAndDestroy(float seconds = 0.8f) => StartCoroutine(Fade(seconds));

        private IEnumerator Fade(float seconds)
        {
            _fading = true;
            if (PaperText != null) { PaperText.color = _paperBase; PaperText.rectTransform.anchoredPosition = _paperBasePos; }
            for (float e = 0; e < seconds; e += Time.deltaTime)
            {
                foreach (var w in _words)
                {
                    var c = w.Text.color;
                    c.a = 1f - e / seconds;
                    w.Text.color = c;
                }
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
