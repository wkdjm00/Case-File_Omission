using UnityEngine;

namespace CaseFileOmission.Props
{
    /// <summary>
    /// 수화기를 받침대에서 들어 올리는 동작의 자세 계산(순수 함수). t=0이 받침대 위, t=1이 들어 올린 자세.
    /// 처음에는 받침대 갈래에서 빠지도록 곧장 위로 살짝 뜨고, 그다음 들어 올린 자세로 부드럽게 이동·회전한다.
    /// 내려놓기는 같은 경로를 거꾸로(t를 1에서 0으로) 따른다.
    /// </summary>
    public static class HandsetLiftMotion
    {
        /// <summary>곧장 위로 뜨는 구간이 전체에서 차지하는 비율</summary>
        public const float LiftOffPortion = 0.2f;

        public static Vector3 EvaluatePosition(Vector3 rest, Vector3 lifted, Vector3 up, float liftOffHeight, float t)
        {
            t = Mathf.Clamp01(t);
            Vector3 raised = rest + up.normalized * liftOffHeight;
            if (t <= LiftOffPortion)
            {
                // 갈래에서 빠지는 구간: 위로만, 끝에서 느려지게
                float k = EaseOut(t / LiftOffPortion);
                return Vector3.Lerp(rest, raised, k);
            }

            float m = SmoothStep((t - LiftOffPortion) / (1f - LiftOffPortion));
            return Vector3.Lerp(raised, lifted, m);
        }

        /// <summary>회전을 시작하는 진행도. 그 전까지 위로 뜨고 앞으로 빠져 받침대 갈래와 떨어진 뒤에 돌린다.</summary>
        public const float RotationStart = 0.45f;

        public static Quaternion EvaluateRotation(Quaternion rest, Quaternion lifted, float t)
        {
            t = Mathf.Clamp01(t);
            // 송화구 쪽을 내리는 회전을 일찍 하면 꺾인 관이 받침대 갈래 옆을 스친다. 갈래에서 충분히 멀어진 뒤에 돌린다.
            float r = t <= RotationStart ? 0f : SmoothStep((t - RotationStart) / (1f - RotationStart));
            return Quaternion.Slerp(rest, lifted, r);
        }

        private static float EaseOut(float x) => 1f - (1f - x) * (1f - x);
        private static float SmoothStep(float x) => x * x * (3f - 2f * x);
    }
}
