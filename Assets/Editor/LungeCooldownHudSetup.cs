using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MyDongari.UI;

namespace MyDongari.EditorTools
{
    /// <summary>
    /// V9g: 각 HUD에 런지 쿨다운 호(LungeCooldownArc + 배경)를 만들고 HUDController에 연결한다.
    /// 자리·크기·각도는 예전 부스트 호와 같다(히트 호의 좌우 대칭). 이미 있으면 연결만 다시 한다.
    /// 메뉴 Mydongari/Setup Lunge Cooldown Arc.
    /// </summary>
    public static class LungeCooldownHudSetup
    {
        private const string ArcName = "LungeCooldownArc";
        private const string BackgroundName = "LungeCooldownBG";

        [MenuItem("Mydongari/Setup Lunge Cooldown Arc")]
        public static void Setup()
        {
            int made = 0;
            foreach (HUDController hud in Object.FindObjectsByType<HUDController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                SerializedObject so = new SerializedObject(hud);
                ArcGauge heatArc = so.FindProperty("heatGaugeArc").objectReferenceValue as ArcGauge;
                if (heatArc == null)
                {
                    Debug.LogWarning($"[LungeCooldownHud] {hud.name}: Heat Gauge Arc가 비어 있어 건너뜀", hud);
                    continue;
                }

                RectTransform heatRect = heatArc.rectTransform;
                Transform parent = heatRect.parent;
                Transform existing = parent.Find(ArcName);
                ArcGauge arc = existing != null ? existing.GetComponent<ArcGauge>() : null;
                if (arc == null)
                {
                    arc = CreateArc(ArcName, parent, heatArc);
                    RectTransform rect = arc.rectTransform;
                    // 히트 호를 화면 가운데 기준으로 좌우로 뒤집은 자리 (예전 부스트 호와 같은 값)
                    rect.anchorMin = heatRect.anchorMin;
                    rect.anchorMax = heatRect.anchorMax;
                    rect.pivot = heatRect.pivot;
                    rect.sizeDelta = heatRect.sizeDelta;
                    rect.anchoredPosition = new Vector2(-heatRect.anchoredPosition.x, heatRect.anchoredPosition.y);
                    Vector3 scale = heatRect.localScale;
                    rect.localScale = new Vector3(-scale.x, scale.y, scale.z);
                    arc.isBackground = false;
                    arc.raycastTarget = false;

                    ArcGauge background = CreateArc(BackgroundName, rect, heatArc);
                    RectTransform bgRect = background.rectTransform;
                    bgRect.anchorMin = Vector2.zero;
                    bgRect.anchorMax = Vector2.one;
                    bgRect.offsetMin = Vector2.zero;
                    bgRect.offsetMax = Vector2.zero;
                    bgRect.localScale = Vector3.one;
                    background.isBackground = true;
                    background.reverseDirection = false;
                    background.raycastTarget = false;
                    made++;
                }

                so.FindProperty("lungeCooldownArc").objectReferenceValue = arc;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(hud);
            }
            Debug.Log($"[LungeCooldownHud] 런지 쿨다운 호 {made}개 생성");
        }

        private static ArcGauge CreateArc(string name, Transform parent, ArcGauge template)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            Undo.RegisterCreatedObjectUndo(go, "Setup Lunge Cooldown Arc");
            go.layer = template.gameObject.layer;
            go.transform.SetParent(parent, false);
            ArcGauge arc = go.AddComponent<ArcGauge>();
            arc.startAngle = template.startAngle;
            arc.endAngle = template.endAngle;
            arc.thickness = template.thickness;
            arc.segments = template.segments;
            arc.reverseDirection = template.reverseDirection;
            arc.backgroundColor = template.backgroundColor;
            return arc;
        }

        /// <summary>
        /// 명령줄(batchmode)용: Build Settings 첫 씬을 열어 호를 만들고 저장한 뒤 끝낸다.
        /// 예: Unity.exe -batchmode -projectPath . -executeMethod MyDongari.EditorTools.LungeCooldownHudSetup.SetupFromCommandLine -logFile hud.log
        /// </summary>
        public static void SetupFromCommandLine()
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
                    var opened = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    Setup();
                    EditorSceneManager.MarkSceneDirty(opened);
                    if (EditorSceneManager.SaveOpenScenes()) exitCode = 0;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
            EditorApplication.Exit(exitCode);
        }
    }
}
