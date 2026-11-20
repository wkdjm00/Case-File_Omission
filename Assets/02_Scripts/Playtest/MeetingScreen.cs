using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Core;
using CaseFileOmission.Evidence;
using CaseFileOmission.Game;
using CaseFileOmission.Meeting;
using CaseFileOmission.Police;
using UnityEngine;
using UnityEngine.UI;

namespace CaseFileOmission.Playtest
{
    /// <summary>
    /// 블러핑 카드게임 회의(09·06). 3D 현장과 다른 조작 화면이다.
    /// 1) 회의 전: 보고서의 본인 획득 카드 중 손패 5장을 직접 고른다(나머지는 대체 카드 묶음).
    /// 2) 회의: 카드 제시(그대로/뒤집기=왜곡/끌어오기=은폐), 의문 대응·포기·추가 검증·보류, 차례 넘기기, 결론 제안, 결론 없이 마치기.
    /// 판정은 전부 GameSession이 한다. "근거가 있는지"와 "대응 성공"은 원문에 판정 기준이 없어
    /// 같은 사건 시점·장소를 다루는 다른 사실 자료를 보유했는지로 정한다(TEST_TEMP).
    /// </summary>
    public sealed class MeetingScreen
    {
        private readonly PlaytestGame _g;
        private readonly HashSet<string> _chosen = new HashSet<string>();
        private string _selectedCard;
        private CardManipulation _manipulation = CardManipulation.None;
        private bool _lying;
        private string _accused;
        private string _message = "";

        public MeetingScreen(PlaytestGame g) => _g = g;

        private GameSession S => _g.Session;

        public void Open()
        {
            _chosen.Clear();
            _selectedCard = null;
            _message = "";
            Rebuild();
        }

        private void Rebuild()
        {
            Ui.Clear(_g.WindowLayer);
            if (S.State.CurrentPhase == GamePhase.Meeting && S.MeetingFlow != null) BuildTable();
            else if (S.State.CurrentPhase == GamePhase.Investigation) BuildHandSelection();
        }

        // ───────── 손패 선택 ─────────

        private void BuildHandSelection()
        {
            var s = S;
            var body = Ui.Window(_g.WindowLayer, "회의 준비 — 손패 고르기", () => { Ui.Clear(_g.WindowLayer); _g.GoBoard(); }, out _, 0.15f, 0.1f, 0.85f, 0.9f);
            var held = s.Clues.GetHeldByPlayerClueIds(s.CurrentContract.ContractId);
            int required = Mathf.Min(CardPresentationLimits.PlayerNormalTurnLimit, held.Count);

            var content = Ui.Scroll(Ui.Anchor(Ui.Rect(body, "List"), 0, 0.12f, 1, 1));
            Ui.Line(content, $"보고서에 기록된 본인 획득 카드 중 {required}장을 고르세요. 고르지 않은 카드는 대체 카드 묶음이 됩니다(은폐할 때만 보충).", 19, Ui.DimTextColor);
            if (held.Count == 0) Ui.Line(content, "보유한 카드가 없습니다. 증거 없이도 회의는 열 수 있습니다.", 19);
            foreach (var id in held)
            {
                string cid = id;
                Ui.Toggle(content, $"{PlaytestTexts.MaterialTitle(s, cid)} — {PlaytestTexts.MaterialBody(s, cid)}", _chosen.Contains(cid), on =>
                {
                    if (on) _chosen.Add(cid); else _chosen.Remove(cid);
                    Rebuild();
                });
            }

            var bottom = Ui.Anchor(Ui.Rect(body, "Bottom"), 0, 0, 1, 0.1f);
            Ui.Horizontal(bottom, 10, 4);
            Ui.Label(bottom, $"선택 {_chosen.Count}/{required}  {_message}", 20).gameObject.AddComponent<LayoutElement>().flexibleWidth = 2;
            Ui.Button(bottom, "회의 시작", () =>
            {
                var result = s.StartMeeting(_chosen.ToList());
                if (!result.Success) { _message = PlaytestTexts.Failure(result.Failure); Rebuild(); return; }
                _g.Log($"회의 시작 — 손패 {s.MeetingFlow.PlayerHand.Count}장, 대체 묶음 {s.MeetingFlow.PlayerReserve.Count}장");
                _accused = s.Report?.GetSection(Reports.ReportSection.Perpetrator).ClaimedNpcId;
                _message = "";
                Rebuild();
            }, _chosen.Count == required, 52, Ui.AccentColor);
        }

        // ───────── 회의 테이블 ─────────

        private void BuildTable()
        {
            var s = S;
            var flow = s.MeetingFlow;
            var body = Ui.Window(_g.WindowLayer, "회의 — 블러핑 카드게임", null, out _, 0.01f, 0.01f, 0.99f, 0.95f);

            // 위: 참가자·차례
            var top = Ui.Anchor(Ui.Panel(body, "Participants", Ui.SubPanelColor), 0, 0.86f, 1, 1);
            Ui.Horizontal(top, 10, 8);
            foreach (var id in flow.ParticipantIds)
            {
                bool turn = id == flow.CurrentTurnId;
                // 09: 의문을 제기한 참가자는 ?를 독립적으로 표시한다(해결·포기되면 사라진다).
                bool questioning = s.MeetingResolver.Questions.Any(q => !q.IsClosed && q.JudgeId == id);
                string name = (id == s.PlayerId ? "나" : PoliceRoster.AllPoliceIds.Contains(id) ? PlaytestTexts.PoliceName(id) + "(담당)" : PlaytestTexts.TempName(s, id))
                              + (questioning ? "  ?" : "");
                var p = Ui.Panel(top, "P", turn ? Ui.AccentColor : Ui.ButtonColor);
                var t = Ui.Label(p, (turn ? "▶ " : "") + name, 19, Ui.TextColor, TextAnchor.MiddleCenter);
                Ui.Stretch(t.rectTransform);
            }

            // 가운데 왼쪽: 지난 제시 결과·의문
            var log = Ui.Scroll(Ui.Anchor(Ui.Rect(body, "Resolution"), 0, 0.30f, 0.5f, 0.85f));
            BuildResolution(log);

            // 가운데 오른쪽: 결론
            var conclusion = Ui.Scroll(Ui.Anchor(Ui.Rect(body, "Conclusion"), 0.505f, 0.30f, 1, 0.85f));
            BuildConclusion(conclusion);

            // 아래: 손패와 제시 조작
            var hand = Ui.Anchor(Ui.Panel(body, "Hand", new Color(0.08f, 0.16f, 0.1f)), 0, 0, 1, 0.29f);
            BuildHand(hand);
        }

        private void BuildResolution(RectTransform content)
        {
            var s = S;
            Ui.Line(content, _message, 19, new Color(1f, 0.85f, 0.5f));
            var r = s.LastCardResolution;
            if (r != null)
            {
                Ui.Line(content, "지난 제시에 대한 반응", 20);
                foreach (var j in r.Judgements.Where(j => j.Participated))
                    Ui.Line(content, $"· {JudgeName(j.JudgeId)}: {(j.Accepts ? "받아들임" : "받아들이지 않음")}{(j.RaisesQuestion ? " · 의문 제기" : "")}{(j.LieDetected ? " · 거짓을 눈치챔" : "")}", 18);
            }

            var resolver = s.MeetingResolver;
            var pendingCarried = resolver.PendingCarriedOverQuestions;
            var open = resolver.Questions.Where(q => !q.IsClosed).ToList();
            Ui.Line(content, open.Count > 0 ? "열린 의문" : "열린 의문 없음", 20);
            if (pendingCarried.Count > 0) Ui.Line(content, "이전 회의에서 넘어온 의문을 먼저 처리해야 카드를 낼 수 있습니다.", 17, new Color(1f, 0.6f, 0.5f));
            foreach (var q in open)
            {
                string qid = q.QuestionId;
                bool support = HasSupport(q.ClueId);
                Ui.Line(content, $"? {JudgeName(q.JudgeId)} ({StateName(q.State)}, 추가 검증 {q.AdditionalVerificationsUsed}/{MeetingCardResolver.MaxAdditionalVerifications})", 18, new Color(1f, 0.85f, 0.5f));
                Ui.Line(content, QuestionText(q.ClueId), 17);
                var row = Ui.Row(content, 36);
                Ui.Button(row, support ? "근거로 대응" : "대응(근거 없음)", () => Do(s.RespondToQuestion(qid, support)), true, 36, null, 15);
                Ui.Button(row, "포기", () => Do(s.GiveUpQuestion(qid)), true, 36, null, 15);
                Ui.Button(row, "추가 검증", () => Do(s.VerifyQuestion(qid, support)), q.AdditionalVerificationsUsed < MeetingCardResolver.MaxAdditionalVerifications, 36, null, 15);
                Ui.Button(row, "보류", () => Do(s.DeferQuestion(qid)), q.State != CardQuestionState.Deferred, 36, null, 15);
            }
        }

        private void BuildConclusion(RectTransform content)
        {
            var s = S;
            Ui.Line(content, "결론 제안 — 가해자로 지목할 인물", 20);
            var report = s.Report;
            Ui.Line(content, s.Campaign.CulpritIdentified ? "의뢰 보고서의 인물 정보 문단 완료" : "보고서로 가해자를 특정하지 않았어도 지목은 여기서 고를 수 있음", 16, Ui.DimTextColor);
            Ui.Line(content, "각 참가자는 지목 인물에 대한 개인 의견 점수가 50을 넘을 때만 찬성한다. 카드를 받아들인 참가자만 의견이 바뀐다.", 15, Ui.DimTextColor);
            foreach (var id in PlaytestTexts.AllContractNpcIds(s))
            {
                string nid = id;
                var npc = s.Npcs.Get(nid);
                Ui.Button(content, $"{PlaytestTexts.TempName(s, nid)} · {PlaytestTexts.PublicRole(s, npc)}{(npc.IsAlive ? "" : " (사망 — 지목하면 '자살' 결론 주장)")}", () => { _accused = nid; Rebuild(); },
                    true, 40, nid == _accused ? Ui.AccentColor : (Color?)null, 17);
            }
            var last = s.MeetingFlow.LastConclusion;
            if (last != null)
                Ui.Line(content, $"지난 결론 판정: 찬성 {last.AcceptCount} · 반대 {last.RejectCount} · 추가 검증 {last.VerificationsUsed}/2 → {OutcomeName(last.Outcome)}", 17);
            Ui.Button(content, "결론 제안 (참가자 과반 찬성 시 확정)", ProposeConclusion, _accused != null, 48, Ui.AccentColor);
            Ui.Button(content, "결론 없이 회의 마치기 → 다음 조사 시간대", () => Do(s.LeaveMeeting()), true, 44);
        }

        private void BuildHand(RectTransform hand)
        {
            var s = S;
            var flow = s.MeetingFlow;
            bool myTurn = flow.CurrentTurnId == s.PlayerId;

            var cards = Ui.Anchor(Ui.Rect(hand, "Cards"), 0, 0, 0.62f, 1);
            Ui.Horizontal(cards, 8, 10);
            if (flow.PlayerHand.Count == 0)
                Ui.Label(cards, "손패가 비었습니다. 회의는 자동으로 끝나지 않습니다 — 결론을 제안하거나 회의를 마치세요.", 18, Ui.DimTextColor);
            foreach (var id in flow.PlayerHand)
            {
                string cid = id;
                var btn = Ui.Button(cards, $"{PlaytestTexts.MaterialTitle(s, cid)}\n\n{Shorten(PlaytestTexts.MaterialBody(s, cid), 60)}", () => { _selectedCard = cid; Rebuild(); },
                    true, 220, cid == _selectedCard ? Ui.AccentColor : new Color(0.85f, 0.82f, 0.7f), 15);
                btn.GetComponentInChildren<Text>().color = cid == _selectedCard ? Ui.TextColor : new Color(0.1f, 0.08f, 0.05f);
            }

            var controls = Ui.Anchor(Ui.Rect(hand, "Controls"), 0.63f, 0, 1, 1);
            Ui.Vertical(controls, 6, 10);
            Ui.Line(controls, $"대체 카드 묶음 {flow.PlayerReserve.Count}장 · 이번 차례 제시 {flow.PresentedThisTurn(s.PlayerId)}/{MeetingFlow.GetTurnLimit(MeetingParticipantKind.Player)}", 17, Ui.DimTextColor);
            var row = Ui.Row(controls, 38);
            Ui.Button(row, "그대로", () => { _manipulation = CardManipulation.None; Rebuild(); }, true, 38, _manipulation == CardManipulation.None ? Ui.AccentColor : (Color?)null, 16);
            Ui.Button(row, "뒤집기(왜곡)", () => { _manipulation = CardManipulation.Distort; Rebuild(); }, true, 38, _manipulation == CardManipulation.Distort ? Ui.AccentColor : (Color?)null, 16);
            Ui.Button(row, "끌어오기(은폐)", () => { _manipulation = CardManipulation.Conceal; Rebuild(); }, flow.PlayerReserve.Count > 0, 38, _manipulation == CardManipulation.Conceal ? Ui.AccentColor : (Color?)null, 16);
            Ui.Toggle(controls, "거짓으로 바꾸어 말한다", _lying, v => _lying = v);
            Ui.Button(controls, "카드 제시", Present, myTurn && _selectedCard != null, 46, Ui.AccentColor);
            Ui.Button(controls, flow.PresentedThisTurn(s.PlayerId) == 0 ? "차례 넘기기 (카드 없이 — 의심 상승)" : "차례 끝내기", () =>
            {
                var result = s.EndPlayerTurn();
                Do(result);
                if (result.Success && S.State.CurrentPhase == GamePhase.Meeting) _g.ShowDialogue(TurnStatements());
            }, myTurn, 42);
        }

        private void Present()
        {
            var s = S;
            string card = _selectedCard;
            var result = s.PresentCard(card, _manipulation, _lying, HasSupport(card));
            if (result.Success)
            {
                _g.Log($"카드 제시: {PlaytestTexts.MaterialTitle(s, card)} ({_manipulation}{(_lying ? ", 거짓" : "")})");
                _selectedCard = null;
                _manipulation = CardManipulation.None;
                _lying = false;
            }
            Do(result);
            if (result.Success && S.State.CurrentPhase == GamePhase.Meeting) _g.ShowDialogue(ReactionLines(card));
        }

        // ───────── 회의 대사(09 2026-11-17: 발언·주장 !, 의문 ?) ─────────
        // 대사 문장은 원문에 없어 판정 결과·자료 내용에서 만든 TEST_TEMP 문장이다. 판정·상태는 바꾸지 않고 보여 주기만 한다.

        /// <summary>카드 제시 직후 참가자별 반응을 한 사람씩 대사로 보인다. 의문을 제기한 사람은 무엇이 의심스러운지 말한다.</summary>
        private List<(string, string)> ReactionLines(string clueId)
        {
            var s = S;
            var pages = new List<(string, string)>();
            var r = s.LastCardResolution;
            if (r == null) return pages;
            foreach (var j in r.Judgements.Where(j => j.Participated))
            {
                string mark = j.RaisesQuestion || j.LieDetected ? "  ?" : "  !";
                string line;
                if (j.LieDetected) line = $"\"지금 그 말, 제가 알고 있는 것과 다르군요. '{Title(clueId)}'의 내용을 바꿔 말하고 있지 않습니까?\"";
                else if (j.RaisesQuestion) line = QuestionText(clueId);
                else if (j.Accepts) line = $"\"'{Title(clueId)}'라… 그렇다면 납득이 갑니다.\"";
                else line = $"\"'{Title(clueId)}'만으로는 받아들일 수 없습니다.\"";
                pages.Add((JudgeName(j.JudgeId) + mark, line));
            }
            return pages;
        }

        /// <summary>의문의 내용: 제시된 자료가 말하는 시각·장소를 짚어 그 근거를 묻는다.</summary>
        private string QuestionText(string clueId)
        {
            var m = PlaytestTexts.FindMaterial(S, clueId);
            string about = m != null && m.AboutRoomId != null
                ? $"{PlaytestTexts.Clock(m.AboutMinute)}에 {PlaytestTexts.Room(m.AboutRoomId)}이라는 부분"
                : "그 내용";
            return $"\"'{Title(clueId)}' — {about}이 정말 맞습니까? 그걸 뒷받침할 다른 자료가 있나요?\"";
        }

        /// <summary>
        /// 차례를 넘긴 뒤 다른 참가자들이 자기 차례에 하는 발언(!). NPC·경찰 차례의 카드 선택 규칙이 원문에 없어 카드는 내지 않고,
        /// 07 「개인 의견 점수 구간별 행동·대사 표현(1-1)」에 따라 가장 의심하는 인물에 대한 태도만 말한다.
        /// </summary>
        private List<(string, string)> TurnStatements()
        {
            var s = S;
            var pages = new List<(string, string)>();
            var suspects = PlaytestTexts.AllContractNpcIds(s);
            // 사용자 확정 2026-11-20: 다른 참가자도 자기 차례에 카드를 낸다(가장 의심하는 인물에 관해 아는 정보 1장). 낸 카드는 그대로 보여 준다.
            var plays = s.LastParticipantPlays.Where(p => p.MaterialId != null).ToList();
            foreach (var p in plays)
            {
                string target = p.TargetId == s.PlayerId ? "탐정" : PlaytestTexts.TempName(s, p.TargetId);
                pages.Add((JudgeName(p.PresenterId) + "  !",
                    $"\"{target}에 관해 제가 아는 게 있습니다. '{Title(p.MaterialId)}' — {PlaytestTexts.MaterialBody(s, p.MaterialId)}\""
                    + (p.AcceptedBy.Count > 0 ? $"\n(받아들인 사람: {string.Join(", ", p.AcceptedBy.Select(JudgeName))})" : "\n(아무도 받아들이지 않았다)")));
            }
            foreach (var id in s.MeetingFlow.ParticipantIds.Where(p => p != s.PlayerId && plays.All(x => x.PresenterId != p)))
            {
                var target = suspects.Where(x => x != id).OrderByDescending(x => s.Opinions.Get(id, x)).FirstOrDefault();
                if (target == null) continue;
                float score = s.Opinions.Get(id, target);
                string name = PlaytestTexts.TempName(s, target);
                string line = score switch
                {
                    < 20 => $"\"{name} 씨의 설명은 대체로 믿을 만해 보입니다.\"",
                    < 40 => $"\"{name} 씨 말에 조금 걸리는 데가 있긴 합니다만…\"",
                    < 60 => $"\"아직 판단하기 이릅니다. {name} 씨에 대해서는 더 확인이 필요합니다.\"",
                    < 80 => $"\"{name} 씨의 설명에는 모순이 있어요. 그때 정확히 어디 있었습니까?\"",
                    _ => $"\"{name} 씨, 당신이 한 일 아닙니까?\""
                };
                pages.Add((JudgeName(id) + "  !", line));
            }
            return pages;
        }

        private string Title(string clueId)
        {
            var m = PlaytestTexts.FindMaterial(S, clueId);
            return m?.Title ?? PlaytestTexts.MaterialTitle(S, clueId);
        }

        private void ProposeConclusion()
        {
            var s = S;
            var result = s.ProposeConclusion(_accused);
            _message = $"결론 판정: 찬성 {result.AcceptCount} · 반대 {result.RejectCount} → {OutcomeName(result.Outcome)}";
            _g.Log(_message);
            if (s.State.CurrentPhase != GamePhase.Meeting) { Ui.Clear(_g.WindowLayer); _g.AfterStateChange(); return; }
            Rebuild();
        }

        private void Do(GameActionResult result)
        {
            _message = result.Success ? "" : PlaytestTexts.Failure(result.Failure);
            if (S.State.CurrentPhase != GamePhase.Meeting || S.IsGameOver)
            {
                Ui.Clear(_g.WindowLayer);
                _g.AfterStateChange();
                return;
            }
            _g.RefreshHud();
            Rebuild();
        }

        /// <summary>TEST_TEMP: 같은 사건 시점·장소를 다루는 다른 사실 자료를 보고서에 보유하고 있으면 근거가 있다고 본다.</summary>
        private bool HasSupport(string clueId)
        {
            var s = S;
            var m = PlaytestTexts.FindMaterial(s, clueId);
            if (m == null) return false;
            return s.Clues.GetAllClueIds(s.CurrentContract.ContractId).Any(other =>
            {
                if (other == clueId) return false;
                var o = PlaytestTexts.FindMaterial(s, other);
                return o != null && !o.IsFalseOrFabricated && o.AboutMinute == m.AboutMinute && o.AboutRoomId == m.AboutRoomId;
            });
        }

        private string JudgeName(string id) => PoliceRoster.AllPoliceIds.Contains(id) ? PlaytestTexts.PoliceName(id) : PlaytestTexts.TempName(S, id);

        private static string Shorten(string s, int n) => s.Length > n ? s.Substring(0, n) + "…" : s;

        private static string OutcomeName(ConclusionOutcome o) => o switch
        {
            ConclusionOutcome.Adopted => "채택(공식 결론 확정)",
            ConclusionOutcome.NeedsVerification => "과반 미달 — 추가 검증 필요",
            ConclusionOutcome.Unsolved => "미제",
            _ => o.ToString()
        };

        private static string StateName(CardQuestionState st) => st switch
        {
            CardQuestionState.Open => "열림",
            CardQuestionState.Deferred => "보류",
            _ => st.ToString()
        };
    }
}
