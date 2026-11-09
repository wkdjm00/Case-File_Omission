using System;

namespace CaseFileOmission.Core
{
    /// <summary>
    /// 개별 의뢰의 진행 단계. 06페이지 "의뢰 수락과 사건 시작" / "조사 시간 종료와 회의" 확정 흐름을 따른다.
    /// </summary>
    public enum GamePhase
    {
        Idle,
        Investigation,
        Meeting,
        Resolved
    }

    /// <summary>
    /// 현재 의뢰와 상위 진행 단계를 관리하는 최상위 상태 컨트롤러.
    /// 실제 사건 진실·조직 신뢰도·경찰 의심도 등 세부 상태는 각 모듈이 별도로 관리하며,
    /// 이 클래스는 "지금 어떤 의뢰가 어느 단계에 있는지"만 책임진다.
    /// </summary>
    public sealed class GameStateController
    {
        public GamePhase CurrentPhase { get; private set; } = GamePhase.Idle;
        public string CurrentContractId { get; private set; }
        public GameEventLog EventLog { get; } = new GameEventLog();

        /// <summary>§2-2 확정: 하루는 오전/오후 두 조사 구간. 의뢰 시작 시 오전부터 시작한다.</summary>
        public TimeBlock CurrentTimeBlock { get; private set; } = TimeBlock.Morning;

        /// <summary>며칠째인지. 의뢰 시작 시 1일차부터 시작한다.</summary>
        public int CurrentDay { get; private set; } = 1;

        /// <summary>
        /// 시간 구간이 바뀔 때 발생. NPC 일정·경찰 행동 갱신은 이 이벤트를 구독해 처리한다
        /// (§2-2: "시간 구간이 변경되면 NPC 일정과 경찰 행동을 갱신한다").
        /// </summary>
        public event Action<TimeBlock, int> TimeBlockAdvanced;

        public void BeginContract(string contractId)
        {
            if (string.IsNullOrEmpty(contractId))
                throw new ArgumentException("contractId는 비어 있을 수 없습니다.", nameof(contractId));

            CurrentContractId = contractId;
            CurrentPhase = GamePhase.Investigation;
            CurrentTimeBlock = TimeBlock.Morning;
            CurrentDay = 1;
        }

        /// <summary>
        /// 플레이어가 현재 조사 구간 종료를 직접 선택했을 때만 호출한다. 이동·조사 행동만으로는
        /// 자동 호출되지 않는다(§2-2 확정). 조사 단계에서만 허용한다.
        /// </summary>
        public void AdvanceTimeBlock()
        {
            if (CurrentPhase != GamePhase.Investigation)
                throw new InvalidOperationException("조사 단계에서만 시간 구간을 진행할 수 있습니다.");

            if (CurrentTimeBlock == TimeBlock.Morning)
            {
                CurrentTimeBlock = TimeBlock.Afternoon;
            }
            else
            {
                CurrentTimeBlock = TimeBlock.Morning;
                CurrentDay++;
            }

            TimeBlockAdvanced?.Invoke(CurrentTimeBlock, CurrentDay);
        }

        public void EnterMeeting()
        {
            if (CurrentPhase != GamePhase.Investigation)
                throw new InvalidOperationException("조사 단계에서만 회의로 전환할 수 있습니다.");

            CurrentPhase = GamePhase.Meeting;
        }

        public void ResolveContract()
        {
            if (CurrentPhase != GamePhase.Meeting)
                throw new InvalidOperationException("회의 단계에서만 의뢰를 종료할 수 있습니다.");

            CurrentPhase = GamePhase.Resolved;
        }

        public void ResetToIdle()
        {
            CurrentPhase = GamePhase.Idle;
            CurrentContractId = null;
            CurrentTimeBlock = TimeBlock.Morning;
            CurrentDay = 1;
        }

        /// <summary>
        /// 저장/불러오기 전용. BeginContract 등 정상 플레이 경로의 상태 전이 규칙을 거치지 않고
        /// 저장된 값을 그대로 복원한다. 일반 게임플레이 중에는 사용하지 않는다.
        /// </summary>
        public void RestoreState(string contractId, GamePhase phase, TimeBlock timeBlock, int day)
        {
            CurrentContractId = contractId;
            CurrentPhase = phase;
            CurrentTimeBlock = timeBlock;
            CurrentDay = day;
        }
    }
}
