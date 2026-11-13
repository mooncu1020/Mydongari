using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using MyDongari.Core;
using MyDongari.Game.Flow;

namespace MyDongari.EditorTools
{
    /// <summary>
    /// 벨포레스트 박스 맵 생성 (V7a, 기획서 "맵" 표·docs/map_bellforest.png) + 낮 하늘·조명 (V7b). 메뉴 Tools/Build Bellforest Blockout —
    /// 다시 누르면 이전 Bellforest를 지우고 새로 만든다. 소행성 맵(AsteroidArena)과 예전 맵은 지우지 않고 끈다.
    /// 씬 파일을 손으로 고치지 않고 열린 씬에 오브젝트를 만든다(저장은 사용자). 블록마다 별도 오브젝트라 나중에 모델로 바꿔 끼울 수 있다.
    /// 판정: 블록 윗면(두께 TopSlab) = Walkable, 그 아래 몸통 = Wall, 갑판 = Walkable, 테두리 = 보이지 않는 Wall.
    /// 외형: MyDongari/ToonFlat(스페큘러 없는 두 단계 툰) — 넓은 바닥에 하얀 반사 얼룩이 안 생긴다.
    /// 생성 메시는 Assets/Arena/Generated, 머티리얼은 Assets/Arena/Materials(M_Bell_*, 이미 있으면 색을 손댔을 수 있어 그대로 둔다).
    /// </summary>
    public static class BellforestBuilder
    {
        private const string RootName = "Bellforest";
        private static readonly string[] OldMapNames = { "AsteroidArena", "Map_Spaceport." };
        private const string MaterialFolder = "Assets/Arena/Materials";
        private const string ArenaConfigPath = "Assets/Arena/AsteroidArenaConfig.asset";
        private const string FlatShaderName = "MyDongari/ToonFlat";
        private const string SkyShaderName = "MyDongari/DaySky";

        // ---- 낮 하늘·조명 (V7b) ----
        /// <summary>해 높이(°)·방위(°). 방위는 갑판 길이 방향에서 비스듬히 — 블록 그림자가 갑판 위로 떨어져 높이가 읽힌다.</summary>
        private const float SunElevation = 50f;
        private const float SunAzimuth = 35f;
        private const float SunIntensity = 1.3f;
        private static readonly Color SunColor = new Color(1f, 0.97f, 0.90f);
        /// <summary>밝은 앰비언트 (위 / 옆 / 아래). 그늘진 기체가 검게 죽지 않게.</summary>
        private static readonly Color AmbientSky = new Color(0.78f, 0.86f, 1f);
        private static readonly Color AmbientEquator = new Color(0.70f, 0.76f, 0.86f);
        private static readonly Color AmbientGround = new Color(0.58f, 0.64f, 0.76f);
        /// <summary>환경 반사 세기. 밝은 하늘이 기체(Lit 셰이더 그래프)에 하얗게 비치지 않게 낮춘다.</summary>
        private const float ReflectionIntensity = 0.35f;

        // ---- 배치 (m). X = 폭(−28 ~ 28), Z = 길이(−60 ~ 60, +Z가 뱃머리). 그림의 왼쪽 = −Z(P1), 위 = −X. 블록은 점대칭((x, z) → (−x, −z)) ----
        /// <summary>갑판 윤곽 120 × 56m. 뱃머리(+Z) 쪽 두 모서리만 깎아 좁힌다.</summary>
        private static readonly Vector2[] DeckOutline =
        {
            new Vector2(-28f, -60f), new Vector2(-28f, 48f), new Vector2(-18f, 60f),
            new Vector2(18f, 60f), new Vector2(28f, 54f), new Vector2(28f, -60f)
        };
        /// <summary>고지대 A (−X 긴 변). B는 점대칭. 안쪽 끝이 비스듬하다.</summary>
        private static readonly Vector2[] PlateauOutline =
        {
            new Vector2(-28f, -54f), new Vector2(-28f, -4f), new Vector2(-17f, -8f), new Vector2(-17f, -54f)
        };
        private const float PlateauHeight = 8.1f;
        /// <summary>발판: 고지대 A 앞 2개(중심). B 쪽은 점대칭.</summary>
        private static readonly Vector2[] StepCenters = { new Vector2(-13f, -40f), new Vector2(-13f, -20f) };
        private const float StepSize = 6f;
        private const float StepHeight = 3.7f;
        /// <summary>낮은 엄폐물(중심). 나머지 하나는 점대칭.</summary>
        private static readonly Vector2 CoverCenter = new Vector2(-12f, 26f);
        private static readonly Vector2 CoverSize = new Vector2(4f, 8f);
        private const float CoverHeight = 2.5f;
        /// <summary>스폰: 중앙선(X = 0) 위, 서로 50m.</summary>
        private const float SpawnDistance = 50f;

        // ---- 판정 ----
        /// <summary>블록 윗면 Walkable 콜라이더 두께. 그 아래는 Wall 몸통.</summary>
        private const float TopSlab = 0.2f;
        private const float BoundaryWallHeight = 40f;
        private const float BoundaryWallThickness = 2f;
        private const float DeckThickness = 3f;

        // ---- 갑판 표시 (속도감용 기준선) ----
        private const float BorderInset = 1.2f;
        private const float BorderWidth = 0.4f;
        /// <summary>표시를 그리는 안쪽 영역(노란 경계선 안)까지의 거리.</summary>
        private const float FieldInset = 2.4f;
        private const float RunwayX = 8f;
        private const float RunwayWidth = 0.5f;
        private const float RunwayDash = 4f;
        private const float RunwayGap = 4f;
        private static readonly float[] RailX = { 0f, 4f, 12f, 16f, 20f, 24f };
        private const float RailWidth = 0.12f;
        private const float TickSpacing = 10f;
        private const float TickThickness = 0.3f;
        private const float CenterTickHalf = 1.5f;
        private static readonly Vector2 EdgeTickX = new Vector2(21f, 25f);
        private const float CenterCircleRadius = 14f;
        private const float CenterCircleWidth = 0.25f;
        private const int CenterCircleSegments = 64;
        private const float RailY = 0.02f;
        private const float MarkY = 0.03f;
        private const float RailingHeight = 1f;
        private const float RailingThickness = 0.3f;

        // ---- 색 ----
        private static readonly Color DeckColor = new Color(0.09f, 0.16f, 0.36f);
        private static readonly Color TopColor = new Color(0.17f, 0.28f, 0.52f);
        private static readonly Color OrangeColor = new Color(0.95f, 0.47f, 0.10f);
        private static readonly Color WhiteColor = new Color(0.94f, 0.95f, 0.96f);
        private static readonly Color YellowColor = new Color(0.98f, 0.80f, 0.12f);
        private static readonly Color RailColor = new Color(0.20f, 0.30f, 0.54f);
        private static readonly Color CoverColor = new Color(0.46f, 0.50f, 0.58f);
        private static readonly Color BridgeColor = new Color(0.56f, 0.60f, 0.66f);

        [MenuItem("Tools/Build Bellforest Blockout")]
        private static void BuildMenu()
        {
            string report = Build();
            if (report != null) EditorUtility.DisplayDialog("Bellforest", report, "확인");
        }

        /// <summary>
        /// 명령줄(batchmode)용: Build Settings 첫 씬을 열어 맵을 만들고 저장한 뒤 끝낸다.
        /// 예: Unity.exe -batchmode -projectPath . -executeMethod MyDongari.EditorTools.BellforestBuilder.BuildFromCommandLine -logFile map.log
        /// </summary>
        public static void BuildFromCommandLine()
        {
            int exitCode = 1;
            try
            {
                string scenePath = null;
                foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
                {
                    if (!scene.enabled) continue;
                    scenePath = scene.path;
                    break;
                }
                if (scenePath != null)
                {
                    EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    if (Build() != null && EditorSceneManager.SaveOpenScenes()) exitCode = 0;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
            EditorApplication.Exit(exitCode);
        }

        /// <summary>맵을 만들고 보고 글을 돌려준다 (실패하면 null).</summary>
        private static string Build()
        {
            AsteroidArenaBuilder.EnsureFolders();
            if (!GroundLayerSetup.EnsureLayers(out int walkable, out int wall)) return null;
            Shader flat = Shader.Find(FlatShaderName);
            if (flat == null)
            {
                Debug.LogError($"[BellforestBuilder] 셰이더 {FlatShaderName}를 못 찾음 — Assets/Materials/ToonFlat.shader 컴파일 오류 확인");
                return null;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Build Bellforest Blockout");
            int undoGroup = Undo.GetCurrentGroup();
            var report = new StringBuilder();

            // 이전 벨포레스트는 지우고, 다른 맵은 지우지 않고 끈다
            foreach (GameObject old in AsteroidArenaBuilder.FindRoots(RootName)) Undo.DestroyObjectImmediate(old);
            foreach (string oldName in OldMapNames)
            {
                foreach (GameObject old in AsteroidArenaBuilder.FindRoots(oldName))
                {
                    if (!old.activeSelf) continue;
                    Undo.RecordObject(old, "Disable Old Map");
                    old.SetActive(false);
                    report.AppendLine($"예전 맵 '{oldName}' 끔 (지우지 않음)");
                }
            }

            // 갑판 높이는 소행성 맵 바닥과 같게 (스폰 높이를 안 바꿔도 되게)
            var arenaConfig = AssetDatabase.LoadAssetAtPath<AsteroidArenaConfig>(ArenaConfigPath);
            float groundY = arenaConfig != null ? arenaConfig.groundHeight : 0f;
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Bellforest Blockout");
            root.transform.position = new Vector3(0f, groundY, 0f);

            Material deck = EnsureMaterial("M_Bell_Deck", flat, DeckColor);
            Material top = EnsureMaterial("M_Bell_Top", flat, TopColor);
            Material orange = EnsureMaterial("M_Bell_Orange", flat, OrangeColor);
            Material white = EnsureMaterial("M_Bell_White", flat, WhiteColor);
            Material yellow = EnsureMaterial("M_Bell_Yellow", flat, YellowColor);
            Material rail = EnsureMaterial("M_Bell_Rail", flat, RailColor);
            Material cover = EnsureMaterial("M_Bell_Cover", flat, CoverColor);
            Material bridge = EnsureMaterial("M_Bell_Bridge", flat, BridgeColor);

            BuildDeck(root.transform, deck, orange, walkable, wall);
            BuildMarkings(root.transform, white, yellow, rail);

            // 블록: A 쪽과 점대칭인 B 쪽
            Transform blocks = AsteroidArenaBuilder.Child(root.transform, "Blocks").transform;
            Block(blocks, "Plateau_A", PlateauOutline, PlateauHeight, top, orange, walkable, wall);
            Block(blocks, "Plateau_B", Mirror(PlateauOutline), PlateauHeight, top, orange, walkable, wall);
            for (int i = 0; i < StepCenters.Length; i++)
            {
                Vector2[] step = Rect(StepCenters[i], new Vector2(StepSize, StepSize));
                Block(blocks, $"Step_A{i + 1}", step, StepHeight, top, orange, walkable, wall);
                Block(blocks, $"Step_B{i + 1}", Mirror(step), StepHeight, top, orange, walkable, wall);
            }
            Vector2[] coverRect = Rect(CoverCenter, CoverSize);
            Block(blocks, "Cover_1", coverRect, CoverHeight, top, cover, walkable, wall);
            Block(blocks, "Cover_2", Mirror(coverRect), CoverHeight, top, cover, walkable, wall);
            report.AppendLine($"갑판 120 × 56m(뱃머리 +Z), 고지대 2곳 {PlateauHeight}m, 발판 4개 {StepHeight}m, 엄폐물 2개 {CoverHeight}m — 윗면 Walkable, 옆면·테두리 Wall");

            BuildBridge(root.transform, bridge);
            PlaceSpawns(report);
            BuildSkyAndLight(report);

            Undo.CollapseUndoOperations(undoGroup);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            report.AppendLine("씬을 저장하세요.");
            Debug.Log("[BellforestBuilder]\n" + report);
            return report.ToString();
        }

        // ---------------- 갑판·테두리 ----------------

        private static void BuildDeck(Transform root, Material deck, Material side, int walkable, int wall)
        {
            // 외형: 윤곽대로 깎은 두꺼운 판 (윗면 파랑, 옆면 주황)
            GameObject visual = AsteroidArenaBuilder.Child(root, "Deck_Visual");
            visual.AddComponent<MeshFilter>().sharedMesh = AsteroidArenaBuilder.SaveMesh(Prism(DeckOutline, -DeckThickness, 0f, true), "Bell_Deck");
            var renderer = visual.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { deck, side };
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            // 판정: 평평한 상자 하나 (깎인 모서리 밖은 테두리 벽이 막는다)
            Bounds bounds = BoundsOf(DeckOutline);
            GameObject floor = AsteroidArenaBuilder.Child(root, "Deck_Collider");
            floor.layer = walkable;
            var box = floor.AddComponent<BoxCollider>();
            box.size = new Vector3(bounds.size.x, DeckThickness, bounds.size.z);
            box.center = new Vector3(bounds.center.x, -DeckThickness * 0.5f, bounds.center.z);

            // 테두리: 변마다 보이지 않는 벽(Wall) + 낮은 주황 난간(외형만)
            GameObject walls = AsteroidArenaBuilder.Child(root, "Boundary_Wall");
            GameObject railings = AsteroidArenaBuilder.Child(root, "Railings_Visual");
            Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            Vector2 centroid = Centroid(DeckOutline);
            for (int i = 0; i < DeckOutline.Length; i++)
            {
                Vector2 a = DeckOutline[i], b = DeckOutline[(i + 1) % DeckOutline.Length];
                Vector2 mid = (a + b) * 0.5f;
                Vector2 outward = -InwardNormal(a, b, centroid);
                float length = (b - a).magnitude;
                Quaternion rotation = Quaternion.LookRotation(P(b - a, 0f).normalized, Vector3.up);

                GameObject piece = AsteroidArenaBuilder.Child(walls.transform, $"Wall_{i + 1:00}");
                piece.layer = wall;
                piece.transform.localPosition = P(mid + outward * (BoundaryWallThickness * 0.5f), 0f);
                piece.transform.localRotation = rotation;
                var collider = piece.AddComponent<BoxCollider>();
                // 모서리에 틈이 없게 양쪽으로 두께만큼 더 길게, 갑판 아래까지 조금 내려서
                collider.size = new Vector3(BoundaryWallThickness, BoundaryWallHeight, length + BoundaryWallThickness * 2f);
                collider.center = new Vector3(0f, BoundaryWallHeight * 0.5f - 1f, 0f);

                GameObject railing = AsteroidArenaBuilder.Child(railings.transform, $"Railing_{i + 1:00}");
                railing.transform.localPosition = P(mid + outward * (RailingThickness * 0.5f), RailingHeight * 0.5f);
                railing.transform.localRotation = rotation;
                railing.transform.localScale = new Vector3(RailingThickness, RailingHeight, length + RailingThickness);
                AsteroidArenaBuilder.AddRenderer(railing, cube, side, castShadows: false);
            }
        }

        // ---------------- 갑판 표시 ----------------

        /// <summary>
        /// 속도감용 기준선: 흰 점선 활주로 2줄, 노란 경계선, 길이 방향 레일 여러 줄, 10m마다 눈금, 중앙 개방 구역 원.
        /// 색마다 메시 하나(납작한 사각형 모음), 갑판 바로 위에 띄운다. 판정 없음.
        /// </summary>
        private static void BuildMarkings(Transform root, Material white, Material yellow, Material rail)
        {
            var whiteQuads = new MeshData();
            var yellowQuads = new MeshData();
            var railQuads = new MeshData();
            Vector2[] field = Inset(DeckOutline, FieldInset);

            // 노란 경계선: 윤곽을 안으로 들인 띠
            Vector2[] outer = Inset(DeckOutline, BorderInset);
            Vector2[] inner = Inset(DeckOutline, BorderInset + BorderWidth);
            for (int i = 0; i < outer.Length; i++)
            {
                int j = (i + 1) % outer.Length;
                yellowQuads.AddFlatQuad(outer[i], outer[j], inner[j], inner[i], MarkY);
            }

            // 길이 방향 레일 (가운데 선 포함, 좌우 대칭)
            foreach (float x in RailX)
            {
                AddLengthLine(railQuads, field, x, RailWidth, RailY);
                if (x > 0f) AddLengthLine(railQuads, field, -x, RailWidth, RailY);
            }

            // 흰 점선 활주로 2줄
            foreach (float x in new[] { -RunwayX, RunwayX })
            {
                if (!ZRange(field, x, out float zMin, out float zMax)) continue;
                float period = RunwayDash + RunwayGap;
                for (float z = Mathf.Ceil(zMin / period) * period; z + RunwayDash <= zMax; z += period)
                {
                    whiteQuads.AddFlatRect(x - RunwayWidth * 0.5f, x + RunwayWidth * 0.5f, z, z + RunwayDash, MarkY);
                }
            }

            // 일정 간격 눈금: 가운데 흰 눈금 + 양쪽 가장자리 노란 눈금 (영역 밖으로 나가는 것은 뺀다)
            Bounds bounds = BoundsOf(DeckOutline);
            for (float z = Mathf.Ceil(bounds.min.z / TickSpacing) * TickSpacing; z <= bounds.max.z; z += TickSpacing)
            {
                float z0 = z - TickThickness * 0.5f, z1 = z + TickThickness * 0.5f;
                AddRectIfInside(whiteQuads, field, -CenterTickHalf, CenterTickHalf, z0, z1, MarkY);
                AddRectIfInside(yellowQuads, field, EdgeTickX.x, EdgeTickX.y, z0, z1, MarkY);
                AddRectIfInside(yellowQuads, field, -EdgeTickX.y, -EdgeTickX.x, z0, z1, MarkY);
            }

            // 중앙 개방 구역 원 (반경 14m)
            float r0 = CenterCircleRadius - CenterCircleWidth * 0.5f, r1 = CenterCircleRadius + CenterCircleWidth * 0.5f;
            for (int i = 0; i < CenterCircleSegments; i++)
            {
                float a0 = (float)i / CenterCircleSegments * Mathf.PI * 2f, a1 = (float)(i + 1) / CenterCircleSegments * Mathf.PI * 2f;
                var d0 = new Vector2(Mathf.Sin(a0), Mathf.Cos(a0));
                var d1 = new Vector2(Mathf.Sin(a1), Mathf.Cos(a1));
                whiteQuads.AddFlatQuad(d0 * r0, d1 * r0, d1 * r1, d0 * r1, MarkY);
            }

            GameObject markings = AsteroidArenaBuilder.Child(root, "Deck_Markings");
            AddMarkingMesh(markings.transform, "Marks_Rail", railQuads, rail);
            AddMarkingMesh(markings.transform, "Marks_White", whiteQuads, white);
            AddMarkingMesh(markings.transform, "Marks_Yellow", yellowQuads, yellow);
        }

        private static void AddLengthLine(MeshData data, Vector2[] field, float x, float width, float y)
        {
            if (!ZRange(field, x, out float zMin, out float zMax)) return;
            data.AddFlatRect(x - width * 0.5f, x + width * 0.5f, zMin, zMax, y);
        }

        private static void AddRectIfInside(MeshData data, Vector2[] field, float x0, float x1, float z0, float z1, float y)
        {
            if (!Inside(field, new Vector2(x0, z0)) || !Inside(field, new Vector2(x1, z0)) ||
                !Inside(field, new Vector2(x0, z1)) || !Inside(field, new Vector2(x1, z1))) return;
            data.AddFlatRect(x0, x1, z0, z1, y);
        }

        private static void AddMarkingMesh(Transform parent, string name, MeshData data, Material material)
        {
            GameObject go = AsteroidArenaBuilder.Child(parent, name);
            AsteroidArenaBuilder.AddRenderer(go, AsteroidArenaBuilder.SaveMesh(data.ToMesh(), "Bell_" + name), material, castShadows: false);
        }

        // ---------------- 블록·배경 ----------------

        /// <summary>윤곽을 height만큼 세운 블록: 외형(윗면·옆면 머티리얼 따로) + 윗면 Walkable 콜라이더 + 몸통 Wall 콜라이더.</summary>
        private static void Block(Transform parent, string name, Vector2[] outline, float height, Material top, Material side, int walkable, int wall)
        {
            GameObject go = AsteroidArenaBuilder.Child(parent, name);
            go.AddComponent<MeshFilter>().sharedMesh = AsteroidArenaBuilder.SaveMesh(Prism(outline, 0f, height, true), "Bell_" + name);
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { top, side };
            renderer.shadowCastingMode = ShadowCastingMode.On;

            GameObject topCollider = AsteroidArenaBuilder.Child(go.transform, "Top_Walkable");
            topCollider.layer = walkable;
            AddConvexCollider(topCollider, AsteroidArenaBuilder.SaveMesh(Prism(outline, height - TopSlab, height, false), $"Bell_{name}_Top"));

            GameObject sideCollider = AsteroidArenaBuilder.Child(go.transform, "Side_Wall");
            sideCollider.layer = wall;
            AddConvexCollider(sideCollider, AsteroidArenaBuilder.SaveMesh(Prism(outline, 0f, height - TopSlab, false), $"Bell_{name}_Side"));
        }

        private static void AddConvexCollider(GameObject go, Mesh mesh)
        {
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = true;
        }

        /// <summary>함교 구조물: 갑판 밖(−X) 배경. 판정 없음 — 테두리 벽 밖이라 못 올라간다.</summary>
        private static void BuildBridge(Transform root, Material material)
        {
            GameObject bridge = AsteroidArenaBuilder.Child(root, "Bridge_Visual");
            AddBridgePart(bridge.transform, "Bridge_Base", Rect(new Vector2(-36f, 20f), new Vector2(10f, 32f)), -DeckThickness, 16f, material);
            AddBridgePart(bridge.transform, "Bridge_Tower", Rect(new Vector2(-36f, 20f), new Vector2(6f, 12f)), 16f, 27f, material);
        }

        private static void AddBridgePart(Transform parent, string name, Vector2[] outline, float y0, float y1, Material material)
        {
            GameObject go = AsteroidArenaBuilder.Child(parent, name);
            go.AddComponent<MeshFilter>().sharedMesh = AsteroidArenaBuilder.SaveMesh(Prism(outline, y0, y1, false), "Bell_" + name);
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
        }

        /// <summary>
        /// 낮 하늘과 조명 (V7b): 코드 셰이더 스카이박스(밝은 하늘 + 갑판 아래 구름층), 해 높이 약 50°, 밝은 3색 앰비언트, 안개 끔.
        /// 해는 씬의 디렉셔널 라이트 하나를 그대로 쓰고(없으면 만든다) 나머지 디렉셔널은 끈다 — 소행성 맵과 같은 방식.
        /// </summary>
        private static void BuildSkyAndLight(StringBuilder report)
        {
            string skyPath = $"{MaterialFolder}/M_BellSky.mat";
            Material sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (sky == null)
            {
                Shader skyShader = Shader.Find(SkyShaderName);
                if (skyShader == null)
                {
                    report.AppendLine($"셰이더 {SkyShaderName}를 못 찾음 — Assets/Materials/DaySky.shader 컴파일 오류 확인");
                }
                else
                {
                    sky = new Material(skyShader) { name = "M_BellSky" };
                    AssetDatabase.CreateAsset(sky, skyPath);
                }
            }
            if (sky != null) RenderSettings.skybox = sky;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = AmbientSky;
            RenderSettings.ambientEquatorColor = AmbientEquator;
            RenderSettings.ambientGroundColor = AmbientGround;
            RenderSettings.reflectionIntensity = ReflectionIntensity;

            Light sun = null;
            int disabled = 0;
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.InstanceID))
            {
                if (light.type != LightType.Directional) continue;
                if (sun == null)
                {
                    sun = light;
                    continue;
                }
                Undo.RecordObject(light, "Disable Extra Sun");
                light.enabled = false;
                disabled++;
            }
            if (sun == null)
            {
                var sunObject = new GameObject("Sun");
                Undo.RegisterCreatedObjectUndo(sunObject, "Create Sun");
                sun = sunObject.AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            Undo.RecordObject(sun, "Set Sun");
            Undo.RecordObject(sun.transform, "Set Sun");
            sun.transform.rotation = Quaternion.Euler(SunElevation, SunAzimuth, 0f);
            sun.color = SunColor;
            sun.intensity = SunIntensity;
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;
            DynamicGI.UpdateEnvironment();
            report.AppendLine($"하늘: 낮 하늘 + 아래 구름층(M_BellSky), 해 '{sun.name}' 높이 {SunElevation:0}°·세기 {SunIntensity}, 앰비언트 3색 밝게, 반사 {ReflectionIntensity}{(disabled > 0 ? $", 다른 디렉셔널 라이트 {disabled}개 끔" : "")}");
        }

        private static void PlaceSpawns(StringBuilder report)
        {
            GameManager gm = Object.FindFirstObjectByType<GameManager>();
            if (gm == null)
            {
                report.AppendLine("GameManager가 없어 스폰을 못 옮김");
                return;
            }
            var so = new SerializedObject(gm);
            float half = SpawnDistance * 0.5f;
            AsteroidArenaBuilder.MoveSpawn(so.FindProperty("player1SpawnPoint"), new Vector3(0f, 0f, -half), 0f, report);
            AsteroidArenaBuilder.MoveSpawn(so.FindProperty("player2SpawnPoint"), new Vector3(0f, 0f, half), 180f, report);
        }

        private static Material EnsureMaterial(string name, Shader shader, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        // ---------------- 도형 ----------------

        private static Vector3 P(Vector2 p, float y) => new Vector3(p.x, y, p.y);

        private static Vector2[] Rect(Vector2 center, Vector2 size)
        {
            Vector2 h = size * 0.5f;
            return new[]
            {
                new Vector2(center.x - h.x, center.y - h.y), new Vector2(center.x - h.x, center.y + h.y),
                new Vector2(center.x + h.x, center.y + h.y), new Vector2(center.x + h.x, center.y - h.y)
            };
        }

        /// <summary>점대칭 (x, z) → (−x, −z).</summary>
        private static Vector2[] Mirror(Vector2[] outline)
        {
            var result = new Vector2[outline.Length];
            for (int i = 0; i < outline.Length; i++) result[i] = -outline[i];
            return result;
        }

        private static Vector2 Centroid(Vector2[] outline)
        {
            Vector2 sum = Vector2.zero;
            foreach (Vector2 p in outline) sum += p;
            return sum / outline.Length;
        }

        private static Bounds BoundsOf(Vector2[] outline)
        {
            var bounds = new Bounds(P(outline[0], 0f), Vector3.zero);
            foreach (Vector2 p in outline) bounds.Encapsulate(P(p, 0f));
            return bounds;
        }

        /// <summary>변 a→b의 안쪽(중심 쪽) 단위 법선.</summary>
        private static Vector2 InwardNormal(Vector2 a, Vector2 b, Vector2 centroid)
        {
            Vector2 d = (b - a).normalized;
            var n = new Vector2(-d.y, d.x);
            return Vector2.Dot(n, centroid - (a + b) * 0.5f) >= 0f ? n : -n;
        }

        /// <summary>볼록 윤곽을 안쪽으로 distance만큼 들인 윤곽 (이웃한 두 변을 평행 이동해 만나는 점).</summary>
        private static Vector2[] Inset(Vector2[] outline, float distance)
        {
            int n = outline.Length;
            Vector2 centroid = Centroid(outline);
            var result = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 p0 = outline[(i + n - 1) % n], p1 = outline[i], p2 = outline[(i + 1) % n];
                Vector2 a = p1 + InwardNormal(p0, p1, centroid) * distance, da = p1 - p0;
                Vector2 b = p1 + InwardNormal(p1, p2, centroid) * distance, db = p2 - p1;
                float cross = da.x * db.y - da.y * db.x;
                if (Mathf.Abs(cross) < 0.0001f)
                {
                    result[i] = a;
                    continue;
                }
                float t = ((b.x - a.x) * db.y - (b.y - a.y) * db.x) / cross;
                result[i] = a + da * t;
            }
            return result;
        }

        private static bool Inside(Vector2[] outline, Vector2 p)
        {
            Vector2 centroid = Centroid(outline);
            for (int i = 0; i < outline.Length; i++)
            {
                Vector2 a = outline[i], b = outline[(i + 1) % outline.Length];
                if (Vector2.Dot(InwardNormal(a, b, centroid), p - a) < 0f) return false;
            }
            return true;
        }

        /// <summary>볼록 윤곽 안에서 X = x인 선이 지나는 Z 범위.</summary>
        private static bool ZRange(Vector2[] outline, float x, out float zMin, out float zMax)
        {
            zMin = float.MaxValue;
            zMax = float.MinValue;
            for (int i = 0; i < outline.Length; i++)
            {
                Vector2 a = outline[i], b = outline[(i + 1) % outline.Length];
                if (Mathf.Abs(a.x - b.x) < 0.0001f || (x - a.x) * (x - b.x) > 0f) continue;
                float z = Mathf.Lerp(a.y, b.y, (x - a.x) / (b.x - a.x));
                zMin = Mathf.Min(zMin, z);
                zMax = Mathf.Max(zMax, z);
            }
            return zMax > zMin;
        }

        /// <summary>
        /// 볼록 윤곽을 y0 ~ y1로 세운 기둥. 면마다 꼭짓점을 따로 둬 각진 면(셀 셰이딩·외곽선용).
        /// splitSides면 서브메시 0 = 위·아래 면, 1 = 옆면 (머티리얼을 따로 쓴다).
        /// </summary>
        private static Mesh Prism(Vector2[] outline, float y0, float y1, bool splitSides)
        {
            var caps = new MeshData();
            var sides = splitSides ? new MeshData() : caps;
            Vector2 centroid = Centroid(outline);
            int n = outline.Length;
            for (int i = 1; i < n - 1; i++)
            {
                caps.AddTriangle(P(outline[0], y1), P(outline[i], y1), P(outline[i + 1], y1), Vector3.up);
                caps.AddTriangle(P(outline[0], y0), P(outline[i], y0), P(outline[i + 1], y0), Vector3.down);
            }
            for (int i = 0; i < n; i++)
            {
                Vector2 a = outline[i], b = outline[(i + 1) % n];
                Vector3 outward = P(-InwardNormal(a, b, centroid), 0f);
                sides.AddTriangle(P(a, y0), P(b, y0), P(b, y1), outward);
                sides.AddTriangle(P(a, y0), P(b, y1), P(a, y1), outward);
            }
            return splitSides ? MeshData.ToMesh(caps, sides) : caps.ToMesh();
        }

        /// <summary>삼각형 모음. 앞면이 원하는 쪽을 보게 감김 방향을 맞춰 넣는다.</summary>
        private class MeshData
        {
            private readonly List<Vector3> _vertices = new List<Vector3>();
            private readonly List<int> _triangles = new List<int>();

            public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 facing)
            {
                // Unity 앞면 = cross(b − a, c − a) 쪽
                bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), facing) < 0f;
                _triangles.Add(_vertices.Count);
                _triangles.Add(_vertices.Count + 1);
                _triangles.Add(_vertices.Count + 2);
                _vertices.Add(a);
                _vertices.Add(flip ? c : b);
                _vertices.Add(flip ? b : c);
            }

            /// <summary>위를 보는 납작한 사각형 (꼭짓점은 둘레 순서).</summary>
            public void AddFlatQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float y)
            {
                AddTriangle(P(a, y), P(b, y), P(c, y), Vector3.up);
                AddTriangle(P(a, y), P(c, y), P(d, y), Vector3.up);
            }

            public void AddFlatRect(float x0, float x1, float z0, float z1, float y)
            {
                AddFlatQuad(new Vector2(x0, z0), new Vector2(x0, z1), new Vector2(x1, z1), new Vector2(x1, z0), y);
            }

            public Mesh ToMesh()
            {
                var mesh = new Mesh();
                if (_vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(_vertices);
                mesh.SetTriangles(_triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }

            /// <summary>두 모음을 서브메시 0·1로 가진 메시.</summary>
            public static Mesh ToMesh(MeshData first, MeshData second)
            {
                var vertices = new List<Vector3>(first._vertices);
                vertices.AddRange(second._vertices);
                var secondTriangles = new List<int>(second._triangles.Count);
                foreach (int index in second._triangles) secondTriangles.Add(index + first._vertices.Count);
                var mesh = new Mesh { subMeshCount = 2 };
                if (vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(vertices);
                mesh.SetTriangles(first._triangles, 0);
                mesh.SetTriangles(secondTriangles, 1);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
