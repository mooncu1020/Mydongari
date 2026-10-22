using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MyDongari.Visual;

namespace MyDongari.EditorTools
{
    /// <summary>
    /// 임시 상자 대신 모델(FBX)을 기체에 붙이는 메뉴. 모델 안에서 리그 슬롯 이름(Body, Head, Arm_L_Upper …)의 부품을 찾아
    /// MechVisualRig에 연결하고 VisualPlaceholder를 끈다. 못 찾은 이름은 목록으로 경고(그 슬롯은 비워져 안 움직임).
    /// 모델 원점은 발바닥 가운데라고 보고, 루트 캡슐 바닥에 맞춰 놓는다. 자세한 규칙은 docs/MODEL_IMPORT.md.
    /// </summary>
    public static class ModelImportSetup
    {
        public const string ModelName = "VisualModel";

        [MenuItem("Mydongari/Visual/Apply Model To Mechs")]
        private static void ApplyModel()
        {
            List<MechVisualRig> rigs = TargetRigs();
            if (rigs.Count == 0)
            {
                EditorUtility.DisplayDialog("Apply Model", "MechVisualRig가 있는 기체가 없습니다. 먼저 Mydongari/Visual/Create Placeholder Parts를 실행하세요.", "확인");
                return;
            }

            string path = EditorUtility.OpenFilePanel("기체 모델(FBX) 선택", Application.dataPath, "fbx");
            if (string.IsNullOrEmpty(path)) return;
            string assetPath = ToAssetPath(path);
            GameObject model = assetPath != null ? AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) : null;
            if (model == null)
            {
                EditorUtility.DisplayDialog("Apply Model", "Assets 폴더 안의 FBX를 골라 주세요. (프로젝트 밖 파일은 먼저 Assets로 복사)", "확인");
                return;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Mydongari Apply Model");
            int undoGroup = Undo.GetCurrentGroup();

            List<string> missing = null;
            foreach (MechVisualRig rig in rigs)
            {
                missing = Apply(rig, model);
            }

            Undo.CollapseUndoOperations(undoGroup);
            string names = string.Join(", ", rigs.ConvertAll(r => r.name));
            string message = $"{model.name}을(를) {rigs.Count}대({names})에 붙였습니다.\n";
            message += missing == null || missing.Count == 0
                ? "부품 10개를 모두 찾았습니다."
                : $"⚠ 모델에서 찾지 못한 부품 {missing.Count}개 (이 부품은 움직이지 않음):\n- " + string.Join("\n- ", missing);
            message += "\n\n정면(+Z)·크기·피벗을 확인하고, 축이 다른 부품은 MechVisualRig의 Rotation Correction으로 맞추세요. 씬을 저장하세요.";
            EditorUtility.DisplayDialog("Apply Model", message, "확인");
        }

        /// <summary>선택한 기체들. 아무것도 안 골랐으면 씬의 모든 기체 (두 기체·더미 같은 모델).</summary>
        private static List<MechVisualRig> TargetRigs()
        {
            var rigs = new List<MechVisualRig>();
            foreach (GameObject go in Selection.gameObjects)
            {
                MechVisualRig rig = go.GetComponentInParent<MechVisualRig>();
                if (rig != null && !rigs.Contains(rig)) rigs.Add(rig);
            }
            if (rigs.Count > 0) return rigs;
            rigs.AddRange(Object.FindObjectsByType<MechVisualRig>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            return rigs;
        }

        private static List<string> Apply(MechVisualRig rig, GameObject model)
        {
            Transform mech = rig.transform;

            // 이전 모델은 지우고 새로 (같은 메뉴를 다시 실행해 모델을 갈아 끼울 수 있게)
            Transform old = mech.Find(ModelName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model, mech);
            Undo.RegisterCreatedObjectUndo(instance, "Instantiate Model");
            instance.name = ModelName;
            instance.transform.localPosition = new Vector3(0f, FeetHeight(mech.gameObject), 0f);
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            // 모델 부품은 보이기만: 모델에 콜라이더가 들어 있으면 끈다 (피격은 루트 캡슐)
            foreach (Collider col in instance.GetComponentsInChildren<Collider>(true))
            {
                Undo.RecordObject(col, "Disable Model Collider");
                col.enabled = false;
            }

            List<string> missing = VisualRigSetup.AssignSlots(rig, instance.transform);

            Transform placeholder = mech.Find(VisualRigSetup.PlaceholderName);
            if (placeholder != null)
            {
                Undo.RecordObject(placeholder.gameObject, "Hide Placeholder");
                placeholder.gameObject.SetActive(false);
            }

            EditorSceneManager.MarkSceneDirty(mech.gameObject.scene);
            return missing;
        }

        /// <summary>루트 기준 발바닥 높이: 캡슐 콜라이더 바닥 (없으면 -1).</summary>
        private static float FeetHeight(GameObject mech)
        {
            CapsuleCollider capsule = mech.GetComponent<CapsuleCollider>();
            if (capsule == null || capsule.direction != 1) return -1f;
            return capsule.center.y - capsule.height * 0.5f;
        }

        private static string ToAssetPath(string fullPath)
        {
            string dataPath = Application.dataPath.Replace('\\', '/');
            fullPath = fullPath.Replace('\\', '/');
            if (!fullPath.StartsWith(dataPath)) return null;
            return "Assets" + fullPath.Substring(dataPath.Length);
        }
    }
}
