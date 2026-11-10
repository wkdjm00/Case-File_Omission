using System;
using System.Collections.Generic;

namespace CaseFileOmission.Core
{
    /// <summary>§15 구조화된 오류 기록 항목.</summary>
    public sealed class GameLogEntry
    {
        public string ErrorCode;
        public string System;
        public string NpcId;
        public string ClueId;
        public string ContractId;
        public GamePhase Phase;
        public string Cause;
        public bool Recoverable;
        public DateTime OccurredAtUtc;
    }

    /// <summary>
    /// §15 확정 항목(오류코드/발생시스템/관련인물ID/관련단서ID/관련사건ID/게임단계/발생원인/복구여부)을
    /// 기록한다. 플레이어에게는 노출하지 않고 개발 진단용으로만 사용한다(§15 확정).
    /// </summary>
    public sealed class GameLogger
    {
        private readonly List<GameLogEntry> _entries = new List<GameLogEntry>();

        public IReadOnlyList<GameLogEntry> Entries => _entries;

        public GameLogEntry LogError(
            string errorCode,
            string system,
            GamePhase phase,
            string cause,
            bool recoverable,
            string contractId = null,
            string npcId = null,
            string clueId = null)
        {
            var entry = new GameLogEntry
            {
                ErrorCode = errorCode,
                System = system,
                ContractId = contractId,
                NpcId = npcId,
                ClueId = clueId,
                Phase = phase,
                Cause = cause,
                Recoverable = recoverable,
                OccurredAtUtc = DateTime.UtcNow
            };

            _entries.Add(entry);
            return entry;
        }
    }
}
