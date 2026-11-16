using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace CaseFileOmission.Playtest
{
    /// <summary>
    /// UI-404 타자기 연출(03 「⭐ 중요 확정 사항 — 타자기 보고서 연출」 3절, 구현 우선순위 1~4).
    /// 1) 글자 단위 자동 출력 2) 글자마다 타건음 3) 캐리지가 글자 칸을 따라 움직이고 줄바꿈 때 다음 줄 처음으로 돌아가며 리턴음
    /// 4) 한글은 한 칸 안에서 초성 → 초성+중성 → 완성 글자로 조합하고, 조합 중에는 캐리지가 그 칸에 머문다.
    /// 막 채운 빈칸 문구는 글자를 흩어 보였다가 올바른 순서로 재배열한 뒤 계속 친다(진행 방식 8).
    /// 키 입력 애니메이션(우선순위 5)은 넣지 않았다. 소리는 효과음 자원이 없어 코드로 만든 임시 음이다(TEST_TEMP).
    /// </summary>
    public sealed class TypewriterText : MonoBehaviour
    {
        public Text Target;
        public RectTransform Carriage;
        public string FullText = "";
        public int StartLength;
        public float StageDelay = 0.03f;
        public float ReturnDelay = 0.18f;
        /// <summary>막 채운 빈칸 문구의 시작 위치 → 길이. 이 구간은 재배열 연출 뒤 한 번에 나타난다.</summary>
        public Dictionary<int, int> RevealedRanges = new Dictionary<int, int>();
        public Action<int> OnProgress;
        public Action OnFinished;

        public bool IsTyping { get; private set; }

        private bool _skip;

        /// <summary>남은 출력을 기다림 없이 끝낸다(테스트 편의 — 연출 순서는 그대로).</summary>
        public void Skip() => _skip = true;

        private object Wait(float seconds) => _skip ? null : new WaitForSeconds(seconds);

        private static AudioClip _click, _return;
        private AudioSource _audio;
        private const string Cursor = "▌";
        private const int CarriageColumns = 46;

        private void Start()
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = 0.35f;
            _click ??= MakeClick();
            _return ??= MakeReturn();
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            IsTyping = true;
            var shown = new StringBuilder(FullText.Substring(0, Mathf.Clamp(StartLength, 0, FullText.Length)));
            Render(shown.ToString(), null);

            for (int i = shown.Length; i < FullText.Length; i++)
            {
                if (RevealedRanges.TryGetValue(i, out int len) && i + len <= FullText.Length)
                {
                    yield return Rearrange(shown, FullText.Substring(i, len));
                    shown.Append(FullText, i, len);
                    i += len - 1;
                    OnProgress?.Invoke(shown.Length);
                    continue;
                }

                char c = FullText[i];
                if (c == '\n')
                {
                    shown.Append(c);
                    Render(shown.ToString(), null);
                    Play(_return);
                    if (!_skip) yield return Wait(ReturnDelay);
                }
                else
                {
                    // 한글 음절은 같은 칸에서 단계적으로 조합한다(캐리지는 완성 뒤 다음 칸으로).
                    foreach (var stage in ComposeStages(c))
                    {
                        Render(shown.ToString(), stage);
                        Play(_click);
                        if (!_skip) yield return Wait(StageDelay);
                    }
                    shown.Append(c);
                }
                OnProgress?.Invoke(shown.Length);
            }

            Render(shown.ToString(), null);
            IsTyping = false;
            OnFinished?.Invoke();
        }

        /// <summary>입력한 핵심 단어의 글자를 흩어 보였다가 올바른 순서로 정렬한다.</summary>
        private IEnumerator Rearrange(StringBuilder shown, string word)
        {
            var letters = new List<char>(word);
            var random = new System.Random(word.GetHashCode());
            for (int step = 0; step < 6; step++)
            {
                for (int k = letters.Count - 1; k > 0; k--)
                {
                    int j = random.Next(k + 1);
                    (letters[k], letters[j]) = (letters[j], letters[k]);
                }
                Render(shown.ToString(), new string(letters.ToArray()));
                if (!_skip) yield return Wait(0.08f);
            }
            // 앞에서부터 한 글자씩 제자리를 찾는다.
            for (int fixedCount = 1; fixedCount <= word.Length; fixedCount++)
            {
                var rest = new List<char>(word.Substring(fixedCount));
                for (int k = rest.Count - 1; k > 0; k--)
                {
                    int j = random.Next(k + 1);
                    (rest[k], rest[j]) = (rest[j], rest[k]);
                }
                Render(shown.ToString(), word.Substring(0, fixedCount) + new string(rest.ToArray()));
                Play(_click);
                if (!_skip) yield return Wait(0.05f);
            }
        }

        private void Render(string committed, string composing)
        {
            if (Target == null) return;
            Target.text = committed + (composing ?? "") + Cursor;
            if (Carriage != null)
            {
                int lineStart = committed.LastIndexOf('\n') + 1;
                int column = committed.Length - lineStart;
                float x = Mathf.Clamp01((column % CarriageColumns) / (float)CarriageColumns);
                Carriage.anchorMin = new Vector2(x, Carriage.anchorMin.y);
                Carriage.anchorMax = new Vector2(x, Carriage.anchorMax.y);
            }
        }

        private void Play(AudioClip clip)
        {
            if (_audio != null && clip != null) _audio.PlayOneShot(clip);
        }

        private static readonly char[] Choseong =
            { 'ㄱ', 'ㄲ', 'ㄴ', 'ㄷ', 'ㄸ', 'ㄹ', 'ㅁ', 'ㅂ', 'ㅃ', 'ㅅ', 'ㅆ', 'ㅇ', 'ㅈ', 'ㅉ', 'ㅊ', 'ㅋ', 'ㅌ', 'ㅍ', 'ㅎ' };

        /// <summary>한글 음절이면 "초성 → 초성+중성(→ 종성 포함 완성)" 단계 문자열, 아니면 그 글자 하나.</summary>
        public static IEnumerable<string> ComposeStages(char c)
        {
            if (c < 0xAC00 || c > 0xD7A3)
            {
                yield return c.ToString();
                yield break;
            }
            int index = c - 0xAC00;
            int cho = index / (21 * 28);
            int jung = index % (21 * 28) / 28;
            int jong = index % 28;
            yield return Choseong[cho].ToString();
            yield return ((char)(0xAC00 + (cho * 21 + jung) * 28)).ToString();
            if (jong != 0) yield return c.ToString();
        }

        private static AudioClip MakeClick()
        {
            const int rate = 22050;
            int n = rate / 30;
            var data = new float[n];
            var r = new System.Random(7);
            for (int i = 0; i < n; i++)
                data[i] = (float)(r.NextDouble() * 2 - 1) * Mathf.Exp(-i / (n * 0.12f)) * 0.8f;
            var clip = AudioClip.Create("TypewriterClick_TEMP", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip MakeReturn()
        {
            const int rate = 22050;
            int n = rate / 3;
            var data = new float[n];
            for (int i = 0; i < n; i++)
                data[i] = Mathf.Sin(2 * Mathf.PI * 1760f * i / rate) * Mathf.Exp(-i / (n * 0.25f)) * 0.5f;
            var clip = AudioClip.Create("TypewriterReturn_TEMP", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
