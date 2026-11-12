using System;
using System.Collections.Generic;
using CaseFileOmission.Props;
using UnityEngine;

namespace CaseFileOmission.UI
{
    /// <summary>
    /// 통화 테스트용 진행기. 수화기를 다 들어 올리면(PickedUp) 단계별로 상대 대사·선택지·플레이어 대사를 보여 주고,
    /// 끝나면 UI를 닫고 수화기를 내려놓는다. 나중에는 게임 흐름(확대 → 선택창 → 수화기 들기 → 통화 UI)이 이 역할을 맡는다.
    /// </summary>
    public sealed class PhoneCallDemo : MonoBehaviour
    {
        public enum StepKind { Caller, Player, Choice }

        [Serializable]
        public sealed class Step
        {
            public StepKind kind = StepKind.Caller;
            [TextArea] public string text;
            [Tooltip("Choice일 때 선택지들. 고른 선택지가 플레이어 대사로 나온다.")]
            public string[] choices;
        }

        [SerializeField] private TelephoneHandsetAnimator handset;
        [SerializeField] private PhoneCallUI ui;
        [Tooltip("통화가 끝나면 수화기를 내려놓는다")]
        [SerializeField] private bool hangUpWhenDone = true;
        [SerializeField] private List<Step> steps = new List<Step>
        {
            new Step { kind = StepKind.Caller, text = "들켰어. 지금 당장 부두에서 만나." },
            new Step { kind = StepKind.Choice, choices = new[] { "누구시죠?", "알겠어. 바로 갈게." } },
            new Step { kind = StepKind.Caller, text = "시간 없어. 아무도 믿지 마." },
            new Step { kind = StepKind.Player, text = "...끊겼다." },
        };

        private int _index = -1;

        /// <summary>상대 대사 앞에 붙일 이름. 비우면 PhoneCallUI 기본값("발신자").</summary>
        public string CallerLabel { get; set; }

        /// <summary>선택지를 고를 때마다(단계 번호, 선택 번호)</summary>
        public event Action<int, int> Chosen;

        private void OnEnable()
        {
            if (handset != null) handset.PickedUp += StartCall;
        }

        private void OnDisable()
        {
            if (handset != null) handset.PickedUp -= StartCall;
        }

        public void StartCall()
        {
            _index = -1;
            Next();
        }

        private void Next()
        {
            _index++;
            if (ui == null) return;
            if (_index >= steps.Count)
            {
                ui.HideAll();
                if (hangUpWhenDone && handset != null) handset.HangUp();
                return;
            }

            Step step = steps[_index];
            switch (step.kind)
            {
                case StepKind.Caller:
                    ui.ShowCallerLine(step.text, Next, CallerLabel);
                    break;
                case StepKind.Player:
                    ui.ShowPlayerLine(step.text, Next);
                    break;
                case StepKind.Choice:
                    int stepIndex = _index;
                    ui.ShowChoices(step.choices ?? Array.Empty<string>(), chosen =>
                    {
                        Chosen?.Invoke(stepIndex, chosen);
                        ui.ShowPlayerLine(step.choices[chosen], Next);
                    });
                    break;
            }
        }
    }
}
