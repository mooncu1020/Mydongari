using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using MyDongari.Game.Visual;
using Debug = UnityEngine.Debug;

namespace MyDongari.EditorTools
{
    /// <summary>
    /// ArenaLook(화면 정리 값) 에셋을 Resources에 만든다 — YAML을 손으로 쓰지 않으려고 Unity가 쓰게 한다.
    /// 이미 있으면 그대로 두고(인스펙터에서 바꾼 값 보존) 지금 코드 구조대로 다시 저장만 한다.
    /// </summary>
    public static class ArenaLookSetup
    {
        private const string Path = "Assets/Resources/" + ArenaLook.ResourcesName + ".asset";

        [MenuItem("Mydongari/Tuning/Create Arena Look Asset")]
        private static void CreateMenu()
        {
            Selection.activeObject = Ensure(out bool created);
            EditorUtility.DisplayDialog("Arena Look", created ? $"{Path}를 코드 기본값으로 만들었습니다." : $"이미 있습니다: {Path}", "확인");
        }

        public static ArenaLook Ensure(out bool created)
        {
            created = false;
            ArenaLook look = AssetDatabase.LoadAssetAtPath<ArenaLook>(Path);
            if (look == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
                look = ScriptableObject.CreateInstance<ArenaLook>();
                AssetDatabase.CreateAsset(look, Path);
                created = true;
            }
            EditorUtility.SetDirty(look);
            AssetDatabase.SaveAssets();
            return look;
        }

        /// <summary>명령줄(batchmode)용: -executeMethod MyDongari.EditorTools.ArenaLookSetup.CreateFromCommandLine -quit</summary>
        public static void CreateFromCommandLine()
        {
            Ensure(out bool created);
            Debug.Log($"[ArenaLook] {(created ? "만듦" : "다시 저장")}: {Path}");
        }
    }
}
