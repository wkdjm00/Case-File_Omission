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
