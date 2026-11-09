using CaseFileOmission.Meeting;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class MeetingSuspicionTableTests
    {
        [TestCase(1, 5f)]
        [TestCase(2, 8f)]
        [TestCase(3, 12f)]
        [TestCase(4, 15f)]
        [TestCase(10, 15f)]
        public void ConsecutiveTurnSkipIncrease_MatchesConfirmedTable(int count, float expected)
        {
            Assert.AreEqual(expected, MeetingSuspicionTable.ConsecutiveTurnSkipIncrease(count));
        }

        [TestCase(1, -4f)]
        [TestCase(2, -7f)]
        [TestCase(3, -11f)]
        [TestCase(4, -16f)]
        [TestCase(9, -16f)]
        public void ConsecutiveAcceptDecrease_MatchesConfirmedTable(int count, float expected)
        {
            Assert.AreEqual(expected, MeetingSuspicionTable.ConsecutiveAcceptDecrease(count));
        }

        [TestCase(1, 8f)]
        [TestCase(2, 11f)]
        [TestCase(3, 15f)]
        [TestCase(4, 20f)]
        public void ConsecutiveResponseFailIncrease_MatchesConfirmedTable(int count, float expected)
        {
            Assert.AreEqual(expected, MeetingSuspicionTable.ConsecutiveResponseFailIncrease(count));
        }

        [TestCase(1, 12f)]
        [TestCase(2, 15f)]
        [TestCase(3, 19f)]
        [TestCase(4, 24f)]
        public void ConsecutiveGiveUpIncrease_MatchesConfirmedTable(int count, float expected)
        {
            Assert.AreEqual(expected, MeetingSuspicionTable.ConsecutiveGiveUpIncrease(count));
        }
    }
}
