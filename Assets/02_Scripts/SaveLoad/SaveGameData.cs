using System;
using CaseFileOmission.Core;
using CaseFileOmission.Endings;
using CaseFileOmission.MainCase;

namespace CaseFileOmission.SaveLoad
{
    [Serializable]
    public sealed class PoliceSuspicionEntry
    {
        public string PoliceId;
        public float Suspicion;
    }

    /// <summary>
    /// §12 저장 데이터. 기본 구역(Core 진행 상태, 조직 신뢰도, 경찰 의심도·생존상태, 메인 사건 단계,
    /// 엔딩 실행 상태)은 SaveGameService가, Dictionary 기반 세부 상태(단서 그래프, NPC 지식,
    /// 회의 연속 카운터, 보고서 등)는 World 구역에 WorldStateSaveService가 담당한다.
    /// World가 없는 예전 저장 파일도 기본 구역만으로 그대로 불러올 수 있다.
    /// </summary>
    [Serializable]
    public sealed class SaveGameData
    {
        public string ContractId;
        public GamePhase Phase;
        public TimeBlock TimeBlock;
        public int CurrentDay;

        public float OrganizationTrust;

        public PoliceSuspicionEntry[] PoliceSuspicions;
        public string[] DeceasedPoliceIds;

        public MainCaseTruthStage MainCaseStage;
        public string[] AcquiredMainCaseClueNames;

        /// <summary>GameEndingType.None이면 아직 어떤 엔딩도 실행되지 않은 상태다(게임 종료 엔딩이 있으면 그것, 없으면 메인 서사 엔딩).</summary>
        public GameEndingType ExecutedEnding;

        /// <summary>실행된 메인 서사 엔딩. 게임 종료 엔딩과 따로 저장한다(메인 엔딩 뒤에도 진행이 계속되므로).</summary>
        public GameEndingType MainNarrativeEnding;

        /// <summary>조사 시간 예산이 저장되었는지. 예전 저장 파일에는 없다.</summary>
        public bool HasTimeBudget;
        public int RemainingTimeSlots;
        public bool TimeBlockEnded;

        /// <summary>세부 월드 상태. 예전 저장 파일에는 없을 수 있다(Version 0 또는 null).</summary>
        public WorldSaveData World;

        // 06 「2026-11-13 저장/불러오기·버전·복구 구현 계약」 메타데이터. 예전 파일에는 없다(0·null).
        public int SchemaVersion;
        /// <summary>한 플레이(새 게임)마다 다른 ID.</summary>
        public string RunId;
        /// <summary>같은 플레이 안에서 저장할 때마다 1씩 오른다(슬롯 사이 최신 판별).</summary>
        public int SaveSequence;
        public string SavedAtUtc;
        /// <summary>저장된 슬롯(수동 1~3, 자동 0).</summary>
        public int Slot;
        /// <summary>게임용 난수 생성기의 seed와 상태. 비어 있으면 이전 저장 또는 외부 Random 주입 데이터다.</summary>
        public int RandomSeed;
        public string RandomState;
        public string ContentVersion;
        public string BuildVersion;
    }
}
