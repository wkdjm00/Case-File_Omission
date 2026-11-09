using System.Collections.Generic;

namespace CaseFileOmission.Core
{
    /// <summary>
    /// 06페이지 확정 공통 규칙: "확률은 정수 퍼센트포인트 보정을 합산한 뒤 해당 시스템의
    /// 하한·상한을 적용한다." 여러 모듈(NPC 정보전달, 회유, 거짓말 판정, 총기 명중 등)이
    /// 동일한 계산 방식을 쓰므로 한 곳에 모은다.
    /// </summary>
    public static class ProbabilityCalculator
    {
        public static float ApplyModifiers(float basePercent, IEnumerable<float> modifiersPercentagePoints, float minPercent, float maxPercent)
        {
            float result = basePercent;
            foreach (var m in modifiersPercentagePoints)
                result += m;

            if (result < minPercent) result = minPercent;
            if (result > maxPercent) result = maxPercent;
            return result;
        }

        public static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
