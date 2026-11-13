using System;
using System.Collections;
using System.Collections.Generic;
using CaseFileOmission.Props;
using CaseFileOmission.UI;
using UnityEngine;

namespace CaseFileOmission.Playtest
{
    /// <summary>통화 한 단계: 상대 대사, 플레이어 대사, 또는 선택지(고르면 콜백).</summary>
    public sealed class CallStep
    {
        public string Caller;
        public string Text;
        public bool IsPlayer;
        public string[] Choices;
        public Action<int> OnChosen;

        public static CallStep Say(string caller, string text) => new CallStep { Caller = caller, Text = text };
        public static CallStep Me(string text) => new CallStep { IsPlayer = true, Text = text };
        public static CallStep Choose(string prompt, string[] choices, Action<int> onChosen) =>
            new CallStep { Text = prompt, Choices = choices, OnChosen = onChosen };
    }

    /// <summary>
    /// 09 전화기 흐름: 전화기 클릭 → 줌인 → 수화기 들기 → 상대 말풍선·하단 플레이어 대사/선택지 → 클릭 진행(자동 진행 없음)
    /// → 통화 종료 → 수화기 내림 → 줌아웃. 화면은 보유한 PhoneCallUI·TelephoneHandsetAnimator를 그대로 쓰고,
    /// 대사 내용만 게임 상태에서 받는다(선택지를 고르면 단계 목록에 다음 단계를 덧붙일 수 있다).
    /// </summary>
    public sealed class PhoneController : MonoBehaviour
    {
        private TelephoneHandsetAnimator _handset;
        private PhoneCallUI _ui;
        private Camera _camera;
        private readonly List<CallStep> _steps = new List<CallStep>();
        private int _index;
        private Action _onFinished;
        private Vector3 _homePos;
        private Quaternion _homeRot;

        public bool InCall { get; private set; }

        public void Bind(GameObject telephone, Camera camera)
        {
            _camera = camera;
            _handset = telephone.GetComponentInChildren<TelephoneHandsetAnimator>();
            _ui = telephone.GetComponentInChildren<PhoneCallUI>();

            // 프리팹에 들어 있는 테스트용 진행기·클릭 처리는 끈다(게임 흐름이 대신 맡는다).
            foreach (var demo in telephone.GetComponentsInChildren<PhoneCallDemo>()) demo.enabled = false;
            foreach (var interaction in telephone.GetComponentsInChildren<TelephoneInteraction>()) interaction.enabled = false;
            if (_handset != null) _handset.PickedUp += OnPickedUp;
            if (_handset != null) _handset.HungUp += OnHungUp;
        }

        private void OnDestroy()
        {
            if (_handset != null)
            {
                _handset.PickedUp -= OnPickedUp;
                _handset.HungUp -= OnHungUp;
            }
        }

        /// <summary>단계 목록 뒤에 단계를 더한다(선택지 콜백 안에서 다음 대사를 이어 붙일 때).</summary>
        public void Append(params CallStep[] steps) => _steps.AddRange(steps);

        public void StartCall(IEnumerable<CallStep> steps, Action onFinished)
        {
            if (InCall) return;
            InCall = true;
            _steps.Clear();
            _steps.AddRange(steps);
            _index = -1;
            _onFinished = onFinished;
            StartCoroutine(BeginRoutine());
        }

        private IEnumerator BeginRoutine()
        {
            _homePos = _camera.transform.position;
            _homeRot = _camera.transform.rotation;
            var bounds = PhoneBounds();
            var center = bounds.center + Vector3.up * bounds.size.y * 0.35f;
            var dir = (_homePos - center).normalized;
            var targetPos = center + dir * Mathf.Max(0.9f, bounds.size.magnitude * 1.6f);
            yield return MoveCamera(targetPos, Quaternion.LookRotation(center - targetPos, Vector3.up));

            if (_handset != null) _handset.PickUp();
            else OnPickedUp();
        }

        private void OnPickedUp()
        {
            if (InCall && _index < 0) Next();
        }

        private void Next()
        {
            _index++;
            if (_index >= _steps.Count)
            {
                _ui.HideAll();
                if (_handset != null) _handset.HangUp();
                else OnHungUp();
                return;
            }

            var step = _steps[_index];
            if (step.Choices != null)
            {
                _ui.ShowChoices(step.Choices, chosen =>
                {
                    step.OnChosen?.Invoke(chosen);
                    _ui.ShowPlayerLine(step.Choices[chosen], Next);
                }, step.Text);
            }
            else if (step.IsPlayer)
                _ui.ShowPlayerLine(step.Text, Next);
            else
                _ui.ShowCallerLine(step.Text, Next, step.Caller);
        }

        private void OnHungUp()
        {
            if (InCall) StartCoroutine(EndRoutine());
        }

        private IEnumerator EndRoutine()
        {
            yield return MoveCamera(_homePos, _homeRot);
            InCall = false;
            var done = _onFinished;
            _onFinished = null;
            done?.Invoke();
        }

        private IEnumerator MoveCamera(Vector3 pos, Quaternion rot)
        {
            Vector3 fromPos = _camera.transform.position;
            Quaternion fromRot = _camera.transform.rotation;
            const float duration = 0.7f;
            for (float t = 0; t < duration; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0, 1, t / duration);
                _camera.transform.SetPositionAndRotation(Vector3.Lerp(fromPos, pos, k), Quaternion.Slerp(fromRot, rot, k));
                yield return null;
            }
            _camera.transform.SetPositionAndRotation(pos, rot);
        }

        private Bounds PhoneBounds()
        {
            var root = _handset != null ? _handset.gameObject : gameObject;
            var rs = root.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(root.transform.position, Vector3.one * 0.3f);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
