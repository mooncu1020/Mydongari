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

        /// <summary>
        /// 모델 로컬 회전(°). 회전은 계산하지 않고 이 값을 그대로 넣는다 — bernard.fbx는 Bake Axis Conversion을 꺼서
        /// Blender 축(Z 위)을 Unity 축(Y 위)으로 세우려면 X -90이 필요하다. 다른 모델이면 이 값을 바꾼다.
        /// </summary>
        public static Vector3 ModelLocalRotation = new Vector3(-90f, 0f, 0f);

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
            // bernard는 임시 부품 루트 아래에 들어가므로, 임시 부품을 다룰 때 bernard 아래 오브젝트는 절대 건드리지 않는다 (H2)
            float placeholderHeight = 0f;
            if (placeholder != null)
            {
                if (TryGetMeshBounds(placeholder, out Bounds placeholderBounds, IsUnderModel)) placeholderHeight = placeholderBounds.size.y;
                foreach (Renderer renderer in placeholder.GetComponentsInChildren<Renderer>(true))
                {
                    if (IsUnderModel(renderer.transform)) continue;
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

            // 3. 비주얼 루트 자식, 위치 0, 회전 = 모델 로컬 회전 필드 값 그대로. 배율은 FBX 프리팹 루트 값에서 시작한다.
            // 높이 측정·발바닥 맞추기는 회전을 넣은 뒤(선 자세)에 한다
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model, visualRoot);
            Undo.RegisterCreatedObjectUndo(instance, "Instantiate Bernard");
            instance.name = InstanceName;
            Vector3 baseScale = model.transform.localScale;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Euler(ModelLocalRotation);
            instance.transform.localScale = baseScale;

            // 모델 부품은 보이기만: FBX에 콜라이더가 들어 있으면 끈다 (피격은 루트 캡슐)
            foreach (Collider col in instance.GetComponentsInChildren<Collider>(true)) col.enabled = false;

            // 파츠 누락 점검: bernard 아래 렌더러는 전부 켠다 (Blender에서 숨긴 오브젝트는 Import Visibility로 꺼진 채 들어온다)
            var wasOff = new List<string>();
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.enabled) continue;
                renderer.enabled = true;
                wasOff.Add(renderer.name);
            }
            if (wasOff.Count > 0)
                Debug.LogWarning($"[Apply Bernard Model] {label}: 꺼져 있던 bernard 렌더러 {wasOff.Count}개를 켰습니다: {string.Join(", ", wasOff)}");
            else
                Debug.Log($"[Apply Bernard Model] {label}: bernard 렌더러가 모두 켜져 있습니다.");

            // 4. 배율 = 임시 부품 높이 / bernard 높이. 회전을 적용한(선 자세) 상태의 렌더러 바운드로 잰다
            if (!TryGetRendererBounds(instance.transform, out Bounds modelBounds) || modelBounds.size.y <= 0f)
            {
                Debug.LogWarning($"[Apply Bernard Model] {label}: bernard에서 렌더러를 찾지 못해 배율을 그대로 둡니다.");
            }
            else
            {
                float modelHeight = modelBounds.size.y;
                float ratio = placeholderHeight / modelHeight;
                instance.transform.localScale = baseScale * ratio;

                // 3. 크기를 맞춘 뒤 다시 재서 발바닥(바운드 최저점)을 기체 바닥(캡슐 아래)에 맞춘다
                TryGetRendererBounds(instance.transform, out modelBounds);
                float feetY = GroundWorldY(mech);
                instance.transform.position += Vector3.up * (feetY - modelBounds.min.y);
                TryGetRendererBounds(instance.transform, out modelBounds);

                Debug.Log($"[Apply Bernard Model] {label}: 임시 파츠 높이 {placeholderHeight:0.###} m, bernard 높이 {modelHeight:0.###} m(원래 크기), " +
                          $"적용 배율 {ratio:0.#####} (최종 localScale {instance.transform.localScale.x:0.#####}), " +
                          $"맞춘 뒤 높이 {modelBounds.size.y:0.###} m · 발바닥 y {modelBounds.min.y:0.###} (기체 바닥 y {feetY:0.###})");
            }

            // 5. 슬롯을 bernard의 같은 이름 뼈로 다시 연결. 못 찾은 슬롯은 경고
            var optionalMissing = new List<string>();
            List<string> missing = VisualRigSetup.AssignSlots(rig, instance.transform, optionalMissing);
            foreach (string slot in missing)
                Debug.LogWarning($"[Apply Bernard Model] {label}: '{slot}' 뼈를 찾지 못했습니다 (이 부품은 움직이지 않음).");
            foreach (string slot in optionalMissing)
                Debug.LogWarning($"[Apply Bernard Model] {label}: '{slot}' 뼈를 찾지 못했습니다 (선택 슬롯 — 아래팔 끝을 펀치 지점으로 씀).");

            // bernard는 MechVisualRig만 움직인다: FBX가 붙인 Animator는 컨트롤러를 비우고 끈다
            // (반복 동작 컨트롤러가 붙으면 같은 이름의 뼈를 Animator도 움직여서 리그와 다툰다). 리그의 Animator 칸도 비운다
            foreach (Animator animator in instance.GetComponentsInChildren<Animator>(true))
            {
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                animator.enabled = false;
            }
            SerializedObject rigSo = new SerializedObject(rig);
            rigSo.FindProperty("animator").objectReferenceValue = null;
            rigSo.ApplyModifiedProperties();

            // 7. 저장 표시
            EditorSceneManager.MarkSceneDirty(mech.gameObject.scene);
        }

        /// <summary>bernard 인스턴스(이름 Bernard 또는 bernard.fbx 프리팹 인스턴스) 아래인지.</summary>
        private static bool IsUnderModel(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent)
            {
                if (p.name == InstanceName) return true;
                GameObject go = p.gameObject;
                if (PrefabUtility.IsOutermostPrefabInstanceRoot(go) && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go) == ModelPath) return true;
            }
            return false;
        }

        private static void RemovePreviousInstances(Transform mech)
        {
            var old = new List<GameObject>();
            foreach (Transform t in mech.GetComponentsInChildren<Transform>(true))
            {
                GameObject go = t.gameObject;
                // 이미 지울 목록에 든 bernard의 자식은 부모와 같이 지워진다 (프리팹 인스턴스 자식은 따로 못 지움)
                if (old.Exists(o => t.IsChildOf(o.transform))) continue;
                // 이 메뉴가 넣은 것(이름) — 프리팹 연결이 끊겼어도(Unpack) 지운다
                if (go.name == InstanceName && t != mech)
                {
                    old.Add(go);
                    continue;
                }
                if (!PrefabUtility.IsOutermostPrefabInstanceRoot(go)) continue;
                if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go) == ModelPath) old.Add(go);
            }
            if (old.Count > 0) Debug.Log($"[Apply Bernard Model] {mech.name}: 이전 bernard {old.Count}개를 지우고 새로 붙입니다.");
            foreach (GameObject go in old)
            {
                if (go != null) Undo.DestroyObjectImmediate(go);
            }
        }

        /// <summary>
        /// 아래 모든 메시의 월드 경계. 렌더러가 꺼져 있어도 재야 해서(임시 부품) Renderer.bounds 대신
        /// 메시 경계 상자의 꼭짓점 8개를 월드로 옮겨 잰다. 스킨 메시는 렌더러 경계를 쓴다.
        /// </summary>
        private static bool TryGetMeshBounds(Transform root, out Bounds bounds, System.Func<Transform, bool> exclude = null)
        {
            bounds = default;
            bool any = false;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                if (exclude != null && exclude(filter.transform)) continue;
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
                if (exclude != null && exclude(skinned.transform)) continue;
                Encapsulate(ref bounds, ref any, skinned.bounds.min);
                Encapsulate(ref bounds, ref any, skinned.bounds.max);
            }
            return any;
        }

        /// <summary>
        /// 아래 모든 렌더러(메시·스킨 메시)의 월드 바운드. 방금 넣은 bernard는 렌더러가 켜져 있어 회전·배율이 반영된 값이 나온다.
        /// 켜진 렌더러가 하나도 없으면 메시 꼭짓점 방식으로 잰다.
        /// </summary>
        private static bool TryGetRendererBounds(Transform root, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                Encapsulate(ref bounds, ref any, renderer.bounds.min);
                Encapsulate(ref bounds, ref any, renderer.bounds.max);
            }
            return any || TryGetMeshBounds(root, out bounds);
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

        /// <summary>
        /// 기체 바닥(월드 y) = 기체 루트의 물리 콜라이더(트리거 제외) 바운드 최저점. 런타임 접지 보정(MechVisualRig)과 같은 기준.
        /// 콜라이더가 없으면 루트 기준 -1 m.
        /// </summary>
        private static float GroundWorldY(Transform mech)
        {
            float lowest = float.MaxValue;
            foreach (Collider col in mech.GetComponents<Collider>())
            {
                if (col == null || !col.enabled || col.isTrigger) continue;
                if (col.bounds.min.y < lowest) lowest = col.bounds.min.y;
            }
            return lowest < float.MaxValue ? lowest : mech.TransformPoint(new Vector3(0f, -1f, 0f)).y;
        }
    }
}
