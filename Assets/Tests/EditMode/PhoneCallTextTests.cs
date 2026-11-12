using CaseFileOmission.UI;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class PhoneCallTextTests
    {
        [Test]
        public void SplitTextElements_KoreanSyllablesAreWholeCharacters()
        {
            // 한글은 음절 하나가 글자 하나(자음·모음으로 나누지 않음)
            string[] e = TypewriterSequence.SplitTextElements("들켰어. 지금");
            CollectionAssert.AreEqual(new[] { "들", "켰", "어", ".", " ", "지", "금" }, e);
        }

        [Test]
        public void Advance_RevealsOneSyllableAtATime()
        {
            var seq = new TypewriterSequence("안녕하세요", 10f, 0f, 0f);
            Assert.AreEqual(0, seq.Advance(0.05));
            Assert.AreEqual(1, seq.Advance(0.06));
            Assert.AreEqual(1, seq.VisibleCount);
            Assert.AreEqual(4, seq.Advance(1.0));
            Assert.IsTrue(seq.IsComplete);
        }

        [Test]
        public void Advance_PausesAfterSentenceEnd()
        {
            var seq = new TypewriterSequence("가. 나", 10f, 0.5f, 0f);
            seq.Advance(0.2);                 // "가", "." 표시
            Assert.AreEqual(2, seq.VisibleCount);
            Assert.AreEqual(0, seq.Advance(0.3), "마침표 뒤에는 잠깐 멈춰야 한다.");
            Assert.Greater(seq.Advance(0.5), 0);
        }

        [Test]
        public void Complete_ShowsEverything()
        {
            var seq = new TypewriterSequence("시간 없어", 5f);
            seq.Complete();
            Assert.IsTrue(seq.IsComplete);
            Assert.AreEqual(5, seq.VisibleCount);
        }

        [Test]
        public void IsVoiced_SpacesAndPunctuationAreQuiet()
        {
            var seq = new TypewriterSequence("가 .", 10f);
            Assert.IsTrue(seq.IsVoiced(0));
            Assert.IsFalse(seq.IsVoiced(1));
            Assert.IsFalse(seq.IsVoiced(2));
        }

        [Test]
        public void Waveform_PulseRaisesBarsAndDecays()
        {
            var wave = new WaveformModel(48, new System.Random(3));
            wave.Pulse(1f);
            float peak = 0f;
            for (int i = 0; i < wave.BarCount; i++) peak = System.Math.Max(peak, wave.GetAmplitude(i));
            Assert.Greater(peak, 0.3f, "글자가 나오면 음파가 출렁여야 한다.");

            for (int k = 0; k < 120; k++) wave.Update(1f / 60f);
            float after = 0f;
            for (int i = 0; i < wave.BarCount; i++) after = System.Math.Max(after, wave.GetAmplitude(i));
            Assert.Less(after, peak * 0.05f, "시간이 지나면 가라앉아야 한다.");
        }
    }
}
