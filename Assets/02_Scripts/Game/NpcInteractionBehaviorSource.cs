using System;
using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.NPC;
using CaseFileOmission.Police;

namespace CaseFileOmission.Game
{
    /// <summary>
    /// NPC가 같은 방의 플레이어를 피하거나, 다른 NPC에게 질문해 정보를 얻는 행동 후보.
    /// 질문 결과는 기존 정보 전달 확률·관계·신뢰 보정을 사용한다.
    /// </summary>
    public sealed class NpcInteractionBehaviorSource : INpcBehaviorCandidateSource
    {
        private readonly GameSession _session;

        public NpcInteractionBehaviorSource(GameSession session) =>
            _session = session ?? throw new ArgumentNullException(nameof(session));

        public IEnumerable<NpcBehaviorCandidate> GetCandidates(NpcState actor, string currentRoomId, int currentMinute)
        {
            var s = _session;
            var contract = s.CurrentContract;
            if (contract == null || actor == null || !actor.IsAlive
                || actor.ContractId != contract.ContractId
                || string.IsNullOrEmpty(currentRoomId))
                yield break;

            string handler = s.HandlerPoliceId;
            if (handler != null && PoliceRoster.AllPoliceIds.Contains(handler)
                && s.PoliceLife.IsAlive(handler) && s.Occupancy.HasPosition(handler)
                && s.Occupancy.GetRoom(handler) == currentRoomId)
            {
                var reportable = actor.InfoHistory
                    .Where(r => r != null && actor.Knows(r.InfoId)
                        && r.State == NpcInformationState.KnowsFact
                        && !string.IsNullOrEmpty(r.SourceEventId)
                        && (actor.WitnessedEventIds.Contains(r.SourceEventId)
                            || r.InfoId.StartsWith("missing:", StringComparison.Ordinal))
                        && !s.HasNpcReportedInfo(actor.NpcId, r.InfoId))
                    .OrderBy(r => r.Minute ?? int.MaxValue)
                    .ThenBy(r => r.InfoId, StringComparer.Ordinal)
                    .FirstOrDefault();

                if (reportable != null)
                {
                    string infoId = reportable.InfoId;
                    bool urgent = actor.WitnessedEventIds.Contains(reportable.SourceEventId);
                    yield return new NpcBehaviorCandidate
                    {
                        Kind = NpcBehaviorKind.Report,
                        TargetId = handler,
                        InfoId = infoId,
                        RequiredRoomId = currentRoomId,
                        IsUrgent = urgent,
                        TargetIsTrusted = s.Planner.IsTrusted(actor.NpcId, handler),
                        Execute = () =>
                        {
                            if (actor.IsAlive && s.Occupancy.HasPosition(actor.NpcId)
                                && s.Occupancy.GetRoom(actor.NpcId) == currentRoomId
                                && s.PoliceLife.IsAlive(handler) && s.Occupancy.HasPosition(handler)
                                && s.Occupancy.GetRoom(handler) == currentRoomId
                                && actor.Knows(infoId) && !s.HasNpcReportedInfo(actor.NpcId, infoId))
                                s.ReportNpcInfoToPolice(actor.NpcId, infoId, currentRoomId, currentMinute);
                        }
                    };
                }
            }

            string playerRoom = s.PlayerRoom;
            if (playerRoom == currentRoomId)
            {
                string escapeRoom = s.Rooms.GetAdjacentRooms(currentRoomId)
                    .Where(room => room != playerRoom)
                    .OrderBy(room => room, StringComparer.Ordinal)
                    .FirstOrDefault();

                if (escapeRoom != null)
                {
                    yield return new NpcBehaviorCandidate
                    {
                        Kind = NpcBehaviorKind.Avoid,
                        TargetId = escapeRoom,
                        RequiredRoomId = currentRoomId,
                        IsUrgent = actor.Personality == NpcPersonality.Timid,
                        Execute = () =>
                        {
                            if (actor.IsAlive && s.Occupancy.HasPosition(actor.NpcId)
                                && s.Occupancy.GetRoom(actor.NpcId) == currentRoomId
                                && s.PlayerRoom == currentRoomId
                                && s.Rooms.AreConnected(currentRoomId, escapeRoom))
                                s.Occupancy.TryMove(actor.NpcId, escapeRoom);
                        }
                    };
                }
            }

            var partners = s.Npcs.GetByContract(contract.ContractId)
                .Where(n => n.NpcId != actor.NpcId && n.IsAlive
                    && s.Occupancy.HasPosition(n.NpcId)
                    && s.Occupancy.GetRoom(n.NpcId) == currentRoomId)
                .OrderBy(n => n.NpcId, StringComparer.Ordinal);

            foreach (var responder in partners)
            {
                string infoId = responder.KnownInfoIds
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .FirstOrDefault(id => !actor.Knows(id)
                        && !s.InfoTransfer.HasTransferred(responder.NpcId, actor.NpcId, id));

                if (infoId == null)
                    continue;

                string capturedInfoId = infoId;
                string responderId = responder.NpcId;
                yield return new NpcBehaviorCandidate
                {
                    Kind = NpcBehaviorKind.Question,
                    TargetId = responderId,
                    InfoId = capturedInfoId,
                    RequiredRoomId = currentRoomId,
                    TargetIsTrusted = s.Planner.IsTrusted(actor.NpcId, responderId),
                    Execute = () =>
                    {
                        if (!actor.IsAlive || !responder.IsAlive
                            || !s.Occupancy.HasPosition(actor.NpcId)
                            || !s.Occupancy.HasPosition(responderId)
                            || s.Occupancy.GetRoom(actor.NpcId) != currentRoomId
                            || s.Occupancy.GetRoom(responderId) != currentRoomId
                            || actor.Knows(capturedInfoId)
                            || !responder.Knows(capturedInfoId))
                            return;

                        var context = s.Planner.TransferContext?.Invoke(responder, actor, capturedInfoId) ?? (false, false);
                        float directionalSuspicion = s.Relationships.GetSuspicion(responderId, actor.NpcId);
                        var level = s.Planner.SuspicionClassifier != null
                            ? s.Planner.SuspicionClassifier(directionalSuspicion)
                            : NpcInfoTransferService.ClassifySuspicion(directionalSuspicion);

                        var transfer = s.InfoTransfer.TryTransfer(
                            responder,
                            actor,
                            capturedInfoId,
                            canContact: true,
                            level,
                            context.HasConcreteBenefit,
                            context.HasExposureRisk,
                            s.Planner.IsTrusted(responderId, actor.NpcId));

                        if (transfer.Outcome == InfoTransferOutcome.Succeeded || transfer.Outcome == InfoTransferOutcome.Failed)
                        {
                            s.RecentExchanges.Add(new OverheardExchange
                            {
                                SpeakerId = responderId,
                                ListenerId = actor.NpcId,
                                InfoId = capturedInfoId,
                                RoomId = currentRoomId,
                                Minute = currentMinute,
                                Succeeded = transfer.Outcome == InfoTransferOutcome.Succeeded
                            });
                        }
                    }
                };
            }
        }
    }
}
