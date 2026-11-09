using System;
using System.Collections.Generic;
using System.Linq;

namespace CaseFileOmission.NPC
{
    /// <summary>한 의뢰에 등장하는 NPC 상태를 모아서 관리하는 저장소.</summary>
    public sealed class NpcRegistry
    {
        private readonly Dictionary<string, NpcState> _npcs = new Dictionary<string, NpcState>();

        public void Add(NpcState npc)
        {
            if (npc == null) throw new ArgumentNullException(nameof(npc));
            if (_npcs.ContainsKey(npc.NpcId))
                throw new InvalidOperationException($"NPC '{npc.NpcId}'는 이미 등록되어 있습니다.");

            _npcs[npc.NpcId] = npc;
        }

        public NpcState Get(string npcId)
        {
            if (!_npcs.TryGetValue(npcId, out var npc))
                throw new KeyNotFoundException($"NPC '{npcId}'를 찾을 수 없습니다.");
            return npc;
        }

        public bool Exists(string npcId) => _npcs.ContainsKey(npcId);

        public IEnumerable<NpcState> All => _npcs.Values;

        public IReadOnlyList<NpcState> GetByContract(string contractId) =>
            _npcs.Values.Where(n => n.ContractId == contractId).ToList();
    }
}
