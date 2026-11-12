using CaseFileOmission.Props;
using NUnit.Framework;
using UnityEngine;

namespace CaseFileOmission.Tests
{
    public class HandsetLiftMotionTests
    {
        private static readonly Vector3 Rest = new Vector3(0f, 0.58f, 0f);
        private static readonly Vector3 Lifted = new Vector3(-0.06f, 0.92f, 0.24f);

        [Test]
        public void EvaluatePosition_EndsMatchRestAndLifted()
        {
            Assert.AreEqual(Rest, HandsetLiftMotion.EvaluatePosition(Rest, Lifted, Vector3.up, 0.05f, 0f));
            Assert.AreEqual(Lifted, HandsetLiftMotion.EvaluatePosition(Rest, Lifted, Vector3.up, 0.05f, 1f));
        }

        [Test]
        public void EvaluatePosition_LiftOff_GoesStraightUpFirst()
        {
            // 받침대 갈래에서 빠지는 구간에서는 옆으로 움직이지 않고 위로만 떠야 한다.
            Vector3 p = HandsetLiftMotion.EvaluatePosition(Rest, Lifted, Vector3.up, 0.05f, HandsetLiftMotion.LiftOffPortion);
            Assert.AreEqual(Rest.x, p.x, 1e-5f);
            Assert.AreEqual(Rest.z, p.z, 1e-5f);
            Assert.AreEqual(Rest.y + 0.05f, p.y, 1e-5f);
        }

        [Test]
        public void EvaluatePosition_HeightNeverDecreasesWhileLifting()
        {
            float previous = float.NegativeInfinity;
            for (int i = 0; i <= 50; i++)
            {
                float y = HandsetLiftMotion.EvaluatePosition(Rest, Lifted, Vector3.up, 0.05f, i / 50f).y;
                Assert.GreaterOrEqual(y, previous - 1e-6f, $"t={i / 50f}에서 높이가 내려갔다.");
                previous = y;
            }
        }

        [Test]
        public void EvaluateRotation_DoesNotTurnBeforeLeavingCradle()
        {
            Quaternion rest = Quaternion.identity;
            Quaternion lifted = Quaternion.Euler(0f, 12f, -35f);
            Quaternion early = HandsetLiftMotion.EvaluateRotation(rest, lifted, HandsetLiftMotion.RotationStart);

            Assert.AreEqual(0f, Quaternion.Angle(rest, early), 1e-3f, "갈래에서 충분히 멀어지기 전에는 돌리지 않아야 한다.");
            Assert.AreEqual(0f, Quaternion.Angle(lifted, HandsetLiftMotion.EvaluateRotation(rest, lifted, 1f)), 1e-3f);
        }
    }
}
