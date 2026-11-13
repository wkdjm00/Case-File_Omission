using System;
using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Contracts;
using CaseFileOmission.NPC;
using CaseFileOmission.Reports;
using CaseFileOmission.Rooms;

namespace CaseFileOmission.ContractContent
{
    /// <summary>
    /// 04 「무작위」 2026-11-12 보고서 목표 5분류(사건의 실체·사건 경위·인물 정보·범행 정보·은폐 정보) 중 이 사건에 해당하는 것만 만든다.
    /// 목표는 의뢰 데이터(진실·자료)에서 결정적으로 다시 만들 수 있어 저장하지 않는다. 문단 본문은 생성 시점에 잠긴 템플릿 문장이다(TEST_TEMP).
    /// 첫 문단 본문은 경찰 인계 정보만 쓰고 실제 진실을 답처럼 노출하지 않는다.
    /// </summary>
    public static class ReportTargetBuilder
    {
        public const string CulpritKey = "culprit";

        public static List<ReportTarget> Build(ContractInstance c, NpcRegistry npcs, IReadOnlyList<EvidenceMaterial> materials, string sceneRoomId)
        {
            var targets = new List<ReportTarget>();
            string Find(string kind) => materials.FirstOrDefault(m => m.MaterialId.Contains(kind))?.MaterialId;
            var presence = materials.Where(m => m.MaterialId.Contains("presence-testimony")).Select(m => m.MaterialId).ToList();
            string scene = Find("scene-trace");
            string body = materials.FirstOrDefault(m => m.MaterialId == ContractPipeline.BodyMaterialId(c.ContractId))?.MaterialId;
            string witness = Find("witness-testimony");
            string alibi = Find("false-alibi");
            string tool = Find("tool-evidence");
            string motive = Find("motive-document");
            string victimDoc = Find("victim-document");
            string cover = Find("coverup-trace");
            var relation = materials.Where(m => m.MaterialId.Contains("relation-testimony")).Select(m => m.MaterialId).ToList();

            string victimName = npcs.Exists(c.Truth.VictimId) ? npcs.Get(c.Truth.VictimId).DisplayName : null;
            string perpName = c.Truth.PerpetratorId != null && npcs.Exists(c.Truth.PerpetratorId) ? npcs.Get(c.Truth.PerpetratorId).DisplayName : null;
            string rep = CaseCatalog.Name(CaseCatalog.RepresentativeSubtypes[c.PoliceCrimeType], c.Truth.RepresentativeSubtypeId);

            // 1. 사건의 실체
            var t1 = Add(targets, "사건의 실체", "실제로 어떤 사건(실제 사건 유형)이 일어났고, 피해자는 누구인가?",
                $"경찰 인계 기록: {c.IncidentDate}, 주택에서 '{rep}' 건으로 접수되었다. 분류 근거: {c.PoliceClassificationBasis} 경찰은 아직 실제 경위와 관계자의 역할을 확정하지 못했다. 경찰 분류와 실제로 일어난 일이 같은지 확인해야 한다.");
            t1.Concepts.Add(Concept("사건 유형", c.Truth.VictimDeparted ? CrimeWords(c.PrimaryCrimeType).Concat(new[] { "떠났", "나갔", "스스로", "숨었" }) : CrimeWords(c.PrimaryCrimeType)));
            if (victimName != null) t1.Concepts.Add(Concept("피해자", NameWords(victimName)));
            string leaveTrace = Find("departure-trace");
            string leaveWitness = Find("departure-testimony");
            AddGroups(t1.EvidenceGroups, new[] { scene }, new[] { body }, new[] { victimDoc }, new[] { witness }, new[] { leaveTrace, leaveWitness }, new[] { leaveWitness });
            t1.FollowUpHint = $"사건 현장({Room(sceneRoomId)})의 흔적과 관계자의 진술로 시각·장소를 좁혀라.";

            // 2. 사건 경위
            var core = scene != null ? materials.First(m => m.MaterialId == scene) : null;
            var t2 = Add(targets, "사건 경위", "사건은 언제, 어디서 일어났는가?",
                "확보한 자료로 사건이 벌어진 시각과 장소를 정리할 수 있다. 그 시각 누가 어디에 있었는지도 함께 따져야 한다.");
            t2.Concepts.Add(Concept("장소", RoomWords(sceneRoomId)));
            if (core != null) t2.Concepts.Add(Concept("시각", HourWords(c.IncidentClockMinute + core.AboutMinute)));
            AddGroups(t2.EvidenceGroups, new[] { scene, witness }, new[] { scene }.Concat(presence.Take(1)).ToArray(), new[] { body, witness });
            AddGroups(t2.UnlockGroups, new[] { scene });
            t2.FollowUpHint = "그 시각 현장 가까이에 있던 사람과 알리바이가 맞지 않는 사람을 찾아라.";

            // 3. 인물 정보(가해자)
            if (perpName != null)
            {
                var t3 = Add(targets, "인물 정보", "누가 저질렀으며, 피해자와 어떤 관계인가?",
                    "관계자들의 진술이 서로 어긋나는 부분이 있다. 누구의 말이 사실과 맞지 않는지, 피해자와 누가 얽혀 있었는지 확인해야 한다.");
                t3.Key = CulpritKey;
                t3.Concepts.Add(Concept("가해자", NameWords(perpName)));
                AddGroups(t3.EvidenceGroups, new[] { witness }, new[] { tool, motive }, new[] { alibi, witness }, new[] { motive, victimDoc });
                AddGroups(t3.UnlockGroups, new[] { witness }, new[] { motive }, new[] { victimDoc });
                t3.FollowUpHint = $"{perpName}이(가) 무엇으로, 왜 그랬는지 뒷받침할 물건과 기록을 찾아라.";
            }

            // 4. 범행 정보(방법·도구·동기)
            if (perpName != null)
            {
                var t4 = Add(targets, "범행 정보", "어떤 방법(도구)으로, 왜 저질렀는가?",
                    "범행 방법과 동기를 설명할 물건·기록이 있다. 동기는 피해자와 가해자의 관계에서 드러난다.");
                t4.Concepts.Add(Concept("방법", MethodWords(c.Truth.MethodId)));
                t4.Concepts.Add(Concept("동기", MotiveWords(c.Truth.MotiveId)));
                var methodEvidence = new[] { tool, body, witness }.Where(x => x != null).ToList();
                var motiveEvidence = new[] { motive }.Concat(relation).Where(x => x != null).ToList();
                foreach (var me in methodEvidence)
                foreach (var mo in motiveEvidence)
                    t4.EvidenceGroups.Add(new List<string> { me, mo });
                AddGroups(t4.UnlockGroups, new[] { tool }, new[] { motive });
                t4.FollowUpHint = c.Truth.CoverUp != CoverUpMethod.None ? "외부에 알려진 경위와 실제가 다른 흔적을 찾아라." : "보고서 목표를 모두 정리했다. 회의에서 결론을 제안하라.";
            }

            // 부수 범죄(04: 부수 범죄가 생성된 의뢰에서만 요구) — 사건 경위·범행 정보에 속하는 별도 문단.
            string subTrace = Find("subcrime-trace");
            if (c.SubCrimeLinks.Count > 0 && subTrace != null && perpName != null)
            {
                var link = c.SubCrimeLinks[0];
                var ts = Add(targets, "부수 범죄", "핵심 사건 직후 가해자가 저지른 다른 범죄는 무엇인가?",
                    "핵심 사건과 이어진 또 다른 행위의 흔적이 있다. 사건 직후 누가 무엇을 했는지 확인해야 한다.");
                ts.Concepts.Add(Concept("부수 범죄 유형", CrimeWords(link.Type)));
                ts.Concepts.Add(Concept("행위자", NameWords(perpName)));
                AddGroups(ts.EvidenceGroups, new[] { subTrace });
                AddGroups(ts.UnlockGroups, new[] { subTrace });
                ts.FollowUpHint = "부수 범죄도 보고서에 정리했다.";
            }

            // 5. 은폐 정보
            if (c.Truth.CoverUp != CoverUpMethod.None)
            {
                var t5 = Add(targets, "은폐 정보", "외부에 알려진 내용과 실제는 어떻게 다르며, 무엇이 조작되었는가?",
                    $"외부에는 '{c.ExternalState.InitialExplanation}'로 알려져 있다. 실제와 다른 점이 있는지 확인해야 한다.");
                t5.Concepts.Add(Concept("은폐", CoverUpWords(c.Truth.CoverUp)));
                AddGroups(t5.EvidenceGroups, new[] { cover });
                AddGroups(t5.UnlockGroups, new[] { cover });
                t5.FollowUpHint = "보고서 목표를 모두 정리했다. 회의에서 결론을 제안하라.";
            }
            return targets;
        }

        private static ReportTarget Add(List<ReportTarget> list, string category, string question, string common)
        {
            var t = new ReportTarget { Index = list.Count, Category = category, Question = question, CommonText = common };
            list.Add(t);
            return t;
        }

        private static void AddGroups(List<List<string>> groups, params string[][] candidates)
        {
            foreach (var g in candidates)
            {
                if (g.Any(x => x == null)) continue;
                groups.Add(g.Distinct().ToList());
            }
        }

        private static ReportConcept Concept(string label, IEnumerable<string> words)
        {
            var c = new ReportConcept { Label = label };
            c.Synonyms.AddRange(words.Where(w => !string.IsNullOrEmpty(w)).Distinct());
            return c;
        }

        private static string Room(string id) => HouseLayoutFactory.GetDisplayName(id);

        public static IEnumerable<string> NameWords(string fullName)
        {
            yield return fullName;
            var parts = fullName.Split(' ');
            if (parts.Length > 1 && parts[0].Length >= 2) yield return parts[0];
        }

        private static IEnumerable<string> RoomWords(string roomId) => roomId switch
        {
            HouseLayoutFactory.EntranceHall => new[] { "현관" },
            HouseLayoutFactory.PantryUtility => new[] { "저장실", "다용도실" },
            HouseLayoutFactory.CorridorStairs => new[] { "복도", "계단" },
            _ => new[] { HouseLayoutFactory.GetDisplayName(roomId) }
        };

        private static IEnumerable<string> HourWords(int minuteOfDayFromOrigin)
        {
            int h = (minuteOfDayFromOrigin / 60) % 24;
            yield return $"{h}시";
            yield return $"{h:00}:";
            yield return $"{h}:";
            if (h > 12) yield return $"{h - 12}시";
        }

        private static IEnumerable<string> CrimeWords(CrimeType crime) => crime switch
        {
            CrimeType.Murder => new[] { "살해", "살인", "죽였", "죽인", "죽임", "숨졌" },
            CrimeType.Missing => new[] { "실종", "사라졌", "데려갔", "끌고", "감금", "유인" },
            CrimeType.AssaultInjury => new[] { "폭행", "때렸", "상해", "다치게", "공격" },
            CrimeType.TheftRobbery => new[] { "훔쳤", "훔친", "절도", "도난", "가져갔" },
            _ => new[] { "협박", "위협" }
        };

        private static IEnumerable<string> MethodWords(string methodId) => methodId switch
        {
            "METHOD-01" => new[] { "맨손", "주먹", "때렸", "때려" },
            "METHOD-02" => new[] { "칼", "날붙이", "찔" },
            "METHOD-03" => new[] { "둔기", "내리쳐", "내리쳤", "가격" },
            "METHOD-05" => new[] { "목을", "졸", "질식", "끈" },
            "METHOD-06" => new[] { "독" },
            "METHOD-07" => new[] { "추락", "떨어", "밀어" },
            "METHOD-09" => new[] { "유인", "속여", "꾀어" },
            "METHOD-10" => new[] { "강제", "끌고", "감금" },
            "METHOD-11" => new[] { "침입", "몰래" },
            "METHOD-12" => new[] { "열쇠", "권한" },
            "METHOD-13" => new[] { "틈", "기회" },
            "METHOD-14" => new[] { "속여", "속임" },
            "METHOD-15" => new[] { "직접", "대면", "찾아가" },
            "METHOD-16" => new[] { "편지", "서신", "전화" },
            "METHOD-17" => new[] { "시켜", "제3자", "대신" },
            _ => new[] { methodId }
        };

        private static IEnumerable<string> MotiveWords(string motiveId) => motiveId switch
        {
            "MOT-01" => new[] { "돈", "재산", "빚", "금전", "유산" },
            "MOT-02" => new[] { "원한", "복수", "원망" },
            "MOT-03" => new[] { "갈등", "다툼", "관계", "결별" },
            "MOT-04" => new[] { "비밀", "과거", "숨기" },
            "MOT-05" => new[] { "자리", "지위", "권한", "업무" },
            "MOT-06" => new[] { "두려", "궁지", "자기보호" },
            "MOT-07" => new[] { "집착", "욕망" },
            _ => new[] { "개인", "불만" }
        };

        private static IEnumerable<string> CoverUpWords(CoverUpMethod c) => c switch
        {
            CoverUpMethod.SuicideDisguise => new[] { "자살", "유서" },
            CoverUpMethod.AccidentalDeathDisguise => new[] { "사고" },
            CoverUpMethod.DisappearanceDisguise => new[] { "가출", "떠난것처럼", "위장" },
            _ => new[] { "기록", "조작", "고쳐" }
        };
    }
}
