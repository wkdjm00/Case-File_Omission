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
    /// 이 클래스는 "지금 어떤 의뢰가 어느 단계에 있는지"와 조사 시간 예산만 책임진다.
    /// </summary>
    public sealed class GameStateController
    {
        /// <summary>
        /// 06 2026-11-11 확정: 세계 시간대는 아침·낮 06:00~18:00, 저녁·야간 18:00~다음 날 06:00(각 12시간).
        /// 플레이어 조사 예산은 시간대마다 8시간 = 15분 1칸 × 32칸이다. 예산과 세계 시각은 별도 상태다.
        /// </summary>
        public const int TimeSlotsPerBlock = 32;

        /// <summary>조사 예산 1칸의 길이(분).</summary>
        public const int SlotMinutes = 15;

        /// <summary>세계 시간대 하나의 길이(분).</summary>
        public const int BlockMinutes = 12 * 60;

        public const int MorningStartMinuteOfDay = 6 * 60;
        public const int EveningStartMinuteOfDay = 18 * 60;

        public GamePhase CurrentPhase { get; private set; } = GamePhase.Idle;
        public string CurrentContractId { get; private set; }
        public GameEventLog EventLog { get; } = new GameEventLog();

        /// <summary>현재 조사 구간(아침/저녁). 의뢰가 시작하는 구간은 의뢰 데이터가 정한다(06 2026-11-10).</summary>
        public TimeBlock CurrentTimeBlock { get; private set; } = TimeBlock.Morning;

        /// <summary>며칠째인지. 의뢰 시작 시 1일차부터 시작한다.</summary>
        public int CurrentDay { get; private set; } = 1;

        /// <summary>현재 구간에 남은 조사 시간 칸. 시간이 드는 행동을 할 때만 줄어든다(실시간으로 흐르지 않음).</summary>
        public int RemainingTimeSlots { get; private set; } = TimeSlotsPerBlock;

        /// <summary>
        /// 예산을 다 썼거나 플레이어가 "조사 종료"를 골라 이번 구간이 끝났는지. 끝난 뒤에는 회의 시작 또는
        /// 다음 구간 진행 중 하나를 골라야 하며, 시간이 드는 행동은 할 수 없다(06 확정).
        /// </summary>
        public bool IsTimeBlockEnded { get; private set; }

        /// <summary>
        /// 시간 구간이 바뀔 때 발생. NPC 일정·경찰 행동 갱신은 이 이벤트를 구독해 처리한다
        /// (§2-2: "시간 구간이 변경되면 NPC 일정과 경찰 행동을 갱신한다").
        /// </summary>
        public event Action<TimeBlock, int> TimeBlockAdvanced;

        /// <summary>시간이 소모될 때마다 (소모한 칸 수, 남은 칸 수)로 발생. NPC 일정·위치·정보 전달 갱신에 쓴다.</summary>
        public event Action<int, int> TimeSpent;

        public void BeginContract(string contractId, TimeBlock startBlock = TimeBlock.Morning)
        {
            if (string.IsNullOrEmpty(contractId))
                throw new ArgumentException("contractId는 비어 있을 수 없습니다.", nameof(contractId));
            if (CurrentPhase == GamePhase.Investigation || CurrentPhase == GamePhase.Meeting)
                throw new InvalidOperationException("진행 중인 의뢰가 끝나기 전에는 새 의뢰를 시작할 수 없습니다.");

            CurrentContractId = contractId;
            CurrentPhase = GamePhase.Investigation;
            CurrentTimeBlock = startBlock;
            CurrentDay = 1;
            RemainingTimeSlots = TimeSlotsPerBlock;
            IsTimeBlockEnded = false;
        }

        /// <summary>
        /// 시간이 드는 행동(이동·새 대상 조사·대화·엿듣기 등)의 비용을 소모한다. 비용 0인 행동(자료 다시 읽기, UI 조작)은
        /// 호출하지 않는다. 남은 칸보다 비싼 행동은 할 수 없다. 예산을 다 쓰면 구간이 끝난다.
        /// </summary>
        public bool TrySpendTime(int slots)
        {
            if (slots <= 0)
                throw new ArgumentOutOfRangeException(nameof(slots), "시간 비용은 1칸 이상이어야 합니다.");
            if (CurrentPhase != GamePhase.Investigation)
                throw new InvalidOperationException("조사 단계에서만 시간을 소모할 수 있습니다.");
            if (IsTimeBlockEnded || slots > RemainingTimeSlots)
                return false;

            RemainingTimeSlots -= slots;
            if (RemainingTimeSlots == 0)
                IsTimeBlockEnded = true;

            TimeSpent?.Invoke(slots, RemainingTimeSlots);
            return true;
        }

        /// <summary>예산이 남아 있어도 플레이어가 "조사 종료"를 고를 수 있다(06 2026-11-10).</summary>
        public void EndTimeBlockEarly()
        {
            if (CurrentPhase != GamePhase.Investigation)
                throw new InvalidOperationException("조사 단계에서만 조사를 종료할 수 있습니다.");
            IsTimeBlockEnded = true;
        }

        /// <summary>
        /// 다음 조사 구간으로 넘어간다(아침 → 저녁, 저녁 → 다음 날 아침). 단서와 사건 상태는 유지되고 예산은 다시 12칸이 된다.
        /// 조사 단계에서만 허용한다.
        /// </summary>
        public void AdvanceTimeBlock()
        {
            if (CurrentPhase != GamePhase.Investigation)
                throw new InvalidOperationException("조사 단계에서만 시간 구간을 진행할 수 있습니다.");

            MoveToNextBlock();
        }

        public void EnterMeeting()
        {
            if (CurrentPhase != GamePhase.Investigation)
                throw new InvalidOperationException("조사 단계에서만 회의로 전환할 수 있습니다.");

            CurrentPhase = GamePhase.Meeting;
        }

        /// <summary>
        /// 결론 없이 회의를 마치고 조사로 돌아간다(06: 회의 뒤 추가 조사 → 다음 회의 첫머리에 재검증).
        /// 회의는 구간이 끝난 뒤 열리므로 다음 조사 구간에서 다시 시작한다(해석 — 복귀 시점은 원문에 없음).
        /// </summary>
        public void ReturnToInvestigation()
        {
            if (CurrentPhase != GamePhase.Meeting)
                throw new InvalidOperationException("회의 단계에서만 조사로 돌아갈 수 있습니다.");

            CurrentPhase = GamePhase.Investigation;
            MoveToNextBlock();
        }

        /// <summary>회의에서 결론이 나 의뢰를 종료한다.</summary>
        public void ResolveContract()
        {
            if (CurrentPhase != GamePhase.Meeting)
                throw new InvalidOperationException("회의 단계에서만 의뢰를 종료할 수 있습니다.");

            CurrentPhase = GamePhase.Resolved;
        }

        /// <summary>
        /// 회의 결론 없이 의뢰를 끝낸다 — 담당 경찰 사망(미제 종료)이나 의뢰 포기. 조사·회의 단계 모두에서 허용한다.
        /// 포기를 회의 중에 할 수 있는지는 원문에 없으므로 호출하는 쪽이 판단한다.
        /// </summary>
        public void EndContractEarly()
        {
            if (CurrentPhase != GamePhase.Investigation && CurrentPhase != GamePhase.Meeting)
                throw new InvalidOperationException("진행 중인 의뢰가 없습니다.");

            CurrentPhase = GamePhase.Resolved;
        }

        public void ResetToIdle()
        {
            CurrentPhase = GamePhase.Idle;
            CurrentContractId = null;
            CurrentTimeBlock = TimeBlock.Morning;
            CurrentDay = 1;
            RemainingTimeSlots = TimeSlotsPerBlock;
            IsTimeBlockEnded = false;
        }

        private void MoveToNextBlock()
        {
            if (CurrentTimeBlock == TimeBlock.Morning)
            {
                CurrentTimeBlock = TimeBlock.Evening;
            }
            else
            {
                CurrentTimeBlock = TimeBlock.Morning;
                CurrentDay++;
            }

            RemainingTimeSlots = TimeSlotsPerBlock;
            IsTimeBlockEnded = false;
            TimeBlockAdvanced?.Invoke(CurrentTimeBlock, CurrentDay);
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

        /// <summary>저장/불러오기 전용. 남은 조사 시간 칸과 구간 종료 여부를 되살린다.</summary>
        public void RestoreTimeBudget(int remainingSlots, bool blockEnded)
        {
            RemainingTimeSlots = Math.Max(0, Math.Min(TimeSlotsPerBlock, remainingSlots));
            IsTimeBlockEnded = blockEnded || RemainingTimeSlots == 0;
        }
    }
}
