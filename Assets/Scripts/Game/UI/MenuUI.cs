using System.Text;
using UnityEngine;
using UnityEngine.UI;
using MyDongari.Game.Bot;
using MyDongari.Game.Flow;

namespace MyDongari.Game.UI
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

        [Header("조작법 화면 (타이틀·연습 일시정지 메뉴의 CONTROLS) — 기준: docs/GAME_SPEC_V3.md 조작표")]
        [SerializeField] private string controlsTitle = "조작법";
        [Tooltip("왼쪽 열: 패드 버튼 (PS4 이름, 괄호 안은 엑스박스)")]
        [TextArea(10, 14)] [SerializeField] private string controlsPad =
            "L스틱\nR스틱 좌우\n○ (B)\n△ (Y)\n□ (X) · R2 (RT)\n✕ (A)\nL1 (LB)\nR1 (RB)\nOPTIONS (MENU)\nSHARE (VIEW)";
        [Tooltip("가운데 열: 하는 일")]
        [TextArea(10, 14)] [SerializeField] private string controlsAction =
            "이동\n몸 회전 (가까우면 자동으로 상대를 본다)\n대시 — 방향 + ○, 대시 중 다시 누르면 정지\n점프 — 하강 중 누르고 있으면 호버\n공격\n땅: 가드 · 누르는 순간 패리 / 공중: 급강하\n앵커 클로\n임팩트 슬램\n메뉴 (연습 모드)\n연습 모드: 봇 모드 바꾸기 · 길게 누르면 리셋";
        [Tooltip("오른쪽 열: 키보드·마우스")]
        [TextArea(10, 14)] [SerializeField] private string controlsKeyboard =
            "W A S D\n마우스 좌우\nSpace\nF\n마우스 왼쪽\n마우스 오른쪽\nQ\nE\nEnter\nTab · Backspace";
        [Tooltip("공격 규칙 한 줄 요약")]
        [TextArea(2, 4)] [SerializeField] private string controlsAttackRule =
            "공격 버튼 하나로 —  가까우면(근접 구역 안) 펀치, 연타하면 원투쓰리  ·  멀면 런지  ·  대시 중이면 대시 공격  ·  점프 중이면 내려찍기";
        [SerializeField] private string controlsClose = "✕ (A) / ENTER : 닫기";
        [SerializeField] private Color controlsBackground = new Color(0.03f, 0.05f, 0.09f, 0.94f);
        [SerializeField] private Color controlsHeaderColor = new Color(1f, 0.82f, 0.29f);

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
        private GameObject _controlsRoot;

        private void Awake()
        {
            // HUD 공용 폰트 (ArenaLook — 한글 포함). 꺼져 있거나 없으면 기본 폰트
            _font = Visual.ArenaLook.HudFont;
            if (_font == null) _font = Resources.GetBuiltinResource<Font>(BuiltinFontName);
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

            BuildControls(root);

            // 연습 모드 봇 표시: 왼쪽 위 구석 (HUD와 겹치지 않게 작게)
            _practiceLabel = CreateText(root, "PracticeLabel", "", 28, new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(900f, 160f));
            RectTransform labelRect = (RectTransform)_practiceLabel.transform;
            labelRect.pivot = new Vector2(0f, 1f);
            _practiceLabel.alignment = TextAnchor.UpperLeft;
        }

        /// <summary>
        /// 조작법 화면: 화면 전체를 덮는 패널에 세 열(패드 / 하는 일 / 키보드) + 공격 규칙 한 줄. 문구는 인스펙터 칸에서 고친다.
        /// 열마다 같은 줄 수의 글자를 줄바꿈으로 넣어 줄을 맞춘다.
        /// </summary>
        private void BuildControls(Transform root)
        {
            _controlsRoot = CreatePanel(root, "Controls", controlsBackground, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Transform t = _controlsRoot.transform;
            CreateText(t, "Title", controlsTitle, 72, new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(1200f, 100f));
            const float top = -190f;
            Text padHeader = Column(t, "PadHeader", "패드  PS4 (엑스박스)", 34, -760f, top, 480f, 50f);
            Text actionHeader = Column(t, "ActionHeader", "하는 일", 34, -250f, top, 760f, 50f);
            Text keyHeader = Column(t, "KeyHeader", "키보드 · 마우스", 34, 560f, top, 400f, 50f);
            padHeader.color = actionHeader.color = keyHeader.color = controlsHeaderColor;
            Column(t, "Pad", controlsPad, 34, -760f, top - 64f, 480f, 560f);
            Column(t, "Action", controlsAction, 34, -250f, top - 64f, 760f, 560f);
            Column(t, "Keyboard", controlsKeyboard, 34, 560f, top - 64f, 400f, 560f);
            Text rule = CreateText(t, "AttackRule", controlsAttackRule, 29, new Vector2(0.5f, 0f), new Vector2(0f, 170f), new Vector2(1800f, 90f));
            rule.color = controlsHeaderColor;
            CreateText(t, "Close", controlsClose, 28, new Vector2(0.5f, 0f), new Vector2(0f, 70f), new Vector2(1200f, 50f));
        }

        /// <summary>조작법 화면의 열 하나: 화면 위쪽 가운데 기준 (x, y)에서 시작하는 왼쪽 정렬 글자.</summary>
        private Text Column(Transform parent, string name, string value, int size, float x, float y, float width, float height)
        {
            Text text = CreateText(parent, name, value, size, new Vector2(0.5f, 1f), new Vector2(x, y), new Vector2(width, height));
            ((RectTransform)text.transform).pivot = new Vector2(0f, 1f);
            text.alignment = TextAnchor.UpperLeft;
            text.fontStyle = FontStyle.Normal;
            text.lineSpacing = 1.25f;
            return text;
        }

        private void Update()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null) return;

            // 조작법 화면이 열려 있으면 그것만 보인다
            bool controls = gm.IsControlsOpen;
            SetActive(_controlsRoot, controls);

            bool title = gm.Phase == MatchPhase.Title;
            SetActive(_titleRoot, title && !controls);
            if (title) _titleOptions.text = OptionsText(gm.MenuOptions, gm.MenuIndex);

            bool menu = !title && gm.IsMenuOpen && !controls;
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
