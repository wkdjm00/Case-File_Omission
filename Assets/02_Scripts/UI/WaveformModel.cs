using System;

namespace CaseFileOmission.UI
{
    /// <summary>
    /// 통화 음파 막대 높이 계산(순수 C#). 글자가 하나 나올 때마다 Pulse로 몇 개 막대를 출렁이게 하고, 시간이 지나면 가라앉는다.
    /// 높이는 0(점선 기준선)~1.
    /// </summary>
    public sealed class WaveformModel
    {
        private readonly float[] _amplitude;
        private readonly Random _random;

        public int BarCount => _amplitude.Length;

        /// <summary>초당 가라앉는 빠르기(클수록 빨리 잦아듦)</summary>
        public float Decay { get; set; } = 3f;

        public WaveformModel(int barCount, Random random = null)
        {
            if (barCount < 1) throw new ArgumentOutOfRangeException(nameof(barCount), "막대는 1개 이상이어야 한다.");
            _amplitude = new float[barCount];
            _random = random ?? new Random();
        }

        public float GetAmplitude(int index) => _amplitude[index];

        /// <summary>
        /// 글자 하나에 맞춰 출렁임을 넣는다. 무작위 위치를 중심으로 가운데가 가장 높고 양옆이 낮아지는 묶음을 만든다.
        /// strength 0~1(공백·문장부호는 작게).
        /// </summary>
        public void Pulse(float strength)
        {
            if (strength <= 0f) return;
            int n = _amplitude.Length;
            int center = _random.Next(n);
            int halfWidth = Math.Max(2, n / 12 + _random.Next(Math.Max(1, n / 16)));
            float peak = Clamp01(strength * (0.75f + 0.25f * (float)_random.NextDouble()));
            for (int k = -halfWidth; k <= halfWidth; k++)
            {
                int i = center + k;
                if (i < 0 || i >= n) continue;
                float falloff = 1f - Math.Abs(k) / (float)(halfWidth + 1);
                // 참고 이미지처럼 이웃 막대끼리 높낮이가 들쭉날쭉하게(가운데 막대는 꼭대기 유지)
                float jitter = k == 0 ? 1f : 0.3f + 0.7f * (float)_random.NextDouble();
                float a = peak * falloff * jitter;   // 묶음이 넓게 차오르게(제곱 감쇠면 가운데 몇 개만 보임)
                if (a > _amplitude[i]) _amplitude[i] = a;
            }
        }

        public void Update(float deltaTime)
        {
            if (deltaTime <= 0f) return;
            float keep = (float)Math.Exp(-Decay * deltaTime);
            for (int i = 0; i < _amplitude.Length; i++) _amplitude[i] *= keep;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
