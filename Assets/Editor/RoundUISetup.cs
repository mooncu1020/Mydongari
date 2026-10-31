using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using MyDongari.UI;

namespace MyDongari.EditorTools
{
    /// <summary>
    /// 화면 분할 가운데 공용 라운드 UI(카운트다운·FIGHT·승자·승수) 캔버스를 만든다.
    /// 각 화면 HUD와 겹치지 않게 별도 Overlay 캔버스로, 정렬 순서를 HUD보다 위로 둔다.
    /// </summary>
    public static class RoundUISetup
    {
        private const string CanvasName = "RoundUI";
        private const string BuiltinFontName = "LegacyRuntime.ttf";

        [MenuItem("Mydongari/Setup Round UI")]
        private static void Menu()
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Mydongari Setup Round UI");
            int undoGroup = Undo.GetCurrentGroup();
            bool created = CreateIfMissing();
            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.DisplayDialog("Round UI",
                created ? "RoundUI 캔버스를 만들었습니다. 씬을 저장하세요." : "이미 RoundUI가 있습니다.", "확인");
        }

        /// <summary>RoundUI가 없으면 만든다. 만들었으면 true. 호출 쪽의 Undo 그룹에 포함된다.</summary>
        public static bool CreateIfMissing()
        {
            if (Object.FindFirstObjectByType<RoundUI>(FindObjectsInactive.Include) != null) return false;

            Font font = Resources.GetBuiltinResource<Font>(BuiltinFontName);

            GameObject canvasGo = new GameObject(CanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            Undo.RegisterCreatedObjectUndo(canvasGo, "Create Round UI");
            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            RoundUI roundUi = canvasGo.AddComponent<RoundUI>();

            Text center = CreateText(canvasGo.transform, "CenterText", font, 140, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(1600f, 200f));
            Text sub = CreateText(canvasGo.transform, "SubText", font, 44, new Vector2(0.5f, 0.5f), new Vector2(0f, -80f), new Vector2(1600f, 80f));
            Text p1Wins = CreateText(canvasGo.transform, "P1Wins", font, 40, new Vector2(0.5f, 1f), new Vector2(-160f, -30f), new Vector2(280f, 60f));
            p1Wins.alignment = TextAnchor.MiddleRight;
            Text p2Wins = CreateText(canvasGo.transform, "P2Wins", font, 40, new Vector2(0.5f, 1f), new Vector2(160f, -30f), new Vector2(280f, 60f));
            p2Wins.alignment = TextAnchor.MiddleLeft;

            SerializedObject so = new SerializedObject(roundUi);
            so.FindProperty("centerText").objectReferenceValue = center;
            so.FindProperty("subText").objectReferenceValue = sub;
            so.FindProperty("player1WinsText").objectReferenceValue = p1Wins;
            so.FindProperty("player2WinsText").objectReferenceValue = p2Wins;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(canvasGo.scene);
            return true;
        }

        private static Text CreateText(Transform parent, string name, Font font, int size,
            Vector2 anchor, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = sizeDelta;

            Text text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.text = "";
            // 밝은 배경에서도 읽히게 외곽선
            Outline outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
            outline.effectDistance = new Vector2(3f, -3f);
            return text;
        }
    }
}
