using System.Text;
using UnityEngine;
using UnityEngine.UI;
using MyDongari.Combat;
using MyDongari.Core;

namespace MyDongari.UI
{
    /// <summary>
    /// 타이틀(VERSUS / PRACTICE), 매치 종료·연습 메뉴 선택지, 연습 모드 봇 표시(왼쪽 위 구석).
    /// 씬을 편집하지 않도록 Overlay 캔버스와 텍스트를 런타임에 스스로 만든다. GameManager 상태를 읽기만 한다.
    /// GameManager 오브젝트에 하나 (없으면 GameManager가 자동 부착).
    /// </summary>
    public class MenuUI : MonoBehaviour
    {
        private const string BuiltinFontName = "LegacyRuntime.ttf";

        [Header("문구 (기본 폰트에 한글이 없을 수 있어 영어)")]
        [SerializeField] private string gameTitle = "DRAFT HOUND PATRASCHE";
        [SerializeField] private string titleSubtitle = "SELECT MODE";
        [SerializeField] private string menuHint = "STICK / D-PAD / W S : SELECT     A / START / ENTER : CONFIRM";
        [Tooltip("연습 모드 왼쪽 위 안내 (봇 모드 줄 아래)")]
        [TextArea] [SerializeField] private string practiceHint =
            "TAB / SELECT : BOT MODE    BACKSPACE / HOLD SELECT : RESET\n1-4 : MY HEAT 0 / 50 / 90 / 100%    START : MENU";

        [Header("모양")]
        [SerializeField] private Color selectedColor = new Color(1f, 0.82f, 0.29f);
        [SerializeField] private Color titleBackground = new Color(0f, 0f, 0f, 0.85f);
        [SerializeField] private Color menuBackground = new Color(0f, 0f, 0f, 0.6f);
        [Tooltip("다른 UI(라운드 UI 10)보다 위에 그리기")]
        [SerializeField] private int sortingOrder = 20;

        private Font _font;
        private GameObject _titleRoot;
        private GameObject _menuRoot;
        private Text _titleOptions;
        private Text _menuOptions;
        private Text _practiceLabel;

        private void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>(BuiltinFontName);
            BuildCanvas();
        }

        private void BuildCanvas()
        {
            GameObject canvasGo = new GameObject("MenuUI_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            Transform root = canvasGo.transform;

            // 타이틀: 화면 전체를 어둡게 덮고 가운데에 제목·선택지
            _titleRoot = CreatePanel(root, "Title", titleBackground, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            CreateText(_titleRoot.transform, "GameTitle", gameTitle, 120, new Vector2(0.5f, 0.5f), new Vector2(0f, 220f), new Vector2(1800f, 160f));
            CreateText(_titleRoot.transform, "Subtitle", titleSubtitle, 40, new Vector2(0.5f, 0.5f), new Vector2(0f, 100f), new Vector2(1200f, 60f));
            _titleOptions = CreateText(_titleRoot.transform, "Options", "", 64, new Vector2(0.5f, 0.5f), new Vector2(0f, -80f), new Vector2(1200f, 260f));
            CreateText(_titleRoot.transform, "Hint", menuHint, 28, new Vector2(0.5f, 0f), new Vector2(0f, 80f), new Vector2(1800f, 50f));

            // 매치 종료·연습 메뉴: 가운데 아래쪽 작은 패널
            _menuRoot = CreatePanel(root, "Menu", menuBackground, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -200f), new Vector2(640f, 240f));
            _menuOptions = CreateText(_menuRoot.transform, "Options", "", 52, new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(600f, 160f));
            CreateText(_menuRoot.transform, "Hint", "A / START / ENTER", 24, new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(600f, 36f));

            // 연습 모드 봇 표시: 왼쪽 위 구석 (HUD와 겹치지 않게 작게)
            _practiceLabel = CreateText(root, "PracticeLabel", "", 28, new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(900f, 160f));
            RectTransform labelRect = (RectTransform)_practiceLabel.transform;
            labelRect.pivot = new Vector2(0f, 1f);
            _practiceLabel.alignment = TextAnchor.UpperLeft;
        }

        private void Update()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null) return;

            bool title = gm.Phase == MatchPhase.Title;
            SetActive(_titleRoot, title);
            if (title) _titleOptions.text = OptionsText(gm.MenuOptions, gm.MenuIndex);

            bool menu = !title && gm.IsMenuOpen;
            SetActive(_menuRoot, menu);
            if (menu) _menuOptions.text = OptionsText(gm.MenuOptions, gm.MenuIndex);

            bool practice = !title && gm.Mode == GameMode.Practice;
            SetActive(_practiceLabel.gameObject, practice);
            if (practice) _practiceLabel.text = PracticeText(gm.PracticeBot);
        }

        private string OptionsText(string[] options, int selected)
        {
            if (options == null) return "";
            string color = ColorUtility.ToHtmlStringRGB(selectedColor);
            var sb = new StringBuilder();
            for (int i = 0; i < options.Length; i++)
            {
                if (i > 0) sb.Append('\n');
                if (i == selected) sb.Append("<color=#").Append(color).Append(">> ").Append(options[i]).Append(" <</color>");
                else sb.Append(options[i]);
            }
            return sb.ToString();
        }

        private string PracticeText(MeleeDummy bot)
        {
            string botMode = "-";
            if (bot != null)
            {
                botMode = bot.Mode.ToString().ToUpperInvariant();
                // Mixed면 지금 고른 하위 행동도 보여 준다
                if (bot.Mode == BotMode.Mixed) botMode += " (" + bot.ActiveBehaviour.ToString().ToUpperInvariant() + ")";
            }
            return "PRACTICE   BOT: " + botMode + "\n" + practiceHint;
        }

        private static void SetActive(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active) go.SetActive(active);
        }

        private static GameObject CreatePanel(Transform parent, string name, Color color,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = sizeDelta;
            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            go.SetActive(false);
            return go;
        }

        private Text CreateText(Transform parent, string name, string value, int size,
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
            text.font = _font;
            text.fontSize = size;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = value;
            // 밝은 배경(경기장)에서도 읽히게 외곽선
            Outline outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
            outline.effectDistance = new Vector2(2f, -2f);
            return text;
        }
    }
}
