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

        /// <summary>GameEndingType.None이면 아직 어떤 엔딩도 실행되지 않은 상태다.</summary>
        public GameEndingType ExecutedEnding;

        /// <summary>세부 월드 상태. 예전 저장 파일에는 없을 수 있다(Version 0 또는 null).</summary>
        public WorldSaveData World;
    }
}
