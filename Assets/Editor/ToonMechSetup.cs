using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MyDongari.EditorTools
{
    /// <summary>
    /// 셀 셰이딩 세팅 메뉴 (S1·S3). 셰이더 그래프(ToonMech)·M_ToonMech는 건드리지 않는다.
    /// S1: M_ToonMech를 복제해 장갑·관절 머티리얼을 만들고, bernard.fbx의 Mecha_Armor·Mecha_Joint에 임포터 머티리얼 리맵으로 연결.
    ///     씬에 풀어 둔 bernard(Mech_P2)는 렌더러가 FBX 안 머티리얼을 직접 들고 있어 리맵만 하면 끊기므로, 리맵 전에 씬 렌더러부터 바꾼다.
    /// S3: 외곽선 셰이더(Hidden/MyDongari/ScreenOutline) 머티리얼을 만들고, 쓰이는 모든 URP 렌더러에 FullScreenPassRendererFeature로 붙인다.
    /// </summary>
    public static class ToonMechSetup
    {
        private const string FbxPath = "Assets/Models/bernard.fbx";
        private const string BaseMaterialPath = "Assets/Materials/M_ToonMech.mat";
        private const string ArmorMaterialPath = "Assets/Materials/M_ToonMech_Armor.mat";
        private const string JointMaterialPath = "Assets/Materials/M_ToonMech_Joint.mat";
        private const string ArmorSourceName = "Mecha_Armor";
        private const string JointSourceName = "Mecha_Joint";
        private static readonly Color ArmorColor = new Color32(0x8A, 0x8D, 0x94, 0xFF);
        private static readonly Color JointColor = new Color32(0x3A, 0x3C, 0x41, 0xFF);

        public const string OutlineShaderName = "Hidden/MyDongari/ScreenOutline";
        private const string OutlineMaterialPath = "Assets/Materials/M_ScreenOutline.mat";
        public const string OutlineFeatureName = "ScreenOutline";

        // ---------------- S1 ----------------

        [MenuItem("Tools/Toon Mech/1. Apply Toon Materials To Bernard (S1)")]
        private static void ApplyToonMaterials()
        {
            Material baseMaterial = AssetDatabase.LoadAssetAtPath<Material>(BaseMaterialPath);
            if (baseMaterial == null)
            {
                EditorUtility.DisplayDialog("Toon Mech", $"{BaseMaterialPath}가 없습니다.", "확인");
                return;
            }

            var report = new StringBuilder();
            Material armor = EnsureMaterial(ArmorMaterialPath, baseMaterial, ArmorColor, report);
            Material joint = EnsureMaterial(JointMaterialPath, baseMaterial, JointColor, report);

            // 리맵하면 FBX 안 머티리얼이 사라지므로, 그 전에 씬 렌더러가 들고 있는 FBX 머티리얼을 새 머티리얼로 바꾼다
            int replaced = ReplaceSceneMaterials(armor, joint, report);
            report.AppendLine($"씬 렌더러 머티리얼 칸 {replaced}개 교체 (씬을 저장하세요)");

            var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            if (importer == null)
            {
                report.AppendLine($"{FbxPath} 임포터를 못 찾음");
            }
            else
            {
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), ArmorSourceName), armor);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), JointSourceName), joint);
                importer.SaveAndReimport();
                report.AppendLine($"bernard.fbx 리맵: {ArmorSourceName} → {armor.name}, {JointSourceName} → {joint.name}");
            }

            report.AppendLine(RenderFaceReport(baseMaterial, armor, joint));
            Debug.Log("[ToonMechSetup] " + report);
            EditorUtility.DisplayDialog("Toon Mech", report.ToString(), "확인");
        }

        /// <summary>있으면 그대로(사용자가 고친 색 유지), 없으면 M_ToonMech를 복제해 기본색만 넣어 만든다.</summary>
        private static Material EnsureMaterial(string path, Material baseMaterial, Color baseColor, StringBuilder report)
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                report.AppendLine($"{path}: 이미 있음 (색 그대로)");
                return existing;
            }
            var material = new Material(baseMaterial) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
            AssetDatabase.CreateAsset(material, path);
            AssetDatabase.SaveAssets();
            report.AppendLine($"{path}: 만듦 (_BaseColor #{ColorUtility.ToHtmlStringRGB(baseColor)})");
            return material;
        }

        private static int ReplaceSceneMaterials(Material armor, Material joint, StringBuilder report)
        {
            int replaced = 0;
            var unknown = new HashSet<string>();
            foreach (Renderer renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material m = materials[i];
                    if (m == null || AssetDatabase.GetAssetPath(m) != FbxPath) continue;
                    if (m.name.Contains("Armor")) materials[i] = armor;
                    else if (m.name.Contains("Joint")) materials[i] = joint;
                    else
                    {
                        unknown.Add(m.name);
                        continue;
                    }
                    changed = true;
                    replaced++;
                }
                if (!changed) continue;
                Undo.RecordObject(renderer, "Apply Toon Materials");
                renderer.sharedMaterials = materials;
                EditorSceneManager.MarkSceneDirty(renderer.gameObject.scene);
            }
            if (unknown.Count > 0) report.AppendLine("이름으로 못 정한 FBX 머티리얼(그대로 둠): " + string.Join(", ", unknown));
            return replaced;
        }

        /// <summary>Render Face 확인: 머티리얼 _Cull(0 = Both, 1 = Back, 2 = Front)이 Both면 알린다.</summary>
        private static string RenderFaceReport(params Material[] materials)
        {
            var both = new List<string>();
            foreach (Material m in materials)
            {
                if (m != null && m.HasProperty("_Cull") && Mathf.Approximately(m.GetFloat("_Cull"), (float)CullMode.Off)) both.Add(m.name);
            }
            return both.Count > 0 ? "Render Face가 Both인 머티리얼: " + string.Join(", ", both) : "Render Face가 Both인 머티리얼 없음";
        }

        // ---------------- S3 ----------------

        [MenuItem("Tools/Toon Mech/2. Add Screen Outline Renderer Feature (S3)")]
        private static void AddScreenOutline()
        {
            Shader shader = Shader.Find(OutlineShaderName);
            if (shader == null)
            {
                EditorUtility.DisplayDialog("Screen Outline", $"셰이더 {OutlineShaderName}를 못 찾았습니다 (Assets/Materials/ScreenOutline.shader 컴파일 오류 확인).", "확인");
                return;
            }
            Material material = AssetDatabase.LoadAssetAtPath<Material>(OutlineMaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "M_ScreenOutline" };
                AssetDatabase.CreateAsset(material, OutlineMaterialPath);
            }

            var report = new StringBuilder();
            foreach (UniversalRendererData data in UsedRendererData())
            {
                FullScreenPassRendererFeature feature = FindOutline(data);
                bool created = feature == null;
                if (created)
                {
                    feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
                    feature.name = OutlineFeatureName;
                    AssetDatabase.AddObjectToAsset(feature, data);
                }
                feature.passMaterial = material;
                feature.passIndex = 0;
                feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
                feature.fetchColorBuffer = true;
                feature.requirements = ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal;
                EditorUtility.SetDirty(feature);
                if (created) LinkFeature(data, feature);
                report.AppendLine($"{data.name}: 외곽선 기능 {(created ? "추가" : "갱신")}");
            }
            AssetDatabase.SaveAssets();
            if (report.Length == 0) report.AppendLine("URP 렌더러를 못 찾았습니다");
            report.AppendLine($"선 굵기·색·민감도는 {OutlineMaterialPath}");
            Debug.Log("[ToonMechSetup] " + report);
            EditorUtility.DisplayDialog("Screen Outline", report.ToString(), "확인");
        }

        /// <summary>기본·품질 단계별 파이프라인 에셋이 쓰는 URP 렌더러 전부 (화면 분할 두 카메라는 기본 렌더러를 쓴다).</summary>
        private static List<UniversalRendererData> UsedRendererData()
        {
            var pipelines = new List<UniversalRenderPipelineAsset>();
            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset defaultAsset) pipelines.Add(defaultAsset);
            for (int i = 0; i < QualitySettings.count; i++)
            {
                if (QualitySettings.GetRenderPipelineAssetAt(i) is UniversalRenderPipelineAsset asset && !pipelines.Contains(asset)) pipelines.Add(asset);
            }
            // rendererDataList는 ReadOnlySpan이라 이터레이터(yield) 안에서 못 돌린다 — 목록으로 모은다
            var result = new List<UniversalRendererData>();
            foreach (UniversalRenderPipelineAsset pipeline in pipelines)
            {
                foreach (ScriptableRendererData data in pipeline.rendererDataList)
                {
                    if (data is UniversalRendererData universal && !result.Contains(universal)) result.Add(universal);
                }
            }
            return result;
        }

        private static FullScreenPassRendererFeature FindOutline(ScriptableRendererData data)
        {
            foreach (ScriptableRendererFeature feature in data.rendererFeatures)
            {
                if (feature is FullScreenPassRendererFeature fullScreen && feature.name == OutlineFeatureName) return fullScreen;
            }
            return null;
        }

        /// <summary>URP 렌더러 인스펙터의 Add Renderer Feature와 같은 방식: 목록과 로컬 ID 맵에 같이 넣는다.</summary>
        private static void LinkFeature(ScriptableRendererData data, ScriptableRendererFeature feature)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);
            var so = new SerializedObject(data);
            SerializedProperty list = so.FindProperty("m_RendererFeatures");
            SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(data);
        }
    }
}
