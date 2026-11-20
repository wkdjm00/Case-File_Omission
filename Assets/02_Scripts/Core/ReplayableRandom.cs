using System;
using System.Globalization;

namespace CaseFileOmission.Core
{
    /// <summary>저장/불러오기 뒤에도 이후 난수 결과를 재현할 수 있는 상태 보존형 난수 생성기.</summary>
    public sealed class ReplayableRandom : Random
    {
        private const ulong GoldenRatio = 0x9E3779B97F4A7C15UL;
        private ulong _state;
        public int Seed { get; }

        public ReplayableRandom(int seed)
        {
            Seed = seed;
            ulong value = unchecked((ulong)(uint)seed) + GoldenRatio;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            _state = value ^ (value >> 31);
            if (_state == 0) _state = GoldenRatio;
        }

        public string CaptureState() => _state.ToString("X16", CultureInfo.InvariantCulture);

        public bool TryRestoreState(string state)
        {
            if (string.IsNullOrWhiteSpace(state) || state.Length != 16
                || !ulong.TryParse(state, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong parsed)
                || parsed == 0)
                return false;
            _state = parsed;
            return true;
        }

        private ulong NextUInt64()
        {
            ulong x = _state;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            _state = x;
            return x * 0x2545F4914F6CDD1DUL;
        }

        private ulong NextBounded(ulong bound)
        {
            if (bound == 0) return 0;
            ulong threshold = unchecked(0UL - bound) % bound;
            while (true)
            {
                ulong value = NextUInt64();
                if (value >= threshold) return value % bound;
            }
        }

        public override int Next() => (int)(NextUInt64() & 0x7FFFFFFFUL);

        public override int Next(int maxValue)
        {
            if (maxValue < 0) throw new ArgumentOutOfRangeException(nameof(maxValue));
            if (maxValue == 0) return 0;
            return (int)NextBounded((ulong)maxValue);
        }

        public override int Next(int minValue, int maxValue)
        {
            if (minValue > maxValue) throw new ArgumentOutOfRangeException(nameof(minValue));
            long range = (long)maxValue - minValue;
            if (range == 0) return minValue;
            return (int)(minValue + (long)NextBounded((ulong)range));
        }

        public override double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

        public override void NextBytes(byte[] buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            int index = 0;
            while (index < buffer.Length)
            {
                ulong value = NextUInt64();
                for (int i = 0; i < 8 && index < buffer.Length; i++, index++)
                    buffer[index] = (byte)(value >> (i * 8));
            }
        }
    }
}
