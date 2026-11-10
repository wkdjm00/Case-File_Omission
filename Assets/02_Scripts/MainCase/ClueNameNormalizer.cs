using System.Text;

namespace CaseFileOmission.MainCase
{
    /// <summary>
    /// 최종 보고서에 입력한 단서명을 비교하기 전에 표기를 맞춘다.
    /// - 한글 조합형(자모 분리)과 완성형을 같은 문자로 취급하도록 NFC로 정규화한다.
    /// - 가운뎃점 변형(·, ‧, ・, ･, ㆍ)을 원문 표기인 '·'로 통일한다. 원문 단서명에 '·'가 들어가는데
    ///   한국어 자판에서는 'ㆍ'로 입력되는 경우가 많기 때문이다.
    /// - 앞뒤 공백을 지우고 연속 공백을 하나로 줄인다.
    /// 띄어쓰기 무시(조카의일기 = 조카의 일기)는 ignoreWhitespace로 켠다. 최종 보고서는 2026-11-10 확정으로 켠다
    /// (FinalReportAnswerConfig.IgnoreWhitespaceInComparison). 띄어쓰기 외의 글자·숫자·기호는 지우지 않는다.
    /// </summary>
    public static class ClueNameNormalizer
    {
        private static readonly char[] MiddleDotVariants = { '‧', '・', '･', 'ㆍ' };

        public static string Normalize(string input, bool ignoreWhitespace = false)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            string text = input.Normalize(NormalizationForm.FormC);

            var sb = new StringBuilder(text.Length);
            bool previousWasSpace = false;
            foreach (char raw in text.Trim())
            {
                char c = System.Array.IndexOf(MiddleDotVariants, raw) >= 0 ? '·' : raw;

                if (char.IsWhiteSpace(c))
                {
                    if (ignoreWhitespace || previousWasSpace)
                        continue;

                    sb.Append(' ');
                    previousWasSpace = true;
                    continue;
                }

                sb.Append(c);
                previousWasSpace = false;
            }

            return sb.ToString();
        }
    }
}
