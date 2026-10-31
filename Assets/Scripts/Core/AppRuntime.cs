using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MyDongari.Core
{
    /// <summary>
    /// 로케테스트 빌드 운영용: ESC 길게 누르기로 종료, 커서 숨김, 시작 시 전체 화면.
    /// 종료 키는 게임 입력이 아니라 운영자 키라서, 패드·키보드 페어링(PlayerInput)과 무관하게
    /// 이 컴포넌트가 가진 전용 InputAction으로 읽는다 (키보드가 어느 플레이어에게도 배정되지 않아도 동작).
    /// GameManager 오브젝트에 하나 (없으면 GameManager가 자동 부착).
    /// </summary>
    public class AppRuntime : MonoBehaviour
    {
        [Header("종료 (빌드)")]
        [Tooltip("ESC를 이 시간(초) 동안 누르고 있으면 종료")]
        [SerializeField] private float quitHoldSeconds = 1f;
        [Tooltip("에디터에서도 ESC 길게 누르기로 플레이 모드를 끝낸다 (기능 확인용)")]
        [SerializeField] private bool quitInEditor = false;
        [Tooltip("누르고 있는 동안 화면 아래에 진행 표시")]
        [SerializeField] private bool showQuitProgress = true;

        [Header("화면 (빌드)")]
        [Tooltip("빌드에서 마우스 커서를 숨기고 창 안에 가둔다")]
        [SerializeField] private bool hideCursorInBuild = true;
        [Tooltip("빌드 시작 시 모니터 기본 해상도의 전체 화면(창 모드 전체 화면)으로 맞춘다. 예전 실행에서 창 모드로 저장된 설정을 덮어쓰기 위함")]
        [SerializeField] private bool forceFullscreenOnStart = true;

        private InputAction _quitAction;
        private float _holdStartTime = -1f;
        private GUIStyle _progressStyle;

        private bool QuitEnabled => !Application.isEditor || quitInEditor;

        private void Awake()
        {
            string duration = Mathf.Max(0.1f, quitHoldSeconds).ToString(CultureInfo.InvariantCulture);
            _quitAction = new InputAction("QuitHold", InputActionType.Button, "<Keyboard>/escape", "hold(duration=" + duration + ")");
            _quitAction.started += _ => _holdStartTime = Time.unscaledTime;
            _quitAction.canceled += _ => _holdStartTime = -1f;
            _quitAction.performed += _ => Quit();
        }

        private void OnEnable()
        {
            if (QuitEnabled) _quitAction.Enable();
        }

        private void OnDisable()
        {
            _quitAction.Disable();
            _holdStartTime = -1f;
        }

        private void OnDestroy()
        {
            _quitAction.Dispose();
        }

        private void Start()
        {
            if (Application.isEditor) return;

            // 빌드 스모크 테스트(Tools/Build And Smoke Test)가 창 모드로 띄울 때는 전체 화면을 강제하지 않는다
            bool smokeTest = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-smokeTest") >= 0;
            if (forceFullscreenOnStart && !smokeTest)
            {
                // 모니터 실제 해상도(데스크톱 해상도) 기준. 창 모드 전체 화면은 Alt+Tab이 빠르고 안전하다
                Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, FullScreenMode.FullScreenWindow);
            }
            ApplyCursor();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // Alt+Tab 등으로 포커스를 잃었다 돌아오면 커서 잠금이 풀려 있을 수 있어 다시 건다
            if (hasFocus && !Application.isEditor) ApplyCursor();
        }

        private void ApplyCursor()
        {
            if (!hideCursorInBuild) return;
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }

        private void Quit()
        {
            _holdStartTime = -1f;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            // PlayLogger는 OnApplicationQuit에서 로그를 저장한다
            Application.Quit();
#endif
        }

        private void OnGUI()
        {
            if (!showQuitProgress || _holdStartTime < 0f) return;

            float progress = Mathf.Clamp01((Time.unscaledTime - _holdStartTime) / Mathf.Max(0.1f, quitHoldSeconds));
            if (_progressStyle == null)
            {
                _progressStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 28,
                    fontStyle = FontStyle.Bold
                };
                _progressStyle.normal.textColor = Color.white;
            }

            // 오조작 방지용 안내: 끝까지 누르고 있어야 꺼진다는 걸 보여 준다
            // 기본 GUI 폰트에 없는 글리프를 피하려고 ASCII로 그린다
            int filled = Mathf.RoundToInt(progress * 10f);
            string bar = "[" + new string('#', filled) + new string('-', 10 - filled) + "]";
            GUI.Label(new Rect(0f, Screen.height - 80f, Screen.width, 40f), "HOLD ESC TO QUIT  " + bar, _progressStyle);
        }
    }
}
