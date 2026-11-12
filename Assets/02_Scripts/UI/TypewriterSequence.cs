using System;
using System.Globalization;

namespace CaseFileOmission.UI
{
    /// <summary>
    /// 대사를 글자 하나씩 보여 주는 진행 계산(순수 C#). 한글은 완성된 음절 단위로 나오며 자음·모음으로 나누지 않는다.
    /// 문장부호 뒤에는 잠깐 멈춘다.
    /// </summary>
    public sealed class TypewriterSequence
    {
        private readonly string[] _elements;
        private readonly double _interval;
        private readonly double _sentencePause;
        private readonly double _commaPause;
        private double _timer;

        public string Text { get; }
        public int Length => _elements.Length;
        public int VisibleCount { get; private set; }
        public bool IsComplete => VisibleCount >= _elements.Length;

        /// <param name="charactersPerSecond">초당 글자 수</param>
        /// <param name="sentencePause">. ! ? … 뒤에 더 기다리는 시간(초)</param>
        /// <param name="commaPause">, 뒤에 더 기다리는 시간(초)</param>
        public TypewriterSequence(string text, float charactersPerSecond, float sentencePause = 0.28f, float commaPause = 0.12f)
        {
            if (charactersPerSecond <= 0f) throw new ArgumentOutOfRangeException(nameof(charactersPerSecond), "초당 글자 수는 0보다 커야 한다.");
            Text = text ?? string.Empty;
            _elements = SplitTextElements(Text);
            _interval = 1.0 / charactersPerSecond;
            _sentencePause = sentencePause;
            _commaPause = commaPause;
        }

        /// <summary>문자열을 화면에 보이는 글자(텍스트 요소) 단위로 나눈다. 한글 음절·이모지 등은 하나로 센다.</summary>
        public static string[] SplitTextElements(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
            var e = StringInfo.GetTextElementEnumerator(text);
            var list = new System.Collections.Generic.List<string>();
            while (e.MoveNext()) list.Add((string)e.Current);
            return list.ToArray();
        }

        public string GetElement(int index) => _elements[index];

        /// <summary>시간을 진행하고 이번에 새로 나타난 글자 수를 돌려준다.</summary>
        public int Advance(double deltaTime)
        {
            if (IsComplete || deltaTime <= 0) return 0;
            _timer += deltaTime;
            int revealed = 0;
            while (!IsComplete)
            {
                double wait = _interval + PauseAfter(VisibleCount - 1);
                if (_timer < wait) break;
                _timer -= wait;
                VisibleCount++;
                revealed++;
            }
            return revealed;
        }

        /// <summary>남은 글자를 한꺼번에 보여 준다.</summary>
        public void Complete()
        {
            VisibleCount = _elements.Length;
            _timer = 0;
        }

        /// <summary>index번째 글자가 화면에 보이는 글자인지(공백·문장부호가 아닌지). 음파를 크게 흔들 글자를 고를 때 쓴다.</summary>
        public bool IsVoiced(int index)
        {
            string s = _elements[index];
            if (string.IsNullOrWhiteSpace(s)) return false;
            char c = s[0];
            return char.IsLetterOrDigit(c);
        }

        private double PauseAfter(int index)
        {
            if (index < 0) return 0;
            string s = _elements[index];
            if (s == "." || s == "!" || s == "?" || s == "…" || s == "。") return _sentencePause;
            if (s == "," || s == "、") return _commaPause;
            return 0;
        }
    }
}
