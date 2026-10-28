using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MyDongari.Visual;

namespace MyDongari.EditorTools
{
    /// <summary>
    /// bernard.fbx를 P1(Mech)·P2(Mech_P2)에 한 번에 붙이는 메뉴. FBX 단위가 기체와 달라 그대로 놓으면 훨씬 크게 나오므로
    /// 임시 상자 부품 전체 높이에 맞춰 배율을 자동으로 정하고, 발바닥을 기체 바닥(캡슐 아래)에 맞춘다.
    /// 임시 부품은 렌더러만 끈다(콜라이더·Rigidbody·판정 오브젝트는 그대로). 다시 누르면 이전 bernard를 지우고 새로 붙인다.
    /// Undo 한 번으로 되돌린다.
    /// </summary>
    public static class BernardModelSetup
    {
        private const string ModelPath = "Assets/Models/bernard.fbx";
        private const string InstanceName = "Bernard";
        private static readonly string[] MechNames = { "Mech", "Mech_P2" };

        [MenuItem("Tools/Apply Bernard Model")]
        private static void ApplyBernard()
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[Apply Bernard Model] {ModelPath}를 찾지 못했습니다.");
                return;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Apply Bernard Model");
            int undoGroup = Undo.GetCurrentGroup();

            int applied = 0;
            foreach (string mechName in MechNames)
            {
                MechVisualRig rig = FindMech(mechName);
                if (rig == null)
                {
                    Debug.LogWarning($"[Apply Bernard Model] 씬에서 '{mechName}'(MechVisualRig 있는 기체)을 찾지 못해 건너뜁니다.");
                    continue;
                }
                Apply(rig, model);
                applied++;
            }

            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log($"[Apply Bernard Model] {applied}대에 적용했습니다. 씬을 저장하세요.");
        }

        /// <summary>이름으로 기체를 찾는다. Mech_P2는 Versus 전용이라 꺼져 있을 수 있어 비활성 오브젝트도 본다.</summary>
        private static MechVisualRig FindMech(string mechName)
        {
            foreach (MechVisualRig rig in Object.FindObjectsByType<MechVisualRig>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (rig.name == mechName) return rig;
            }
            return null;
        }

        private static void Apply(MechVisualRig rig, GameObject model)
        {
            Transform mech = rig.transform;
            string label = mech.name;

            // 6. 중복 방지: 이 기체 아래의 이전 bernard 인스턴스(이 메뉴든 Apply Model To Mechs든)를 먼저 지운다
            RemovePreviousInstances(mech);

            // 비주얼 루트 = 임시 부품 루트. 없으면(임시 부품을 안 만든 기체) 기체 루트
            Transform placeholder = mech.Find(VisualRigSetup.PlaceholderName);
            Transform visualRoot = placeholder != null ? placeholder : mech;

            // 2. 임시 부품은 렌더러만 끈다. 높이는 메시로 재므로 이미 꺼져 있어도 된다
            float placeholderHeight = 0f;
            if (placeholder != null)
            {
                if (TryGetMeshBounds(placeholder, out Bounds placeholderBounds)) placeholderHeight = placeholderBounds.size.y;
                foreach (Renderer renderer in placeholder.GetComponentsInChildren<Renderer>(true))
                {
                    Undo.RecordObject(renderer, "Hide Placeholder Renderer");
                    renderer.enabled = false;
                }
            }
            if (placeholderHeight <= 0f)
            {
                // 임시 부품이 없으면 캡슐 높이를 기준으로
                CapsuleCollider capsule = mech.GetComponent<CapsuleCollider>();
                placeholderHeight = capsule != null ? capsule.height * mech.lossyScale.y : 2f;
                Debug.LogWarning($"[Apply Bernard Model] {label}: 임시 부품 높이를 못 재서 캡슐 높이 {placeholderHeight:0.###}를 기준으로 씁니다.");
            }

            // 3. 비주얼 루트 자식, 위치·회전 0
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model, visualRoot);
            Undo.RegisterCreatedObjectUndo(instance, "Instantiate Bernard");
            instance.name = InstanceName;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            // 모델 부품은 보이기만: FBX에 콜라이더가 들어 있으면 끈다 (피격은 루트 캡슐)
            foreach (Collider col in instance.GetComponentsInChildren<Collider>(true)) col.enabled = false;

            // 4. 배율 = 임시 부품 높이 / bernard 높이
            if (!TryGetMeshBounds(instance.transform, out Bounds modelBounds) || modelBounds.size.y <= 0f)
            {
                Debug.LogWarning($"[Apply Bernard Model] {label}: bernard에서 메시를 찾지 못해 배율을 1로 둡니다.");
            }
            else
            {
                float scale = placeholderHeight / modelBounds.size.y;
                instance.transform.localScale = Vector3.one * scale;
                Debug.Log($"[Apply Bernard Model] {label}: 배율 {scale:0.#####} (임시 부품 높이 {placeholderHeight:0.###} / bernard 높이 {modelBounds.size.y:0.###})");

                // 3. 발바닥(메시 맨 아래)을 기체 바닥(캡슐 아래)에 맞춘다
                TryGetMeshBounds(instance.transform, out modelBounds);
                float feetY = mech.TransformPoint(new Vector3(0f, FeetHeight(mech), 0f)).y;
                instance.transform.position += Vector3.up * (feetY - modelBounds.min.y);
            }

            // 5. 슬롯을 bernard의 같은 이름 뼈로 다시 연결. 못 찾은 슬롯은 경고
            var optionalMissing = new List<string>();
            List<string> missing = VisualRigSetup.AssignSlots(rig, instance.transform, optionalMissing);
            foreach (string slot in missing)
                Debug.LogWarning($"[Apply Bernard Model] {label}: '{slot}' 뼈를 찾지 못했습니다 (이 부품은 움직이지 않음).");
            foreach (string slot in optionalMissing)
                Debug.LogWarning($"[Apply Bernard Model] {label}: '{slot}' 뼈를 찾지 못했습니다 (선택 슬롯 — 아래팔 끝을 펀치 지점으로 씀).");

            // Apply Model To Mechs와 같게 Animator를 리그에 연결하고, 반복 동작 컨트롤러가 있으면 붙인다
            Animator animator = instance.GetComponentInChildren<Animator>(true);
            SerializedObject rigSo = new SerializedObject(rig);
            rigSo.FindProperty("animator").objectReferenceValue = animator;
            rigSo.ApplyModifiedProperties();
            if (animator != null) AnimatorSetup.Configure(animator, AnimatorSetup.LoadController());

            // 7. 저장 표시
            EditorSceneManager.MarkSceneDirty(mech.gameObject.scene);
        }

        private static void RemovePreviousInstances(Transform mech)
        {
            var old = new List<GameObject>();
            foreach (Transform t in mech.GetComponentsInChildren<Transform>(true))
            {
                GameObject go = t.gameObject;
                if (!PrefabUtility.IsOutermostPrefabInstanceRoot(go)) continue;
                if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go) == ModelPath) old.Add(go);
            }
            foreach (GameObject go in old)
            {
                if (go != null) Undo.DestroyObjectImmediate(go);
            }
        }

        /// <summary>
        /// 아래 모든 메시의 월드 경계. 렌더러가 꺼져 있어도 재야 해서(임시 부품) Renderer.bounds 대신
        /// 메시 경계 상자의 꼭짓점 8개를 월드로 옮겨 잰다. 스킨 메시는 렌더러 경계를 쓴다.
        /// </summary>
        private static bool TryGetMeshBounds(Transform root, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                Bounds local = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = local.center + Vector3.Scale(local.extents,
                        new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    Encapsulate(ref bounds, ref any, filter.transform.TransformPoint(corner));
                }
            }
            foreach (SkinnedMeshRenderer skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skinned.sharedMesh == null) continue;
                Encapsulate(ref bounds, ref any, skinned.bounds.min);
                Encapsulate(ref bounds, ref any, skinned.bounds.max);
            }
            return any;
        }

        private static void Encapsulate(ref Bounds bounds, ref bool any, Vector3 point)
        {
            if (!any)
            {
                bounds = new Bounds(point, Vector3.zero);
                any = true;
            }
            else bounds.Encapsulate(point);
        }

        /// <summary>루트 기준 발바닥 높이: 캡슐 콜라이더 바닥 (없으면 -1). ModelImportSetup과 같은 기준.</summary>
        private static float FeetHeight(Transform mech)
        {
            CapsuleCollider capsule = mech.GetComponent<CapsuleCollider>();
            if (capsule == null || capsule.direction != 1) return -1f;
            return capsule.center.y - capsule.height * 0.5f;
        }
    }
}
