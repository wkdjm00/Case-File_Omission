using System;
using UnityEngine;

namespace CaseFileOmission.Playtest
{
    public enum DeskObjectKind
    {
        Telephone,
        Computer,
        Report,
        CaseBoard,
        Typewriter,
        ToScene
    }

    /// <summary>책상 위 오브젝트 클릭 판정용 표식. 고정 ID(Kind)만 들고 있다.</summary>
    public sealed class DeskObjectMarker : MonoBehaviour
    {
        public DeskObjectKind Kind;
    }

    /// <summary>
    /// UI-003 메인 책상(09). 전화기·컴퓨터·보고서·사건보드와 최종 보고서용 타자기를 놓는다. 조사 현장·UI-404와는 다른 공간이다.
    /// 전화기는 보유 모델(Telephone 프리팹)을 그대로 쓰고, 나머지는 임시 도형이다. 외형을 바꿔도 표식(Kind)만 유지하면 된다.
    /// </summary>
    public sealed class DeskView : MonoBehaviour
    {
        public static readonly Vector3 DeskOrigin = new Vector3(60f, 0f, 0f);
        public static readonly Vector3 TelephoneLocalPosition = new Vector3(-1.05f, 0.79f, 0.05f);
        public static readonly Quaternion TelephoneLocalRotation = Quaternion.Euler(0, 180f, 0);
        public const float TelephoneScale = 0.38f;

        public Font LabelFont;
        [Tooltip("씬에 미리 놓은 전화기 프리팹 인스턴스(씬 빌더가 테스트 키를 끈 오버라이드로 배치). 비우면 TelephonePrefab으로 만든다.")]
        public GameObject Telephone;
        public GameObject TelephonePrefab;
        public GameObject CandlePrefab;
        public Transform CameraPose { get; private set; }
        private GameObject _ringIndicator;
        private readonly System.Collections.Generic.List<GameObject> _labels = new System.Collections.Generic.List<GameObject>();

        /// <summary>통화 중에는 책상 라벨을 숨긴다(통화 화면에 겹치지 않게).</summary>
        public void SetLabelsVisible(bool visible)
        {
            foreach (var label in _labels)
                if (label != null && label.activeSelf != visible) label.SetActive(visible);
        }

        public void Build(BoardView labels)
        {
            transform.position = DeskOrigin;

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "OfficeFloor";
            floor.transform.SetParent(transform, false);
            floor.transform.localScale = new Vector3(8, 0.1f, 6);
            floor.transform.localPosition = new Vector3(0, -0.05f, 0);
            floor.GetComponent<Renderer>().material = BoardView.MakeMaterial(new Color(0.22f, 0.18f, 0.15f));

            var desk = Box("Desk", new Vector3(0, 0.75f, 0), new Vector3(3.2f, 0.08f, 1.6f), new Color(0.36f, 0.22f, 0.13f));
            for (int i = 0; i < 4; i++)
            {
                float x = i % 2 == 0 ? -1.5f : 1.5f, z = i < 2 ? -0.7f : 0.7f;
                Box("Leg" + i, new Vector3(x, 0.37f, z), new Vector3(0.08f, 0.74f, 0.08f), new Color(0.3f, 0.18f, 0.1f));
            }
            Destroy(desk.GetComponent<Collider>());

            if (Telephone == null && TelephonePrefab != null)
            {
                Telephone = Instantiate(TelephonePrefab, transform);
                Telephone.name = "Telephone";
                Telephone.transform.localPosition = TelephoneLocalPosition;
                Telephone.transform.localRotation = TelephoneLocalRotation;
                Telephone.transform.localScale = Vector3.one * TelephoneScale;
            }
            if (Telephone != null) AddMarker(Telephone, DeskObjectKind.Telephone, true);

            var monitor = Box("Computer", new Vector3(0.1f, 1.08f, 0.45f), new Vector3(0.9f, 0.6f, 0.08f), new Color(0.12f, 0.13f, 0.14f));
            Box("ComputerScreen", new Vector3(0.1f, 1.08f, 0.40f), new Vector3(0.8f, 0.5f, 0.01f), new Color(0.15f, 0.35f, 0.25f)).transform.SetParent(monitor.transform, true);
            Box("ComputerBase", new Vector3(0.1f, 0.83f, 0.45f), new Vector3(0.4f, 0.08f, 0.3f), new Color(0.12f, 0.13f, 0.14f));
            AddMarker(monitor, DeskObjectKind.Computer, false);

            var report = Box("ReportFolder", new Vector3(0.95f, 0.81f, -0.25f), new Vector3(0.45f, 0.04f, 0.6f), new Color(0.75f, 0.62f, 0.38f));
            AddMarker(report, DeskObjectKind.Report, false);

            var typewriter = Box("Typewriter", new Vector3(-0.2f, 0.88f, -0.35f), new Vector3(0.6f, 0.2f, 0.4f), new Color(0.1f, 0.1f, 0.1f));
            AddMarker(typewriter, DeskObjectKind.Typewriter, false);

            var board = Box("CaseBoard", new Vector3(0, 1.9f, 1.6f), new Vector3(2.6f, 1.4f, 0.06f), new Color(0.62f, 0.45f, 0.28f));
            AddMarker(board, DeskObjectKind.CaseBoard, false);

            var door = Box("OfficeDoor_ToScene", new Vector3(3.2f, 1.0f, 0.8f), new Vector3(0.1f, 2.0f, 1.0f), new Color(0.3f, 0.2f, 0.15f));
            AddMarker(door, DeskObjectKind.ToScene, false);

            if (CandlePrefab != null)
            {
                var candle = Instantiate(CandlePrefab, transform);
                candle.transform.localPosition = new Vector3(1.3f, 0.79f, 0.5f);
                candle.transform.localScale = Vector3.one * 1.5f;
            }

            Label(labels, "전화기", new Vector3(-1.05f, 1.35f, 0.05f));
            Label(labels, "컴퓨터", new Vector3(0.1f, 1.48f, 0.45f));
            Label(labels, "의뢰 보고서", new Vector3(0.95f, 0.95f, -0.25f));
            Label(labels, "타자기(최종 보고서)", new Vector3(-0.2f, 1.1f, -0.35f));
            Label(labels, "사건보드", new Vector3(0, 2.75f, 1.55f));
            Label(labels, "현장으로", new Vector3(3.1f, 2.2f, 0.8f));

            _ringIndicator = Box("RingIndicator", new Vector3(-1.05f, 1.6f, 0.05f), new Vector3(0.12f, 0.12f, 0.12f), new Color(1f, 0.25f, 0.2f));
            Destroy(_ringIndicator.GetComponent<Collider>());
            _ringIndicator.SetActive(false);

            var light = new GameObject("DeskLight").AddComponent<Light>();
            light.transform.SetParent(transform, false);
            light.type = LightType.Point;
            light.range = 8f;
            light.intensity = 3f;
            light.color = new Color(1f, 0.88f, 0.7f);
            light.transform.localPosition = new Vector3(0, 2.6f, -0.5f);

            CameraPose = new GameObject("DeskCameraPose").transform;
            CameraPose.SetParent(transform, false);
            CameraPose.localPosition = new Vector3(0, 2.45f, -2.6f);
            CameraPose.localRotation = Quaternion.Euler(24f, 0, 0);
        }

        public void SetRinging(bool ringing)
        {
            if (_ringIndicator == null) return;
            _ringIndicator.SetActive(ringing);
            if (ringing) _ringIndicator.transform.localScale = Vector3.one * (0.1f + Mathf.PingPong(Time.time * 0.2f, 0.06f));
        }

        private GameObject Box(string name, Vector3 local, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = local;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().material = BoardView.MakeMaterial(color);
            return go;
        }

        private static void AddMarker(GameObject go, DeskObjectKind kind, bool addBoundsCollider)
        {
            go.AddComponent<DeskObjectMarker>().Kind = kind;
            if (!addBoundsCollider) return;

            // 전화기 모델은 충돌체가 없어 렌더러 전체를 감싸는 클릭 상자를 붙인다(외형과 분리).
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            var box = go.AddComponent<BoxCollider>();
            box.center = go.transform.InverseTransformPoint(b.center);
            var s = go.transform.lossyScale;
            box.size = new Vector3(b.size.x / Mathf.Max(s.x, 1e-4f), b.size.y / Mathf.Max(s.y, 1e-4f), b.size.z / Mathf.Max(s.z, 1e-4f));
        }

        private void Label(BoardView labels, string text, Vector3 local)
        {
            var tm = labels.MakeLabel(transform, text, 0.09f, new Color(1f, 0.95f, 0.8f));
            tm.transform.localPosition = local;
            tm.gameObject.AddComponent<FaceCamera>();
            _labels.Add(tm.gameObject);
        }
    }
}
