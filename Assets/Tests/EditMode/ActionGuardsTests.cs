using System.Collections.Generic;
using CaseFileOmission.Core;
using CaseFileOmission.NPC;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    public class ActionGuardsTests
    {
        private sealed class FakeAlive : IAliveCheckable
        {
            public bool IsAlive { get; set; } = true;
        }

        private sealed class FakeRoomProvider : IRoomConnectivityProvider
        {
            private readonly HashSet<string> _rooms = new HashSet<string>();
            private readonly HashSet<(string, string)> _connections = new HashSet<(string, string)>();

            public void AddRoom(string id) => _rooms.Add(id);

            public void Connect(string a, string b)
            {
                _connections.Add((a, b));
                _connections.Add((b, a));
            }

            public bool RoomExists(string roomId) => _rooms.Contains(roomId);
            public bool AreConnected(string roomIdA, string roomIdB) => _connections.Contains((roomIdA, roomIdB));
        }

        [Test]
        public void Evaluate_ContractResolved_FailsRegardlessOfOtherChecks()
        {
            var state = new GameStateController();
            state.BeginContract("c1");
            state.EnterMeeting();
            state.ResolveContract();

            var result = ActionGuards.Evaluate(state, new[] { GamePhase.Investigation });

            Assert.IsFalse(result.Allowed);
            Assert.AreEqual(ActionGuardFailureReason.ContractAlreadyResolved, result.Reason);
        }

        [Test]
        public void Evaluate_WrongPhase_Fails()
        {
            var state = new GameStateController();
            state.BeginContract("c1");
            state.EnterMeeting();

            var result = ActionGuards.Evaluate(state, new[] { GamePhase.Investigation });

            Assert.AreEqual(ActionGuardFailureReason.WrongPhase, result.Reason);
        }

        [Test]
        public void Evaluate_DeceasedTarget_Fails()
        {
            var state = new GameStateController();
            state.BeginContract("c1");

            var target = new FakeAlive { IsAlive = false };
            var result = ActionGuards.Evaluate(state, new[] { GamePhase.Investigation }, target: target);

            Assert.AreEqual(ActionGuardFailureReason.TargetDeceased, result.Reason);
        }

        [Test]
        public void Evaluate_NullTarget_PassesAliveCheck()
        {
            var state = new GameStateController();
            state.BeginContract("c1");

            var result = ActionGuards.Evaluate(state, new[] { GamePhase.Investigation }, target: null);

            Assert.IsTrue(result.Allowed);
        }

        [Test]
        public void Evaluate_RoomsNotConnected_Fails()
        {
            var state = new GameStateController();
            state.BeginContract("c1");

            var rooms = new FakeRoomProvider();
            rooms.AddRoom("living-room");
            rooms.AddRoom("attic");
            // 연결하지 않음

            var result = ActionGuards.Evaluate(state, new[] { GamePhase.Investigation },
                rooms: rooms, actorRoomId: "living-room", targetRoomId: "attic");

            Assert.AreEqual(ActionGuardFailureReason.RoomsNotConnected, result.Reason);
        }

        [Test]
        public void Evaluate_RoomsConnected_Passes()
        {
            var state = new GameStateController();
            state.BeginContract("c1");

            var rooms = new FakeRoomProvider();
            rooms.AddRoom("living-room");
            rooms.AddRoom("kitchen");
            rooms.Connect("living-room", "kitchen");

            var result = ActionGuards.Evaluate(state, new[] { GamePhase.Investigation },
                rooms: rooms, actorRoomId: "living-room", targetRoomId: "kitchen");

            Assert.IsTrue(result.Allowed);
        }

        [Test]
        public void Evaluate_UnknownRoom_FailsWithRoomNotFound()
        {
            var state = new GameStateController();
            state.BeginContract("c1");

            var rooms = new FakeRoomProvider();
            rooms.AddRoom("living-room");

            var result = ActionGuards.Evaluate(state, new[] { GamePhase.Investigation },
                rooms: rooms, actorRoomId: "living-room", targetRoomId: "does-not-exist");

            Assert.AreEqual(ActionGuardFailureReason.RoomNotFound, result.Reason);
        }

        [Test]
        public void Evaluate_RealDeceasedNpcState_FailsTargetDeceased()
        {
            // NPC 모듈의 실제 NpcState가 Core.IAliveCheckable을 구현해 ActionGuards와
            // 실제로 연결되는지 확인한다(계산기만 있고 연결이 끊긴 것이 아님을 증명).
            var state = new GameStateController();
            state.BeginContract("c1");

            var npc = new NpcState("npc-1", "c1", NpcActualRole.Witness, NpcPersonality.Indifferent);
            npc.MarkDeceased();

            var result = ActionGuards.Evaluate(state, new[] { GamePhase.Investigation }, target: npc);

            Assert.AreEqual(ActionGuardFailureReason.TargetDeceased, result.Reason);
        }

        [Test]
        public void TryBeginOnce_SameEventId_OnlySucceedsFirstTime()
        {
            var log = new GameEventLog();

            bool first = ActionGuards.TryBeginOnce(log, "action-1", GameEventType.Custom, "c1");
            bool second = ActionGuards.TryBeginOnce(log, "action-1", GameEventType.Custom, "c1");

            Assert.IsTrue(first);
            Assert.IsFalse(second, "동일 행동 eventId는 중복 실행되지 않아야 한다.");
        }
    }
}
