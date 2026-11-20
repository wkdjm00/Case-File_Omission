using System.Collections.Generic;
using NUnit.Framework;
using CaseFileOmission.Core;

namespace CaseFileOmission.Tests
{
    public sealed class ReplayableRandomTests
    {
        [Test]
        public void RestoredState_ProducesTheSameFutureSequence()
        {
            var original = new ReplayableRandom(123456);
            original.Next();
            original.NextDouble();
            original.Next(7, 900);
            string state = original.CaptureState();

            var restored = new ReplayableRandom(123456);
            Assert.IsTrue(restored.TryRestoreState(state));

            var expected = new List<int>();
            var actual = new List<int>();
            for (int i = 0; i < 20; i++)
            {
                expected.Add(original.Next(100000));
                actual.Add(restored.Next(100000));
            }
            CollectionAssert.AreEqual(expected, actual);
        }

        [Test]
        public void InvalidState_IsRejectedWithoutChangingState()
        {
            var random = new ReplayableRandom(7);
            string before = random.CaptureState();
            Assert.IsFalse(random.TryRestoreState("not-a-state"));
            Assert.AreEqual(before, random.CaptureState());
        }
    }
}
