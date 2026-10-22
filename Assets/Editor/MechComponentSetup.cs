using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using MyDongari.Combat;

namespace MyDongari.EditorTools
{
    /// <summary>
    /// 새로 생긴 기체 컴포넌트(스러스터 런지 등)를 씬의 플레이어 기체(PlayerInput이 있는 오브젝트)에 붙인다.
    /// 런타임 자동 부착도 되지만, 인스펙터에서 수치를 조정·저장하려면 씬에 붙어 있어야 한다.
    /// </summary>
    public static class MechComponentSetup
    {
        [MenuItem("Mydongari/Add Missing Mech Components")]
        private static void AddMissing()
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Mydongari Add Missing Mech Components");
            int undoGroup = Undo.GetCurrentGroup();

            int added = 0;
            foreach (PlayerInput playerInput in Object.FindObjectsByType<PlayerInput>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                GameObject mech = playerInput.gameObject;
                if (mech.GetComponent<WeaponSystem>() == null) continue;
                if (mech.GetComponent<ThrusterLunge>() == null)
                {
                    Undo.AddComponent<ThrusterLunge>(mech);
                    added++;
                }
                EditorSceneManager.MarkSceneDirty(mech.scene);
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.DisplayDialog("Mech Components", $"추가한 컴포넌트: {added}개 (ThrusterLunge). 씬을 저장하세요.", "확인");
        }
    }
}
