using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Core;
using CaseFileOmission.Endings;
using CaseFileOmission.MainCase;
using CaseFileOmission.Organization;
using CaseFileOmission.Police;
using UnityEngine;

namespace CaseFileOmission.SaveLoad
{
    /// <summary>
    /// §12 확정 완료 기준 중: "저장 후 게임을 재시작해도 필요한 상태가 복원된다",
    /// "저장 데이터에 없는 값은 안전하게 초기화하거나 오류를 보고한다",
    /// "잘못된 저장 데이터가 게임 상태를 망가뜨리지 않는다",
    /// "엔딩이 이미 실행된 상태를 불러왔을 때 엔딩이 중복 실행되지 않는다"를 다룬다.
    /// 직렬화는 UnityEngine.JsonUtility를 사용한다(Dictionary를 직접 다루지 않는 범위로 DTO를 설계했다).
    /// </summary>
    public static class SaveGameService
    {
        public static SaveGameData Capture(
            GameStateController gameState,
            OrganizationState organization,
            PoliceSuspicionService policeSuspicion,
            PoliceLifeStateService policeLifeState,
            MainCaseProgressTracker mainCaseProgress,
            EndingExecutor endingExecutor,
            string playerId)
        {
            var data = new SaveGameData
            {
                ContractId = gameState.CurrentContractId,
                Phase = gameState.CurrentPhase,
                TimeBlock = gameState.CurrentTimeBlock,
                CurrentDay = gameState.CurrentDay,
                OrganizationTrust = organization.Trust,
                MainCaseStage = mainCaseProgress.CurrentStage,
                AcquiredMainCaseClueNames = mainCaseProgress.AcquiredClueNames.ToArray(),
                DeceasedPoliceIds = policeLifeState.GetDeceasedIds().ToArray(),
                ExecutedEnding = endingExecutor.ExecutedEnding ?? GameEndingType.None,
                MainNarrativeEnding = endingExecutor.MainNarrativeEnding ?? GameEndingType.None,
                HasTimeBudget = true,
                RemainingTimeSlots = gameState.RemainingTimeSlots,
                TimeBlockEnded = gameState.IsTimeBlockEnded
            };

            var suspicionEntries = new List<PoliceSuspicionEntry>();
            foreach (var policeId in PoliceRoster.AllPoliceIds)
            {
                suspicionEntries.Add(new PoliceSuspicionEntry
                {
                    PoliceId = policeId,
                    Suspicion = policeSuspicion.GetSuspicion(policeId, playerId)
                });
            }
            data.PoliceSuspicions = suspicionEntries.ToArray();

            return data;
        }

        public static string ToJson(SaveGameData data) => JsonUtility.ToJson(data);

        public static SaveGameData FromJson(string json) => JsonUtility.FromJson<SaveGameData>(json);

        /// <summary>
        /// 저장된 데이터를 "새로 생성된" 대상 객체들에 복원한다. 존재하지 않는 경찰 ID 등
        /// 알 수 없는 참조는 건너뛰고(안전 처리), logger가 주어지면 기록을 남긴다.
        /// </summary>
        public static void Restore(
            SaveGameData data,
            GameStateController gameState,
            OrganizationState organization,
            PoliceSuspicionService policeSuspicion,
            PoliceLifeStateService policeLifeState,
            MainCaseProgressTracker mainCaseProgress,
            EndingExecutor endingExecutor,
            string playerId,
            GameLogger logger = null)
        {
            if (data == null)
            {
                logger?.LogError("SAVE_DATA_NULL", "SaveLoad", GamePhase.Idle, "불러올 저장 데이터가 null입니다.", recoverable: true);
                return;
            }

            gameState.RestoreState(data.ContractId, data.Phase, data.TimeBlock, data.CurrentDay);
            if (data.HasTimeBudget)
                gameState.RestoreTimeBudget(data.RemainingTimeSlots, data.TimeBlockEnded);
            organization.SetTrust(data.OrganizationTrust);

            if (data.PoliceSuspicions != null)
            {
                foreach (var entry in data.PoliceSuspicions)
                {
                    if (!PoliceRoster.AllPoliceIds.Contains(entry.PoliceId))
                    {
                        logger?.LogError("SAVE_UNKNOWN_POLICE_ID", "SaveLoad", data.Phase,
                            $"알 수 없는 경찰 ID '{entry.PoliceId}'는 건너뜁니다.", recoverable: true);
                        continue;
                    }

                    policeSuspicion.SetSuspicion(entry.PoliceId, playerId, entry.Suspicion);
                }
            }

            if (data.DeceasedPoliceIds != null)
            {
                foreach (var policeId in data.DeceasedPoliceIds)
                {
                    if (!PoliceRoster.AllPoliceIds.Contains(policeId))
                    {
                        logger?.LogError("SAVE_UNKNOWN_POLICE_ID", "SaveLoad", data.Phase,
                            $"알 수 없는 경찰 ID '{policeId}'는 건너뜁니다.", recoverable: true);
                        continue;
                    }

                    policeLifeState.MarkDeceased(policeId);
                }
            }

            if (data.AcquiredMainCaseClueNames != null)
            {
                foreach (var clueName in data.AcquiredMainCaseClueNames)
                    mainCaseProgress.AcquireClue(clueName);
            }
            mainCaseProgress.RestoreStage(data.MainCaseStage);

            // 엔딩이 이미 실행된 상태로 저장되어 있었다면, 불러온 뒤에도 같은 엔딩으로 1회만 복원한다
            // (중복 실행 방지 — EndingExecutor.TryExecute는 이미 실행된 상태에서는 아무 효과가 없다).
            // 메인 서사 엔딩을 먼저, 게임 종료 엔딩을 나중에 복원해야 둘 다 잠긴 상태가 그대로 되살아난다.
            if (data.MainNarrativeEnding != GameEndingType.None)
                endingExecutor.TryExecute(data.MainNarrativeEnding);
            if (data.ExecutedEnding != GameEndingType.None)
                endingExecutor.TryExecute(data.ExecutedEnding);
        }
    }
}
