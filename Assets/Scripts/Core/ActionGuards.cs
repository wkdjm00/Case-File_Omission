using System.Linq;

namespace CaseFileOmission.Core
{
    /// <summary>
    /// §2-4 공통 예외 처리 규칙을 검사하는 공용 가드. 행동 자체를 실행하지 않고 "실행해도 되는지"만
    /// 판정한다 — 실제 실행(§2-3 6번)은 각 모듈이 담당한다.
    /// </summary>
    public enum ActionGuardFailureReason
    {
        None,
        WrongPhase,
        TargetDeceased,
        RoomNotFound,
        RoomsNotConnected,
        ContractAlreadyResolved,
        DuplicateExecution
    }

    public sealed class ActionGuardResult
    {
        public bool Allowed => Reason == ActionGuardFailureReason.None;
        public ActionGuardFailureReason Reason;

        public static readonly ActionGuardResult Ok = new ActionGuardResult { Reason = ActionGuardFailureReason.None };

        public static ActionGuardResult Fail(ActionGuardFailureReason reason) =>
            new ActionGuardResult { Reason = reason };
    }

    public static class ActionGuards
    {
        /// <summary>§2-3 1번: 현재 게임 단계 확인.</summary>
        public static bool PhaseAllowed(GamePhase current, params GamePhase[] allowedPhases) =>
            allowedPhases.Contains(current);

        /// <summary>§2-4: 사건 종료 후에는 해당 사건의 남은 행동을 취소한다.</summary>
        public static bool ContractIsResolved(GameStateController state) =>
            state.CurrentPhase == GamePhase.Resolved;

        /// <summary>§2-4: 사망한 인물과는 직접 상호작용할 수 없다. target이 null이면 대상이 없는 행동이므로 통과시킨다.</summary>
        public static bool TargetIsUsable(IAliveCheckable target) => target == null || target.IsAlive;

        /// <summary>§2-4: 연결되지 않은 방에서는 상호작용할 수 없다.</summary>
        public static bool RoomsConnected(IRoomConnectivityProvider rooms, string roomIdA, string roomIdB)
        {
            if (!rooms.RoomExists(roomIdA) || !rooms.RoomExists(roomIdB))
                return false;

            return roomIdA == roomIdB || rooms.AreConnected(roomIdA, roomIdB);
        }

        /// <summary>
        /// §2-4: 동일 행동이 동시에 중복 실행되지 않도록 한다. 고유 eventId로 중복 여부를 판정한다.
        /// true를 반환하면 이번 호출에서 처음 실행하는 것이고, false면 이미 처리된 중복 요청이다.
        /// </summary>
        public static bool TryBeginOnce(GameEventLog log, string actionEventId, GameEventType type, string contractId)
        {
            if (log.HasProcessed(actionEventId))
                return false;

            return log.TryRecord(new GameEvent(actionEventId, type, contractId, payload: null));
        }

        /// <summary>
        /// 위 개별 검사를 묶어 한 번에 판정한다. target/rooms 인자는 해당 검사가 필요 없으면 null로 둔다.
        /// </summary>
        public static ActionGuardResult Evaluate(
            GameStateController state,
            GamePhase[] allowedPhases,
            IAliveCheckable target = null,
            IRoomConnectivityProvider rooms = null,
            string actorRoomId = null,
            string targetRoomId = null)
        {
            if (ContractIsResolved(state))
                return ActionGuardResult.Fail(ActionGuardFailureReason.ContractAlreadyResolved);

            if (allowedPhases != null && allowedPhases.Length > 0 && !PhaseAllowed(state.CurrentPhase, allowedPhases))
                return ActionGuardResult.Fail(ActionGuardFailureReason.WrongPhase);

            if (!TargetIsUsable(target))
                return ActionGuardResult.Fail(ActionGuardFailureReason.TargetDeceased);

            if (rooms != null && actorRoomId != null && targetRoomId != null)
            {
                if (!rooms.RoomExists(actorRoomId) || !rooms.RoomExists(targetRoomId))
                    return ActionGuardResult.Fail(ActionGuardFailureReason.RoomNotFound);

                if (!RoomsConnected(rooms, actorRoomId, targetRoomId))
                    return ActionGuardResult.Fail(ActionGuardFailureReason.RoomsNotConnected);
            }

            return ActionGuardResult.Ok;
        }
    }
}
