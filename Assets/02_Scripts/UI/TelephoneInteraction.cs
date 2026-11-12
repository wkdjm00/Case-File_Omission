using System.Collections;
using System.Collections.Generic;
using CaseFileOmission.Props;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CaseFileOmission.UI
{
    /// <summary>
    /// 전화기 상호작용 흐름: 전화기를 클릭 → 카메라가 전화기를 화면 가운데로 당겨 옴 → 선택창
    /// (걸 때: 누구에게 걸지 / 올 때: 받을지) → 고르면 수화기를 들고 통화(PhoneCallDemo) → 끊으면 카메라가 제자리로.
    /// </summary>
    public sealed class TelephoneInteraction : MonoBehaviour
    {
        public enum CallMode { Outgoing, Incoming }

        [SerializeField] private TelephoneHandsetAnimator handset;
        [SerializeField] private PhoneCallUI ui;
        [SerializeField] private PhoneCallDemo call;

        [Header("전화 상황(테스트)")]
        [Tooltip("Outgoing: 내가 건다(누구에게 걸지 묻기). Incoming: 전화가 왔다(받을지 묻기).")]
        [SerializeField] private CallMode mode = CallMode.Outgoing;
        [SerializeField] private string outgoingPrompt = "누구에게 전화할까?";
        [SerializeField] private string[] contacts = { "사무소", "정보원", "경찰서" };
        [SerializeField] private string outgoingCancel = "그만둔다";
        [SerializeField] private string incomingPrompt = "전화가 울린다.";
        [SerializeField] private string incomingAnswer = "전화를 받는다";
        [SerializeField] private string incomingIgnore = "받지 않는다";

        [Header("카메라")]
        [Tooltip("비우면 Camera.main")]
        [SerializeField] private Camera targetCamera;
        [Tooltip("전화기 정면을 알려 주는 부품(다이얼). 전화기 중심에서 이 부품 쪽이 정면이다. 비우면 이름에 'Dial'이 들어간 자식을 찾는다.")]
        [SerializeField] private Transform frontReference;
        [Tooltip("정면에서 내려다보는 각도(도). 0이면 수평 정면.")]
        [SerializeField, Range(-30f, 60f)] private float focusPitch = 5f;
        [Tooltip("화면에 담을 높이 = 전화기 높이 × (1 + 이 값). 들어 올린 수화기와 그 옆 대사 자리까지 위쪽을 더 담는다.")]
        [SerializeField] private float frameExtraTop = 0.7f;
        [Tooltip("담을 영역 바깥 여백(1이면 화면에 꽉 차게)")]
        [SerializeField] private float frameMargin = 1.1f;
        [Tooltip("화면 가운데에 둘 지점을 옮기는 값(m, 월드)")]
        [SerializeField] private Vector3 focusCenterOffset = Vector3.zero;
        [SerializeField] private float moveDuration = 0.8f;

        private enum State { Idle, Moving, Choosing, InCall }

        private State _state = State.Idle;
        private Vector3 _homePosition;
        private Quaternion _homeRotation;
        private readonly List<Renderer> _renderers = new List<Renderer>();

        private Camera Cam => targetCamera != null ? targetCamera : Camera.main;

        private void OnEnable()
        {
            if (handset != null) handset.HungUp += OnHungUp;
        }

        private void OnDisable()
        {
            if (handset != null) handset.HungUp -= OnHungUp;
        }

        private void Update()
        {
            if (_state != State.Idle || !ClickedThisFrame()) return;
            Camera cam = Cam;
            if (cam == null || IsPointerOverUi()) return;
            if (!TryGetPhoneBounds(out Bounds bounds)) return;
            if (bounds.IntersectRay(cam.ScreenPointToRay(PointerPosition()))) StartCoroutine(Focus(cam, bounds));
        }

        // ── 흐름 ────────────────────────────────────────────────

        private IEnumerator Focus(Camera cam, Bounds bounds)
        {
            _state = State.Moving;
            _homePosition = cam.transform.position;
            _homeRotation = cam.transform.rotation;

            // 전화기 정면(다이얼 쪽)에서 거의 수평으로 보며, 받침대부터 들어 올린 수화기 위 대사 자리까지 화면에 담기게
            float frameHeight = bounds.size.y * (1f + frameExtraTop);
            Vector3 center = new Vector3(bounds.center.x, bounds.min.y + frameHeight * 0.5f, bounds.center.z) + focusCenterOffset;
            Vector3 front = FrontDirection(bounds.center);
            Vector3 right = Vector3.Cross(Vector3.up, front);
            float frameWidth = Mathf.Abs(Vector3.Dot(bounds.size, right)) * 1.6f;   // 옆으로 늘어진 선·대사 자리까지
            float halfFov = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float distForHeight = frameHeight * 0.5f / Mathf.Tan(halfFov);
            float distForWidth = frameWidth * 0.5f / (Mathf.Tan(halfFov) * Mathf.Max(cam.aspect, 0.1f));
            float distance = Mathf.Max(distForHeight, distForWidth) * frameMargin + Mathf.Abs(Vector3.Dot(bounds.extents, front));
            Vector3 toCamera = Quaternion.AngleAxis(-focusPitch, right) * front;
            Vector3 targetPos = center + toCamera * distance;
            Quaternion targetRot = Quaternion.LookRotation(center - targetPos, Vector3.up);
            yield return MoveCamera(cam, targetPos, targetRot);

            _state = State.Choosing;
            if (mode == CallMode.Outgoing)
            {
                var options = new List<string>(contacts) { outgoingCancel };
                ui.ShowChoices(options, chosen =>
                {
                    if (chosen >= contacts.Length) { Cancel(); return; }
                    BeginCall(contacts[chosen]);
                }, outgoingPrompt);
            }
            else
            {
                ui.ShowChoices(new[] { incomingAnswer, incomingIgnore }, chosen =>
                {
                    if (chosen == 0) BeginCall(null);
                    else Cancel();
                }, incomingPrompt);
            }
        }

        /// <summary>수화기를 든다. 다 들면 PhoneCallDemo가 통화를 진행하고, 끝나면 수화기를 내려놓는다(OnHungUp).</summary>
        private void BeginCall(string callerLabel)
        {
            _state = State.InCall;
            ui.HideAll();
            if (call != null) call.CallerLabel = callerLabel;   // 건 상대 이름을 대사 앞에 붙임(받을 때는 "발신자")
            handset.PickUp();
        }

        private void Cancel()
        {
            ui.HideAll();
            StartCoroutine(Return());
        }

        private void OnHungUp()
        {
            if (_state == State.InCall) StartCoroutine(Return());
        }

        private IEnumerator Return()
        {
            _state = State.Moving;
            Camera cam = Cam;
            if (cam != null) yield return MoveCamera(cam, _homePosition, _homeRotation);
            _state = State.Idle;
        }

        private IEnumerator MoveCamera(Camera cam, Vector3 toPos, Quaternion toRot)
        {
            Vector3 fromPos = cam.transform.position;
            Quaternion fromRot = cam.transform.rotation;
            float elapsed = 0f;
            while (elapsed < moveDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / Mathf.Max(moveDuration, 1e-4f)));
                cam.transform.SetPositionAndRotation(Vector3.Lerp(fromPos, toPos, t), Quaternion.Slerp(fromRot, toRot, t));
                yield return null;
            }
            cam.transform.SetPositionAndRotation(toPos, toRot);
        }

        /// <summary>전화기 정면 방향(수평). 전화기 중심에서 다이얼 쪽.</summary>
        private Vector3 FrontDirection(Vector3 phoneCenter)
        {
            if (frontReference == null && handset != null)
            {
                foreach (var t in handset.GetComponentsInChildren<Transform>())
                    if (t.name.Contains("Dial") && !t.name.Contains("Stop")) { frontReference = t; break; }
            }

            Vector3 dir = Vector3.zero;
            if (frontReference != null)
            {
                // 다이얼 원판이 바라보는 방향(로컬 Y = 원판 법선). 전체 중심 기준 방향은 옆으로 튀어나온 크랭크 때문에 비뚤어진다.
                var r = frontReference.GetComponent<Renderer>();
                Vector3 p = r != null ? r.bounds.center : frontReference.position;
                Vector3 normal = Vector3.ProjectOnPlane(frontReference.up, Vector3.up);
                Vector3 outward = Vector3.ProjectOnPlane(p - phoneCenter, Vector3.up);
                dir = Vector3.Dot(normal, outward) < 0f ? -normal : normal;
                if (dir.sqrMagnitude < 1e-6f) dir = outward;
            }
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f && handset != null) dir = Vector3.ProjectOnPlane(handset.transform.forward, Vector3.up);
            return dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
        }

        // ── 입력·판정 ───────────────────────────────────────────

        /// <summary>전화기(수화기 애니메이터가 붙은 루트) 아래 모든 렌더러를 감싸는 상자. 콜라이더 없이 클릭을 판정한다.</summary>
        private bool TryGetPhoneBounds(out Bounds bounds)
        {
            bounds = default;
            if (handset == null) return false;
            handset.GetComponentsInChildren(_renderers);
            bool any = false;
            foreach (var r in _renderers)
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return any;
        }

        private static bool IsPointerOverUi() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        private static bool ClickedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }

        private static Vector2 PointerPosition()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
            return Input.mousePosition;
#endif
        }
    }
}
