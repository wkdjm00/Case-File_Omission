using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.ContractContent;
using CaseFileOmission.Contracts;
using CaseFileOmission.Evidence;
using CaseFileOmission.Game;
using CaseFileOmission.NPC;
using CaseFileOmission.Police;
using CaseFileOmission.Rooms;

namespace CaseFileOmission.Playtest
{
    /// <summary>
    /// 화면 표시용 문구. 게임 데이터에는 표시 문장이 없어 여기서 데이터로부터 만든다.
    /// 인물 이름·시각 표기·자료 문장은 전부 TEST_TEMP이며 정식 콘텐츠 데이터가 생기면 이 파일만 교체한다.
    /// 내부 ID(예: contract-1-perpetrator)에는 실제 역할이 드러나므로 화면에 절대 쓰지 않고 인물 코드로 바꾼다.
    /// </summary>
    public static class PlaytestTexts
    {
        // TEST_TEMP: 사건 당일 시간표 0분을 저녁 7시로 본다.
        private const int ClockBaseMinute = 19 * 60;

        public static string PersonCode(string npcId) => "P-" + (GameSession.StableSeed("person:" + npcId) % 9000 + 1000);

        /// <summary>화면 이름. 의뢰 생성기가 정한 표시 이름(TEST_TEMP 이름 목록)을 쓴다.</summary>
        public static string TempName(GameSession s, string npcId)
        {
            if (npcId == null) return "누군가";
            if (PoliceRoster.AllPoliceIds.Contains(npcId)) return PoliceName(npcId);
            if (npcId == s.PlayerId) return "나(탐정)";
            return s.Npcs.Exists(npcId) ? s.Npcs.Get(npcId).DisplayName ?? "알 수 없는 인물" : "알 수 없는 인물";
        }

        /// <summary>세계 시각(사건 발생일 00:00 기준 분)을 "1984-12-03 08:15"로.</summary>
        public static string WorldClock(GameSession s, int worldMinute)
        {
            var contract = s.CurrentContract ?? s.LastContract;
            var date = System.DateTime.TryParse(contract?.IncidentDate, out var d) ? d : new System.DateTime(1984, 10, 1);
            return date.AddMinutes(worldMinute).ToString("yyyy-MM-dd HH:mm");
        }

        public static string PoliceName(string policeId) => policeId switch
        {
            PoliceRoster.PoliceA => "경찰 A(수사형)",
            PoliceRoster.PoliceB => "경찰 B",
            PoliceRoster.PoliceC => "경찰 C",
            PoliceRoster.PoliceD => "경찰 D",
            PoliceRoster.PoliceE => "경찰 E",
            _ => policeId
        };

        /// <summary>내부 ID 순서가 역할을 드러내지 않게 인물 코드 순으로 정렬한다.</summary>
        public static List<string> AllContractNpcIds(GameSession s)
        {
            var contractId = s.CurrentContract?.ContractId ?? s.LastContract?.ContractId;
            return s.Npcs.All.Where(n => n.ContractId == contractId).Select(n => n.NpcId)
                .OrderBy(PersonCode).ToList();
        }

        public static string Room(string roomId) => roomId == null ? "알 수 없음" : HouseLayoutFactory.GetDisplayName(roomId);

        public static string Clock(int minute)
        {
            int m = ClockBaseMinute + minute;
            return $"{(m / 60) % 24:00}:{m % 60:00}";
        }

        public static string Crime(CrimeType type) => type switch
        {
            CrimeType.Murder => "살인",
            CrimeType.Missing => "실종",
            CrimeType.AssaultInjury => "폭행·상해",
            CrimeType.TheftRobbery => "절도·도난",
            CrimeType.Blackmail => "협박",
            _ => type.ToString()
        };

        public static string Condition(GameSession s, OrganizationCondition c)
        {
            if (c == null || !c.HasCondition) return "추가 조건 없음 — 의뢰인의 요청대로 사건을 해결하면 된다.";
            string target = string.IsNullOrEmpty(c.TargetId) ? "" : $" (대상: {TargetName(s, c.TargetId)})";
            string text = c.Type switch
            {
                OrganizationConditionType.RecoverBody => "시신을 회수해 조직에 넘길 것",
                OrganizationConditionType.RecoverItem => "특정 물품을 회수해 조직에 넘길 것",
                OrganizationConditionType.DesignateOfficialCulprit => "지정한 인물을 공식 가해자로 만들 것",
                OrganizationConditionType.KeepUnsolved => "사건을 미제로 남길 것",
                _ => c.Type.ToString()
            };
            return text + target;
        }

        private static string TargetName(GameSession s, string id) =>
            s.Npcs.Exists(id) ? TempName(s, id) : "지정 물품";

        public static string Personality(NpcPersonality p) => p switch
        {
            NpcPersonality.Suspicious => "의심이 많은",
            NpcPersonality.Greedy => "욕심이 많은",
            NpcPersonality.Timid => "겁이 많은",
            NpcPersonality.FrequentLiar => "거짓말을 자주 하는",
            NpcPersonality.Secretive => "비밀을 감추는",
            NpcPersonality.Guilty => "죄책감이 있는",
            NpcPersonality.BlindlyTrusting => "남을 맹목적으로 믿는",
            NpcPersonality.Indifferent => "무관심한",
            NpcPersonality.Jealous => "질투하는",
            NpcPersonality.Dependent => "의존하는",
            _ => p.ToString()
        };

        /// <summary>08 얼굴 오브젝트(성격별). 말 머리 자리에 임시 도형+이름으로 표시한다.</summary>
        public static string FaceObject(NpcPersonality p) => p switch
        {
            NpcPersonality.Suspicious => "눈알",
            NpcPersonality.Greedy => "파리지옥",
            NpcPersonality.Timid => "조개껍데기",
            NpcPersonality.FrequentLiar => "입",
            NpcPersonality.Secretive => "자물쇠",
            NpcPersonality.Guilty => "한쪽만 촛농이 흘러내린 양초",
            NpcPersonality.BlindlyTrusting => "자석",
            NpcPersonality.Indifferent => "생선",
            NpcPersonality.Jealous => "깨진 거울",
            NpcPersonality.Dependent => "꼭두각시 조종 손잡이와 줄",
            _ => ""
        };

        /// <summary>08 경찰 얼굴 오브젝트(A 돋보기, B 수첩, C 철제 덫, D 왕관, E 나침반).</summary>
        public static string PoliceFaceObject(string policeId) => policeId switch
        {
            PoliceRoster.PoliceA => "돋보기",
            PoliceRoster.PoliceB => "수첩",
            PoliceRoster.PoliceC => "철제 덫",
            PoliceRoster.PoliceD => "왕관",
            PoliceRoster.PoliceE => "나침반",
            _ => ""
        };

        /// <summary>
        /// 공개 역할(07 public_role, 실제 역할과 분리). 모두 '사건 관련 인물'로 시작하고, 경찰 초기 정보의 피해자·
        /// 공식 기록에 들어간 증언의 목격자·공식 결론의 가해자만 바뀐다.
        /// </summary>
        public static string PublicRole(GameSession s, NpcState npc) => s.GetPublicRole(npc.NpcId) switch
        {
            Game.PublicRole.Perpetrator => "가해자(공식 지목)",
            Game.PublicRole.Victim => "피해자",
            Game.PublicRole.Witness => "목격자",
            _ => "사건 관련 인물"
        };

        public static bool IsPubliclyAccused(GameSession s, string npcId)
        {
            var contract = s.CurrentContract ?? s.LastContract;
            return contract != null && contract.OfficialConclusion.OfficiallyAccusedNpcId == npcId;
        }

        public static string MaterialTitle(GameSession s, string clueId)
        {
            var m = FindMaterial(s, clueId);
            if (m != null && m.Title != null) return $"[{Format(m.Format)}] {m.Title}";
            if (!s.Clues.Exists(clueId)) return clueId;
            var def = s.Clues.GetDefinition(clueId);
            return def.DisplayName ?? clueId;
        }

        public static string MaterialBody(GameSession s, string clueId)
        {
            var m = FindMaterial(s, clueId);
            if (m != null && m.Content != null) return m.Content;
            if (m == null)
            {
                if (!s.Clues.Exists(clueId)) return "(자료 없음)";
                var def = s.Clues.GetDefinition(clueId);
                return def.DisplayedContent ?? def.DisplayName ?? clueId;
            }

            string time = Clock(m.AboutMinute);
            string room = Room(m.AboutRoomId);
            string source = m.SourceNpcId != null ? $"{TempName(s, m.SourceNpcId)} ({PersonCode(m.SourceNpcId)})" : null;
            var contract = s.CurrentContract ?? s.LastContract;

            if (m.MaterialId.Contains("scene-trace"))
                return $"{room}에서 {time}경 생긴 것으로 보이는 흔적. 사건이 이 방에서 일어났음을 보여 준다.";
            if (m.MaterialId.Contains("witness-testimony"))
            {
                string perp = contract != null ? TempName(s, contract.Truth.PerpetratorId) : "누군가";
                string victim = contract != null ? TempName(s, contract.Truth.VictimId) : "피해자";
                return $"{source}: \"{time}쯤 {room}에서 {perp}이(가) {victim}에게 손을 대는 걸 봤어요.\"";
            }
            if (m.MaterialId.Contains("presence-testimony") || m.MaterialId.Contains("false-alibi"))
                return $"{source}: \"{time}쯤이면… 저는 {room}에 있었어요.\"";
            return $"{Format(m.Format)} 자료 — {room}, {time}";
        }

        public static string MaterialSource(GameSession s, string clueId)
        {
            var m = FindMaterial(s, clueId);
            if (m == null) return "조직·경찰 제공";
            return m.Acquisition switch
            {
                MaterialAcquisition.TalkToNpc => $"대화 — {TempName(s, m.SourceNpcId)}",
                MaterialAcquisition.Overheard => $"엿듣기 — {TempName(s, m.SourceNpcId)}",
                _ => $"현장 조사 — {Room(m.RoomId)}{(m.ObjectId != null ? " · " + CaseCatalog.ObjectName(m.ObjectId) : "")}"
            };
        }

        public static string Format(ClueFormat f) => f switch
        {
            ClueFormat.PhysicalObject => "물리 증거",
            ClueFormat.Document => "문서·기록",
            ClueFormat.Testimony => "증언",
            ClueFormat.Recording => "기록",
            _ => f.ToString()
        };

        public static EvidenceMaterial FindMaterial(GameSession s, string clueId) =>
            s.Materials.FirstOrDefault(m => m.MaterialId == clueId);

        public static string Phase(GameSession s) => s.State.CurrentPhase switch
        {
            Core.GamePhase.Idle => "대기(사무실)",
            Core.GamePhase.Investigation => "조사",
            Core.GamePhase.Meeting => "회의",
            Core.GamePhase.Resolved => "종료",
            _ => ""
        };

        public static string TimeBlock(Core.TimeBlock b) => b == Core.TimeBlock.Morning ? "아침" : "저녁";

        public static string Failure(GameActionFailure f) => f switch
        {
            GameActionFailure.None => "",
            GameActionFailure.GameOver => "게임이 끝났습니다.",
            GameActionFailure.WrongPhase => "지금 단계에서는 할 수 없습니다.",
            GameActionFailure.TimeBlockEnded => "이번 조사 시간대가 끝났습니다.",
            GameActionFailure.NotEnoughTime => "남은 조사 시간이 부족합니다.",
            GameActionFailure.TimeBlockNotEnded => "조사 시간대가 아직 끝나지 않았습니다.",
            GameActionFailure.RoomNotConnected => "바로 이어진 방이 아닙니다.",
            GameActionFailure.TargetNotInRoom => "대상이 같은 방에 없습니다.",
            GameActionFailure.TargetDeceased => "대상이 사망했습니다.",
            GameActionFailure.UnknownTarget => "알 수 없는 대상입니다.",
            GameActionFailure.NotPlayersTurn => "내 차례가 아닙니다.",
            GameActionFailure.TurnLimitReached => "이번 차례에 낼 수 있는 카드를 다 냈습니다.",
            GameActionFailure.CardNotInHand => "손패에 없는 카드입니다.",
            GameActionFailure.PendingCarriedOverQuestions => "이전 회의에서 넘어온 의문을 먼저 처리해야 합니다.",
            GameActionFailure.NoAmmo => "탄약이 없습니다.",
            GameActionFailure.NothingToReload => "재장전할 탄약이 없습니다.",
            GameActionFailure.NoOffer => "제안된 의뢰가 없습니다.",
            GameActionFailure.ReportLocked => "보고서를 쓸 수 없습니다.",
            GameActionFailure.InvalidInput => "할 수 없는 선택입니다.",
            _ => f.ToString()
        };
    }
}
