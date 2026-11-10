using System.Collections.Generic;
using System.Linq;

namespace CaseFileOmission.MainCase
{
    public enum ReportSegmentKind
    {
        Text,
        Blank
    }

    public sealed class ReportSegment
    {
        public ReportSegmentKind Kind { get; }

        /// <summary>Text 구간이면 그대로 출력할 문장, Blank 구간이면 정답 시 드러나는 원문 문구.</summary>
        public string Text { get; }

        public int BlankIndex { get; }

        /// <summary>같은 문장에 속한 빈칸 묶음. 타자기는 묶음 단위로 멈추고, 묶음의 빈칸이 모두 채워지면 이어서 출력한다.</summary>
        public int GroupId { get; }

        private ReportSegment(ReportSegmentKind kind, string text, int blankIndex, int groupId)
        {
            Kind = kind;
            Text = text;
            BlankIndex = blankIndex;
            GroupId = groupId;
        }

        public static ReportSegment TextOf(string text) => new ReportSegment(ReportSegmentKind.Text, text, -1, -1);
        public static ReportSegment BlankOf(int index, int groupId, string revealedText) =>
            new ReportSegment(ReportSegmentKind.Blank, revealedText, index, groupId);
    }

    /// <summary>
    /// 노션 "최종 보고서 원문 및 타자기 연출"의 원문을 그대로 옮긴 템플릿이다. 문구를 수정하지 않는다.
    /// 빨간 배경 14곳이 Blank 구간이며, 0번과 1번은 같은 문장("약 한 달 … 1명만 생존")에 있어 같은 묶음이다.
    /// </summary>
    public static class FinalReportTemplate
    {
        public static readonly IReadOnlyList<ReportSegment> Segments = Build();

        public static IReadOnlyList<ReportSegment> Blanks =>
            Segments.Where(s => s.Kind == ReportSegmentKind.Blank).OrderBy(s => s.BlankIndex).ToList();

        private static List<ReportSegment> Build()
        {
            var s = new List<ReportSegment>();
            void T(string text) => s.Add(ReportSegment.TextOf(text));
            void B(int index, int group, string revealed) => s.Add(ReportSegment.BlankOf(index, group, revealed));

            T("비밀 생존 실험 및 관련 권력 구조 조사 최종 보고서\n\n");
            T("문서 분류: 기밀\n조사 대상: 정부 관계 기관 및 비밀 생존 실험 관련자\n작성 목적: 확보된 기록과 증거를 종합하여 실험의 운영 구조, 관련 기관의 개입, 실험이 지속된 배경을 밝힌다.\n\n");

            T("1. 사건 개요\n\n");
            T("확보된 일기, 실험 기록, 관계자 문서 및 기타 증거를 종합한 결과, 정부와 연관된 관계자들이 학생들을 대상으로 비밀 생존 실험을 진행해 왔다는 정황이 확인되었다.\n\n");
            T("해당 실험은 참가자들이 일정 기간 생존 경쟁을 벌이도록 설계되었으며, 약 ");
            B(0, 0, "한 달");
            T(" 동안 진행되어 최종적으로 ");
            B(1, 0, "1명");
            T("만 생존하는 구조였던 것으로 파악된다.\n\n");
            T("참가자들은 자발적으로 실험에 참여한 것이 아니었다. 이들은 ");
            B(2, 1, "강제적으로 참가자를 모집·이송한 주체");
            T("에 의해 실험에 끌려왔으며, 외부와의 정상적인 접촉 및 구조 요청이 제한된 환경에 놓였다.\n\n");

            T("2. 참가자 및 가족에 대한 은폐\n\n");
            T("실험 대상자의 가족에게는 실제 목적이 공개되지 않았다. 일부 가족은 참가자가 ");
            B(3, 2, "정상적인 교육·연수 프로그램");
            T("에 참여하는 것으로 안내받았으며, 실험의 존재와 실제 진행 상황을 알지 못했다.\n\n");
            T("실험 과정에서 사망한 참가자에 대해서는 사망 사실과 경위가 은폐되었으며, 관련 기록이 실제 상황과 다르게 처리된 정황도 확인되었다.\n\n");
            T("특히 확보된 자료에서는 실험이 시작된 시점과 참가자의 사망 처리 기록 사이에 ");
            B(4, 3, "실제 실험 참여 시기와 외부 사망 처리 기록의 불일치");
            T("가 존재한다. 이는 참가자가 실제로 실험에 참여하고 있던 시기에도 외부 기록에서는 이미 사망한 것으로 처리되었을 가능성을 보여 준다.\n\n");
            T("이러한 기록 조작은 실험의 존재를 숨기고, 참가자와 가족이 실험의 실체를 외부에 알리기 어렵게 만드는 역할을 했다.\n\n");

            T("3. 실험의 운영과 이익 구조\n\n");
            T("실험은 단순한 비밀 연구에 그치지 않았다. 확보된 자료에 따르면 실험은 ");
            B(5, 4, "외부의 특정 계층에 제공되는 오락·접대의 성격도 지니고 있었다.");
            T("\n\n");
            T("실험과 관련된 기록물 및 상품의 판매는 ");
            B(6, 5, "경제적 수익");
            T("을 발생시키는 수단으로 이용되었다. 실험을 관람하거나 관련 상품을 구매하는 사람들은 실험의 지속에 경제적으로 기여할 수 있었다.\n\n");
            T("이러한 구조에서 ");
            B(7, 6, "실험 참가자는 보호받아야 할 사람이 아니라, 타인의 오락과 이익을 위해 이용되는 대상으로 취급되었다.");
            T("\n\n");

            T("4. 정부의 개입과 권력 유지\n\n");
            B(8, 7, "정부와 관련된 일부 관계자 및 부서는 실험의 운영과 은폐에 관여한 것으로 보인다.");
            T(" 다만 현재까지 확보된 자료만으로 정부의 모든 구성원이 실험의 존재나 전체 운영 구조를 알고 있었다고 단정할 수는 없다.\n\n");
            T("실험과 관련된 자료에서는 정부가 ");
            B(9, 8, "상류층과의 관계를 통해 지지 및 자금을 확보하려 했다");
            T("는 정황이 확인된다.\n\n");
            T("이는 실험이 특정 관계자들의 개인적인 이익을 넘어, 정부의 권력 유지와 연결되어 있었음을 시사한다.\n\n");
            T("또한 일부 관계자들은 실험의 잔혹한 실체를 인지한 뒤에도 이를 중단하지 않았다. 그 이유는 ");
            B(10, 9, "정치적·경제적 이익");
            T("을 계속 확보할 수 있었기 때문인 것으로 추정된다.\n\n");
            T("따라서 실험이 지속된 배경에는 실험 자체에서 발생하는 경제적 이익뿐 아니라, 상류층과의 관계를 유지하고 정부에 대한 지지와 협조를 확보하려는 목적이 함께 작용했을 가능성이 높다.\n\n");

            T("5. 반란 세력의 존재와 활동\n\n");
            T("확보된 자료에서는 실험의 실체를 알고 ");
            B(11, 10, "기존 체계에 반대하는 반란 세력의 존재");
            T("도 확인된다.\n\n");
            T("이들은 실험과 관련된 정보를 수집하고, 기존 체계에 불만을 가진 인물들을 끌어들이려 했다.\n\n");
            T("그러나 현재 확보된 자료만으로는 이 세력이 실험의 전체 구조와 정부의 권력 유지 방식까지 정확히 파악하고 있었는지, 또는 ");
            B(12, 11, "실험을 종식시킬 구체적인 계획을 갖고 있었는지 확정하기 어렵다");
            T(".\n\n");
            T("따라서 이들의 존재와 활동은 확인하되, 그들의 목적과 실질적인 영향력에 대해서는 추가적인 증거가 필요하다.\n\n");

            T("6. 종합 판단\n\n");
            T("현재까지 확보된 증거를 종합하면, 해당 생존 실험은 참가자의 생존 여부를 관찰하는 행위에 그치지 않았다.\n\n");
            T("실험은 참가자와 가족에게 진실을 숨기는 방식으로 운영되었으며, 관련 상품과 관람을 통한 경제적 이익을 발생시켰다. 동시에 일부 정부 관계자들은 상류층과의 관계를 이용해 지지와 자금을 확보하고 권력을 유지하는 데 실험을 활용한 것으로 판단된다.\n\n");
            T("이러한 구조는 실험을 통해 이익을 얻는 사람들과 실험의 피해를 감당하는 사람들 사이에 뚜렷한 불균형이 존재했음을 보여 준다.\n\n");
            T("다만 확보된 자료만으로는 ");
            B(13, 12, "최종 설계자의 구체적인 신원과 개인적인 동기");
            T(", 그리고 사건 전체를 관통하는 최종 계획의 전모를 확정할 수 없다.\n\n");
            T("따라서 본 보고서는 현재 확인 가능한 운영 구조와 권력 관계를 정리한 결과이며, 미확인 사항에 대해서는 추가 증거 확보와 교차 검증이 필요하다.\n\n");
            T("보고서 작성 종료.");

            return s;
        }
    }
}
