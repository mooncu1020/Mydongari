using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MyDongari.Core;

namespace MyDongari.EditorTools
{
    /// <summary>
    /// V5 접지 레이어 세팅. "Walkable"(설 수 있는 땅 — 위를 향한 면만 땅으로 본다)과 "Wall"(벽) 레이어를 프로젝트에 만들고
    /// 맵 콜라이더에 지정한다. 런타임 판정은 Core/GroundLayers.
    /// - Assign Arena Ground Layers: 지금 맵(AsteroidArena)의 바닥·바위 → Walkable, 경계 벽 → Wall. 예전 맵(Map_Spaceport.)이 켜져 있으면 그 바닥도.
    /// - Selected → Walkable / Wall: 선택한 오브젝트와 자식의 콜라이더 오브젝트에 지정 (벨포레스트 발판·고지대·벽용).
    /// 지정 후 씬을 저장해야 빌드에 들어간다.
    /// </summary>
    public static class GroundLayerSetup
    {
        private const string ArenaRootName = "AsteroidArena";
        private const string OldMapName = "Map_Spaceport.";

        [MenuItem("Mydongari/Map/Assign Arena Ground Layers (바닥·바위 = Walkable, 경계 벽 = Wall)")]
        private static void AssignArena()
        {
            if (!EnsureLayers(out int walkable, out int wall)) return;
            var report = new StringBuilder();
            int changed = 0;

            GameObject arena = GameObject.Find(ArenaRootName);
            if (arena != null)
            {
                changed += AssignChild(arena.transform, "Floor_Collider", walkable, report);
                changed += AssignChild(arena.transform, "Rocks", walkable, report);
                changed += AssignChild(arena.transform, "Boundary_Wall", wall, report);
            }
            else
            {
                report.AppendLine($"'{ArenaRootName}'이 씬에 없습니다 (Tools/Build Asteroid Arena로 만든 맵).");
            }

            GameObject oldMap = GameObject.Find(OldMapName);
            if (oldMap != null && oldMap.activeInHierarchy)
            {
                changed += AssignColliders(oldMap.transform, walkable, report);
            }

            Finish(changed, report);
        }

        [MenuItem("Mydongari/Map/Selected → Walkable (설 수 있는 땅)")]
        private static void AssignSelectedWalkable() => AssignSelected(useWall: false);

        [MenuItem("Mydongari/Map/Selected → Wall (벽)")]
        private static void AssignSelectedWall() => AssignSelected(useWall: true);

        [MenuItem("Mydongari/Map/Selected → Walkable (설 수 있는 땅)", true)]
        [MenuItem("Mydongari/Map/Selected → Wall (벽)", true)]
        private static bool HasSelection() => Selection.gameObjects.Length > 0;

        private static void AssignSelected(bool useWall)
        {
            if (!EnsureLayers(out int walkable, out int wall)) return;
            var report = new StringBuilder();
            int changed = 0;
            foreach (GameObject go in Selection.gameObjects)
            {
                changed += AssignColliders(go.transform, useWall ? wall : walkable, report);
            }
            Finish(changed, report);
        }

        private static int AssignChild(Transform root, string childName, int layer, StringBuilder report)
        {
            Transform child = root.Find(childName);
            if (child == null)
            {
                report.AppendLine($"{root.name}/{childName} 없음 — 건너뜀");
                return 0;
            }
            return AssignColliders(child, layer, report);
        }

        /// <summary>root와 자식 중 콜라이더(트리거 제외)가 붙은 오브젝트의 레이어를 바꾼다. 기체(리지드바디)는 건드리지 않는다.</summary>
        private static int AssignColliders(Transform root, int layer, StringBuilder report)
        {
            int changed = 0;
            var seen = new HashSet<GameObject>();
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (collider.isTrigger || collider.attachedRigidbody != null) continue;
                GameObject go = collider.gameObject;
                if (!seen.Add(go) || go.layer == layer) continue;
                Undo.RecordObject(go, "Assign Ground Layer");
                go.layer = layer;
                changed++;
            }
            report.AppendLine($"{root.name}: 콜라이더 오브젝트 {changed}개 → {LayerMask.LayerToName(layer)}");
            return changed;
        }

        private static void Finish(int changed, StringBuilder report)
        {
            if (changed > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            string text = report + (changed > 0 ? "\n씬을 저장하세요." : "\n바뀐 것이 없습니다.");
            Debug.Log("[GroundLayerSetup] " + text);
            EditorUtility.DisplayDialog("Ground Layers", text, "확인");
        }

        /// <summary>Walkable·Wall 레이어가 없으면 비어 있는 사용자 레이어(8~31)에 만든다.</summary>
        private static bool EnsureLayers(out int walkable, out int wall)
        {
            walkable = EnsureLayer(GroundLayers.WalkableLayerName);
            wall = EnsureLayer(GroundLayers.WallLayerName);
            if (walkable < 0 || wall < 0)
            {
                EditorUtility.DisplayDialog("Ground Layers", "빈 사용자 레이어(8~31)가 없어 Walkable/Wall 레이어를 만들 수 없습니다.", "확인");
                return false;
            }
            return true;
        }

        private static int EnsureLayer(string name)
        {
            int existing = LayerMask.NameToLayer(name);
            if (existing >= 0) return existing;

            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue)) continue;
                slot.stringValue = name;
                tagManager.ApplyModifiedProperties();
                Debug.Log($"[GroundLayerSetup] 레이어 {i} = {name} 추가");
                return i;
            }
            return -1;
        }
    }
}
