using UnityEngine;

namespace MyDongari.Game.Visual
{
    /// <summary>
    /// 화면 정리 값 모음 (발표용, 2026-11-16): 1P·2P 기체 색, 하늘 색, 경기장 채도, 외곽선 굵기, 주 조명 방향, HUD 폰트·글자 색.
    /// 묶음마다 "적용" 칸을 끄면 그 묶음은 씬·머티리얼에 원래 들어 있는 값으로 돌아간다 — 전부 실행 중에만 덮어쓰고 씬·머티리얼 에셋은 고치지 않는다
    /// (ArenaLookApplier가 시작할 때 적용, 기체 색은 CombatFeedback의 틴트가 적용).
    /// 에셋: Assets/Resources/ArenaLook.asset (메뉴 Mydongari/Tuning/Create Arena Look Asset). 없으면 아래 코드 기본값으로 돈다.
    /// </summary>
    [CreateAssetMenu(menuName = "Mydongari/Arena Look", fileName = "ArenaLook")]
    public class ArenaLook : ScriptableObject
    {
        public const string ResourcesName = "ArenaLook";

        [Header("1P·2P 기체 색 (기체 틴트 — 상태 틴트(가드·과열·번쩍임)는 이 색 위에 얹힌다)")]
        [Tooltip("끄면 두 기체 모두 머티리얼 원래 색")]
        public bool teamTintEnabled = true;
        public Color player1Color = new Color(0.16f, 0.46f, 1f);
        [Tooltip("2P와 연습 봇")]
        public Color player2Color = new Color(1f, 0.58f, 0.12f);
        [Tooltip("원래 색에서 팀 색 쪽으로 섞는 비율 (0 = 원래 색, 1 = 팀 색 단색)")]
        [Range(0f, 1f)] public float teamTintStrength = 0.6f;
        [Tooltip("팀 색을 켰을 때 가드 틴트 색. 원래 가드 틴트(파랑)는 1P 색과 겹치고 2P 주황을 회색으로 만들어서, 어느 팀 색 위에서도 '밝아지는' 옅은 색으로 바꾼다")]
        public Color guardTint = new Color(0.82f, 0.93f, 1f);
        [Tooltip("팀 색을 켰을 때 가드 틴트 세기. 크면 가드 중에 팀 색이 바래서 누구 기체인지 안 보인다")]
        [Range(0f, 1f)] public float guardTintStrength = 0.25f;

        [Header("하늘 (MyDongari/DaySky 스카이박스의 색을 덮어쓴다)")]
        [Tooltip("끄면 M_BellSky 머티리얼 값 그대로")]
        public bool skyEnabled = true;
        public Color skyZenith = new Color(0.17f, 0.36f, 0.70f);
        public Color skyHorizon = new Color(0.76f, 0.85f, 0.93f);
        public Color cloudColor = new Color(0.97f, 0.98f, 1f);
        public Color cloudShadeColor = new Color(0.70f, 0.76f, 0.86f);
        public Color cloudGapColor = new Color(0.42f, 0.56f, 0.76f);

        [Header("경기장 (바닥·구조물 — 기체가 도드라지게 채도를 낮춘다)")]
        [Tooltip("끄면 M_Bell_* 머티리얼 색 그대로")]
        public bool arenaEnabled = true;
        [Tooltip("이 이름의 오브젝트 아래 렌더러에만 적용")]
        public string arenaRootName = "Bellforest";
        [Tooltip("채도 배율 (1 = 그대로, 0 = 회색)")]
        [Range(0f, 1f)] public float arenaSaturation = 0.3f;
        [Tooltip("밝기 배율 (1 = 그대로)")]
        [Range(0.3f, 1.5f)] public float arenaBrightness = 0.85f;

        [Header("셀 셰이딩 외곽선 (화면 외곽선 머티리얼 M_ScreenOutline)")]
        [Tooltip("끄면 머티리얼 값 그대로 (1.5px)")]
        public bool outlineEnabled = true;
        [Tooltip("선 굵기(px, 화면 높이 1080 기준)")]
        [Range(0.5f, 4f)] public float outlineThickness = 2.2f;

        [Header("주 조명 (해)")]
        [Tooltip("끄면 씬의 해 방향 그대로 (높이 50°·방위 35°)")]
        public bool sunEnabled = true;
        [Tooltip("해 높이(°)")]
        [Range(5f, 90f)] public float sunElevation = 46f;
        [Tooltip("해 방위(°, Y 회전). 90 = 갑판 길이 방향에 수직(옆에서) — 양쪽 플레이어 모두 자기 기체 등이 반쯤 밝게 보인다. " +
                 "씬 값 35는 길이 방향에 가까워서 1P는 등이 밝고 2P는 등이 그늘이었다")]
        [Range(0f, 360f)] public float sunAzimuth = 78f;

        [Header("HUD 글자 (씬·메뉴·머리 위 팝업의 폰트를 하나로)")]
        [Tooltip("끄면 기본 폰트(LegacyRuntime)와 씬에 들어 있는 글자 색 그대로")]
        public bool hudEnabled = true;
        [Tooltip("운영체제 폰트 이름 — 앞에서부터 있는 것을 쓴다. 한글이 들어 있는 폰트여야 한다 (Windows: 맑은 고딕)")]
        public string[] hudFontNames = { "Malgun Gothic", "맑은 고딕", "Noto Sans CJK KR", "Arial" };
        [Tooltip("일반 글자 색 (흰색·회색 계열 글자를 전부 이 색으로)")]
        public Color hudTextColor = new Color(0.96f, 0.97f, 1f);
        [Tooltip("경고 글자 색 (씬에서 빨강처럼 색이 들어가 있던 글자를 전부 이 색으로)")]
        public Color hudWarningColor = new Color(1f, 0.38f, 0.30f);
        [Tooltip("오른쪽 위 프레임률 표시(개발용)를 F9·F10을 누르기 전에는 숨긴다")]
        public bool hideFpsOverlayUntilKey = true;

        private static ArenaLook _current;

        /// <summary>쓸 에셋: Resources/ArenaLook → 없으면 코드 기본값 인스턴스.</summary>
        public static ArenaLook Current
        {
            get
            {
                if (_current == null)
                {
                    _current = Resources.Load<ArenaLook>(ResourcesName);
                    if (_current == null)
                    {
                        _current = CreateInstance<ArenaLook>();
                        _current.name = "ArenaLook (코드 기본값)";
                        _current.hideFlags = HideFlags.DontSave;
                    }
                }
                return _current;
            }
        }

        /// <summary>이 플레이어 번호의 기체 색 (1 = 1P, 그 밖 = 2P·연습 봇).</summary>
        public Color TeamColor(int playerIndex) => playerIndex == 1 ? player1Color : player2Color;

        private static Font _font;
        private static bool _fontLooked;

        /// <summary>
        /// HUD 공용 폰트: hudFontNames 중 운영체제에 있는 첫 폰트(동적 폰트 — 한글 포함). HUD 묶음이 꺼져 있거나 못 만들면 기본 폰트.
        /// </summary>
        public static Font HudFont
        {
            get
            {
                if (_fontLooked && _font != null) return _font;
                _fontLooked = true;
                ArenaLook look = Current;
                if (look.hudEnabled && look.hudFontNames != null && look.hudFontNames.Length > 0)
                {
                    var installed = new System.Collections.Generic.HashSet<string>(Font.GetOSInstalledFontNames());
                    foreach (string name in look.hudFontNames)
                    {
                        if (string.IsNullOrEmpty(name) || !installed.Contains(name)) continue;
                        _font = Font.CreateDynamicFontFromOSFont(name, 32);
                        if (_font != null) break;
                    }
                }
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }
    }
}
