using UnityEngine;

namespace CaseFileOmission.Props
{
    /// <summary>
    /// 전화기 몸통과 수화기를 잇는 검은 고무 코일 전화선. 매 프레임 줄 중심선을 시뮬레이션하고, 그 둘레에 감긴 나선 관 메시로 그린다.
    /// 수화기를 들면 끝 앵커가 따라 움직이고, 코일이 늘어나며 따라온다(놓으면 다시 줄어들어 처짐).
    /// 앵커는 모델의 Telephone_CordAnchor_Body / Telephone_CordAnchor_Handset 빈 오브젝트를 연결한다.
    /// 편집 모드(씬·프리팹 화면)에서도 자리 잡은 모양을 그려 플레이 없이 확인할 수 있다.
    /// </summary>
    [ExecuteAlways]
    public sealed class TelephoneCord : MonoBehaviour
    {
        [Header("앵커")]
        [SerializeField] private Transform bodyAnchor;
        [SerializeField] private Transform handsetAnchor;
        [Tooltip("몸통 앵커에서 선이 빠져나가는 방향(앵커의 로컬 축)")]
        [SerializeField] private Vector3 bodyExitDirection = Vector3.zero;
        [Tooltip("수화기 앵커에서 선이 빠져나가는 방향(앵커의 로컬 축). 수화기 몸체를 뚫고 바로 아래로 떨어지지 않게 바깥쪽으로 둔다.")]
        [SerializeField] private Vector3 handsetExitDirection = Vector3.zero;
        [Tooltip("몸통 쪽 끝에서 빠져나가는 방향으로 당길 점 수(첫 점만 고정, 나머지는 멀어질수록 약하게)")]
        [SerializeField, Range(0, 8)] private int exitPoints = 3;
        [Tooltip("수화기 쪽 끝에서 빠져나가는 방향으로 당길 점 수(첫 점만 고정, 나머지는 멀어질수록 약하게)")]
        [SerializeField, Range(0, 8)] private int handsetExitPoints = 5;
        [Tooltip("출구 방향으로 당기는 세기(걸음마다)")]
        [SerializeField, Range(0f, 1f)] private float exitPull = 0.5f;

        [Header("쉬는 모양")]
        [Tooltip("수화기가 제자리에 있을 때의 선 중심선(이 오브젝트 로컬, 몸통 → 수화기 순서). 비우면 물리로만 처진다.")]
        [SerializeField] private Vector3[] restShape =
        {
            // Blender 미리보기(Telephone_Cord_Preview)의 중심선: 수화기 손잡이 송화구(말하는 쪽, 꺾인 관) 끝 기둥 바깥에서 나와 송화구 테두리 바깥으로 처져 내려가
            // 바닥에 닿을락 말락 한 뒤, 왼쪽 손잡이 앞으로 비켜 올라가 몸통 왼쪽 옆면 가운데 위(손잡이 위)로 들어감. 몸통·수화기 메시와 2cm 이상 떨어짐
            new Vector3(0.2180f, 0.4500f, 0.0200f), new Vector3(0.2757f, 0.4486f, 0.0376f), new Vector3(0.3244f, 0.4272f, 0.0651f), new Vector3(0.3490f, 0.3798f, 0.0940f),
            new Vector3(0.3642f, 0.3269f, 0.1199f), new Vector3(0.3740f, 0.2701f, 0.1392f), new Vector3(0.3773f, 0.2112f, 0.1542f), new Vector3(0.3824f, 0.1517f, 0.1660f),
            new Vector3(0.4014f, 0.0945f, 0.1704f), new Vector3(0.4366f, 0.0458f, 0.1626f), new Vector3(0.4858f, 0.0474f, 0.1423f), new Vector3(0.5167f, 0.0963f, 0.1239f),
            new Vector3(0.5387f, 0.1509f, 0.1087f), new Vector3(0.5542f, 0.2085f, 0.0965f), new Vector3(0.5638f, 0.2678f, 0.0869f), new Vector3(0.5704f, 0.3278f, 0.0787f),
            new Vector3(0.5751f, 0.3881f, 0.0711f), new Vector3(0.5785f, 0.4484f, 0.0638f), new Vector3(0.5811f, 0.5088f, 0.0566f), new Vector3(0.5823f, 0.5693f, 0.0498f),
            new Vector3(0.5821f, 0.6299f, 0.0435f), new Vector3(0.5802f, 0.6905f, 0.0373f), new Vector3(0.5760f, 0.7509f, 0.0310f), new Vector3(0.5685f, 0.8110f, 0.0245f),
            new Vector3(0.5569f, 0.8704f, 0.0180f), new Vector3(0.5354f, 0.9267f, 0.0112f), new Vector3(0.4865f, 0.9584f, 0.0047f), new Vector3(0.4260f, 0.9630f, 0.0000f)
        };
        [Tooltip("쉬는 모양으로 당기는 세기(걸음마다)")]
        [SerializeField, Range(0f, 1f)] private float restShapeStrength = 0.35f;
        [Tooltip("수화기가 쉬는 자리에서 이 거리(m) 이상 벗어나면 쉬는 모양을 놓고 물리로만 따라간다")]
        [SerializeField] private float restShapeRelease = 0.08f;

        [Header("줄")]
        [Tooltip("코일이 줄어든 상태의 중심선 길이(m). 수화기를 들면 이 길이를 기준으로 당겨진다(쉬는 모양은 이보다 늘어난 상태).")]
        [SerializeField] private float length = 0.95f;
        [Tooltip("휴지 길이의 몇 배까지 늘어날 수 있는지")]
        [SerializeField, Range(1f, 5f)] private float maxStretch = 2.4f;
        [Tooltip("늘어난 코일이 되돌아가는 세기(반복마다). 낮을수록 잘 늘어난다.")]
        [SerializeField, Range(0.001f, 1f)] private float stiffness = 0.15f;
        [SerializeField, Range(8, 64)] private int pointCount = 32;
        [SerializeField, Range(0f, 0.5f)] private float damping = 0.06f;
        [SerializeField, Range(4, 60)] private int constraintIterations = 16;

        [Tooltip("시뮬레이션 고정 간격(초)")]
        [SerializeField] private float stepInterval = 1f / 120f;
        [Tooltip("편집 모드에서 화면이 갱신될 때마다 진행할 걸음 수(자리 잡은 모양을 바로 보여 주기 위함)")]
        [SerializeField, Range(10, 600)] private int editModeSteps = 240;

        [Header("충돌")]
        [Tooltip("선이 얹힐 바닥·책상 레이어. 비우면 충돌하지 않는다.")]
        [SerializeField] private LayerMask groundLayers;
        [Tooltip("아래 금지 영역 좌표의 기준(보통 수화기)")]
        [SerializeField] private Transform keepOutSpace;
        [Tooltip("선이 들어가면 안 되는 캡슐 영역(keepOutSpace 로컬 좌표). 수화기를 들었을 때 송화구 테두리·컵을 스치지 않게 한다.")]
        [SerializeField] private KeepOutCapsule[] keepOut;
        [Tooltip("양 끝에서 이 수만큼의 점은 금지 영역 밀어내기에서 뺀다(앵커가 기둥 표면 바로 옆에 있어서).")]
        [SerializeField, Range(0, 6)] private int keepOutSkipEnds = 2;

        [System.Serializable]
        private struct KeepOutCapsule
        {
            [Tooltip("이 캡슐 좌표의 기준. 비우면 keepOutSpace를 쓴다(예: 몸통 옆 손잡이는 몸통 기준).")]
            public Transform space;
            public Vector3 start;
            public Vector3 end;
            public float radius;
            [Tooltip("켜면 둥근 끝이 없는 원통(원판)으로 본다. 송화구 테두리처럼 납작하고 모서리가 있는 부분용.")]
            public bool flatCylinder;
        }

        [Header("모양")]
        [Tooltip("고무 선 굵기(반지름, m)")]
        [SerializeField] private float radius = 0.005f;
        [SerializeField] private bool coiled = true;
        [Tooltip("코일 반지름(m, 중심선에서 선 중심까지)")]
        [SerializeField] private float coilRadius = 0.018f;
        [Tooltip("코일 감은 수")]
        [SerializeField, Range(10, 300)] private int coilTurns = 40;
        [SerializeField, Range(6, 24)] private int samplesPerTurn = 12;
        [Tooltip("양 끝의 감기지 않은 곧은 부분 길이(m)")]
        [SerializeField] private float straightLead = 0.06f;
        [SerializeField, Range(3, 16)] private int radialSegments = 6;
        [Tooltip("비우면 검은 고무 재질(URP Lit)을 만들어 쓴다.")]
        [SerializeField] private Material material;

        private CordSimulation _simulation;
        private Mesh _mesh;
        private Vector3[] _path;
        private Vector3[] _vertices;
        private Vector3[] _normals;
        private float _accumulator;
        private Vector3 _lastStart;
        private Vector3 _lastEnd;
        private Material _runtimeMaterial;
        private GameObject _renderObject;
        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private Vector3[] _restTargets;
        private float _restLength;
        private bool _rebuild;

        private int PathCount => coiled ? coilTurns * samplesPerTurn + 1 : pointCount;

        private void OnEnable()
        {
            if (bodyAnchor == null || handsetAnchor == null)
            {
                // 편집 중 앵커를 아직 안 넣은 상태일 수 있으므로 끄지 않고 그리지만 않는다.
                if (Application.isPlaying) Debug.LogWarning($"[TelephoneCord] {name}: 앵커가 연결되지 않아 전화선을 그리지 않습니다.", this);
                return;
            }

            BuildRestTargets();
            _simulation = new CordSimulation(pointCount, CordLength, bodyAnchor.position, handsetAnchor.position)
            {
                Damping = damping,
                ConstraintIterations = constraintIterations,
                Stiffness = coiled ? stiffness : 1f,
                MaxStretch = coiled ? maxStretch : 1f,
                LeadPointsStart = exitPoints,
                LeadPointsEnd = handsetExitPoints,
                Gravity = Physics.gravity
            };
            _lastStart = bodyAnchor.position;
            _lastEnd = handsetAnchor.position;
            EnsureRenderObject();
            BuildMesh();
            // 재질이 비어 있으면 무광 검은 고무를 만들어 쓴다(직렬화 필드에는 넣지 않음).
            if (material == null && _runtimeMaterial == null) _runtimeMaterial = CreateRubberMaterial();
            _renderer.sharedMaterial = material != null ? material : _runtimeMaterial;

            // 처음부터 쉬는 모양(없으면 처진 모양)으로 시작해 직선에서 떨어지는 장면이 보이지 않게 한다.
            if (_restTargets != null)
                for (int i = 1; i < _restTargets.Length - 1; i++) _simulation.SetPoint(i, transform.TransformPoint(_restTargets[i]));
            StepFixed(editModeSteps * 2);
            BuildPath();
            UpdateMesh();
        }

        // 인스펙터에서 값을 바꾸면 다음 갱신 때 새 값으로 다시 만든다(OnValidate 안에서는 렌더러를 바꾸지 않음).
        private void OnValidate() => _rebuild = true;

        private void OnDisable()
        {
            _simulation = null;
            if (_mesh != null) SafeDestroy(_mesh);
            _mesh = null;
            if (_renderObject != null) SafeDestroy(_renderObject);
            _renderObject = null;
        }

        /// <summary>
        /// 선 메시는 숨긴 자식 오브젝트(저장 안 됨)에 그린다. 이 오브젝트의 MeshFilter에 넣으면 편집 모드에서
        /// 씬·프리팹에 쓸모없는 메시 변경 기록이 남기 때문이다.
        /// </summary>
        private void EnsureRenderObject()
        {
            if (_renderObject != null) return;
            _renderObject = new GameObject("_TelephoneCordMesh") { hideFlags = HideFlags.HideAndDontSave };
            _renderObject.transform.SetParent(transform, false);
            _renderObject.layer = gameObject.layer;
            _filter = _renderObject.AddComponent<MeshFilter>();
            _renderer = _renderObject.AddComponent<MeshRenderer>();
        }

        private void LateUpdate()
        {
            if (_rebuild)
            {
                _rebuild = false;
                OnDisable();
                OnEnable();
            }
            if (_simulation == null || bodyAnchor == null || handsetAnchor == null) return;
            Vector3 start = bodyAnchor.position;
            Vector3 end = handsetAnchor.position;

            // 순간이동(씬 로드·리셋)으로 앵커가 크게 튀면 줄을 새로 놓는다.
            float jump = CordLength * maxStretch;
            if ((start - _lastStart).sqrMagnitude > jump * jump || (end - _lastEnd).sqrMagnitude > jump * jump)
                _simulation.Reset(start, end);

            if (!Application.isPlaying)
            {
                // 편집 모드: 화면이 갱신될 때 바로 자리 잡은 모양을 보여 준다(수화기를 옮겨 보면 따라옴).
                StepFixed(editModeSteps);
            }
            else
            {
                _accumulator = Mathf.Min(_accumulator + Time.deltaTime, stepInterval * 8f);
                int steps = 0;
                while (_accumulator >= stepInterval)
                {
                    // 고정 간격 사이에서 앵커 위치를 보간해 빠르게 들어 올려도 끊김이 없게 한다.
                    float t = Mathf.Clamp01((steps + 1) * stepInterval / Mathf.Max(Time.deltaTime, stepInterval));
                    StepOnce(Vector3.Lerp(_lastStart, start, t), Vector3.Lerp(_lastEnd, end, t));
                    _accumulator -= stepInterval;
                    steps++;
                }
            }

            _lastStart = start;
            _lastEnd = end;
            BuildPath();
            UpdateMesh();
        }

        private void StepFixed(int count)
        {
            for (int i = 0; i < count; i++) StepOnce(bodyAnchor.position, handsetAnchor.position);
        }

        private void StepOnce(Vector3 start, Vector3 end)
        {
            Vector3 dirA = bodyExitDirection == Vector3.zero ? Vector3.zero : bodyAnchor.TransformDirection(bodyExitDirection);
            Vector3 dirB = handsetExitDirection == Vector3.zero ? Vector3.zero : handsetAnchor.TransformDirection(handsetExitDirection);
            // 쉬는 자리에서는 코일이 늘어난 채 쉬는 모양을 따르고(되돌아가는 힘 끔), 수화기를 들수록 코일이 다시 줄어든다.
            float rest = RestWeight(start, end);
            if (coiled) _simulation.Stiffness = stiffness * (1f - rest);
            // 끝을 곧게 빼는 점 간격도 쉬는 모양(늘어난 상태)의 점 간격과 맞춰야 끝이 당겨 올라가지 않는다.
            float restSegment = _restLength > 0f ? _restLength / (pointCount - 1) : _simulation.SegmentLength;
            _simulation.LeadSpacing = Mathf.Lerp(_simulation.SegmentLength, restSegment, rest);
            // 끝은 첫 점 하나만 출구 방향으로 고정하고, 그다음 점들은 출구 방향 쪽으로 멀어질수록 약하게 당긴다.
            // 곧게 고정하는 구간이 길면 수화기를 기울일 때 그 끝에서 고리처럼 급하게 꺾이기 때문이다.
            // 쉬는 자리에서는 쉬는 모양이 끝부분 휘어짐까지 정하므로 출구 당김을 끈다.
            _simulation.LeadPointsStart = rest < 0.5f && exitPoints > 0 ? 1 : 0;
            _simulation.LeadPointsEnd = rest < 0.5f && handsetExitPoints > 0 ? 1 : 0;

            _simulation.Step(start, dirA, end, dirB, stepInterval);
            ApplyExitPull(start, dirA, exitPoints, false, 1f - rest);
            ApplyExitPull(end, dirB, handsetExitPoints, true, 1f - rest);
            ApplyRestShape(rest);
            ResolveKeepOut();
            if (groundLayers.value != 0) ResolveGround();
        }

        /// <summary>끝에서 count개 점을 출구 방향 직선 쪽으로, 멀어질수록 약하게 당긴다(첫 점은 시뮬레이션이 고정).</summary>
        private void ApplyExitPull(Vector3 anchor, Vector3 dir, int count, bool fromEnd, float strength)
        {
            if (count <= 1 || strength <= 0f || dir == Vector3.zero) return;
            dir.Normalize();
            int last = _simulation.PointCount - 1;
            float step = _simulation.LeadSpacing > 0f ? _simulation.LeadSpacing : _simulation.SegmentLength;
            for (int k = 2; k <= count && k < last / 2; k++)
            {
                float w = strength * exitPull * (1f - (k - 1f) / count);
                int index = fromEnd ? last - k : k;
                _simulation.PullToward(index, anchor + dir * (step * k), w);
            }
        }

        /// <summary>선이 들어가면 안 되는 원통(캡슐) 영역 밖으로 점을 밀어낸다(예: 수화기 송화구 테두리·컵).</summary>
        private void ResolveKeepOut()
        {
            if (keepOut == null || keepOut.Length == 0) return;
            float margin = (coiled ? coilRadius : 0f) + radius;
            for (int c = 0; c < keepOut.Length; c++)
            {
                Transform space = keepOut[c].space != null ? keepOut[c].space : keepOutSpace;
                if (space == null) continue;
                Vector3 a = space.TransformPoint(keepOut[c].start);
                Vector3 b = space.TransformPoint(keepOut[c].end);
                float reach = keepOut[c].radius + margin;
                Vector3 ab = b - a;
                float abLen2 = Mathf.Max(ab.sqrMagnitude, 1e-8f);
                if (keepOut[c].flatCylinder)
                {
                    PushOutOfCylinder(a, b, keepOut[c].radius, margin);
                    continue;
                }
                for (int i = 1 + keepOutSkipEnds; i < _simulation.PointCount - 1 - keepOutSkipEnds; i++)
                {
                    Vector3 p = _simulation.GetPoint(i);
                    Vector3 closest = a + ab * Mathf.Clamp01(Vector3.Dot(p - a, ab) / abLen2);
                    Vector3 away = p - closest;
                    float d = away.magnitude;
                    if (d >= reach) continue;
                    if (d < 1e-5f) away = Vector3.Cross(ab, Vector3.up).sqrMagnitude > 1e-8f ? Vector3.Cross(ab, Vector3.up) : Vector3.right;
                    _simulation.PullToward(i, closest + away.normalized * reach, 1f);
                }
            }
        }

        /// <summary>a~b를 축으로 하는 납작한 원통(반지름 r) 안의 점을 가장 가까운 면(옆면·윗면·아랫면) 밖으로 민다.</summary>
        private void PushOutOfCylinder(Vector3 a, Vector3 b, float r, float margin)
        {
            Vector3 axis = b - a;
            float height = axis.magnitude;
            if (height < 1e-6f) return;
            axis /= height;
            float reach = r + margin;
            for (int i = 1 + keepOutSkipEnds; i < _simulation.PointCount - 1 - keepOutSkipEnds; i++)
            {
                Vector3 p = _simulation.GetPoint(i);
                float h = Vector3.Dot(p - a, axis);
                if (h < -margin || h > height + margin) continue;
                Vector3 radial = (p - a) - axis * h;
                float d = radial.magnitude;
                if (d >= reach) continue;

                float side = reach - d;            // 옆으로 밀 거리
                float down = h + margin;           // 아랫면 밖으로 밀 거리
                float up = height + margin - h;    // 윗면 밖으로 밀 거리
                Vector3 target;
                if (side <= down && side <= up)
                {
                    Vector3 dir = d > 1e-5f ? radial / d : Vector3.Cross(axis, Vector3.forward).normalized;
                    target = a + axis * h + dir * reach;
                }
                else if (down < up) target = p - axis * down;
                else target = p + axis * up;
                _simulation.PullToward(i, target, 1f);
            }
        }

        private float CordLength => length;

        /// <summary>쉬는 모양을 시뮬레이션 점 수만큼 호 길이로 고르게 다시 뽑는다.</summary>
        private void BuildRestTargets()
        {
            _restTargets = null;
            _restLength = 0f;
            if (restShape == null || restShape.Length < 2) return;

            var cumulative = new float[restShape.Length];
            for (int i = 1; i < restShape.Length; i++) cumulative[i] = cumulative[i - 1] + Vector3.Distance(restShape[i - 1], restShape[i]);
            _restLength = cumulative[restShape.Length - 1];
            if (_restLength <= 0f) return;

            _restTargets = new Vector3[pointCount];
            int seg = 1;
            for (int k = 0; k < pointCount; k++)
            {
                float s = _restLength * k / (pointCount - 1);
                while (seg < restShape.Length - 1 && cumulative[seg] < s) seg++;
                float t = Mathf.InverseLerp(cumulative[seg - 1], cumulative[seg], s);
                _restTargets[k] = Vector3.Lerp(restShape[seg - 1], restShape[seg], t);
            }
        }

        /// <summary>수화기가 쉬는 자리에 있을수록 1, restShapeRelease 이상 벗어나면 0.</summary>
        private float RestWeight(Vector3 start, Vector3 end)
        {
            if (_restTargets == null) return 0f;
            Vector3 restStart = transform.TransformPoint(_restTargets[0]);
            Vector3 restEnd = transform.TransformPoint(_restTargets[_restTargets.Length - 1]);
            float offset = Mathf.Max(Vector3.Distance(start, restStart), Vector3.Distance(end, restEnd));
            return 1f - Mathf.Clamp01(offset / Mathf.Max(restShapeRelease, 1e-4f));
        }

        /// <summary>쉬는 모양 쪽으로 점들을 당긴다. 수화기가 멀어질수록 약해져 물리로 넘어간다.</summary>
        private void ApplyRestShape(float rest)
        {
            float weight = restShapeStrength * rest;
            if (_restTargets == null || weight <= 0f) return;

            for (int i = 1; i < _restTargets.Length - 1; i++)
                _simulation.PullToward(i, transform.TransformPoint(_restTargets[i]), weight);
        }

        private void ResolveGround()
        {
            float lift = coiled ? coilRadius + radius : radius;
            for (int i = 1; i < _simulation.PointCount - 1; i++)
            {
                Vector3 p = _simulation.GetPoint(i);
                Vector3 from = p + Vector3.up * (lift * 4f);
                if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, lift * 5f, groundLayers, QueryTriggerInteraction.Ignore))
                    _simulation.SetPoint(i, hit.point + hit.normal * lift);
            }
        }

        /// <summary>시뮬레이션 점 사이를 부드럽게(Catmull-Rom) 이은 중심선 위의 점. u는 0(몸통)~1(수화기).</summary>
        private Vector3 CenterAt(float u)
        {
            int last = _simulation.PointCount - 1;
            float f = Mathf.Clamp01(u) * last;
            int i = Mathf.Min(Mathf.FloorToInt(f), last - 1);
            float t = f - i;
            Vector3 p0 = _simulation.GetPoint(Mathf.Max(i - 1, 0));
            Vector3 p1 = _simulation.GetPoint(i);
            Vector3 p2 = _simulation.GetPoint(i + 1);
            Vector3 p3 = _simulation.GetPoint(Mathf.Min(i + 2, last));
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }

        /// <summary>선(관)의 중심 경로를 만든다. 코일이면 중심선 둘레를 감는 나선, 양 끝은 곧게.</summary>
        private void BuildPath()
        {
            int n = _path.Length;
            if (!coiled)
            {
                for (int k = 0; k < n; k++) _path[k] = _simulation.GetPoint(k);
                return;
            }

            float lead = Mathf.Clamp(straightLead / length, 0f, 0.3f);
            Vector3 prevNormal = Vector3.zero;
            for (int k = 0; k < n; k++)
            {
                float u = (float)k / (n - 1);
                Vector3 c = CenterAt(u);
                Vector3 tangent = (CenterAt(Mathf.Min(u + 0.002f, 1f)) - CenterAt(Mathf.Max(u - 0.002f, 0f))).normalized;
                if (tangent == Vector3.zero) tangent = Vector3.forward;
                Vector3 normal = k == 0 ? Vector3.Cross(tangent, Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.right).normalized
                                        : Vector3.ProjectOnPlane(prevNormal, tangent).normalized;
                if (normal == Vector3.zero) normal = Vector3.Cross(tangent, Vector3.right).normalized;
                prevNormal = normal;
                Vector3 binormal = Vector3.Cross(tangent, normal);

                // 곧은 끝부분에서 코일 반지름을 0에서 서서히 키운다.
                float fromEnd = Mathf.Min(u, 1f - u);
                float ramp = lead <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(lead * 0.5f, lead, fromEnd));
                float angle = Mathf.PI * 2f * coilTurns * u;
                _path[k] = c + (normal * Mathf.Cos(angle) + binormal * Mathf.Sin(angle)) * (coilRadius * ramp);
            }
        }

        private void BuildMesh()
        {
            if (_mesh != null) SafeDestroy(_mesh);
            // 매 프레임 다시 만드는 메시라 씬·프리팹에 저장하지 않는다.
            _mesh = new Mesh { name = "TelephoneCord", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
            _mesh.MarkDynamic();
            int rings = PathCount;
            int ringVerts = radialSegments + 1;
            _path = new Vector3[rings];
            _vertices = new Vector3[rings * ringVerts];
            _normals = new Vector3[_vertices.Length];
            var uv = new Vector2[_vertices.Length];
            var tris = new int[(rings - 1) * radialSegments * 6];

            int ti = 0;
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < ringVerts; s++)
                {
                    uv[r * ringVerts + s] = new Vector2((float)s / radialSegments, (float)r / (rings - 1));
                    if (r == rings - 1 || s == radialSegments) continue;
                    int a = r * ringVerts + s, b = a + 1, c = a + ringVerts, d = c + 1;
                    tris[ti++] = a; tris[ti++] = c; tris[ti++] = b;
                    tris[ti++] = b; tris[ti++] = c; tris[ti++] = d;
                }
            }

            _mesh.vertices = _vertices;
            _mesh.uv = uv;
            _mesh.triangles = tris;
            _filter.sharedMesh = _mesh;
        }

        private void UpdateMesh()
        {
            int rings = _path.Length;
            int ringVerts = radialSegments + 1;
            Vector3 prevNormal = Vector3.zero;

            for (int r = 0; r < rings; r++)
            {
                Vector3 p = _path[r];
                Vector3 tangent = (_path[Mathf.Min(r + 1, rings - 1)] - _path[Mathf.Max(r - 1, 0)]).normalized;
                if (tangent == Vector3.zero) tangent = Vector3.forward;

                // 평행 이동 틀: 앞 고리의 법선을 이어 받아 관이 비틀리지 않게 한다.
                Vector3 normal = r == 0 ? Vector3.Cross(tangent, Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.right).normalized
                                        : Vector3.ProjectOnPlane(prevNormal, tangent).normalized;
                if (normal == Vector3.zero) normal = Vector3.Cross(tangent, Vector3.right).normalized;
                Vector3 binormal = Vector3.Cross(tangent, normal);
                prevNormal = normal;

                for (int s = 0; s < ringVerts; s++)
                {
                    float angle = s * Mathf.PI * 2f / radialSegments;
                    Vector3 dir = normal * Mathf.Cos(angle) + binormal * Mathf.Sin(angle);
                    int idx = r * ringVerts + s;
                    _vertices[idx] = transform.InverseTransformPoint(p + dir * radius);
                    _normals[idx] = transform.InverseTransformDirection(dir);
                }
            }

            _mesh.vertices = _vertices;
            _mesh.normals = _normals;
            _mesh.RecalculateBounds();
        }

        private static Material CreateRubberMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader) { name = "Telephone_Cord_Rubber (Runtime)", hideFlags = HideFlags.DontSave };
            mat.SetColor("_BaseColor", new Color(0.012f, 0.012f, 0.012f));
            mat.SetColor("_Color", new Color(0.012f, 0.012f, 0.012f));
            mat.SetFloat("_Smoothness", 0.12f);   // 무광에 가까운 고무
            mat.SetFloat("_Glossiness", 0.12f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_EnvironmentReflections", 0f);
            mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            return mat;
        }

        private void OnDestroy()
        {
            if (_mesh != null) SafeDestroy(_mesh);
            if (_renderObject != null) SafeDestroy(_renderObject);
            if (_runtimeMaterial != null) SafeDestroy(_runtimeMaterial);
        }

        private static void SafeDestroy(Object obj)
        {
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
