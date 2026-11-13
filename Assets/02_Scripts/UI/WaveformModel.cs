using System;

namespace CaseFileOmission.UI
{
    /// <summary>
    /// 통화 음파 막대 높이 계산(순수 C#). 사람 목소리처럼 글자(음절)마다 소리가 확 커졌다가 다음 음절 전까지 잦아들고,
    /// 띄어쓰기·문장 끝의 쉼에서는 거의 가라앉는다.
    /// 높이 = 음량(level) × 모양(shape, 음절마다 새로 뽑는 봉우리 2~3개) × 떨림(flicker, 짧은 간격으로 막대마다 다시 뽑음).
    /// 높이는 0(점선 기준선)~1.
    /// </summary>
    public sealed class WaveformModel
    {
        private const float FlickerInterval = 0.045f;

        private readonly float[] _amplitude;
        private readonly float[] _shape;
        private readonly float[] _flicker;
        private readonly Random _random;
        private float _level;
        private float _flickerTimer;

        public int BarCount => _amplitude.Length;

        /// <summary>초당 가라앉는 빠르기(클수록 음절 사이가 또렷하게 끊김)</summary>
        public float Decay { get; set; } = 4f;

        public WaveformModel(int barCount, Random random = null)
        {
            if (barCount < 1) throw new ArgumentOutOfRangeException(nameof(barCount), "막대는 1개 이상이어야 한다.");
            _amplitude = new float[barCount];
            _shape = new float[barCount];
            _flicker = new float[barCount];
            _random = random ?? new Random();
            RollFlicker();
        }

        public float GetAmplitude(int index) => _amplitude[index];

        /// <summary>
        /// 음절 하나가 소리 날 때 부른다. strength 0~1(공백·문장부호는 작게 — 음량을 올리지 못해 쉼처럼 가라앉는다).
        /// </summary>
        public void Pulse(float strength)
        {
            if (strength <= 0f) return;
            float loud = Clamp01(strength * (0.7f + 0.3f * (float)_random.NextDouble()));
            if (loud <= _level * 0.5f) return;   // 쉼(공백·부호)은 소리를 다시 키우지 않음
            _level = Math.Max(_level, loud);
            RollShape();                          // 음절마다 모양이 바뀌어 말하듯 출렁임
            RollFlicker();
            Recompute();
        }

        public void Update(float deltaTime)
        {
            if (deltaTime <= 0f) return;
            _level *= (float)Math.Exp(-Decay * deltaTime);
            _flickerTimer += deltaTime;
            if (_flickerTimer >= FlickerInterval)
            {
                _flickerTimer = 0f;
                RollFlicker();
            }
            Recompute();
        }

        /// <summary>봉우리 2~3개를 무작위 위치·폭·높이로 뽑아 가장 높은 곳이 1이 되게 한다.</summary>
        private void RollShape()
        {
            int n = _shape.Length;
            Array.Clear(_shape, 0, n);
            int bumps = 2 + _random.Next(2);
            for (int b = 0; b < bumps; b++)
            {
                float center = (float)_random.NextDouble() * (n - 1);
                float sigma = Math.Max(1f, n * (0.05f + 0.05f * (float)_random.NextDouble()));
                float height = 0.55f + 0.45f * (float)_random.NextDouble();
                for (int i = 0; i < n; i++)
                {
                    float d = (i - center) / sigma;
                    float v = height * (float)Math.Exp(-0.5f * d * d);
                    if (v > _shape[i]) _shape[i] = v;
                }
            }
            float max = 0f;
            for (int i = 0; i < n; i++) max = Math.Max(max, _shape[i]);
            // 봉우리 사이에도 참고 이미지처럼 기준선 위로 작은 눈금이 살짝 돋게
            if (max > 0f) for (int i = 0; i < n; i++) _shape[i] = Math.Max(_shape[i] / max, 0.12f);
        }

        /// <summary>이웃 막대끼리 높낮이가 들쭉날쭉하게(참고 이미지의 촘촘한 세로선)</summary>
        private void RollFlicker()
        {
            for (int i = 0; i < _flicker.Length; i++) _flicker[i] = 0.25f + 0.75f * (float)_random.NextDouble();
        }

        private void Recompute()
        {
            for (int i = 0; i < _amplitude.Length; i++) _amplitude[i] = _level * _shape[i] * _flicker[i];
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
