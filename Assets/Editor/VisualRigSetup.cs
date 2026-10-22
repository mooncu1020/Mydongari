using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MyDongari.Combat;
using MyDongari.Visual;

namespace MyDongari.EditorTools
{
    /// <summary>
    /// 모델이 오기 전까지 쓸 임시 상자 부품. 씬의 모든 기체(MeleeSystem이 있는 오브젝트: P1·P2·더미)에
    /// 관절 피벗 계층(VisualPlaceholder)을 만들고 MechVisualRig 슬롯에 연결한다.
    /// 크기는 현재 캡슐 콜라이더(반지름 0.5 · 높이 2, 중심 = 루트, 발바닥 y = -1)에 맞췄다.
    /// 부품엔 콜라이더가 없고(보이기만), 루트의 캡슐 렌더러는 끈다. Undo 한 번으로 되돌린다.
    /// </summary>
    public static class VisualRigSetup
    {
        public const string PlaceholderName = "VisualPlaceholder";

        [MenuItem("Mydongari/Visual/Create Placeholder Parts")]
        private static void CreatePlaceholders()
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Mydongari Create Placeholder Parts");
            int undoGroup = Undo.GetCurrentGroup();

            int created = 0;
            int skipped = 0;
            foreach (MeleeSystem melee in Object.FindObjectsByType<MeleeSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                GameObject mech = melee.gameObject;
                if (mech.transform.Find(PlaceholderName) != null)
                {
                    skipped++;
                    continue;
                }
                Build(mech);
                created++;
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.DisplayDialog("Placeholder Parts",
                $"임시 부품을 만든 기체: {created}대" + (skipped > 0 ? $" (이미 있어서 건너뜀: {skipped}대)" : "") +
                "\n루트 캡슐 렌더러는 껐습니다(콜라이더는 그대로). Ctrl+Z로 되돌릴 수 있습니다. 씬을 저장하세요.", "확인");
        }

        private static void Build(GameObject mech)
        {
            // 기체 원래 머티리얼(툰)을 그대로 써서 색 틴트·조명이 같게
            MeshRenderer rootRenderer = mech.GetComponent<MeshRenderer>();
            Material material = rootRenderer != null ? rootRenderer.sharedMaterial : null;

            Transform root = CreatePivot(PlaceholderName, mech.transform, Vector3.zero, Vector3.zero);

            // 다리: 엉덩이 피벗, 아래로
            Transform legL = CreatePivot("Leg_L", root, new Vector3(-0.22f, -0.2f, 0f), Vector3.zero);
            CreateBox(legL, new Vector3(0f, -0.4f, 0f), new Vector3(0.24f, 0.8f, 0.28f), material);
            Transform legR = CreatePivot("Leg_R", root, new Vector3(0.22f, -0.2f, 0f), Vector3.zero);
            CreateBox(legR, new Vector3(0f, -0.4f, 0f), new Vector3(0.24f, 0.8f, 0.28f), material);

            // 몸통: 허리 피벗, 위로
            Transform body = CreatePivot("Body", root, new Vector3(0f, -0.2f, 0f), Vector3.zero);
            CreateBox(body, new Vector3(0f, 0.4f, 0f), new Vector3(0.8f, 0.8f, 0.5f), material);

            // 머리: 목 피벗. 앞면 가늘게 튀어나온 바이저로 정면을 알아보게
            Transform headPivot = CreatePivot("Head", body, new Vector3(0f, 0.8f, 0f), Vector3.zero);
            CreateBox(headPivot, new Vector3(0f, 0.17f, 0f), new Vector3(0.38f, 0.32f, 0.4f), material);
            CreateBox(headPivot, new Vector3(0f, 0.19f, 0.21f), new Vector3(0.3f, 0.07f, 0.04f), material);

            BuildArm(body, "L", -1f, material);
            BuildArm(body, "R", 1f, material);

            if (rootRenderer != null)
            {
                Undo.RecordObject(rootRenderer, "Hide Capsule Renderer");
                rootRenderer.enabled = false;
            }

            MechVisualRig rig = mech.GetComponent<MechVisualRig>();
            if (rig == null) rig = Undo.AddComponent<MechVisualRig>(mech);
            AssignSlots(rig, root);
            EditorSceneManager.MarkSceneDirty(mech.scene);
        }

        /// <summary>어깨 → 팔꿈치 → 손목 피벗. 쉴 때 자세는 위팔 살짝 앞, 아래팔은 앞으로 굽힌 복서 자세.</summary>
        private static void BuildArm(Transform body, string side, float sign, Material material)
        {
            Transform upper = CreatePivot($"Arm_{side}_Upper", body, new Vector3(0.52f * sign, 0.72f, 0f), new Vector3(-10f, 0f, 0f));
            CreateBox(upper, new Vector3(0f, -0.21f, 0f), new Vector3(0.2f, 0.42f, 0.22f), material);
            Transform lower = CreatePivot($"Arm_{side}_Lower", upper, new Vector3(0f, -0.42f, 0f), new Vector3(-75f, 0f, 0f));
            CreateBox(lower, new Vector3(0f, -0.19f, 0f), new Vector3(0.18f, 0.38f, 0.2f), material);
            Transform fist = CreatePivot($"Fist_{side}", lower, new Vector3(0f, -0.38f, 0f), Vector3.zero);
            CreateBox(fist, new Vector3(0f, -0.1f, 0f), new Vector3(0.24f, 0.24f, 0.24f), material);
        }

        /// <summary>리그 슬롯 이름으로 계층에서 찾아 연결한다 (모델 교체 메뉴도 같은 함수를 쓴다).</summary>
        public static System.Collections.Generic.List<string> AssignSlots(MechVisualRig rig, Transform searchRoot)
        {
            var missing = new System.Collections.Generic.List<string>();
            SerializedObject so = new SerializedObject(rig);
            for (int i = 0; i < MechVisualRig.SlotNames.Length; i++)
            {
                Transform found = FindSlotTransform(searchRoot, MechVisualRig.SlotNames[i]);
                if (found == null) missing.Add(MechVisualRig.SlotNames[i]);
                so.FindProperty(MechVisualRig.SlotFieldNames[i] + ".target").objectReferenceValue = found;
            }
            so.ApplyModifiedProperties();
            return missing;
        }

        /// <summary>
        /// 슬롯 이름의 Transform을 찾는다. 리깅된 모델은 같은 이름의 뼈와 메시가 둘 다 있을 수 있어서
        /// 메시가 없는 Transform(뼈·빈 오브젝트)을 먼저 고르고, 없으면 메시 오브젝트(리깅 안 한 모델)를 쓴다.
        /// </summary>
        public static Transform FindSlotTransform(Transform root, string name)
        {
            Transform meshMatch = null;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != name) continue;
                bool hasMesh = t.GetComponent<MeshFilter>() != null || t.GetComponent<Renderer>() != null;
                if (!hasMesh) return t;
                if (meshMatch == null) meshMatch = t;
            }
            return meshMatch;
        }

        public static Transform FindDeep(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            foreach (Transform child in parent)
            {
                Transform found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static Transform CreatePivot(string name, Transform parent, Vector3 localPosition, Vector3 localEuler)
        {
            GameObject go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create Pivot");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            return go.transform;
        }

        private static void CreateBox(Transform parent, Vector3 center, Vector3 size, Material material)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            // 보이기만: 물리·피격은 루트 캡슐이 맡는다
            Object.DestroyImmediate(box.GetComponent<Collider>());
            box.name = "Box";
            Undo.RegisterCreatedObjectUndo(box, "Create Box");
            box.transform.SetParent(parent, false);
            box.transform.localPosition = center;
            box.transform.localScale = size;
            if (material != null) box.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
    }
}
