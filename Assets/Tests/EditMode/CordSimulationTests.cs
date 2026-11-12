using CaseFileOmission.Props;
using NUnit.Framework;
using UnityEngine;

namespace CaseFileOmission.Tests
{
    public class CordSimulationTests
    {
        private static void Run(CordSimulation sim, Vector3 start, Vector3 end, int steps)
        {
            for (int i = 0; i < steps; i++) sim.Step(start, end, 1f / 120f);
        }

        [Test]
        public void Step_EndsStayPinnedToAnchors()
        {
            var start = new Vector3(0f, 0.26f, 0.17f);
            var end = new Vector3(0.3f, 0.9f, 0f);
            var sim = new CordSimulation(20, 1.0f, start, end);

            Run(sim, start, end, 240);

            Assert.AreEqual(start, sim.GetPoint(0));
            Assert.AreEqual(end, sim.GetPoint(sim.PointCount - 1));
        }

        [Test]
        public void Step_SlackCord_SagsBelowStraightLine()
        {
            var start = new Vector3(0f, 1f, 0f);
            var end = new Vector3(0.5f, 1f, 0f);
            var sim = new CordSimulation(20, 1.0f, start, end);

            Run(sim, start, end, 600);

            Vector3 middle = sim.GetPoint(sim.PointCount / 2);
            Assert.Less(middle.y, 0.8f, "늘어진 선은 중력으로 가운데가 처져야 한다.");
        }

        [Test]
        public void Step_KeepsSegmentLengthsNearRest()
        {
            var start = new Vector3(0f, 1f, 0f);
            var end = new Vector3(0.4f, 0.8f, 0.1f);
            var sim = new CordSimulation(24, 1.0f, start, end);

            Run(sim, start, end, 600);

            for (int i = 0; i < sim.PointCount - 1; i++)
            {
                float d = Vector3.Distance(sim.GetPoint(i), sim.GetPoint(i + 1));
                Assert.AreEqual(sim.SegmentLength, d, sim.SegmentLength * 0.1f, $"구간 {i} 길이가 휴지 길이에서 크게 벗어났다.");
            }
        }

        [Test]
        public void Step_LiftedHandset_CordFollowsEnd()
        {
            var start = new Vector3(0f, 0.26f, 0.17f);
            var rest = new Vector3(0f, 0.93f, 0.05f);
            var lifted = new Vector3(0.2f, 1.3f, -0.4f);
            var sim = new CordSimulation(24, 1.2f, start, rest);
            Run(sim, start, rest, 240);

            Run(sim, start, lifted, 240);

            Vector3 nearEnd = sim.GetPoint(sim.PointCount - 2);
            Assert.Less(Vector3.Distance(nearEnd, lifted), sim.SegmentLength * 1.2f, "수화기 쪽 끝 바로 앞 점이 들어 올린 수화기를 따라와야 한다.");
        }

        [Test]
        public void Step_SoftCoil_StretchesButNotBeyondMaxStretch()
        {
            var start = Vector3.zero;
            // 양 끝이 고정이라 최대 길이(3m) 안쪽까지만 당긴다.
            var far = new Vector3(2.5f, 0f, 0f);
            var sim = new CordSimulation(20, 1.0f, start, new Vector3(0.5f, 0f, 0f)) { Stiffness = 0.04f, MaxStretch = 3f, ConstraintIterations = 16 };

            Run(sim, start, far, 600);

            float total = 0f;
            for (int i = 0; i < sim.PointCount - 1; i++)
            {
                float d = Vector3.Distance(sim.GetPoint(i), sim.GetPoint(i + 1));
                Assert.LessOrEqual(d, sim.SegmentLength * 3f * 1.05f, $"구간 {i}이 최대 늘어남(3배)을 넘었다.");
                total += d;
            }
            Assert.Greater(total, sim.Length * 1.5f, "부드러운 코일은 휴지 길이보다 늘어나야 한다.");
        }

        [Test]
        public void Step_SoftCoil_ContractsWhenReleased()
        {
            var start = new Vector3(0f, 1f, 0f);
            // 중력 없이 코일의 되돌아가는 성질만 본다.
            var sim = new CordSimulation(20, 1.0f, start, new Vector3(2.5f, 1f, 0f)) { Stiffness = 0.04f, MaxStretch = 3f, ConstraintIterations = 16, Gravity = Vector3.zero };
            Run(sim, start, new Vector3(2.5f, 1f, 0f), 300);

            var released = new Vector3(0.3f, 1f, 0f);
            Run(sim, start, released, 900);

            float total = 0f;
            for (int i = 0; i < sim.PointCount - 1; i++) total += Vector3.Distance(sim.GetPoint(i), sim.GetPoint(i + 1));
            Assert.Less(total, sim.Length * 1.3f, "당기던 끝을 놓으면 코일이 다시 휴지 길이 가까이 줄어들어야 한다.");
        }

        [Test]
        public void Step_WithExitDirection_LeadPointsLeaveAlongDirection()
        {
            // 수화기 앵커에서 바로 아래로 떨어지지 않고 바깥쪽(+x)으로 빠져나와야 한다.
            var body = new Vector3(0.22f, 0.26f, 0.06f);
            var handset = new Vector3(0.42f, 0.96f, 0f);
            var exit = new Vector3(1f, -0.35f, 0f);
            var sim = new CordSimulation(32, 1.0f, body, handset) { LeadPoints = 3, Stiffness = 0.04f, MaxStretch = 2f, ConstraintIterations = 16 };

            for (int i = 0; i < 600; i++) sim.Step(body, Vector3.right, handset, exit, 1f / 120f);

            int last = sim.PointCount - 1;
            Vector3 lead = sim.GetPoint(last - 3) - handset;
            Assert.AreEqual(sim.SegmentLength * 3f, lead.magnitude, 1e-4f);
            Assert.Greater(Vector3.Dot(lead.normalized, exit.normalized), 0.999f, "수화기 쪽 끝 점들이 빠져나가는 방향을 따라야 한다.");
            Assert.Greater(sim.GetPoint(3).x, body.x + sim.SegmentLength * 2.9f, "몸통 쪽 끝 점들도 빠져나가는 방향(+x)을 따라야 한다.");
        }

        [Test]
        public void Constructor_RejectsInvalidArguments()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new CordSimulation(1, 1f, Vector3.zero, Vector3.one));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new CordSimulation(10, 0f, Vector3.zero, Vector3.one));
        }
    }
}
