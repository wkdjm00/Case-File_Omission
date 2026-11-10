using System;
using System.Collections.Generic;
using CaseFileOmission.Core;

namespace CaseFileOmission.NPC
{
    /// <summary>
    /// NPC 한 명의 런타임 상태. 실제 역할과 지식·믿음을 분리해 관리한다 (04·07페이지 확정 원칙).
    /// </summary>
    public sealed class NpcState : IAliveCheckable
    {
        public string NpcId { get; }
        public string ContractId { get; }
        public NpcActualRole ActualRole;
        public NpcPersonality Personality;

        /// <summary>09페이지 인물 정보 창의 "직업" 항목. 메인 사건 단서 조건("피해자가 비서")에서 쓴다.</summary>
        public string Occupation;

        /// <summary>§5-2 "생존 여부". 사망한 인물은 직접 행동하지 않는다(§5-1 확정).</summary>
        public bool IsAlive { get; private set; } = true;

        public bool IsActualPerpetrator => ActualRole == NpcActualRole.Perpetrator;

        /// <summary>실제로 보유한 정보 ID 집합.</summary>
        public readonly HashSet<string> KnownInfoIds = new HashSet<string>();

        /// <summary>보유한 정보에 대한 판단(사실·의심·보류). 보유하지 않은 정보는 기록하지 않는다.</summary>
        public readonly Dictionary<string, NpcJudgement> Beliefs = new Dictionary<string, NpcJudgement>();

        /// <summary>회유 성공 후 사건이 끝날 때까지 협조 상태인지.</summary>
        public bool IsCooperatingWithPlayer { get; private set; }

        /// <summary>
        /// 실제로 목격한 사건 ID. 07 확정: 목격자 여부는 고정 분류가 아니라 실제 목격 사실로 관리하며, 관계자이면서 목격자일 수 있다.
        /// <see cref="ActualRole"/>의 Witness는 몸체(역할 공개용) 구분일 뿐이다.
        /// </summary>
        public readonly HashSet<string> WitnessedEventIds = new HashSet<string>();

        public bool HasWitnessedAnything => WitnessedEventIds.Count > 0;

        public void MarkWitnessed(string eventId)
        {
            if (!string.IsNullOrEmpty(eventId))
                WitnessedEventIds.Add(eventId);
        }

        public NpcState(string npcId, string contractId, NpcActualRole actualRole, NpcPersonality personality)
        {
            if (string.IsNullOrEmpty(npcId))
                throw new ArgumentException("npcId는 비어 있을 수 없습니다.", nameof(npcId));

            NpcId = npcId;
            ContractId = contractId;
            ActualRole = actualRole;
            Personality = personality;
        }

        public void LearnInfo(string infoId, NpcJudgement judgement)
        {
            KnownInfoIds.Add(infoId);
            Beliefs[infoId] = judgement;
        }

        public bool Knows(string infoId) => KnownInfoIds.Contains(infoId);

        public void MarkCooperating() => IsCooperatingWithPlayer = true;

        /// <summary>§5-1: 사망한 피해자는 직접 행동하지 않는다. 가해자 등 다른 역할도 사망할 수 있다.</summary>
        public void MarkDeceased() => IsAlive = false;
    }
}
