using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MyDongari.EditorTools
{
    /// <summary>
    /// 지운 스크립트(예: 전역 timeScale 히트스톱 HitStop.cs)를 참조하던 컴포넌트가 씬에 "Missing Script"로 남는다.
    /// 메뉴 한 번으로 열린 씬의 Missing Script 컴포넌트를 전부 떼어 낸다. 떼어 낸 뒤 씬을 저장한다(Ctrl+S).
    /// </summary>
    public static class MissingScriptCleanup
    {
        /// <summary>
        /// 명령줄(batchmode)용: Build Settings의 씬을 차례로 열어 Missing Script를 떼고 저장한다.
        /// 예: Unity.exe -batchmode -quit -projectPath . -executeMethod MyDongari.EditorTools.MissingScriptCleanup.RunFromCommandLine
        /// </summary>
        public static void RunFromCommandLine()
        {
            foreach (EditorBuildSettingsScene entry in EditorBuildSettings.scenes)
            {
                if (!entry.enabled) continue;
                var scene = EditorSceneManager.OpenScene(entry.path, OpenSceneMode.Single);
                int removed = 0;
                foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                }
                Debug.Log($"[MissingScriptCleanup] {entry.path}: {removed}개 제거");
                if (removed > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }
        }

        [MenuItem("Mydongari/Remove Missing Scripts In Open Scenes")]
        private static void Run()
        {
            int removed = 0;
            foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                if (count == 0) continue;
                Undo.RegisterCompleteObjectUndo(go, "Remove Missing Scripts");
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                EditorSceneManager.MarkSceneDirty(go.scene);
            }
            EditorUtility.DisplayDialog("Missing Scripts", $"떼어 낸 Missing Script 컴포넌트: {removed}개. 씬을 저장하세요.", "확인");
        }
    }
}
