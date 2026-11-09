using System;
using System.Linq;
using CaseFileOmission.Rooms;

namespace CaseFileOmission.NPC
{
    public enum NpcActionType
    {
        Stay,
        Move,
        AttemptInfoTransfer
    }

    public sealed class NpcActionResult
    {
        public NpcActionType ActionType;
        public string ActorId;

        /// <summary>Move면 목적지 방 ID, AttemptInfoTransfer면 수신자 NPC ID.</summary>
        public string TargetId;

        public string InfoId;
        public InfoTransferResult TransferResult;
    }

    /// <summary>
    /// §5-6 NPC 행동 결정 7단계의 1차 구현. 기존 NpcInfoTransferService를 재작성하지 않고
    /// 그대로 사용하며, 이 클래스는 "언제 누구에게 정보를 전달 시도할지, 아니면 어디로 이동할지"만
    /// 결정한다. 회유(NpcPersuasionService)는 플레이어의 명시적 행동이라 이 자동 루프에 포함하지 않는다.
    ///
    /// 범위 한정: 성격별 우선순위·목표·전략적 판단(§5-5)은 포함하지 않는다. 지금은
    /// "같은 방에 정보를 가진 상대가 있으면 전달을 시도하고, 없으면 인접한 방으로 이동한다"는
    /// 최소 규칙만 구현한다.
    /// </summary>
    public sealed class NpcActionLoop
    {
        private readonly NpcRegistry _registry;
        private readonly RoomOccupancyTracker _occupancy;
        private readonly RoomGraph _rooms;
        private readonly NpcInfoTransferService _transferService;
        private readonly Random _random;

        public NpcActionLoop(
            NpcRegistry registry,
            RoomOccupancyTracker occupancy,
            RoomGraph rooms,
            NpcInfoTransferService transferService,
            Random random = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _occupancy = occupancy ?? throw new ArgumentNullException(nameof(occupancy));
            _rooms = rooms ?? throw new ArgumentNullException(nameof(rooms));
            _transferService = transferService ?? throw new ArgumentNullException(nameof(transferService));
            _random = random ?? new Random();
        }

        public NpcActionResult DecideAndAct(string npcId, string contractId)
        {
            var actor = _registry.Get(npcId);

            // §5-1: 사망한 인물은 직접 행동하지 않는다.
            if (!actor.IsAlive)
                return new NpcActionResult { ActionType = NpcActionType.Stay, ActorId = npcId };

            if (!_occupancy.HasPosition(npcId))
                return new NpcActionResult { ActionType = NpcActionType.Stay, ActorId = npcId };

            string currentRoom = _occupancy.GetRoom(npcId);

            var sameRoomNpcs = _registry.GetByContract(contractId)
                .Where(n => n.NpcId != npcId && n.IsAlive &&
                            _occupancy.HasPosition(n.NpcId) && _occupancy.GetRoom(n.NpcId) == currentRoom)
                .ToList();

            foreach (var other in sameRoomNpcs)
            {
                string infoToShare = other.KnownInfoIds.FirstOrDefault(info => !actor.Knows(info));
                if (infoToShare == null)
                    continue;

                var transferResult = _transferService.TryTransfer(
                    other,
                    actor,
                    infoToShare,
                    canContact: true,
                    senderSuspicionTowardReceiver: SuspicionLevel.None,
                    hasConcreteBenefit: false,
                    hasExposureRisk: false);

                return new NpcActionResult
                {
                    ActionType = NpcActionType.AttemptInfoTransfer,
                    ActorId = other.NpcId,
                    TargetId = npcId,
                    InfoId = infoToShare,
                    TransferResult = transferResult
                };
            }

            var adjacentRooms = _rooms.GetAdjacentRooms(currentRoom).ToList();
            if (adjacentRooms.Count == 0)
                return new NpcActionResult { ActionType = NpcActionType.Stay, ActorId = npcId };

            string destination = adjacentRooms[_random.Next(adjacentRooms.Count)];
            _occupancy.TryMove(npcId, destination);

            return new NpcActionResult { ActionType = NpcActionType.Move, ActorId = npcId, TargetId = destination };
        }
    }
}
