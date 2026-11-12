using System;
using UnityEngine;

namespace CaseFileOmission.Props
{
    /// <summary>
    /// 양 끝이 고정된 줄(전화선 등)의 Verlet 시뮬레이션. 점 사이 거리를 일정하게 유지하고 중력으로 처지게 한다.
    /// 렌더링과 분리된 순수 계산이라 EditMode 테스트로 검증한다.
    /// </summary>
    public sealed class CordSimulation
    {
        private readonly Vector3[] _current;
        private readonly Vector3[] _previous;

        public int PointCount => _current.Length;
        public float Length { get; }
        public float SegmentLength => Length / (_current.Length - 1);
        public Vector3 Gravity { get; set; } = new Vector3(0f, -9.81f, 0f);

        /// <summary>속도 감쇠(0이면 감쇠 없음, 1이면 정지).</summary>
        public float Damping { get; set; } = 0.02f;

        public int ConstraintIterations { get; set; } = 24;

        /// <summary>구간 길이를 되돌리는 세기(1이면 늘어나지 않는 줄, 낮을수록 코일 선처럼 늘어나고 줄어듦).</summary>
        public float Stiffness { get; set; } = 1f;

        /// <summary>구간이 휴지 길이의 몇 배까지 늘어날 수 있는지(넘으면 강제로 당김). 무한대면 제한 없음.</summary>
        public float MaxStretch { get; set; } = float.PositiveInfinity;

        /// <summary>방향을 준 끝에서 그 방향으로 곧게 고정할 점 수(끝점 제외). 양 끝을 같은 값으로 정한다.</summary>
        public int LeadPoints
        {
            get => LeadPointsStart;
            set { LeadPointsStart = value; LeadPointsEnd = value; }
        }

        /// <summary>끝에서 곧게 고정하는 점 사이 간격(m). 0 이하면 SegmentLength를 쓴다.</summary>
        public float LeadSpacing { get; set; }

        /// <summary>시작(몸통) 쪽 고정 점 수.</summary>
        public int LeadPointsStart { get; set; }

        /// <summary>끝(수화기) 쪽 고정 점 수.</summary>
        public int LeadPointsEnd { get; set; }

        public CordSimulation(int pointCount, float length, Vector3 start, Vector3 end)
        {
            if (pointCount < 2) throw new ArgumentOutOfRangeException(nameof(pointCount), "점은 2개 이상이어야 한다.");
            if (length <= 0f) throw new ArgumentOutOfRangeException(nameof(length), "길이는 0보다 커야 한다.");

            Length = length;
            _current = new Vector3[pointCount];
            _previous = new Vector3[pointCount];
            Reset(start, end);
        }

        public Vector3 GetPoint(int index) => _current[index];

        /// <summary>점을 목표 쪽으로 weight(0~1)만큼 옮긴다. 이전 위치도 같이 옮겨 속도를 만들지 않는다(쉬는 모양 유지용).</summary>
        public void PullToward(int index, Vector3 target, float weight)
        {
            Vector3 delta = (target - _current[index]) * Mathf.Clamp01(weight);
            _current[index] += delta;
            _previous[index] += delta;
        }

        /// <summary>충돌 처리로 점을 옮긴다. 속도도 없애 바닥에 닿은 점이 미끄러지지 않게 한다.</summary>
        public void SetPoint(int index, Vector3 position)
        {
            _current[index] = position;
            _previous[index] = position;
        }

        /// <summary>두 끝 사이를 처진 모양 없이 직선으로 다시 놓는다(순간이동·초기화용).</summary>
        public void Reset(Vector3 start, Vector3 end)
        {
            int last = _current.Length - 1;
            for (int i = 0; i <= last; i++)
            {
                Vector3 p = Vector3.Lerp(start, end, (float)i / last);
                _current[i] = p;
                _previous[i] = p;
            }
        }

        public void Step(Vector3 start, Vector3 end, float deltaTime) => Step(start, Vector3.zero, end, Vector3.zero, deltaTime);

        /// <summary>
        /// 한 걸음 진행. startDir/endDir가 0이 아니면 양 끝에서 LeadPoints개 점을 그 방향으로 곧게 고정해
        /// 선이 앵커에서 정해진 방향으로 빠져나오게 한다(예: 수화기 몸체를 뚫고 바로 아래로 떨어지지 않게).
        /// </summary>
        public void Step(Vector3 start, Vector3 startDir, Vector3 end, Vector3 endDir, float deltaTime)
        {
            if (deltaTime <= 0f) return;

            int last = _current.Length - 1;
            int maxLead = Mathf.Max(0, (last - 1) / 2 - 1);
            int leadA = startDir.sqrMagnitude > 1e-8f ? Mathf.Min(LeadPointsStart, maxLead) : 0;
            int leadB = endDir.sqrMagnitude > 1e-8f ? Mathf.Min(LeadPointsEnd, maxLead) : 0;
            Vector3 dirA = leadA > 0 ? startDir.normalized : Vector3.zero;
            Vector3 dirB = leadB > 0 ? endDir.normalized : Vector3.zero;
            Vector3 accel = Gravity * (deltaTime * deltaTime);
            float keep = 1f - Mathf.Clamp01(Damping);

            for (int i = 1; i < last; i++)
            {
                Vector3 velocity = (_current[i] - _previous[i]) * keep;
                _previous[i] = _current[i];
                _current[i] += velocity + accel;
            }

            float rest = SegmentLength;
            float leadStep = LeadSpacing > 0f ? LeadSpacing : rest;
            for (int i = 0; i <= leadA; i++) _previous[i] = _current[i] = start + dirA * (leadStep * i);
            for (int i = 0; i <= leadB; i++) _previous[last - i] = _current[last - i] = end + dirB * (leadStep * i);
            bool Pinned(int i) => i <= leadA || i >= last - leadB;

            float maxLen = rest * Mathf.Max(1f, MaxStretch);
            float stiffness = Mathf.Clamp01(Stiffness);
            for (int iter = 0; iter < ConstraintIterations; iter++)
            {
                for (int i = 0; i < last; i++)
                {
                    Vector3 delta = _current[i + 1] - _current[i];
                    float dist = delta.magnitude;
                    if (dist < 1e-6f) continue;

                    // 최대 길이를 넘으면 그만큼은 반드시 되돌리고, 나머지는 세기만큼만 휴지 길이로 당긴다.
                    float target = dist > maxLen ? maxLen : Mathf.Lerp(dist, rest, stiffness);
                    Vector3 correction = delta * ((dist - target) / dist);
                    bool pinnedA = Pinned(i);
                    bool pinnedB = Pinned(i + 1);
                    if (pinnedA && pinnedB) continue;
                    if (pinnedA) _current[i + 1] -= correction;
                    else if (pinnedB) _current[i] += correction;
                    else
                    {
                        _current[i] += correction * 0.5f;
                        _current[i + 1] -= correction * 0.5f;
                    }
                }
            }        }
    }
}
