using System;
using CaseFileOmission.Contracts;
using CaseFileOmission.Evidence;
using CaseFileOmission.NPC;
using CaseFileOmission.Rooms;

namespace CaseFileOmission.ContractContent
{
    /// <summary>
    /// §3-2 생성 파이프라인 중 인물·공간·단서 생성(5~9단계)의 1차 구현.
    /// ContractGenerator(범죄유형·조직조건 배정)가 만든 ContractInstance를 입력받아
    /// NPC·방 배치·단서를 채운다.
    ///
    /// 범위 한정: 가해자 1명·피해자 1명·목격자 1명 고정 구성, 단서 2개 고정 생성,
    /// 방 배치도 HouseLayoutFactory의 고정 템플릿을 사용한다. 실제 증언·문서 "내용" 집필,
    /// 동기·수단·시간표의 다양한 조합 생성, NPC 일정(스케줄) 충돌 검사는 이 클래스의 범위 밖이며
    /// 이후 단계(§5 NPC 행동 결정, §9 Report)에서 보강한다.
    /// </summary>
    public sealed class ContractContentGenerator
    {
        private readonly Random _random;

        public ContractContentGenerator(Random random = null)
        {
            _random = random ?? new Random();
        }

        public void Populate(
            ContractInstance contract,
            NpcRegistry npcRegistry,
            ClueRepository clueRepository,
            RoomOccupancyTracker occupancy)
        {
            if (contract == null) throw new ArgumentNullException(nameof(contract));
            if (npcRegistry == null) throw new ArgumentNullException(nameof(npcRegistry));
            if (clueRepository == null) throw new ArgumentNullException(nameof(clueRepository));
            if (occupancy == null) throw new ArgumentNullException(nameof(occupancy));

            string victimId = $"{contract.ContractId}-victim";
            string perpetratorId = $"{contract.ContractId}-perpetrator";
            string witnessId = $"{contract.ContractId}-witness-1";

            var victim = new NpcState(victimId, contract.ContractId, NpcActualRole.Victim, PickPersonality());
            var perpetrator = new NpcState(perpetratorId, contract.ContractId, NpcActualRole.Perpetrator, PickPersonality());
            var witness = new NpcState(witnessId, contract.ContractId, NpcActualRole.Witness, PickPersonality());

            // 가해자는 자신의 행위를 사실로 알고 있다(최소한의 진실 지식 시딩).
            string truthInfoId = $"{contract.ContractId}-truth-perpetrator";
            perpetrator.LearnInfo(truthInfoId, NpcJudgement.Believe);

            npcRegistry.Add(victim);
            npcRegistry.Add(perpetrator);
            npcRegistry.Add(witness);

            contract.Truth.VictimId = victimId;
            contract.Truth.PerpetratorId = perpetratorId;
            contract.Truth.PrimaryMotive ??= "원한(임시)";
            contract.Truth.Method ??= "둔기로 가격(임시)";
            contract.Truth.Tool ??= "둔기(임시)";

            string sceneRoomId = HouseLayoutFactory.LivingRoom;
            string witnessRoomId = HouseLayoutFactory.Kitchen;

            occupancy.PlaceAt(victimId, sceneRoomId);
            occupancy.PlaceAt(perpetratorId, sceneRoomId);
            occupancy.PlaceAt(witnessId, witnessRoomId);

            GenerateClues(contract, clueRepository, witnessId, sceneRoomId);
        }

        private static void GenerateClues(ContractInstance contract, ClueRepository repository, string witnessId, string sceneRoomId)
        {
            var weaponClue = new ClueDefinition($"{contract.ContractId}-clue-weapon", contract.ContractId)
            {
                Format = ClueFormat.PhysicalObject,
                TruthStatus = ClueTruthStatus.Factual,
                DisplayName = "범행 도구(임시)",
                DisplayedContent = $"{sceneRoomId}에서 발견된 범행 도구.",
                IdentifiesPerpetrator = true,
                SourceDescription = sceneRoomId,
                AcquisitionCondition = $"{sceneRoomId} 현장 조사"
            };
            repository.Discover(weaponClue);

            var testimonyClue = new ClueDefinition($"{contract.ContractId}-clue-testimony", contract.ContractId)
            {
                Format = ClueFormat.Testimony,
                TruthStatus = ClueTruthStatus.Factual,
                DisplayName = "목격자 증언(임시)",
                DisplayedContent = "목격자의 진술(임시 내용).",
                IdentifiesPerpetrator = false,
                SourceDescription = witnessId,
                AcquisitionCondition = $"{witnessId}와 대화"
            };
            repository.Discover(testimonyClue);
        }

        private NpcPersonality PickPersonality()
        {
            var values = (NpcPersonality[])Enum.GetValues(typeof(NpcPersonality));
            return values[_random.Next(values.Length)];
        }
    }
}
