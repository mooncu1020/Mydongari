using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MyDongari.Combat;
using MyDongari.Mech;

namespace MyDongari.EditorTools
{
    /// <summary>
    /// BernardTuning 에셋 생성과 기체 연결 메뉴. (예전 컴포넌트 값 복사 메뉴는 복사 확인 후 C5에서 삭제)
    /// </summary>
    public static class TuningSetup
    {
        private const string ResourcesFolder = "Assets/Resources";
        private static readonly string AssetPath = ResourcesFolder + "/" + BernardTuning.ResourcesName + ".asset";

        [MenuItem("Mydongari/Tuning/Create Default Tuning And Assign")]
        private static void CreateAndAssignMenu()
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Mydongari Create Tuning");
            int undoGroup = Undo.GetCurrentGroup();

            bool created;
            BernardTuning tuning = EnsureAsset(out created);
            int assigned = AssignToSceneMechs(tuning);

            Undo.CollapseUndoOperations(undoGroup);
            Selection.activeObject = tuning;
            EditorUtility.DisplayDialog("Bernard Tuning",
                (created ? $"{AssetPath}를 코드 기본값으로 만들었습니다.\n" : $"기존 에셋을 사용합니다: {AssetDatabase.GetAssetPath(tuning)}\n") +
                $"기체 {assigned}대(MeleeSystem이 있는 오브젝트)에 MechTuning을 연결했습니다. 씬을 저장하세요.", "확인");
        }

        /// <summary>
        /// 이미 저장된 에셋 값은 코드 기본값을 바꿔도 그대로라, 피드백으로 바뀐 값을 에셋에 넣는다 (.asset을 손으로 고치지 않으려고).
        /// O1: 걷기 최대 속도 2.5m/s.
        /// </summary>
        [MenuItem("Mydongari/Tuning/Apply Feedback Values (O1 걷기 2.5m/s)")]
        private static void ApplyFeedbackValuesMenu()
        {
            BernardTuning tuning = EnsureAsset(out _);
            var so = new SerializedObject(tuning);
            var changes = new System.Text.StringBuilder();
            SetFloat(so, "movement.maxWalkSpeed", 2.5f, changes);
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(tuning);
            AssetDatabase.SaveAssets();
            Selection.activeObject = tuning;
            EditorUtility.DisplayDialog("Bernard Tuning", changes.Length > 0 ? "바꾼 값:\n" + changes : "이미 모두 적용돼 있습니다.", "확인");
        }

        private static void SetFloat(SerializedObject so, string path, float value, System.Text.StringBuilder changes)
        {
            SerializedProperty property = so.FindProperty(path);
            if (property == null || Mathf.Approximately(property.floatValue, value)) return;
            changes.AppendLine($"{path}: {property.floatValue} → {value}");
            property.floatValue = value;
        }

        /// <summary>프로젝트에 BernardTuning 에셋이 있으면 그걸, 없으면 Resources에 코드 기본값으로 만든다.</summary>
        public static BernardTuning EnsureAsset(out bool created)
        {
            created = false;
            BernardTuning existing = AssetDatabase.LoadAssetAtPath<BernardTuning>(AssetPath);
            if (existing != null) return existing;

            string[] guids = AssetDatabase.FindAssets("t:" + nameof(BernardTuning));
            if (guids.Length > 0)
            {
                return AssetDatabase.LoadAssetAtPath<BernardTuning>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            if (!AssetDatabase.IsValidFolder(ResourcesFolder)) AssetDatabase.CreateFolder("Assets", "Resources");
            BernardTuning tuning = ScriptableObject.CreateInstance<BernardTuning>();
            AssetDatabase.CreateAsset(tuning, AssetPath);
            AssetDatabase.SaveAssets();
            created = true;
            return tuning;
        }

        /// <summary>씬의 모든 기체(MeleeSystem이 있는 오브젝트 — 플레이어 기체와 더미)에 같은 에셋을 연결한다.</summary>
        public static int AssignToSceneMechs(BernardTuning tuning)
        {
            int count = 0;
            foreach (MeleeSystem melee in Object.FindObjectsByType<MeleeSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                AssignTo(melee.gameObject, tuning);
                count++;
            }
            return count;
        }

        /// <summary>기체 하나에 MechTuning을 붙이고(없으면) 에셋을 연결한다. 호출 쪽 Undo 그룹에 포함된다.</summary>
        public static void AssignTo(GameObject mech, BernardTuning tuning)
        {
            if (mech == null || tuning == null) return;
            MechTuning holder = mech.GetComponent<MechTuning>();
            if (holder == null) holder = Undo.AddComponent<MechTuning>(mech);

            SerializedObject so = new SerializedObject(holder);
            so.FindProperty("tuning").objectReferenceValue = tuning;
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(mech.scene);
        }
    }
}
