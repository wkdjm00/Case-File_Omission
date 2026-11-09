using System;
using CaseFileOmission.Endings;

namespace CaseFileOmission.MainCase
{
    public enum PhoneCallStage
    {
        NotReached,
        AwaitingAnswer,
        InformationRevealed,
        PlayerKilled
    }

    public enum RebelRecruitmentStage
    {
        NotOffered,
        Offered,
        Accepted,
        Rejected
    }

    /// <summary>
    /// §10-7/§11-4/§11-5 확정 순서: 최종 보고서 완성 → 수화기 질문(설계자 정보) → (수락 시) 반란군
    /// 방문·영입 선택. 수화기 질문 거절과 반란군 영입 거절은 서로 다른 분기이며 하나로 합치지 않는다
    /// (확정 사항). 이 클래스는 상태 전이만 담당하고 장면·대사 콘텐츠는 다루지 않는다.
    /// </summary>
    public sealed class PostReportBranching
    {
        public PhoneCallStage PhoneStage { get; private set; } = PhoneCallStage.NotReached;
        public RebelRecruitmentStage RebelStage { get; private set; } = RebelRecruitmentStage.NotOffered;

        /// <summary>
        /// 저장/불러오기 전용. 전이 규칙을 다시 실행하지 않고 저장된 단계로 되돌린다 — 이미 지나간
        /// 수화기 질문이나 영입 제안이 불러온 뒤 다시 시작되지 않게 한다.
        /// </summary>
        public void RestoreStages(PhoneCallStage phoneStage, RebelRecruitmentStage rebelStage)
        {
            PhoneStage = phoneStage;
            RebelStage = rebelStage;
        }

        /// <summary>최종 보고서가 완성되면 호출한다. 책상 위 수화기가 떨어지고 질문이 시작된다.</summary>
        public void TriggerPhoneCall()
        {
            if (PhoneStage != PhoneCallStage.NotReached)
                throw new InvalidOperationException("수화기 질문은 이미 시작되었습니다.");

            PhoneStage = PhoneCallStage.AwaitingAnswer;
        }

        /// <summary>
        /// 설계자 정보를 들을지 선택한다. 거절하면 조직이 즉시 플레이어를 죽인다(확정, 반란군 영입
        /// 거절과는 별개의 게임 종료 엔딩).
        /// </summary>
        public GameEndingType? AnswerPhoneQuestion(bool wantsInformation)
        {
            if (PhoneStage != PhoneCallStage.AwaitingAnswer)
                throw new InvalidOperationException("지금은 수화기 질문에 답할 수 있는 상태가 아닙니다.");

            if (!wantsInformation)
            {
                PhoneStage = PhoneCallStage.PlayerKilled;
                return GameEndingType.PhoneCallRejected;
            }

            PhoneStage = PhoneCallStage.InformationRevealed;
            RebelStage = RebelRecruitmentStage.Offered;
            return null; // 아직 엔딩이 아니다 — 반란군 방문·영입 선택으로 이어진다.
        }

        /// <summary>반란군의 영입 제안에 응답한다. 수락/거절 모두 메인 서사 엔딩이다(둘 다 반복 진행 가능).</summary>
        public GameEndingType DecideRebelRecruitment(bool accept)
        {
            if (RebelStage != RebelRecruitmentStage.Offered)
                throw new InvalidOperationException("반란군의 영입 제안을 받기 전입니다.");

            RebelStage = accept ? RebelRecruitmentStage.Accepted : RebelRecruitmentStage.Rejected;
            return accept ? GameEndingType.RebelRecruitAccepted : GameEndingType.BossHiddenEnding;
        }
    }
}
