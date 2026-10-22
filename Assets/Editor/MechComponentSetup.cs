using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using MyDongari.Combat;
using MyDongari.Core;
using MyDongari.Feedback;

namespace MyDongari.EditorTools
{
    /// <summary>
    /// 새로 생긴 기체 컴포넌트(스러스터 런지, 튜닝 참조)를 씬의 플레이어 기체(PlayerInput이 있는 오브젝트)에,
    /// 공용 컴포넌트(히트스톱, 상태 피드백)를 GameManager 오브젝트에 붙인다.
    /// 런타임 자동 부착도 되지만, 인스펙터에서 값·효과음을 넣고 저장하려면 씬에 붙어 있어야 한다.
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
            MyDongari.Mech.BernardTuning tuning = TuningSetup.EnsureAsset(out _);
            foreach (PlayerInput playerInput in Object.FindObjectsByType<PlayerInput>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                GameObject mech = playerInput.gameObject;
                if (mech.GetComponent<WeaponSystem>() == null) continue;
                if (mech.GetComponent<ThrusterLunge>() == null)
                {
                    Undo.AddComponent<ThrusterLunge>(mech);
                    added++;
                }
                if (mech.GetComponent<MyDongari.Mech.MechTuning>() == null) added++;
                TuningSetup.AssignTo(mech, tuning);
                EditorSceneManager.MarkSceneDirty(mech.scene);
            }

            GameManager gameManager = Object.FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
            if (gameManager != null)
            {
                added += AddIfMissing<HitStop>(gameManager.gameObject);
                added += AddIfMissing<CombatFeedback>(gameManager.gameObject);
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.DisplayDialog("Mech Components",
                $"추가한 컴포넌트: {added}개 (기체: ThrusterLunge, MechTuning / GameManager: HitStop, CombatFeedback).\n" +
                $"수치 에셋: {AssetDatabase.GetAssetPath(tuning)}. 씬을 저장하세요.", "확인");
        }

        private static int AddIfMissing<T>(GameObject go) where T : Component
        {
            if (go.GetComponent<T>() != null) return 0;
            Undo.AddComponent<T>(go);
            EditorSceneManager.MarkSceneDirty(go.scene);
            return 1;
        }
    }
}
