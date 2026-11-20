using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Game;
using CaseFileOmission.NPC;
using CaseFileOmission.Police;
using CaseFileOmission.Rooms;
using UnityEngine;

namespace CaseFileOmission.Playtest
{
    /// <summary>방 바닥 클릭 판정용. 방 ID만 들고 있고 판정 로직은 없다.</summary>
    public sealed class RoomFloorMarker : MonoBehaviour
    {
        public string RoomId;
    }

    /// <summary>말(인물) 클릭 판정·이동 연출. 위치 계산 결과를 보여 주기만 하며 게임 상태를 바꾸지 않는다.</summary>
    public sealed class PieceView : MonoBehaviour
    {
        public string OccupantId;
        public string BodyKey;
        public TextMesh Label;
        public float Speed = TempLayoutV1.NpcMoveAnimationSpeed;
        /// <summary>참이면 이동을 마치거나 방을 벗어날 때 숨긴다(관찰 범위 밖으로 나간 인물).</summary>
        public bool HideOnArrive;

        private readonly Queue<Vector3> _path = new Queue<Vector3>();

        public bool IsMoving => _path.Count > 0;

        /// <summary>걷던 경로를 버린다(키보드로 직접 움직일 때).</summary>
        public void Stop()
        {
            _path.Clear();
            HideOnArrive = false;
        }

        public void MoveAlong(IEnumerable<Vector3> waypoints)
        {
            _path.Clear();
            HideOnArrive = false;
            foreach (var p in waypoints) _path.Enqueue(p);
        }

        public void Snap(Vector3 position)
        {
            _path.Clear();
            HideOnArrive = false;
            transform.position = position;
        }

        private void Update()
        {
            if (_path.Count == 0) return;
            var target = _path.Peek();
            // 걷는 방향을 바라본다(추적 카메라가 캐릭터 뒤쪽에 선다).
            var dir = target - transform.position;
            dir.y = 0;
            if (dir.sqrMagnitude > 0.0004f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 12f * Time.deltaTime);
            transform.position = Vector3.MoveTowards(transform.position, target, Speed * Time.deltaTime);
            if ((transform.position - target).sqrMagnitude < 0.0004f) _path.Dequeue();
            if (HideOnArrive && _path.Count <= 2)
            {
                // 문을 지나면(남은 경로가 문 바깥 지점·목적지뿐이면) 숨긴다.
                HideOnArrive = false;
                var end = _path.Count > 0 ? _path.Last() : transform.position;
                _path.Clear();
                transform.position = end;
                gameObject.SetActive(false);
            }
        }
    }

    /// <summary>방 하나에 놓을 소품(보유 모델) 배치 데이터. 좌표는 방 중심 기준 m.</summary>
    [Serializable]
    public sealed class PropPlacement
    {
        public GameObject Prefab;
        public string RoomId;
        public Vector3 LocalPosition;
        public float RotationY;
        public float Scale = 1f;
    }

    /// <summary>
    /// 3D 사건 현장 게임판(09 TEMP_LAYOUT_V1). 8개 방의 바닥·벽(공유 벽은 한 번만)·문 개구부·계단·소품·조사 지점을 만들고,
    /// 게임 상태의 인물 위치를 말로 보여 준다. 좌표·크기·외형은 TempLayoutV1/배치 데이터에서만 읽어 교체할 수 있게 한다.
    /// 벽은 판을 내려다보기 쉽게 낮게(시각 높이) 그리며, 공간 데이터상의 방 높이는 TempLayoutV1.RoomHeight(3 m)다.
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        public const float WallVisualHeight = 1.1f;
        private const float PieceTargetHeight = 1.0f;

        public Font LabelFont;
        public GameObject PlayerPrefab, PolicePrefab, CriminalPrefab;
        public GameObject[] NpcBodyPrefabs = new GameObject[5];
        [Tooltip("사망한 NPC(시신) 모델. 누운 자세로 만든 모델이라 따로 눕히지 않는다. 비우면 NPC_Body_03을 눕혀 대신한다.")]
        public GameObject VictimPrefab;
        public List<PropPlacement> Props = new List<PropPlacement>();

        private readonly Dictionary<string, PieceView> _pieces = new Dictionary<string, PieceView>();
        /// <summary>피해자로 공개된 인물에게 한 번 고른 몸체. 다른 인물의 역할 공개로 몸체가 바뀌지 않게 의뢰 동안 유지한다.</summary>
        private readonly Dictionary<string, string> _victimBodies = new Dictionary<string, string>();
        private string _victimBodiesContractId;
        private readonly Dictionary<string, Renderer> _floors = new Dictionary<string, Renderer>();
        private readonly Dictionary<string, Transform> _interactionPoints = new Dictionary<string, Transform>();
        private readonly Dictionary<string, Color> _floorColors = new Dictionary<string, Color>();
        private Transform _piecesRoot;
        private GameObject _sceneMarker;
        private bool _built;

        public IReadOnlyDictionary<string, Transform> InteractionPoints => _interactionPoints;

        public static Vector3 RoomCenter(string roomId)
        {
            var r = TempLayoutV1.GetRoom(roomId);
            return r == null ? Vector3.zero : new Vector3(r.CenterX, 0f, r.CenterZ);
        }

        public void Build()
        {
            if (_built) return;
            _built = true;

            var house = new GameObject("House_TEMP_LAYOUT_V1").transform;
            house.SetParent(transform, false);

            foreach (var room in TempLayoutV1.Rooms)
                BuildRoom(house, room);
            BuildWalls(house);
            BuildStairs(house);
            foreach (var prop in Props)
                PlaceProp(house, prop);

            _piecesRoot = new GameObject("Pieces").transform;
            _piecesRoot.SetParent(transform, false);
        }

        private void BuildRoom(Transform parent, RoomRect room)
        {
            var root = new GameObject(room.RoomId).transform;
            root.SetParent(parent, false);
            root.position = new Vector3(room.CenterX, 0, room.CenterZ);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(root, false);
            floor.transform.localScale = new Vector3(room.Width, 0.1f, room.Depth);
            floor.transform.localPosition = new Vector3(0, -0.05f, 0);
            floor.AddComponent<RoomFloorMarker>().RoomId = room.RoomId;
            var renderer = floor.GetComponent<Renderer>();
            var color = FloorColor(room.RoomId);
            renderer.material = MakeMaterial(color);
            _floors[room.RoomId] = renderer;
            _floorColors[room.RoomId] = color;

            // 조사 지점: 방 안 상호작용 기준점(고정 ID). 외형을 바꿔도 이 지점과 ID는 유지한다.
            var point = new GameObject("INTERACT_" + room.RoomId).transform;
            point.SetParent(root, false);
            point.localPosition = new Vector3(-room.Width * 0.25f, 0, -room.Depth * 0.2f);
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "Marker";
            marker.transform.SetParent(point, false);
            marker.transform.localScale = new Vector3(0.5f, 0.02f, 0.5f);
            marker.transform.localPosition = new Vector3(0, 0.02f, 0);
            Destroy(marker.GetComponent<Collider>());
            marker.GetComponent<Renderer>().material = MakeMaterial(new Color(0.9f, 0.8f, 0.3f));
            _interactionPoints[room.RoomId] = point;

            var label = MakeLabel(root, HouseLayoutFactory.GetDisplayName(room.RoomId), 0.4f, new Color(1, 1, 1, 0.85f));
            label.transform.localPosition = new Vector3(0, 0.05f, room.Depth * 0.5f - 0.6f);
            label.transform.localRotation = Quaternion.Euler(90, 0, 0);
        }

        /// <summary>방마다 네 변의 벽을 모아 같은 선 위의 벽은 합치고(공유 벽 1회 생성), 문 폭만큼 비운다.</summary>
        private void BuildWalls(Transform parent)
        {
            float t = TempLayoutV1.WallThickness;
            // key: (세로벽이면 'X', 가로벽이면 'Z') + 좌표
            var lines = new Dictionary<(char, float), List<(float from, float to)>>();
            void Add(char axis, float coord, float from, float to)
            {
                var key = (axis, Mathf.Round(coord * 100f) / 100f);
                if (!lines.TryGetValue(key, out var list)) lines[key] = list = new List<(float, float)>();
                list.Add((from, to));
            }

            foreach (var r in TempLayoutV1.Rooms)
            {
                Add('Z', r.MaxZ + t * 0.5f, r.MinX - t, r.MaxX + t);
                Add('Z', r.MinZ - t * 0.5f, r.MinX - t, r.MaxX + t);
                Add('X', r.MaxX + t * 0.5f, r.MinZ - t, r.MaxZ + t);
                Add('X', r.MinX - t * 0.5f, r.MinZ - t, r.MaxZ + t);
            }

            var wallMat = MakeMaterial(new Color(0.78f, 0.74f, 0.66f));
            var wallsRoot = new GameObject("Walls").transform;
            wallsRoot.SetParent(parent, false);

            foreach (var pair in lines)
            {
                var (axis, coord) = pair.Key;
                var merged = Merge(pair.Value);
                foreach (var door in TempLayoutV1.Doors)
                {
                    bool onLine = axis == 'X' ? Mathf.Abs(door.CenterX - coord) <= t * 0.5f + 0.01f
                                              : Mathf.Abs(door.CenterZ - coord) <= t * 0.5f + 0.01f;
                    if (!onLine) continue;
                    float along = axis == 'X' ? door.CenterZ : door.CenterX;
                    merged = Subtract(merged, along - TempLayoutV1.DoorWidth * 0.5f, along + TempLayoutV1.DoorWidth * 0.5f);
                }

                foreach (var (from, to) in merged)
                {
                    if (to - from < 0.01f) continue;
                    var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    wall.name = $"Wall_{axis}{coord:0.00}";
                    wall.transform.SetParent(wallsRoot, false);
                    float mid = (from + to) * 0.5f, len = to - from;
                    wall.transform.position = axis == 'X'
                        ? new Vector3(coord, WallVisualHeight * 0.5f, mid)
                        : new Vector3(mid, WallVisualHeight * 0.5f, coord);
                    wall.transform.localScale = axis == 'X'
                        ? new Vector3(t, WallVisualHeight, len)
                        : new Vector3(len, WallVisualHeight, t);
                    wall.GetComponent<Renderer>().material = wallMat;
                }
            }

            var doorMat = MakeMaterial(new Color(0.45f, 0.30f, 0.18f));
            foreach (var door in TempLayoutV1.Doors)
            {
                // 문턱 표시(통행을 막지 않는 얇은 판)
                var sill = GameObject.CreatePrimitive(PrimitiveType.Cube);
                sill.name = door.DoorId;
                sill.transform.SetParent(wallsRoot, false);
                sill.transform.position = new Vector3(door.CenterX, 0.01f, door.CenterZ);
                bool vertical = IsVerticalWallDoor(door);
                sill.transform.localScale = vertical ? new Vector3(t + 0.1f, 0.02f, TempLayoutV1.DoorWidth) : new Vector3(TempLayoutV1.DoorWidth, 0.02f, t + 0.1f);
                Destroy(sill.GetComponent<Collider>());
                sill.GetComponent<Renderer>().material = doorMat;
            }
        }

        private static bool IsVerticalWallDoor(DoorLayout door)
        {
            var a = TempLayoutV1.GetRoom(door.RoomA);
            return door.CenterX >= a.MaxX - 0.01f || door.CenterX <= a.MinX + 0.01f;
        }

        private static List<(float, float)> Merge(List<(float from, float to)> spans)
        {
            var sorted = spans.OrderBy(s => s.from).ToList();
            var result = new List<(float, float)>();
            foreach (var s in sorted)
            {
                if (result.Count > 0 && s.from <= result[result.Count - 1].Item2 + 0.001f)
                    result[result.Count - 1] = (result[result.Count - 1].Item1, Mathf.Max(result[result.Count - 1].Item2, s.to));
                else
                    result.Add((s.from, s.to));
            }
            return result;
        }

        private static List<(float, float)> Subtract(List<(float, float)> spans, float cutFrom, float cutTo)
        {
            var result = new List<(float, float)>();
            foreach (var (from, to) in spans)
            {
                if (cutTo <= from || cutFrom >= to) { result.Add((from, to)); continue; }
                if (cutFrom > from) result.Add((from, cutFrom));
                if (cutTo < to) result.Add((cutTo, to));
            }
            return result;
        }

        private void BuildStairs(Transform parent)
        {
            var s = TempLayoutV1.StairsInHall;
            var root = new GameObject("Stairs_TEMP (현관 홀 오브젝트)").transform;
            root.SetParent(parent, false);
            var mat = MakeMaterial(new Color(0.42f, 0.28f, 0.2f));
            const int steps = 6;
            for (int i = 0; i < steps; i++)
            {
                var step = GameObject.CreatePrimitive(PrimitiveType.Cube);
                step.name = "Step" + i;
                step.transform.SetParent(root, false);
                float depth = s.Depth / steps;
                float h = 0.15f * (i + 1);
                step.transform.position = new Vector3(s.CenterX, h * 0.5f, s.MinZ + depth * (i + 0.5f));
                step.transform.localScale = new Vector3(s.Width, h, depth);
                step.GetComponent<Renderer>().material = mat;
            }
        }

        private void PlaceProp(Transform parent, PropPlacement p)
        {
            if (p.Prefab == null || TempLayoutV1.GetRoom(p.RoomId) == null) return;
            var go = Instantiate(p.Prefab, parent);
            go.name = $"PROP_{p.RoomId}_{p.Prefab.name}";
            go.transform.position = RoomCenter(p.RoomId) + p.LocalPosition;
            go.transform.rotation = Quaternion.Euler(0, p.RotationY, 0);
            go.transform.localScale = Vector3.one * p.Scale;
            foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
        }

        // ───────────────────────── 인물 말 ─────────────────────────

        /// <summary>게임 상태와 말을 맞춘다. animate가 참이면 문을 거쳐 걸어가고, 아니면 바로 놓는다.</summary>
        public void SyncPieces(GameSession s, bool animate)
        {
            if (!_built) Build();
            var wanted = new Dictionary<string, (string room, string body, string label, float speed, bool lying, string face)>();

            if (s.CurrentContract != null)
            {
                if (s.PlayerRoom != null)
                    wanted[s.PlayerId] = (s.PlayerRoom, "player", "나", TempLayoutV1.PlayerMoveAnimationSpeed, false, null);

                var bodies = BodyKeys(s, s.Npcs.GetByContract(s.CurrentContract.ContractId).Where(n => s.Occupancy.HasPosition(n.NpcId)).ToList());
                foreach (var npc in s.Npcs.GetByContract(s.CurrentContract.ContractId))
                {
                    if (!s.Occupancy.HasPosition(npc.NpcId)) continue;
                    // 시신은 전용 모델(VictimPrefab)로 보인다. 모델이 없을 때만 서 있던 몸체를 눕혀 대신한다.
                    bool corpseModel = !npc.IsAlive && VictimPrefab != null;
                    wanted[npc.NpcId] = (s.Occupancy.GetRoom(npc.NpcId), corpseModel ? "victim" : bodies[npc.NpcId],
                        $"{PlaytestTexts.TempName(s, npc.NpcId)}\n{PlaytestTexts.PublicRole(s, npc)}", TempLayoutV1.NpcMoveAnimationSpeed,
                        !npc.IsAlive && !corpseModel, corpseModel ? null : PlaytestTexts.FaceObject(npc.Personality));
                }

                foreach (var police in PoliceRoster.AllPoliceIds)
                {
                    if (!s.Occupancy.HasPosition(police)) continue;
                    wanted[police] = (s.Occupancy.GetRoom(police), "police", PlaytestTexts.PoliceName(police),
                        TempLayoutV1.NpcMoveAnimationSpeed, !s.PoliceLife.IsAlive(police), PlaytestTexts.PoliceFaceObject(police));
                }
            }

            foreach (var id in _pieces.Keys.Where(k => !wanted.ContainsKey(k)).ToList())
            {
                Destroy(_pieces[id].gameObject);
                _pieces.Remove(id);
            }

            var byRoom = wanted.GroupBy(w => w.Value.room).ToDictionary(g => g.Key, g => g.Select(x => x.Key).OrderBy(x => x).ToList());
            foreach (var pair in wanted)
            {
                var (room, body, label, speed, lying, face) = pair.Value;
                if (!_pieces.TryGetValue(pair.Key, out var piece) || piece.BodyKey != body)
                {
                    Vector3? old = piece != null ? piece.transform.position : (Vector3?)null;
                    if (piece != null) Destroy(piece.gameObject);
                    piece = CreatePiece(pair.Key, body);
                    AttachFace(piece, face);
                    _pieces[pair.Key] = piece;
                    if (old.HasValue) piece.transform.position = old.Value;
                    else piece.Snap(SlotPosition(room, byRoom[room].IndexOf(pair.Key)));
                }

                piece.Label.text = label;
                piece.Speed = speed;
                piece.transform.GetChild(0).localRotation = lying ? Quaternion.Euler(0, 0, 90) : Quaternion.identity;

                var target = SlotPosition(room, byRoom[room].IndexOf(pair.Key));
                // 플레이어 말은 WASD로 자유롭게 걷는다 — 그 방 안(문턱 포함)에 있거나 걷는 중이면 자리 배치로 옮기지 않는다.
                if (pair.Key == s.PlayerId && (piece.IsMoving || IsInsideRoom(piece.transform.position, room, 0.4f))) continue;
                bool visible = pair.Key == s.PlayerId || IsObservable(s, room, target, byRoom);
                bool wasVisible = piece.gameObject.activeSelf;
                string fromRoom = RoomAt(piece.transform.position);
                if (visible && !wasVisible)
                {
                    // 시야에 새로 들어온 인물은 보이지 않던 동안의 이동을 걸어서 보여 주지 않고, 지금 자리에서 나타난다.
                    piece.Snap(target);
                    piece.gameObject.SetActive(true);
                }
                else if (!visible && wasVisible && animate && fromRoom != null && fromRoom != room)
                {
                    // 보고 있던 인물이 방을 나가면 문까지 걸어간 뒤 사라진다.
                    piece.MoveAlong(PathBetween(fromRoom, room, target));
                    piece.HideOnArrive = true;
                }
                else if (!visible)
                {
                    piece.Snap(target);
                    if (wasVisible && !piece.IsMoving) piece.gameObject.SetActive(false);
                }
                else if (!animate || fromRoom == null)
                    piece.Snap(target);
                else if ((piece.transform.position - target).sqrMagnitude > 0.01f)
                    piece.MoveAlong(PathBetween(fromRoom, room, target));
            }
        }

        private bool IsObservable(GameSession s, string room, Vector3 target, Dictionary<string, List<string>> byRoom)
        {
            string here = s.PlayerRoom;
            if (here == null || room != here) return false;
            var me = _pieces.TryGetValue(s.PlayerId, out var p) ? p.transform.position : SlotPosition(here, byRoom[here].IndexOf(s.PlayerId));
            return (me - target).sqrMagnitude <= ObservationRange * ObservationRange;
        }

        // ───────────────────────── 자유 이동·상호작용(탐사 조작) ─────────────────────────

        public Transform PlayerPiece(GameSession s) => s != null && _pieces.TryGetValue(s.PlayerId, out var p) ? p.transform : null;

        /// <summary>바닥 클릭 지점으로 플레이어를 걷게 한다. 다른 방이면 문을 거쳐 간다(방 이동 판정은 호출자가 먼저 한다).</summary>
        public void WalkPlayerTo(GameSession s, Vector3 point, string fromRoom)
        {
            if (!_pieces.TryGetValue(s.PlayerId, out var piece)) return;
            string toRoom = RoomAt(point) ?? s.PlayerRoom;
            point.y = 0;
            var r = TempLayoutV1.GetRoom(toRoom);
            if (r != null)
                point = new Vector3(Mathf.Clamp(point.x, r.MinX + 0.35f, r.MaxX - 0.35f), 0, Mathf.Clamp(point.z, r.MinZ + 0.35f, r.MaxZ - 0.35f));
            if (fromRoom == null || fromRoom == toRoom) piece.MoveAlong(new[] { point });
            else piece.MoveAlong(PathBetween(fromRoom, toRoom, point));
        }

        public static string RoomAtPoint(Vector3 p) => RoomAt(p);

        private readonly Dictionary<string, Transform> _objectMarkers = new Dictionary<string, Transform>();
        private string _objectsContractId;

        /// <summary>
        /// 이 의뢰의 조사 오브젝트를 방 안에 표시한다(임시 도형+이름, TEST_TEMP). 방 벽을 따라 고정 순서로 늘어놓으며
        /// 플레이어가 상호작용 거리(1.5 m) 안으로 다가가 E로 조사한다.
        /// </summary>
        public void SyncObjects(GameSession s)
        {
            string cid = s.CurrentContract?.ContractId;
            if (cid != _objectsContractId)
            {
                foreach (var t in _objectMarkers.Values) if (t != null) Destroy(t.gameObject);
                _objectMarkers.Clear();
                _objectsContractId = cid;
                if (cid != null)
                {
                    foreach (var group in s.GetInvestigationObjects().GroupBy(o => o.RoomId))
                    {
                        var list = group.ToList();
                        for (int i = 0; i < list.Count; i++)
                            _objectMarkers[list[i].ObjectId] = MakeObjectMarker(list[i].ObjectId, list[i].Name, ObjectPosition(group.Key, i, list.Count));
                    }
                }
            }
            if (cid == null) return;
            foreach (var pair in _objectMarkers)
            {
                bool done = s.InvestigatedObjectIds.Contains(pair.Key);
                var r = pair.Value.GetComponentInChildren<Renderer>();
                if (r != null) r.material.color = done ? new Color(0.45f, 0.45f, 0.45f) : new Color(0.95f, 0.75f, 0.25f);
            }
        }

        private static Vector3 ObjectPosition(string roomId, int index, int count)
        {
            var r = TempLayoutV1.GetRoom(roomId);
            if (r == null) return Vector3.zero;
            // 방 둘레(벽 안쪽 0.5 m)를 일정 간격으로 나눠 놓는다. 문 앞을 피하려고 반 칸 어긋나게 시작한다.
            float w = r.Width - 1f, d = r.Depth - 1f;
            float perimeter = 2 * (w + d);
            float s = perimeter * (index + 0.5f) / Mathf.Max(1, count);
            float x0 = r.MinX + 0.5f, z0 = r.MinZ + 0.5f;
            if (s < w) return new Vector3(x0 + s, 0, z0 + d);
            s -= w;
            if (s < d) return new Vector3(x0 + w, 0, z0 + d - s);
            s -= d;
            if (s < w) return new Vector3(x0 + w - s, 0, z0);
            s -= w;
            return new Vector3(x0, 0, z0 + s);
        }

        private Transform MakeObjectMarker(string objectId, string name, Vector3 position)
        {
            var root = new GameObject("OBJ_" + objectId).transform;
            root.SetParent(_piecesRoot, false);
            root.position = position;
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Shape";
            box.transform.SetParent(root, false);
            box.transform.localScale = new Vector3(0.35f, 0.35f, 0.35f);
            box.transform.localPosition = new Vector3(0, 0.18f, 0);
            Destroy(box.GetComponent<Collider>());
            box.GetComponent<Renderer>().material = MakeMaterial(new Color(0.95f, 0.75f, 0.25f));
            var label = MakeLabel(root, name, 0.08f, new Color(1f, 0.95f, 0.75f));
            label.transform.localPosition = new Vector3(0, 0.7f, 0);
            label.gameObject.AddComponent<FaceCamera>();
            // 09 §4: 이름은 가까이 갔을 때만 FieldHud가 이름 띠로 보인다. 판 위 상시 이름표는 끈다.
            label.gameObject.SetActive(false);
            return root;
        }

        public enum InteractableKind { None, Object, Npc, Police }

        /// <summary>같은 방에서 보이는 대상 하나(조사 오브젝트·살아 있는 NPC·살아 있는 경찰)와 플레이어까지의 수평 거리.</summary>
        public readonly struct Nearby
        {
            public readonly InteractableKind Kind;
            public readonly string Id;
            public readonly Vector3 Position;
            public readonly float Distance;

            public Nearby(InteractableKind kind, string id, Vector3 position, float distance)
            {
                Kind = kind; Id = id; Position = position; Distance = distance;
            }

            /// <summary>상호작용 거리(09 TEMP_LAYOUT_V1): 물체 1.5 m, 대화 2.0 m.</summary>
            public float InteractRange => Kind == InteractableKind.Object ? TempLayoutV1.InteractionRange : TempLayoutV1.ConversationRange;
        }

        /// <summary>플레이어와 같은 방에 있고 range 안에 있는 대상들. 벽 너머·관찰 범위 밖 인물·사망자는 넣지 않는다.</summary>
        public List<Nearby> NearbyTargets(GameSession s, float range, bool includeDead = false)
        {
            var list = new List<Nearby>();
            if (s?.CurrentContract == null || !_pieces.TryGetValue(s.PlayerId, out var me)) return list;
            var here = RoomAt(me.transform.position);
            if (here == null) return list;
            var mine = me.transform.position;
            foreach (var pair in _objectMarkers)
            {
                if (pair.Value == null || ContractContent.CaseCatalog.ObjectRoom(pair.Key) != here) continue;
                float dist = Flat(pair.Value.position - mine);
                if (dist <= range) list.Add(new Nearby(InteractableKind.Object, pair.Key, pair.Value.position, dist));
            }
            foreach (var pair in _pieces)
            {
                if (pair.Key == s.PlayerId || !pair.Value.gameObject.activeSelf) continue;
                InteractableKind kind;
                if (s.Npcs.Exists(pair.Key)) { if (!includeDead && !s.Npcs.Get(pair.Key).IsAlive) continue; kind = InteractableKind.Npc; }
                else if (PoliceRoster.AllPoliceIds.Contains(pair.Key)) { if (!includeDead && !s.PoliceLife.IsAlive(pair.Key)) continue; kind = InteractableKind.Police; }
                else continue;
                if (RoomAt(pair.Value.transform.position) != here) continue;
                float dist = Flat(pair.Value.transform.position - mine);
                if (dist <= range) list.Add(new Nearby(kind, pair.Key, pair.Value.transform.position, dist));
            }
            return list;
        }

        /// <summary>
        /// 플레이어가 상호작용 거리 안에 있는 가장 가까운 대상(09 TEMP_LAYOUT_V1: 조사 1.5 m, 대화 2.0 m).
        /// 같은 방의 조사 오브젝트, 보이는 살아 있는 NPC·경찰만 고른다.
        /// </summary>
        public InteractableKind FindInteractable(GameSession s, out string id)
        {
            id = null;
            var best = NearbyTargets(s, TempLayoutV1.ConversationRange)
                .Where(n => n.Distance <= n.InteractRange)
                .Where(n => n.Kind != InteractableKind.Police || n.Id == s.HandlerPoliceId)
                .OrderBy(n => n.Distance)
                .FirstOrDefault();
            if (best.Kind == InteractableKind.None) return InteractableKind.None;
            id = best.Id;
            return best.Kind;
        }

        /// <summary>그 인물 말이 지금 걷는 중인지(사격 '빠른 표적' 임시 판정).</summary>
        public bool IsPieceMoving(string occupantId) => _pieces.TryGetValue(occupantId, out var p) && p.IsMoving;

        /// <summary>두 인물 사이 직선에 다른 인물 말이 끼어 있는지(사격 '부분 가림' 임시 판정, 반경 0.35 m).</summary>
        public bool IsLineBlocked(string fromId, string toId)
        {
            if (!_pieces.TryGetValue(fromId, out var a) || !_pieces.TryGetValue(toId, out var b)) return false;
            Vector3 p0 = a.transform.position, p1 = b.transform.position;
            p0.y = p1.y = 0;
            var seg = p1 - p0;
            float len2 = seg.sqrMagnitude;
            if (len2 < 1e-4f) return false;
            foreach (var pair in _pieces)
            {
                if (pair.Key == fromId || pair.Key == toId || !pair.Value.gameObject.activeSelf) continue;
                var q = pair.Value.transform.position; q.y = 0;
                float t = Mathf.Clamp01(Vector3.Dot(q - p0, seg) / len2);
                if (t <= 0f || t >= 1f) continue;
                if ((p0 + seg * t - q).sqrMagnitude <= 0.35f * 0.35f) return true;
            }
            return false;
        }

        public Vector3? PiecePosition(string occupantId) => _pieces.TryGetValue(occupantId, out var p) ? p.transform.position : (Vector3?)null;
        public Vector3? ObjectPosition(string objectId) => _objectMarkers.TryGetValue(objectId, out var t) && t != null ? t.position : (Vector3?)null;

        public static bool IsInsideRoom(Vector3 p, string roomId, float margin)
        {
            var r = TempLayoutV1.GetRoom(roomId);
            return r != null && r.Contains(p.x, p.z, -margin);
        }

        /// <summary>문턱(공유 벽 두께)에서 방이 엇갈려 판정되지 않게, 방 안쪽으로 확실히 들어왔을 때만 그 방을 돌려준다.</summary>
        public static string RoomStrictlyAt(Vector3 p) =>
            TempLayoutV1.Rooms.FirstOrDefault(r => r.Contains(p.x, p.z, 0.05f))?.RoomId;

        private static float Flat(Vector3 v) { v.y = 0; return v.magnitude; }

        private readonly List<GameObject> _exchangeMarks = new List<GameObject>();
        private readonly HashSet<string> _talking = new HashSet<string>();

        /// <summary>지금 다른 NPC와 대화 중인(보이는) 인물. 09 §2 대화 상태 아이콘을 띄울 대상.</summary>
        public IReadOnlyCollection<string> TalkingIds => _talking;

        /// <summary>
        /// 08·09: 지금 같은 방에서 NPC끼리 대화 중이면 두 인물을 대화 상태로 표시하고(아이콘은 FieldHud가 그린다), 방 벽 쪽에 귀(엿듣기 가능) 표시를 띄운다.
        /// 보이지 않는 인물의 대화는 표시하지 않는다. 대화 내용은 엿듣기 전에는 보여 주지 않는다.
        /// </summary>
        public void ShowExchanges(GameSession s)
        {
            foreach (var go in _exchangeMarks) if (go != null) Destroy(go);
            _exchangeMarks.Clear();
            _talking.Clear();
            if (s.CurrentContract == null || s.PlayerRoom == null) return;

            bool any = false;
            foreach (var e in s.CurrentOverhearableExchanges())
            {
                if (!IsPieceVisible(e.SpeakerId) || !IsPieceVisible(e.ListenerId)) continue;
                any = true;
                _talking.Add(e.SpeakerId);
                _talking.Add(e.ListenerId);
            }
            if (!any) return;
            var r = TempLayoutV1.GetRoom(s.PlayerRoom);
            var ear = MakeLabel(transform, "[귀] 엿듣기 가능", 0.13f, new Color(0.6f, 0.9f, 1f));
            ear.transform.position = new Vector3(r.CenterX - r.Width * 0.5f + 0.4f, 1.4f, r.CenterZ + r.Depth * 0.5f - 0.3f);
            ear.gameObject.AddComponent<FaceCamera>();
            _exchangeMarks.Add(ear.gameObject);
        }

        /// <summary>지금 화면에 보이는 인물인지(관찰 범위 안). 클릭·말풍선 표시에 쓴다.</summary>
        public bool IsPieceVisible(string occupantId) => _pieces.TryGetValue(occupantId, out var p) && p.gameObject.activeSelf;

        /// <summary>플레이어 말을 지금 방의 조사 지점(접근 거리 안)으로 걸어가게 한다(연출).</summary>
        public void WalkPlayerToInteractionPoint(string playerId, string roomId)
        {
            if (!_pieces.TryGetValue(playerId, out var piece) || !_interactionPoints.TryGetValue(roomId, out var point)) return;
            var target = point.position + new Vector3(TempLayoutV1.InteractionRange * 0.5f, 0, 0);
            piece.MoveAlong(new[] { target });
        }

        public bool IsAnyPieceMoving => _pieces.Values.Any(p => p.IsMoving);

        private IEnumerable<Vector3> PathBetween(string fromRoom, string toRoom, Vector3 target)
        {
            var graph = HouseLayoutFactory.CreateStandardHouse();
            var path = graph.FindPath(fromRoom, toRoom) ?? Array.Empty<string>();
            string prev = fromRoom;
            foreach (var next in path)
            {
                var door = TempLayoutV1.FindDoor(prev, next);
                if (door != null)
                {
                    var c = new Vector3(door.CenterX, 0, door.CenterZ);
                    var toward = (RoomCenter(next) - c);
                    toward.y = 0;
                    yield return c - toward.normalized * 0.6f;
                    yield return c + toward.normalized * 0.6f;
                }
                prev = next;
            }
            yield return target;
        }

        private static string RoomAt(Vector3 p) =>
            TempLayoutV1.Rooms.FirstOrDefault(r => r.Contains(p.x, p.z, -0.15f))?.RoomId;

        private Vector3 SlotPosition(string roomId, int index)
        {
            var r = TempLayoutV1.GetRoom(roomId);
            if (r == null) return Vector3.zero;
            // 방 가운데에서 시계 방향으로 둘러선다. 방이 좁은 복도면 세로로 늘어선다.
            if (r.Width < 3f)
                return new Vector3(r.CenterX, 0, r.CenterZ - r.Depth * 0.3f + index * 1.1f);
            float radius = Mathf.Min(r.Width, r.Depth) * 0.22f;
            float angle = index * 55f * Mathf.Deg2Rad;
            return new Vector3(r.CenterX + 0.6f + Mathf.Cos(angle) * radius, 0, r.CenterZ + Mathf.Sin(angle) * radius);
        }

        /// <summary>
        /// 공개 역할 기준 몸체(08: 사건 관련 인물 2종·목격자·가해자). 피해자 몸체는 BodyKeys가 고른다. 실제 가해자라도 공식 지목 전에는 가해자 몸체를 쓰지 않는다.
        /// </summary>
        private static string BodyKey(GameSession s, NpcState npc)
        {
            // 07 2026-11-12: 몸체는 실제 역할이 아닌 공개 역할(public_role)만 따른다. '사건 관련 인물'은 인물 코드 해시로
            // NPC_Body_00/01 중 하나를 고르며 실제 가해자도 같은 규칙을 쓴다(몸체만 보고 가해자를 알 수 없게).
            return s.GetPublicRole(npc.NpcId) switch
            {
                PublicRole.Perpetrator => "criminal",
                PublicRole.Witness => "npc2",
                _ => GameSession.StableSeed("body:" + npc.NpcId) % 2 == 0 ? "npc0" : "npc1"
            };
        }

        /// <summary>한 몸체를 함께 쓸 수 있는 최대 인원(사용자 지정 2026-11-20 — 얼굴 오브젝트로 구분하므로 몸체는 겹쳐도 된다).</summary>
        private const int MaxSameBody = 2;

        /// <summary>
        /// 서 있는 인물들의 몸체. 피해자로 공개된 살아 있는 인물은 고정 몸체 대신 NPC_Body_00~04 중에서 고른다(사용자 지정 2026-11-20).
        /// 이미 2명이 쓰는 몸체는 피하고, 고른 몸체는 의뢰 동안 유지한다. 다른 인물의 몸체 규칙은 그대로 둔다.
        /// </summary>
        private Dictionary<string, string> BodyKeys(GameSession s, IReadOnlyList<NpcState> npcs)
        {
            if (_victimBodiesContractId != s.CurrentContract.ContractId)
            {
                _victimBodies.Clear();
                _victimBodiesContractId = s.CurrentContract.ContractId;
            }

            var keys = new Dictionary<string, string>();
            var victims = new List<NpcState>();
            foreach (var npc in npcs)
            {
                if (s.GetPublicRole(npc.NpcId) == PublicRole.Victim) victims.Add(npc);
                else keys[npc.NpcId] = BodyKey(s, npc);
            }

            foreach (var victim in victims.OrderBy(n => _victimBodies.ContainsKey(n.NpcId) ? 0 : 1).ThenBy(n => n.NpcId, System.StringComparer.Ordinal))
            {
                if (!_victimBodies.TryGetValue(victim.NpcId, out var body))
                {
                    var all = Enumerable.Range(0, NpcBodyPrefabs.Length).Select(i => "npc" + i).ToList();
                    // 시신(전용 모델)은 서 있는 몸체를 쓰지 않으므로 세지 않는다.
                    var standing = keys.Where(k => npcs.First(n => n.NpcId == k.Key).IsAlive || VictimPrefab == null).Select(k => k.Value).ToList();
                    var open = all.Where(b => standing.Count(x => x == b) < MaxSameBody).ToList();
                    if (open.Count == 0) open = all;
                    body = open[GameSession.StableSeed("victim-body:" + victim.NpcId) % open.Count];
                    _victimBodies[victim.NpcId] = body;
                }
                keys[victim.NpcId] = body;
            }
            return keys;
        }

        /// <summary>08 관찰 범위: 플레이어와 같은 방, 8 m 안의 인물만 보인다. 플레이어 자신은 항상 보인다.</summary>
        public const float ObservationRange = 8f;

        private PieceView CreatePiece(string id, string body)
        {
            var root = new GameObject("PIECE_" + id).transform;
            root.SetParent(_piecesRoot, false);
            var piece = root.gameObject.AddComponent<PieceView>();
            piece.OccupantId = id;
            piece.BodyKey = body;

            var prefab = body switch
            {
                "player" => PlayerPrefab,
                "police" => PolicePrefab,
                "criminal" => CriminalPrefab,
                "victim" => VictimPrefab,
                _ => NpcBodyPrefabs.ElementAtOrDefault(body[3] - '0')
            };

            var holder = new GameObject("Model").transform;
            holder.SetParent(root, false);
            GameObject model;
            if (prefab != null && body == "victim")
            {
                // 누운 모델: 가장 긴 수평 길이를 눕힌 말(서 있는 NPC 높이)과 맞추고, 말 위치가 몸 가운데가 되게 옮긴다.
                model = Instantiate(prefab, holder);
                var bounds = RenderBounds(model);
                float length = Mathf.Max(bounds.size.x, bounds.size.z);
                float scale = length > 1e-5f ? PieceTargetHeight * 0.8f / length : 1f;
                model.transform.localScale *= scale;
                bounds = RenderBounds(model);
                model.transform.position += new Vector3(holder.position.x - bounds.center.x, holder.position.y - bounds.min.y, holder.position.z - bounds.center.z);
                // 시신마다 누운 방향을 다르게(인물 ID로 고정 — 다시 그려도 같은 방향).
                model.transform.RotateAround(holder.position, Vector3.up, GameSession.StableSeed("corpse-yaw:" + id) % 360);
            }
            else if (prefab != null)
            {
                model = Instantiate(prefab, holder);
                var bounds = RenderBounds(model);
                float baseHeight = body == "player" || body == "criminal" ? PieceTargetHeight : PieceTargetHeight * 0.8f;
                float scale = bounds.size.y > 1e-5f ? baseHeight / bounds.size.y : 1f;
                model.transform.localScale *= scale;
            }
            else
            {
                model = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                model.transform.SetParent(holder, false);
                model.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
                model.transform.localPosition = new Vector3(0, 0.5f, 0);
            }
            foreach (var c in model.GetComponentsInChildren<Collider>()) Destroy(c);

            // 클릭 판정용 충돌체(외형과 분리)
            var col = root.gameObject.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0, 0.55f, 0);
            col.height = 1.1f;
            col.radius = 0.35f;

            // 캐릭터 뒤 45도 추적 카메라(가까운 거리)에 맞춘 이름표 크기.
            piece.Label = MakeLabel(root, id, 0.1f, Color.white);
            piece.Label.transform.localPosition = new Vector3(0, 1.35f, 0);
            piece.Label.gameObject.AddComponent<FaceCamera>();
            // 09 §4: 인물 이름은 접근했을 때만 FieldHud가 이름 띠로 보인다. 판 위 상시 이름표는 끈다.
            piece.Label.gameObject.SetActive(false);
            return piece;
        }

        /// <summary>
        /// 08 얼굴 오브젝트: 말의 머리 자리에 성격(경찰은 유형)을 나타내는 물건을 둔다. 정식 모델이 없어 임시 도형(색은 오브젝트별 고정)으로 대신한다(TEST_TEMP).
        /// </summary>
        private void AttachFace(PieceView piece, string face)
        {
            if (string.IsNullOrEmpty(face)) return;
            var head = GameObject.CreatePrimitive(FaceShape(face));
            head.name = "FaceObject_" + face;
            Destroy(head.GetComponent<Collider>());
            head.transform.SetParent(piece.transform.GetChild(0), false);
            head.transform.localPosition = new Vector3(0, PieceTargetHeight * 0.8f + 0.12f, 0);
            head.transform.localScale = Vector3.one * 0.22f;
            head.GetComponent<Renderer>().material = MakeMaterial(FaceColor(face));
            // 오브젝트 이름은 판 위에 띄우지 않는다(좁은 방에서 인물 이름표와 겹침). 말을 클릭한 행동 아이콘 창에 표시한다.
        }

        private static PrimitiveType FaceShape(string face) => face switch
        {
            "눈알" or "조개껍데기" or "입" or "생선" => PrimitiveType.Sphere,
            "자물쇠" or "수첩" or "깨진 거울" or "철제 덫" => PrimitiveType.Cube,
            "한쪽만 촛농이 흘러내린 양초" or "자석" or "왕관" or "나침반" or "돋보기" => PrimitiveType.Cylinder,
            _ => PrimitiveType.Capsule
        };

        private static Color FaceColor(string face) =>
            Color.HSVToRGB((GameSession.StableSeed("face:" + face) % 360) / 360f, 0.55f, 0.9f);

        private static Bounds RenderBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        // ───────────────────────── 표시 ─────────────────────────

        /// <summary>플레이어 방·이동 가능한 방·사건 현장을 바닥 색으로 표시한다.</summary>
        public void HighlightRooms(GameSession s)
        {
            string here = s.PlayerRoom;
            foreach (var pair in _floors)
            {
                var c = _floorColors[pair.Key];
                if (pair.Key == here) c = Color.Lerp(c, new Color(0.95f, 0.85f, 0.4f), 0.45f);
                else if (here != null && s.Rooms.RoomExists(pair.Key) && s.Rooms.AreConnected(here, pair.Key))
                    c = Color.Lerp(c, new Color(0.4f, 0.75f, 0.45f), 0.35f);
                pair.Value.material.color = c;
            }

            string scene = s.CurrentContract != null ? s.Campaign.SceneRoomId : null;
            if (scene == null)
            {
                if (_sceneMarker != null) _sceneMarker.SetActive(false);
                return;
            }
            if (_sceneMarker == null)
            {
                _sceneMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                _sceneMarker.name = "SceneMarker";
                Destroy(_sceneMarker.GetComponent<Collider>());
                _sceneMarker.transform.SetParent(transform, false);
                _sceneMarker.transform.localScale = new Vector3(1.6f, 0.01f, 1.6f);
                _sceneMarker.GetComponent<Renderer>().material = MakeMaterial(new Color(0.7f, 0.1f, 0.1f));
            }
            _sceneMarker.SetActive(true);
            var r = TempLayoutV1.GetRoom(scene);
            _sceneMarker.transform.position = new Vector3(r.CenterX - r.Width * 0.25f, 0.015f, r.CenterZ + r.Depth * 0.2f);
        }

        public TextMesh MakeLabel(Transform parent, string text, float size, Color color)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.font = LabelFont;
            tm.fontSize = 48;
            tm.characterSize = size * 0.2f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            if (LabelFont != null) go.GetComponent<MeshRenderer>().material = LabelFont.material;
            return tm;
        }

        private static Color FloorColor(string roomId) => roomId switch
        {
            HouseLayoutFactory.EntranceHall => new Color(0.55f, 0.50f, 0.45f),
            HouseLayoutFactory.Reception => new Color(0.50f, 0.36f, 0.30f),
            HouseLayoutFactory.Dining => new Color(0.46f, 0.40f, 0.30f),
            HouseLayoutFactory.Kitchen => new Color(0.62f, 0.62f, 0.58f),
            HouseLayoutFactory.PantryUtility => new Color(0.52f, 0.50f, 0.44f),
            HouseLayoutFactory.CorridorStairs => new Color(0.40f, 0.33f, 0.28f),
            HouseLayoutFactory.Bedroom => new Color(0.38f, 0.42f, 0.50f),
            HouseLayoutFactory.Study => new Color(0.30f, 0.38f, 0.32f),
            _ => Color.gray
        };

        private static Material _baseMaterial;

        public static Material MakeMaterial(Color color)
        {
            if (_baseMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                _baseMaterial = new Material(shader);
            }
            var m = new Material(_baseMaterial) { color = color };
            return m;
        }
    }

    /// <summary>라벨이 항상 카메라를 보게 한다.</summary>
    public sealed class FaceCamera : MonoBehaviour
    {
        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, cam.transform.up);
        }
    }
}
