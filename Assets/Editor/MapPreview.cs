using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MyDongari.Game.Flow;

namespace MyDongari.EditorTools
{
    /// <summary>
    /// 맵 미리보기 캡처 (V7a): 열린 씬을 정해진 시점 몇 곳에서 그려 Builds/MapPreview/*.png로 저장한다.
    /// 에디터를 못 띄우는 환경(batchmode)에서도 맵 색·표시·그림자·하늘을 눈으로 확인하려는 도구. 씬은 바꾸지 않는다(임시 카메라를 만들었다 지움).
    /// 메뉴 Mydongari/Map/Capture Map Preview, 명령줄은 CaptureFromCommandLine.
    /// </summary>
    public static class MapPreview
    {
        private const string OutputFolder = "Builds/MapPreview";
        private const int Width = 1280;
        private const int Height = 720;
        /// <summary>게임 카메라(V8)와 비슷한 구도: 세로 화각 35°, 8.4° 내려다봄, 기체 뒤 9.2m.</summary>
        private const float GameFov = 35f;
        private const float GamePitch = 8.4f;
        private const float GameDistance = 9.2f;
        private const float GameLookHeight = 2f;

        [MenuItem("Mydongari/Map/Capture Map Preview (Builds/MapPreview)")]
        private static void CaptureMenu()
        {
            string folder = Capture();
            EditorUtility.DisplayDialog("Map Preview", $"저장: {folder}", "확인");
        }

        /// <summary>
        /// 예: Unity.exe -batchmode -projectPath . -executeMethod MyDongari.EditorTools.MapPreview.CaptureFromCommandLine -logFile preview.log
        /// (-nographics를 붙이면 그릴 수 없다)
        /// </summary>
        public static void CaptureFromCommandLine()
        {
            int exitCode = 1;
            try
            {
                foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
                {
                    if (!scene.enabled) continue;
                    EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single);
                    Capture();
                    exitCode = 0;
                    break;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
            EditorApplication.Exit(exitCode);
        }

        private static string Capture()
        {
            string folder = Path.GetFullPath(OutputFolder);
            Directory.CreateDirectory(folder);

            // 스폰 높이를 기준으로 (없으면 원점)
            Vector3 p1 = new Vector3(0f, 0f, -25f), p2 = new Vector3(0f, 0f, 25f);
            GameManager gm = Object.FindFirstObjectByType<GameManager>();
            if (gm != null)
            {
                var so = new SerializedObject(gm);
                if (so.FindProperty("player1SpawnPoint")?.objectReferenceValue is Transform s1) p1 = s1.position;
                if (so.FindProperty("player2SpawnPoint")?.objectReferenceValue is Transform s2) p2 = s2.position;
            }
            float ground = p1.y;

            var go = new GameObject("MapPreviewCamera") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var camera = go.AddComponent<UnityEngine.Camera>();
                camera.nearClipPlane = 0.3f;
                camera.farClipPlane = 2000f;

                // 1) P1 스폰 뒤 게임 시점 (상대 쪽을 봄)
                Shot(camera, folder, "1_game_view_p1", GameView(p1, p2 - p1), GameFov);
                // 2) 갑판 한가운데에서 옆(고지대 A 쪽)을 보며 — 옆 대시 때 지나가는 기준선
                Shot(camera, folder, "2_game_view_side", GameView(new Vector3(0f, ground, 0f), Vector3.left), GameFov);
                // 3) 뱃머리 쪽에서 내려다본 전체
                Shot(camera, folder, "3_overview", LookFrom(new Vector3(45f, ground + 55f, -95f), new Vector3(0f, ground, 0f)), 50f);
                // 4) 바로 위에서 (배치 확인)
                Shot(camera, folder, "4_top", (new Vector3(0f, ground + 150f, 0f), Quaternion.Euler(90f, 90f, 0f)), 50f);
                // 5) 바닥에 붙어 해 쪽을 보는 낮은 시점 (반사 얼룩 확인)
                Vector3 toSun = RenderSettings.sun != null ? -RenderSettings.sun.transform.forward : Vector3.forward;
                toSun.y = 0f;
                if (toSun.sqrMagnitude < 0.01f) toSun = Vector3.forward;
                Shot(camera, folder, "5_low_toward_sun", GameView(new Vector3(0f, ground, 0f), toSun), 60f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
            Debug.Log($"[MapPreview] 저장: {folder}");
            return folder;
        }

        /// <summary>mech 위치에 선 기체를 forward 쪽으로 보는 게임 카메라 자리.</summary>
        private static (Vector3, Quaternion) GameView(Vector3 mech, Vector3 forward)
        {
            forward.y = 0f;
            float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            Quaternion rotation = Quaternion.Euler(GamePitch, yaw, 0f);
            Vector3 look = mech + Vector3.up * GameLookHeight;
            return (look - rotation * (Vector3.forward * GameDistance), rotation);
        }

        private static (Vector3, Quaternion) LookFrom(Vector3 position, Vector3 target)
        {
            return (position, Quaternion.LookRotation(target - position, Vector3.up));
        }

        private static void Shot(UnityEngine.Camera camera, string folder, string name, (Vector3 position, Quaternion rotation) pose, float fov)
        {
            camera.transform.SetPositionAndRotation(pose.position, pose.rotation);
            camera.fieldOfView = fov;
            var target = new RenderTexture(Width, Height, 24);
            var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(texture);
            }
        }
    }
}
