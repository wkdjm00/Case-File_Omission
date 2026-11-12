using System;
using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CaseFileOmission.Props
{
    /// <summary>
    /// 전화기 옆에서 사람이 수화기를 드는 것처럼 받침대에서 들어 올렸다가 내려놓는 애니메이션.
    /// 지금은 테스트 키로 확인하고, 나중에 게임 흐름(전화기 확대 → 선택창 → 선택)에서 PickUp()/HangUp()을 부른다.
    /// 전화선(TelephoneCord)은 수화기 앵커를 따라가므로 따로 손댈 필요가 없다.
    /// </summary>
    public sealed class TelephoneHandsetAnimator : MonoBehaviour
    {
        [Tooltip("움직일 수화기(Telephone_Handset_Low)")]
        [SerializeField] private Transform handset;

        [Header("들어 올린 자세(이 오브젝트 로컬 기준, 쉬는 자세에서의 변화)")]
        [Tooltip("이동(m). 기본: 위로 0.34, 앞으로 0.24, 수화부(듣는 쪽, 곧은 끝) 쪽으로 0.06")]
        [SerializeField] private Vector3 liftedOffset = new Vector3(-0.06f, 0.34f, 0.24f);
        [Tooltip("회전(도). 기본: 수화부(듣는 쪽, 곧은 끝)를 위로, 송화구(말하는 쪽, 꺾인 끝)를 아래로 기울이고(Z) 앞쪽으로 살짝 돌림(Y)")]
        [SerializeField] private Vector3 liftedEuler = new Vector3(0f, 12f, -35f);
        [Tooltip("갈래에서 빠지도록 처음에 곧장 위로 뜨는 높이(m). 받침대 갈래 끝이 손잡이 아래보다 약 0.09m 높다.")]
        [SerializeField] private float liftOffHeight = 0.12f;

        [Header("쉬는 자세(받침대 위)")]
        [Tooltip("저장한 쉬는 자세를 쓴다. 끄면 시작할 때 수화기가 있던 자리를 쉬는 자세로 본다.")]
        [SerializeField] private bool useStoredRest = true;
        [SerializeField] private Vector3 restLocalPosition;
        [SerializeField] private Quaternion restLocalRotation = Quaternion.identity;

        [Header("시간")]
        [SerializeField] private float pickUpDuration = 0.9f;
        [SerializeField] private float hangUpDuration = 0.8f;

        [Header("받침대 덜컥거림")]
        [Tooltip("수화기를 올려 두는 받침대(Telephone_Cradle_Low). 들면 위로 튀고, 내려놓으면 눌렸다가 튀어 오른다.")]
        [SerializeField] private Transform cradle;
        [Tooltip("수화기가 없을 때 받침대가 올라오는 높이(m)")]
        [SerializeField] private float cradleTravel = 0.008f;
        [Tooltip("받침대 스프링 세기")]
        [SerializeField] private float cradleSpring = 900f;
        [Tooltip("받침대 흔들림이 잦아드는 정도")]
        [SerializeField] private float cradleDamping = 12f;
        [Tooltip("들기 진행도가 이 값을 넘으면(수화기가 떨어지는 순간) 받침대가 튀어 오른다")]
        [SerializeField, Range(0f, 0.5f)] private float cradleReleaseAt = 0.06f;
        [Tooltip("내려놓을 때 수화기가 닿으며 받침대를 누르는 속도(m/s)")]
        [SerializeField] private float cradlePressSpeed = 0.25f;

        [Header("테스트")]
        [Tooltip("켜면 E 키로 들기/내려놓기를 번갈아 한다(테스트용)")]
        [SerializeField] private bool testKey = true;

        private Vector3 _restLocalPosition;
        private Quaternion _restLocalRotation;
        private Coroutine _running;
        private Vector3 _cradleRestLocal;
        private float _cradleOffset;     // 0이면 눌린 자리(수화기가 올려진 상태), cradleTravel이면 올라온 자리
        private float _cradleVelocity;
        private float _cradleTarget;

        /// <summary>들어 올린 상태인지(들어 올리는 중 포함)</summary>
        public bool IsLifted { get; private set; }

        /// <summary>움직이는 중인지</summary>
        public bool IsBusy => _running != null;

        public event Action PickedUp;
        public event Action HungUp;

        private void Awake()
        {
            if (cradle != null) _cradleRestLocal = cradle.localPosition;
            if (handset == null) return;
            bool hasStored = restLocalPosition != Vector3.zero || restLocalRotation != Quaternion.identity;
            if (useStoredRest && hasStored)
            {
                // 씬에서 수화기를 옮겨 두었더라도 받침대 위에서 시작한다.
                _restLocalPosition = restLocalPosition;
                _restLocalRotation = restLocalRotation;
                handset.localPosition = _restLocalPosition;
                handset.localRotation = _restLocalRotation;
            }
            else
            {
                _restLocalPosition = handset.localPosition;
                _restLocalRotation = handset.localRotation;
            }
        }

        private void Reset()
        {
            if (handset != null) CaptureRest();
        }

        /// <summary>지금 수화기 자세를 쉬는 자세(받침대 위)로 저장한다. 수화기를 받침대에 올려 둔 상태에서 쓴다.</summary>
        [ContextMenu("지금 수화기 자세를 쉬는 자세로 저장")]
        private void CaptureRest()
        {
            if (handset == null) return;
            restLocalPosition = handset.localPosition;
            restLocalRotation = handset.localRotation;
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            if (testKey && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                if (IsLifted) HangUp();
                else PickUp();
            }
#endif
            UpdateCradle(Time.deltaTime);
        }

        /// <summary>받침대 스프링을 진행하고, 수화기가 올려져 있으면 수화기도 받침대와 함께 움직인다.</summary>
        private void UpdateCradle(float dt)
        {
            if (cradle == null || dt <= 0f) return;
            // 프레임이 길어도 튀지 않게 잘게 나눠 적분한다.
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / 0.004f), 1, 20);
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                float accel = cradleSpring * (_cradleTarget - _cradleOffset) - cradleDamping * _cradleVelocity;
                _cradleVelocity += accel * h;
                _cradleOffset += _cradleVelocity * h;
            }
            Transform parent = cradle.parent;
            Vector3 up = parent != null ? parent.InverseTransformDirection(transform.up) : transform.up;
            cradle.localPosition = _cradleRestLocal + up * _cradleOffset;

            // 받침대 위의 수화기도 같이 덜컥인다(흔들림이 끝나면 손대지 않음)
            bool moving = Mathf.Abs(_cradleOffset) > 1e-5f || Mathf.Abs(_cradleVelocity) > 1e-4f;
            if (handset != null && !IsLifted && !IsBusy && moving) Apply(0f);
        }

        /// <summary>수화기를 들어 올린다. 이미 들었거나 움직이는 중이면 무시한다.</summary>
        public void PickUp()
        {
            if (handset == null || IsLifted || IsBusy) return;
            IsLifted = true;
            _running = StartCoroutine(Animate(0f, 1f, pickUpDuration, () => PickedUp?.Invoke()));
        }

        /// <summary>수화기를 받침대에 내려놓는다. 이미 내려놓았거나 움직이는 중이면 무시한다.</summary>
        public void HangUp()
        {
            if (handset == null || !IsLifted || IsBusy) return;
            IsLifted = false;
            _running = StartCoroutine(Animate(1f, 0f, hangUpDuration, () =>
            {
                // 수화기가 닿는 순간 받침대를 눌러 내린다(내려갔다가 튀어 오르며 자리 잡음).
                _cradleTarget = 0f;
                _cradleVelocity = -Mathf.Abs(cradlePressSpeed);
                HungUp?.Invoke();
            }));
        }

        private IEnumerator Animate(float from, float to, float duration, Action done)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / Mathf.Max(duration, 1e-4f)));
                // 들 때: 수화기가 떨어지는 순간 받침대가 위로 튀어 오른다.
                if (to > from && t >= cradleReleaseAt) _cradleTarget = cradleTravel;
                Apply(t);
                yield return null;
            }
            Apply(to);
            _running = null;
            done?.Invoke();
        }

        /// <summary>진행도 t(0 쉬는 자세 ~ 1 들어 올린 자세)에 맞춰 수화기를 놓는다.</summary>
        private void Apply(float t)
        {
            Transform parent = handset.parent;
            Vector3 restWorld = parent != null ? parent.TransformPoint(_restLocalPosition) : _restLocalPosition;
            Quaternion restWorldRot = parent != null ? parent.rotation * _restLocalRotation : _restLocalRotation;

            // 들어 올린 자세는 이 오브젝트(전화기 루트) 기준으로 정해 모델 축 방향과 상관없이 위·앞이 직관적이게 한다.
            Vector3 liftedWorld = restWorld + transform.TransformVector(liftedOffset);
            Quaternion liftedWorldRot = transform.rotation * Quaternion.Euler(liftedEuler) * Quaternion.Inverse(transform.rotation) * restWorldRot;

            // 받침대 위에 있을 때(t≈0)는 받침대 높이를 따라간다. 내려놓을 때 받침대가 올라와 있으면 그 위에 닿는다.
            restWorld += transform.up * _cradleOffset;
            handset.position = HandsetLiftMotion.EvaluatePosition(restWorld, liftedWorld, transform.up, liftOffHeight, t);
            handset.rotation = HandsetLiftMotion.EvaluateRotation(restWorldRot, liftedWorldRot, t);
        }
    }
}
