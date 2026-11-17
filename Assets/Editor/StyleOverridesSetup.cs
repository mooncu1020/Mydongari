using UnityEditor;
using UnityEngine;
using MyDongari.Bridge;
using MyDongari.Game.Tuning;

namespace MyDongari.EditorTools
{
    /// <summary>
    /// StyleOverrides 에셋 생성과, 튜닝 에셋 두 개(StyleOverrides·BernardTuning)를 지금 코드 구조대로 디스크에 다시 쓰는 메뉴.
    /// 에셋 YAML을 손으로 고치지 않으려고 Unity가 직접 쓰게 한다. 다시 쓰면 코드에서 없어진 칸은 빠지고,
    /// 에셋에 없던 칸은 코드 기본값(= 지금까지 실제로 돌던 값)으로 채워져 이후 인스펙터에서 바꾼 값이 디스크에 남는다.
    /// </summary>
    public static class StyleOverridesSetup
    {
        private const string ResourcesFolder = "Assets/Resources";
        private static readonly string StylePath = ResourcesFolder + "/" + StyleOverrides.ResourcesName + ".asset";

        [MenuItem("Mydongari/Tuning/Create Style Overrides Asset")]
        private static void CreateMenu()
        {
            StyleOverrides style = EnsureStyleAsset(out bool created);
            Selection.activeObject = style;
            EditorUtility.DisplayDialog("Style Overrides",
                created ? $"{StylePath}를 코드 기본값(지금 우리 값)으로 만들었습니다." : $"이미 있습니다: {AssetDatabase.GetAssetPath(style)}", "확인");
        }

        [MenuItem("Mydongari/Tuning/Save Tuning Assets To Disk")]
        private static void SaveMenu()
        {
            EditorUtility.DisplayDialog("Tuning", SaveTuningAssets(), "확인");
        }

        /// <summary>프로젝트에 StyleOverrides 에셋이 있으면 그걸, 없으면 Resources에 코드 기본값으로 만든다.</summary>
        public static StyleOverrides EnsureStyleAsset(out bool created)
        {
            created = false;
            StyleOverrides existing = AssetDatabase.LoadAssetAtPath<StyleOverrides>(StylePath);
            if (existing != null) return existing;
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(StyleOverrides));
            if (guids.Length > 0)
            {
                return AssetDatabase.LoadAssetAtPath<StyleOverrides>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }
            if (!AssetDatabase.IsValidFolder(ResourcesFolder)) AssetDatabase.CreateFolder("Assets", "Resources");
            StyleOverrides style = ScriptableObject.CreateInstance<StyleOverrides>();
            AssetDatabase.CreateAsset(style, StylePath);
            created = true;
            return style;
        }

        /// <summary>두 튜닝 에셋을 지금 코드 구조대로 디스크에 쓴다. 한 일을 글로 돌려준다.</summary>
        public static string SaveTuningAssets()
        {
            StyleOverrides style = EnsureStyleAsset(out bool styleCreated);
            BernardTuning tuning = TuningSetup.EnsureAsset(out bool tuningCreated);
            EditorUtility.SetDirty(style);
            EditorUtility.SetDirty(tuning);
            AssetDatabase.SaveAssets();
            // 직렬화 형식이 바뀐 칸까지 확실히 다시 쓰게 한다
            AssetDatabase.ForceReserializeAssets(new[] { AssetDatabase.GetAssetPath(style), AssetDatabase.GetAssetPath(tuning) });
            AssetDatabase.SaveAssets();
            return $"StyleOverrides: {AssetDatabase.GetAssetPath(style)}{(styleCreated ? " (새로 만듦)" : "")}\n" +
                   $"BernardTuning: {AssetDatabase.GetAssetPath(tuning)}{(tuningCreated ? " (새로 만듦)" : "")}\n" +
                   "두 에셋을 디스크에 저장했습니다.";
        }

        /// <summary>명령줄(batchmode)용: Unity.exe -batchmode -projectPath . -executeMethod MyDongari.EditorTools.StyleOverridesSetup.SaveFromCommandLine -quit</summary>
        public static void SaveFromCommandLine()
        {
            Debug.Log("[TuningSave] " + SaveTuningAssets().Replace("\n", " / "));
        }
    }
}
