using System.Collections.Generic;

namespace CaseFileOmission.Game
{
    public enum GameActionFailure
    {
        None,
        GameOver,
        WrongPhase,
        TimeBlockEnded,
        NotEnoughTime,
        TimeBlockNotEnded,
        RoomNotConnected,
        TargetNotInRoom,
        TargetDeceased,
        UnknownTarget,
        NotPlayersTurn,
        TurnLimitReached,
        CardNotInHand,
        PendingCarriedOverQuestions,
        NoAmmo,
        NothingToReload,
        NoOffer,
        ReportLocked,
        InvalidInput
    }

    /// <summary>NPC끼리 같은 방에서 정보를 주고받은 기록(엿듣기 대상). 성공·실패와 관계없이 대화는 일어났다.</summary>
    public sealed class OverheardExchange
    {
        public string SpeakerId, ListenerId, InfoId, RoomId;
        public int Minute;
        public bool Succeeded;
        public bool HeardByPlayer;
    }

    /// <summary>
    /// 07 2026-11-12 공개 역할(public_role). 실제 역할(actual_role)과 분리하며 바뀌어도 실제 역할·진실은 덮어쓰지 않는다.
    /// 모든 NPC는 '사건 관련 인물'로 시작한다.
    /// </summary>
    public enum PublicRole
    {
        CaseRelated,
        Victim,
        Witness,
        Perpetrator
    }

    /// <summary>NPC 관찰 결과: 관찰 시각, 그 인물이 들고 있는 단서, 일정상 곧 갈 곳.</summary>
    public sealed class ObservationRecord
    {
        public string NpcId;
        public int Minute;
        public List<string> HeldClueIds = new List<string>();
        public string ScheduledRoom;
    }

    /// <summary>플레이어 행동 하나의 결과. 실패하면 상태는 바뀌지 않는다.</summary>
    public sealed class GameActionResult
    {
        public bool Success => Failure == GameActionFailure.None;
        public GameActionFailure Failure;

        /// <summary>이번 행동으로 새로 얻은 단서 ID.</summary>
        public readonly List<string> NewClueIds = new List<string>();

        /// <summary>이번 행동 중 담당 경찰이 내용을 확인한 단서 ID(단서 확인 요청 — 거절 불가).</summary>
        public readonly List<string> ConfirmedByPoliceClueIds = new List<string>();

        /// <summary>이번 행동으로 새로 확보한 메인 사건 단서 이름.</summary>
        public readonly List<string> NewMainCaseClues = new List<string>();

        /// <summary>소모한 조사 시간 칸.</summary>
        public int TimeSpent;

        public static GameActionResult Fail(GameActionFailure failure) => new GameActionResult { Failure = failure };
        public static GameActionResult Ok() => new GameActionResult();
    }
}
