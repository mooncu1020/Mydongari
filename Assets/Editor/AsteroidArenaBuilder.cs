using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MyDongari.Core;

namespace MyDongari.EditorTools
{
    /// <summary>
    /// 소행성 표면 맵 생성 (T1~T2). 메뉴 Tools/Build Asteroid Arena — 다시 누르면 이전 AsteroidArena를 지우고 새로 만든다.
    /// 씬 파일을 손으로 고치지 않고 열린 씬에 오브젝트를 만든다(저장은 사용자). 요소마다 별도 오브젝트라 나중에 블렌더 모델로
    /// 메시만 바꿔 끼울 수 있다 — 생성 메시는 Assets/Arena/Generated에 에셋으로, 머티리얼은 Assets/Arena/Materials.
    /// 설정은 Assets/Arena/AsteroidArenaConfig.asset (없으면 기본값으로 만든다).
    /// </summary>
    public static class AsteroidArenaBuilder
    {
        private const string RootName = "AsteroidArena";
        private const string ArenaFolder = "Assets/Arena";
        private const string GeneratedFolder = ArenaFolder + "/Generated";
        private const string MaterialFolder = ArenaFolder + "/Materials";
        private const string ConfigPath = ArenaFolder + "/AsteroidArenaConfig.asset";
        private const string ToonBasePath = "Assets/Materials/M_ToonMech.mat";
        private const string OldMapName = "Map_Spaceport.";

        [MenuItem("Tools/Build Asteroid Arena")]
        private static void Build()
        {
            EnsureFolders();
            AsteroidArenaConfig config = EnsureConfig();
            Material toonBase = AssetDatabase.LoadAssetAtPath<Material>(ToonBasePath);
            if (toonBase == null)
            {
                EditorUtility.DisplayDialog("Asteroid Arena", $"{ToonBasePath}가 없습니다.", "확인");
                return;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Build Asteroid Arena");
            int undoGroup = Undo.GetCurrentGroup();
            var report = new System.Text.StringBuilder();

            // 이전 맵 지우기, 예전 맵(Map_Spaceport.)은 지우지 않고 끈다
            foreach (GameObject old in FindRoots(RootName)) Undo.DestroyObjectImmediate(old);
            foreach (GameObject old in FindRoots(OldMapName))
            {
                Undo.RecordObject(old, "Disable Old Map");
                old.SetActive(false);
                report.AppendLine($"예전 맵 '{OldMapName}' 끔 (지우지 않음)");
            }

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Asteroid Arena");
            root.transform.position = new Vector3(0f, config.groundHeight, 0f);
            var random = new System.Random(config.seed);

            Material ground = EnsureToonMaterial("M_Arena_Ground", toonBase, config.groundColor);
            Material rock = EnsureToonMaterial("M_Arena_Rock", toonBase, config.rockColor);
            Material rim = EnsureToonMaterial("M_Arena_Rim", toonBase, config.rimColor);

            BuildTerrain(root.transform, config, random, ground, rim);
            BuildRocks(root.transform, config, random, rock, report);
            PlaceSpawns(config, report);
            BuildSkyAndLight(root.transform, config, report);

            Undo.CollapseUndoOperations(undoGroup);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            report.AppendLine("씬을 저장하세요. 설정: " + ConfigPath);
            Debug.Log("[AsteroidArenaBuilder]\n" + report);
            EditorUtility.DisplayDialog("Asteroid Arena", report.ToString(), "확인");
        }

        // ---------------- 지형 (T1) ----------------

        private static void BuildTerrain(Transform root, AsteroidArenaConfig config, System.Random random, Material ground, Material rim)
        {
            float radius = config.floorDiameter * 0.5f;

            // 판정: 평평한 바닥 (위 면 = groundHeight). 외형과 따로라 요철에 걸리지 않는다
            GameObject floorCollider = Child(root, "Floor_Collider");
            var box = floorCollider.AddComponent<BoxCollider>();
            float floorSize = (radius + config.slopeWidth) * 2f + 20f;
            box.size = new Vector3(floorSize, 2f, floorSize);
            box.center = new Vector3(0f, -1f, 0f);

            // 외형: 약한 노이즈 요철 원판 (판정 없음)
            GameObject floorVisual = Child(root, "Floor_Visual");
            float noiseOffset = (float)random.NextDouble() * 1000f;
            Mesh floorMesh = SaveMesh(BuildFloorMesh(config, radius, noiseOffset), "Arena_Floor");
            AddRenderer(floorVisual, floorMesh, ground, castShadows: false);

            // 외형: 가장자리 완만한 경사 고리 (판정 없음)
            GameObject slope = Child(root, "Crater_Slope_Visual");
            Mesh slopeMesh = SaveMesh(BuildSlopeMesh(config, radius, noiseOffset), "Arena_Slope");
            AddRenderer(slope, slopeMesh, rim, castShadows: true);

            // 외형: 테두리 바위 (판정 없음 — 판정은 아래 원형 벽)
            GameObject rimRocks = Child(root, "Rim_Rocks");
            for (int i = 0; i < config.rimRockCount; i++)
            {
                float angle = (i + (float)random.NextDouble() * 0.6f) / Mathf.Max(1, config.rimRockCount) * Mathf.PI * 2f;
                float t = Mathf.Lerp(0.55f, 1.05f, (float)random.NextDouble());
                float distance = radius + config.slopeWidth * t;
                float baseY = config.slopeHeight * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                float height = Lerp(config.rimRockHeight, random);
                float rockRadius = Lerp(config.rimRockRadius, random);
                Mesh mesh = SaveMesh(BuildRockMesh(random, rockRadius, height, config.rockDistortion), $"Arena_RimRock_{i + 1}");
                GameObject rimRock = Child(rimRocks.transform, $"RimRock_{i + 1}");
                // 반쯤 묻히게 바닥을 경사 아래로
                rimRock.transform.localPosition = new Vector3(Mathf.Sin(angle) * distance, baseY - height * 0.25f, Mathf.Cos(angle) * distance);
                rimRock.transform.localRotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                AddRenderer(rimRock, mesh, rim, castShadows: true);
            }

            // 판정: 바닥 가장자리를 두르는 보이지 않는 원형 벽 (박스 조각)
            GameObject wall = Child(root, "Boundary_Wall");
            int segments = Mathf.Max(8, config.wallSegments);
            float segmentLength = 2f * Mathf.PI * (radius + config.wallThickness * 0.5f) / segments * 1.05f;
            for (int i = 0; i < segments; i++)
            {
                float angle = (i + 0.5f) / segments * Mathf.PI * 2f;
                GameObject piece = Child(wall.transform, $"Wall_{i + 1:00}");
                float d = radius + config.wallThickness * 0.5f;
                piece.transform.localPosition = new Vector3(Mathf.Sin(angle) * d, config.wallHeight * 0.5f, Mathf.Cos(angle) * d);
                piece.transform.localRotation = Quaternion.Euler(0f, angle * Mathf.Rad2Deg, 0f);
                var collider = piece.AddComponent<BoxCollider>();
                collider.size = new Vector3(segmentLength, config.wallHeight, config.wallThickness);
            }
        }

        private static void BuildRocks(Transform root, AsteroidArenaConfig config, System.Random random, Material material, System.Text.StringBuilder report)
        {
            GameObject rocks = Child(root, "Rocks");
            float spawnHalf = config.spawnDistance * 0.5f;
            for (int i = 0; i < config.rockCount; i++)
            {
                // 네 방향에 고르게(스폰 선 Z축은 피해 45°부터), 거리는 중앙 비움 반지름 ~ 바깥 한계
                float angle = (45f + 360f / Mathf.Max(1, config.rockCount) * i + Lerp(new Vector2(-15f, 15f), random)) * Mathf.Deg2Rad;
                float distance = Mathf.Lerp(Mathf.Max(config.rockClearRadius + 4f, spawnHalf + 6f), config.rockMaxRadius, (float)random.NextDouble());
                float height = Lerp(config.rockHeight, random);
                float rockRadius = Lerp(config.rockRadius, random);
                Mesh mesh = SaveMesh(BuildRockMesh(random, rockRadius, height, config.rockDistortion), $"Arena_Rock_{i + 1}");
                GameObject rock = Child(rocks.transform, $"Rock_{i + 1}");
                rock.transform.localPosition = new Vector3(Mathf.Sin(angle) * distance, 0f, Mathf.Cos(angle) * distance);
                rock.transform.localRotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                AddRenderer(rock, mesh, material, castShadows: true);
                var collider = rock.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                collider.convex = true;
                report.AppendLine($"바위 {i + 1}: 중앙에서 {distance:0.0}m, 높이 {height:0.0}m");
            }
        }

        private static void PlaceSpawns(AsteroidArenaConfig config, System.Text.StringBuilder report)
        {
            GameManager gm = Object.FindFirstObjectByType<GameManager>();
            if (gm == null)
            {
                report.AppendLine("GameManager가 없어 스폰을 못 옮김");
                return;
            }
            var so = new SerializedObject(gm);
            float half = config.spawnDistance * 0.5f;
            MoveSpawn(so.FindProperty("player1SpawnPoint"), new Vector3(0f, 0f, -half), 0f, report);
            MoveSpawn(so.FindProperty("player2SpawnPoint"), new Vector3(0f, 0f, half), 180f, report);
        }

        /// <summary>기존 스폰 포인트를 옮긴다 (높이는 지금 값 그대로 — 바닥 높이를 바꾸지 않았으면 기체가 같은 높이에서 선다).</summary>
        private static void MoveSpawn(SerializedProperty property, Vector3 horizontal, float yaw, System.Text.StringBuilder report)
        {
            var spawn = property != null ? property.objectReferenceValue as Transform : null;
            if (spawn == null)
            {
                report.AppendLine($"{property?.name}가 비어 있어 못 옮김");
                return;
            }
            Undo.RecordObject(spawn, "Move Spawn");
            spawn.SetPositionAndRotation(new Vector3(horizontal.x, spawn.position.y, horizontal.z), Quaternion.Euler(0f, yaw, 0f));
            report.AppendLine($"스폰 {spawn.name.Trim()} → ({horizontal.x:0}, {horizontal.z:0}), 방향 {yaw:0}°");
        }

        // ---------------- 하늘·행성·조명 (T2) ----------------

        private const string SkyShaderName = "MyDongari/AsteroidSky";
        private const string PlanetShaderName = "MyDongari/ToonPlanet";

        private static void BuildSkyAndLight(Transform root, AsteroidArenaConfig config, System.Text.StringBuilder report)
        {
            // 하늘: 코드 셰이더 스카이박스 (검은 배경·별·은하수). 진공이라 안개 끔, 앰비언트는 푸르스름한 평면 색
            Material sky = EnsureShaderMaterial("M_AsteroidSky", SkyShaderName, report);
            if (sky != null) RenderSettings.skybox = sky;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = config.ambientColor;

            // 행성: 지평선 위 멀리 큰 구체 (카메라 먼 평면 안쪽)
            Material planetMaterial = EnsureShaderMaterial("M_Planet", PlanetShaderName, report);
            if (planetMaterial != null)
            {
                planetMaterial.SetColor("_BaseColor", config.planetColor);
                planetMaterial.SetColor("_ShadowColor", config.planetShadowColor);
                planetMaterial.SetColor("_AtmosphereColor", config.planetAtmosphereColor);
                planetMaterial.SetFloat("_RimIntensity", config.planetRimIntensity);
                planetMaterial.SetFloat("_RimPower", config.planetRimPower);
                EditorUtility.SetDirty(planetMaterial);

                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Mesh sphereMesh = sphere.GetComponent<MeshFilter>().sharedMesh;
                Object.DestroyImmediate(sphere);
                GameObject planet = Child(root, "Planet");
                Vector3 direction = Quaternion.Euler(-config.planetElevation, config.planetAzimuth, 0f) * Vector3.forward;
                planet.transform.position = root.position + direction * config.planetDistance;
                planet.transform.localScale = Vector3.one * config.planetRadius * 2f;
                var renderer = planet.AddComponent<MeshRenderer>();
                planet.AddComponent<MeshFilter>().sharedMesh = sphereMesh;
                renderer.sharedMaterial = planetMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                report.AppendLine($"행성: 반지름 {config.planetRadius:0}m, 거리 {config.planetDistance:0}m, 방위 {config.planetAzimuth:0}°·높이 {config.planetElevation:0}°");
            }

            // 해: 디렉셔널 라이트 하나 (기존 것을 쓰고 나머지 디렉셔널은 끔). 낮은 각도·강한 흰빛·그림자
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
            sun.transform.rotation = Quaternion.Euler(config.sunElevation, config.sunAzimuth, 0f);
            sun.color = config.sunColor;
            sun.intensity = config.sunIntensity;
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;
            DynamicGI.UpdateEnvironment();
            report.AppendLine($"해: '{sun.name}' 높이 {config.sunElevation:0}°·세기 {config.sunIntensity}·그림자 켬{(disabled > 0 ? $", 다른 디렉셔널 라이트 {disabled}개 끔" : "")}");
        }

        private static Material EnsureShaderMaterial(string name, string shaderName, System.Text.StringBuilder report)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                report.AppendLine($"셰이더 {shaderName}를 못 찾음 — Assets/Materials의 셰이더 컴파일 오류 확인");
                return null;
            }
            var material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        // ---------------- 메시 ----------------

        private static Mesh BuildFloorMesh(AsteroidArenaConfig config, float radius, float noiseOffset)
        {
            int rings = Mathf.Max(2, config.floorRings);
            int sectors = Mathf.Max(8, config.floorSectors);
            var vertices = new List<Vector3> { new Vector3(0f, FloorNoise(config, 0f, 0f, noiseOffset, 0f, radius), 0f) };
            var triangles = new List<int>();
            for (int r = 1; r <= rings; r++)
            {
                float d = radius * r / rings;
                for (int s = 0; s < sectors; s++)
                {
                    float a = (float)s / sectors * Mathf.PI * 2f;
                    float x = Mathf.Sin(a) * d, z = Mathf.Cos(a) * d;
                    vertices.Add(new Vector3(x, FloorNoise(config, x, z, noiseOffset, d, radius), z));
                }
            }
            for (int s = 0; s < sectors; s++)
            {
                triangles.Add(0);
                triangles.Add(1 + s);
                triangles.Add(1 + (s + 1) % sectors);
            }
            for (int r = 1; r < rings; r++)
            {
                int inner = 1 + (r - 1) * sectors, outer = 1 + r * sectors;
                for (int s = 0; s < sectors; s++)
                {
                    int s1 = (s + 1) % sectors;
                    triangles.AddRange(new[] { inner + s, outer + s, outer + s1, inner + s, outer + s1, inner + s1 });
                }
            }
            return MakeMesh("Arena_Floor", vertices, triangles);
        }

        /// <summary>바닥 요철: 가장자리로 갈수록 0 (경사와 이어지게). 판정은 평평하므로 아주 약하게.</summary>
        private static float FloorNoise(AsteroidArenaConfig config, float x, float z, float offset, float distance, float radius)
        {
            float n = Mathf.PerlinNoise(x * config.floorNoiseScale + offset, z * config.floorNoiseScale + offset) - 0.5f;
            n += 0.5f * (Mathf.PerlinNoise(x * config.floorNoiseScale * 3f + offset, z * config.floorNoiseScale * 3f - offset) - 0.5f);
            float edge = 1f - Mathf.SmoothStep(0.85f, 1f, distance / radius);
            return n * 2f * config.floorNoiseHeight * edge;
        }

        private static Mesh BuildSlopeMesh(AsteroidArenaConfig config, float radius, float noiseOffset)
        {
            int rings = 10;
            int sectors = Mathf.Max(8, config.floorSectors);
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float t = (float)r / rings;
                float d = radius + config.slopeWidth * t;
                for (int s = 0; s < sectors; s++)
                {
                    float a = (float)s / sectors * Mathf.PI * 2f;
                    float x = Mathf.Sin(a) * d, z = Mathf.Cos(a) * d;
                    // 완만한 경사 + 바깥으로 갈수록 커지는 요철 (안쪽 끝은 바닥과 같은 높이 0)
                    float bump = (Mathf.PerlinNoise(x * 0.08f + noiseOffset, z * 0.08f - noiseOffset) - 0.5f) * 2.5f * t;
                    vertices.Add(new Vector3(x, config.slopeHeight * Mathf.SmoothStep(0f, 1f, t) + bump, z));
                }
            }
            for (int r = 0; r < rings; r++)
            {
                int inner = r * sectors, outer = (r + 1) * sectors;
                for (int s = 0; s < sectors; s++)
                {
                    int s1 = (s + 1) % sectors;
                    triangles.AddRange(new[] { inner + s, outer + s, outer + s1, inner + s, outer + s1, inner + s1 });
                }
            }
            return MakeMesh("Arena_Slope", vertices, triangles);
        }

        /// <summary>
        /// 일그러진 다면체: 정이십면체를 한 번 나눈 80면을 노이즈로 일그러뜨리고, 면마다 꼭짓점을 따로 둬 각진 면(셀 셰이딩·외곽선이 잘 보이게).
        /// 바닥은 y = 0에 평평하게 눌러 땅에 앉게 한다.
        /// </summary>
        private static Mesh BuildRockMesh(System.Random random, float radius, float height, float distortion)
        {
            var (points, faces) = Icosphere(1);
            float seedX = (float)random.NextDouble() * 100f, seedY = (float)random.NextDouble() * 100f;
            var shaped = new Vector3[points.Count];
            float minY = float.MaxValue;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 p = points[i];
                float n = Mathf.PerlinNoise(p.x * 1.7f + seedX, p.z * 1.7f + p.y + seedY);
                float scale = 1f + (n - 0.5f) * 2f * distortion;
                shaped[i] = new Vector3(p.x * radius, p.y * height * 0.5f, p.z * radius) * scale;
                minY = Mathf.Min(minY, shaped[i].y);
            }
            // 아래 20%는 평평하게 눌러 땅에 앉힌다
            float flatten = minY + height * 0.2f;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            foreach (int[] f in faces)
            {
                // Unity 앞면 = cross(b - a, c - a) 쪽. 면 목록의 감김 방향과 상관없이 바깥(중심 반대)을 보게 맞춘다
                Vector3 a = shaped[f[0]], b = shaped[f[1]], c = shaped[f[2]];
                bool outward = Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f) >= 0f;
                int[] order = outward ? f : new[] { f[0], f[2], f[1] };
                foreach (int index in order)
                {
                    Vector3 v = shaped[index];
                    v.y = Mathf.Max(v.y, flatten) - flatten;
                    triangles.Add(vertices.Count);
                    vertices.Add(v);
                }
            }
            return MakeMesh("Rock", vertices, triangles);
        }

        private static (List<Vector3>, List<int[]>) Icosphere(int subdivisions)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var points = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            for (int i = 0; i < points.Count; i++) points[i] = points[i].normalized;
            var faces = new List<int[]>
            {
                new[] { 0, 11, 5 }, new[] { 0, 5, 1 }, new[] { 0, 1, 7 }, new[] { 0, 7, 10 }, new[] { 0, 10, 11 },
                new[] { 1, 5, 9 }, new[] { 5, 11, 4 }, new[] { 11, 10, 2 }, new[] { 10, 7, 6 }, new[] { 7, 1, 8 },
                new[] { 3, 9, 4 }, new[] { 3, 4, 2 }, new[] { 3, 2, 6 }, new[] { 3, 6, 8 }, new[] { 3, 8, 9 },
                new[] { 4, 9, 5 }, new[] { 2, 4, 11 }, new[] { 6, 2, 10 }, new[] { 8, 6, 7 }, new[] { 9, 8, 1 }
            };
            for (int s = 0; s < subdivisions; s++)
            {
                var cache = new Dictionary<long, int>();
                var next = new List<int[]>();
                foreach (int[] f in faces)
                {
                    int a = Midpoint(points, cache, f[0], f[1]);
                    int b = Midpoint(points, cache, f[1], f[2]);
                    int c = Midpoint(points, cache, f[2], f[0]);
                    next.Add(new[] { f[0], a, c });
                    next.Add(new[] { f[1], b, a });
                    next.Add(new[] { f[2], c, b });
                    next.Add(new[] { a, b, c });
                }
                faces = next;
            }
            return (points, faces);
        }

        private static int Midpoint(List<Vector3> points, Dictionary<long, int> cache, int i, int j)
        {
            long key = i < j ? ((long)i << 32) | (uint)j : ((long)j << 32) | (uint)i;
            if (cache.TryGetValue(key, out int index)) return index;
            points.Add(((points[i] + points[j]) * 0.5f).normalized);
            cache[key] = points.Count - 1;
            return points.Count - 1;
        }

        private static Mesh MakeMesh(string name, List<Vector3> vertices, List<int> triangles)
        {
            var mesh = new Mesh { name = name };
            if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------------- 공통 ----------------

        private static float Lerp(Vector2 range, System.Random random) => Mathf.Lerp(range.x, range.y, (float)random.NextDouble());

        private static GameObject Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void AddRenderer(GameObject go, Mesh mesh, Material material, bool castShadows)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static List<GameObject> FindRoots(string name)
        {
            var result = new List<GameObject>();
            foreach (GameObject go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (go.name == name) result.Add(go);
            }
            return result;
        }

        /// <summary>생성 메시를 에셋으로 (같은 이름이 있으면 덮어씀 — 다시 만들 때 쌓이지 않게).</summary>
        private static Mesh SaveMesh(Mesh mesh, string name)
        {
            string path = $"{GeneratedFolder}/{name}.asset";
            mesh.name = name;
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>M_ToonMech 복제 머티리얼. 이미 있으면 그대로(색을 손으로 고쳤을 수 있음) — 색을 다시 맞추려면 지우고 메뉴.</summary>
        private static Material EnsureToonMaterial(string name, Material toonBase, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var material = new Material(toonBase) { name = name };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static AsteroidArenaConfig EnsureConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<AsteroidArenaConfig>(ConfigPath);
            if (config != null) return config;
            config = ScriptableObject.CreateInstance<AsteroidArenaConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            return config;
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(ArenaFolder)) AssetDatabase.CreateFolder("Assets", "Arena");
            if (!AssetDatabase.IsValidFolder(GeneratedFolder)) AssetDatabase.CreateFolder(ArenaFolder, "Generated");
            if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder(ArenaFolder, "Materials");
        }
    }
}
