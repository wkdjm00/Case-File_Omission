using System.Collections.Generic;

namespace CaseFileOmission.Police
{
    /// <summary>06·07·08페이지 확정: 경찰은 총 5명, 판단 방식은 A·B=수사중심, C=통제중심, D=권위중심, E=직관중심.
    /// 개별 경찰의 상세 성격과 인물 설정은 아직 확정되지 않았다(미정) — 여기서는 유형 배정만 고정한다.</summary>
    public enum PoliceJudgementType
    {
        Investigative,
        Controlling,
        Authority,
        Intuitive
    }

    public static class PoliceRoster
    {
        public const string PoliceA = "police-A";
        public const string PoliceB = "police-B";
        public const string PoliceC = "police-C";
        public const string PoliceD = "police-D";
        public const string PoliceE = "police-E";

        public static readonly IReadOnlyList<string> AllPoliceIds = new[] { PoliceA, PoliceB, PoliceC, PoliceD, PoliceE };

        public static readonly IReadOnlyDictionary<string, PoliceJudgementType> JudgementTypeById =
            new Dictionary<string, PoliceJudgementType>
            {
                [PoliceA] = PoliceJudgementType.Investigative,
                [PoliceB] = PoliceJudgementType.Investigative,
                [PoliceC] = PoliceJudgementType.Controlling,
                [PoliceD] = PoliceJudgementType.Authority,
                [PoliceE] = PoliceJudgementType.Intuitive,
            };
    }
}
