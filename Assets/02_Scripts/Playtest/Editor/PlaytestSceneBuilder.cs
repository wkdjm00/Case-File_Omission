using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Props;
using CaseFileOmission.Rooms;
using CaseFileOmission.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CaseFileOmission.Playtest.Editor
{
    /// <summary>
    /// P0 테스트 씬(Assets/01_Scenes/P0_Playtest.unity)을 만든다. 보유 모델(말 8종·전화기·가구·소품)을 연결하고
    /// 방별 소품 배치(TEST_TEMP)를 데이터로 넣는다. 다시 실행하면 씬을 새로 만든다.
    /// </summary>
    public static class PlaytestSceneBuilder
    {
        public const string ScenePath = "Assets/01_Scenes/P0_Playtest.unity";

        private const string Pieces = "Assets/00_Art/00_Prefabs/Game_Piece/";
        private const string Common = "Assets/00_Art/09_export/Prefabs/MapDesigne/공용/";
        private const string Sweet = "Assets/00_Art/09_export/Prefabs/MapDesigne/Maze_2/SweetRoom/";
        private const string Candles = "Assets/00_Art/09_export/Candles_Pack/Prefabs/";

        [MenuItem("Case File/P0 테스트 씬 만들기")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildAndSave();
        }

        /// <summary>additive면 지금 열린 씬을 건드리지 않고 새 씬을 옆에 만들어 저장한 뒤 닫는다.</summary>
        public static void BuildAndSave(bool additive = false)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, additive ? NewSceneMode.Additive : NewSceneMode.Single);

            var camera = scene.GetRootGameObjects().Select(g => g.GetComponent<Camera>()).First(c => c != null);
            camera.transform.SetPositionAndRotation(new Vector3(-3f, 26f, -14f), Quaternion.Euler(58f, 0, 0));
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.08f, 0.1f);
            camera.farClipPlane = 300f;

            var light = scene.GetRootGameObjects().Select(g => g.GetComponent<Light>()).FirstOrDefault(l => l != null);
            if (light != null) light.intensity = 1.1f;

            var root = new GameObject("P0_Playtest");
            SceneManager.MoveGameObjectToScene(root, scene);
            var game = root.AddComponent<PlaytestGame>();
            game.MainCamera = camera;
            game.UiFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/00_Art/04_Fonts/Galmuri/Galmuri11.ttf");
            game.Art = LoadFieldArt();

            var board = new GameObject("Board").AddComponent<BoardView>();
            board.transform.SetParent(root.transform, false);
            board.PlayerPrefab = Load(Pieces + "Player.prefab");
            board.PolicePrefab = Load(Pieces + "Police_Body.prefab");
            board.CriminalPrefab = Load(Pieces + "Criminal_Body.prefab");
            board.NpcBodyPrefabs = Enumerable.Range(0, 5).Select(i => Load($"{Pieces}NPC_Body_0{i}.prefab")).ToArray();
            board.Props = BuildProps();
            game.Board = board;

            var desk = new GameObject("Desk_UI-003").AddComponent<DeskView>();
            desk.transform.SetParent(root.transform, false);
            desk.transform.position = DeskView.DeskOrigin;
            desk.CandlePrefab = Load(Candles + "SM_candle_14.prefab");
            desk.Telephone = PlaceTelephone(desk.transform);
            game.Desk = desk;

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings();
            if (additive) EditorSceneManager.CloseScene(scene, true);
            Debug.Log($"[P0] 테스트 씬을 만들었습니다: {ScenePath}");
        }

        private const string InteractionUi = "Assets/00_Art/02_Textures/UI/Interaction/";
        private const string ComputerUi = "Assets/00_Art/02_Textures/UI/Computer/";
        private const string ReportUi = "Assets/00_Art/02_Textures/UI/Report/";

        /// <summary>현장 HUD·상태 아이콘·접근 안내에 쓰는 임시 UI 이미지(09 시안). 메뉴 Case File > 현장 UI 이미지 연결로 열린 씬에도 다시 연결할 수 있다.</summary>
        public static FieldArt LoadFieldArt()
        {
            Texture2D T(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            return new FieldArt
            {
                StatusExclaim = T(InteractionUi + "NPC_상태아이콘_00.png"),
                StatusQuestion = T(InteractionUi + "NPC_상태아이콘_01.png"),
                StatusTalk = T(InteractionUi + "NPC_상태아이콘_02.png"),
                StatusSweat = T(InteractionUi + "NPC_상태아이콘_03.png"),
                StatusEye = T(InteractionUi + "NPC_상태아이콘_04.png"),
                StatusSound = T(InteractionUi + "NPC_상태아이콘_05.png"),
                PromptTalk = T(InteractionUi + "상호작용_대화.png"),
                PromptInvestigate = T(InteractionUi + "상호작용_조사.png"),
                PersonNamePlate = T(InteractionUi + "인물_이름.png"),
                ItemNamePlate = T(InteractionUi + "아이템_이름.png"),
                Portraits = Enumerable.Range(0, 10).Select(i => T($"{ComputerUi}컴퓨터_인물이미지_{i:00}.png")).Where(t => t != null).ToArray(),
                ReportCover = T(ReportUi + "보고서_커버.png"),
                ReportListPaper = T(ReportUi + "보고서_단서목록.png"),
                ReportItem = T(ReportUi + "보고서_단서목록_선택X.png"),
                ReportItemSelected = T(ReportUi + "보고서_단서목록_선택.png"),
                ReportDetail = T(ReportUi + "보고서_단서설명.png"),
                ReportPhoto = T(ReportUi + "보고서_단서이미지.png"),
                ReportWriting = T(ReportUi + "보고서_보고서작성하는곳.png"),
                ClueImages = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/00_Art/02_Textures/Clue" })
                    .Select(g => T(AssetDatabase.GUIDToAssetPath(g))).Where(t => t != null).ToArray()
            };
        }

        [MenuItem("Case File/현장 UI 이미지 연결 (열린 P0 씬)")]
        public static void RelinkFieldArt()
        {
            var game = UnityEngine.Object.FindAnyObjectByType<PlaytestGame>();
            if (game == null) { Debug.LogWarning("[P0] 열린 씬에 PlaytestGame이 없습니다."); return; }
            Undo.RecordObject(game, "현장 UI 이미지 연결");
            game.Art = LoadFieldArt();
            EditorUtility.SetDirty(game);
            EditorSceneManager.MarkSceneDirty(game.gameObject.scene);
        }

        /// <summary>
        /// 전화기 프리팹 인스턴스를 책상에 놓는다. 프리팹 원본은 건드리지 않고, 씬 오버라이드로
        /// 테스트 키(E)와 프리팹 안의 테스트용 진행기·클릭 처리만 끈다(게임 흐름이 대신 맡는다).
        /// </summary>
        private static GameObject PlaceTelephone(Transform desk)
        {
            var prefab = Load("Assets/00_Art/00_Prefabs/MainScreen/Telephone.prefab");
            if (prefab == null) return null;
            var phone = (GameObject)PrefabUtility.InstantiatePrefab(prefab, desk);
            phone.transform.localPosition = DeskView.TelephoneLocalPosition;
            phone.transform.localRotation = DeskView.TelephoneLocalRotation;
            phone.transform.localScale = Vector3.one * DeskView.TelephoneScale;

            foreach (var anim in phone.GetComponentsInChildren<TelephoneHandsetAnimator>())
            {
                var so = new SerializedObject(anim);
                var p = so.FindProperty("testKey");
                if (p != null) { p.boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); }
            }
            foreach (var demo in phone.GetComponentsInChildren<PhoneCallDemo>()) demo.enabled = false;
            foreach (var interaction in phone.GetComponentsInChildren<TelephoneInteraction>()) interaction.enabled = false;
            return phone;
        }

        /// <summary>TEST_TEMP 소품 배치. 방 중심 기준 m. 조사 지점(남서쪽)과 인물 자리(가운데)를 피해 북쪽·동쪽 벽가에 둔다.</summary>
        private static List<PropPlacement> BuildProps()
        {
            var table = Load(Common + "Table/Table_04.prefab");
            var castle = Load(Common + "FramePainting/FramePainting_Castle.prefab");
            var lady = Load(Common + "FramePainting/FramePainting_Lady.prefab");
            var oval = Load(Common + "FramePainting/FramePainting_Oval.prefab");
            var portrait = Load(Common + "FramePainting/FramePainting_Portrait.prefab");
            var sconce = Load(Common + "corridor/wall_candlestick_lit.prefab");
            var candle13 = Load(Candles + "SM_candle_13.prefab");
            var candle15 = Load(Candles + "SM_candle_15.prefab");

            PropPlacement P(GameObject prefab, string room, float x, float y, float z, float rot = 0, float scale = 1) =>
                new PropPlacement { Prefab = prefab, RoomId = room, LocalPosition = new Vector3(x, y, z), RotationY = rot, Scale = scale };

            return new List<PropPlacement>
            {
                P(castle, HouseLayoutFactory.EntranceHall, -2f, 0.05f, 4.9f, 180f, 1f),
                P(sconce, HouseLayoutFactory.EntranceHall, -4.9f, 0f, 2f, 90f, 0.6f),

                P(table, HouseLayoutFactory.Reception, 2.2f, 0, 1.6f, 0, 1.2f),
                P(candle13, HouseLayoutFactory.Reception, 2.2f, 0.7f, 1.6f, 0, 2f),
                P(lady, HouseLayoutFactory.Reception, -2.5f, 0.05f, 2.9f, 180f, 1f),

                P(table, HouseLayoutFactory.Dining, 0.5f, 0, 1.5f, 0, 2f),
                P(Load(Sweet + "ChocolateRoseCake/ChocolateRoseCake.prefab"), HouseLayoutFactory.Dining, 0.5f, 1.16f, 1.5f, 0, 0.3f),
                P(candle15, HouseLayoutFactory.Dining, -0.3f, 1.16f, 1.5f, 0, 2f),
                P(candle13, HouseLayoutFactory.Dining, 1.3f, 1.16f, 1.5f, 0, 2f),

                P(table, HouseLayoutFactory.Kitchen, 1.5f, 0, 2.0f, 0, 1.5f),
                P(Load(Sweet + "Pot1/Pot1.prefab"), HouseLayoutFactory.Kitchen, 1.1f, 0.87f, 2.0f, 0, 0.3f),
                P(Load(Sweet + "CopperKettle/CopperKettle.prefab"), HouseLayoutFactory.Kitchen, 1.9f, 0.87f, 2.0f, 0, 0.28f),
                P(Load(Sweet + "Saucepan/Saucepan.prefab"), HouseLayoutFactory.Kitchen, 3.0f, 0, 2.2f, 0, 0.35f),
                P(Load(Sweet + "Pan1/Pan1.prefab"), HouseLayoutFactory.Kitchen, -2.8f, 0, 2.2f, 0, 0.35f),
                P(Load(Sweet + "Pan2/Pan2.prefab"), HouseLayoutFactory.Kitchen, -2.0f, 0, 2.2f, 0, 0.35f),

                P(Load(Sweet + "Pot2/Pot2.prefab"), HouseLayoutFactory.PantryUtility, 1.3f, 0, 1.6f, 0, 0.4f),
                P(Load(Sweet + "Pan3/Pan3.prefab"), HouseLayoutFactory.PantryUtility, 0.3f, 0, 1.7f, 0, 0.35f),
                P(Load(Sweet + "Hook/Hook.prefab"), HouseLayoutFactory.PantryUtility, -1.2f, 0.4f, 2.1f, 0, 0.4f),

                P(sconce, HouseLayoutFactory.CorridorStairs, -1.0f, 0f, -3.5f, 90f, 0.6f),
                P(sconce, HouseLayoutFactory.CorridorStairs, 1.0f, 0f, 3.5f, -90f, 0.6f),
                P(portrait, HouseLayoutFactory.CorridorStairs, 0f, 0.05f, 5.9f, 180f, 0.9f),

                P(table, HouseLayoutFactory.Bedroom, -2.2f, 0, 1.8f, 0, 0.8f),
                P(candle15, HouseLayoutFactory.Bedroom, -2.2f, 0.47f, 1.8f, 0, 2f),
                P(oval, HouseLayoutFactory.Bedroom, 0.5f, 0.05f, 2.4f, 180f, 1f),

                P(table, HouseLayoutFactory.Study, 1.8f, 0, 1.6f, 0, 1.3f),
                P(candle13, HouseLayoutFactory.Study, 2.3f, 0.75f, 1.6f, 0, 2f),
                P(portrait, HouseLayoutFactory.Study, -1.5f, 0.05f, 2.4f, 180f, 0.9f),
            };
        }

        private static GameObject Load(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) Debug.LogWarning($"[P0] 프리팹을 찾지 못했습니다: {path}");
            return go;
        }

        private static void AddToBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == ScenePath)) return;
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
