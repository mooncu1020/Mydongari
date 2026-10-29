using UnityEngine;
using UnityEngine.InputSystem;

namespace MyDongari.Core
{
    /// <summary>
    /// 테스트용 프레임 제한 전환 (Q 이후): F9를 누를 때마다 제한 없음 → 60 → 30 → 20 → 제한 없음.
    /// 바꿀 때 VSync를 끄고 Application.targetFrameRate를 건다. 현재 제한과 실제 fps를 화면 오른쪽 위에 작게 표시.
    /// F9는 게임 입력이 아니라 운영자 키라서 AppRuntime의 ESC처럼 전용 InputAction으로 읽는다 (패드·키보드 페어링과 무관).
    /// 빌드에서도 동작. GameManager 오브젝트에 하나 (없으면 GameManager가 자동 부착).
    /// </summary>
    public class FrameRateTester : MonoBehaviour
    {
        [Header("프레임 제한 전환 (F9)")]
        [Tooltip("F9로 돌아가며 거는 제한(fps). 0 이하 = 제한 없음")]
        [SerializeField] private int[] limits = { 0, 60, 30, 20 };
        [Tooltip("화면 오른쪽 위에 현재 제한과 실제 fps를 표시")]
        [SerializeField] private bool showOverlay = true;
        [Tooltip("실제 fps를 평균 내는 간격(초, 실제 시간 — 히트스톱 중에도 잰다)")]
        [SerializeField] private float sampleInterval = 0.5f;
        [Tooltip("표시 글자 크기(px, 화면 높이 1080 기준)")]
        [SerializeField] private int fontSize = 16;

        private InputAction _cycleAction;
        // 아직 F9를 안 눌렀으면 -1 (프로젝트 설정 그대로)
        private int _index = -1;
        private int _frames;
        private float _elapsed;
        private float _fps;
        private GUIStyle _style;
        private GUIStyle _shadow;

        private void Awake()
        {
            _cycleAction = new InputAction("FrameRateCycle", InputActionType.Button, "<Keyboard>/f9");
            _cycleAction.performed += _ => Cycle();
        }

        private void OnEnable() => _cycleAction.Enable();
        private void OnDisable() => _cycleAction.Disable();
        private void OnDestroy() => _cycleAction.Dispose();

        private void Cycle()
        {
            if (limits == null || limits.Length == 0) return;
            _index = (_index + 1) % limits.Length;
            // targetFrameRate는 VSync가 켜져 있으면 무시되므로 끈다
            QualitySettings.vSyncCount = 0;
            int limit = limits[_index];
            Application.targetFrameRate = limit > 0 ? limit : -1;
            // 바뀐 직후 평균이 이전 설정에 섞이지 않게
            _frames = 0;
            _elapsed = 0f;
        }

        private void Update()
        {
            _frames++;
            _elapsed += Time.unscaledDeltaTime;
            if (_elapsed >= Mathf.Max(0.05f, sampleInterval))
            {
                _fps = _frames / _elapsed;
                _frames = 0;
                _elapsed = 0f;
            }
        }

        private string LimitText()
        {
            int target = Application.targetFrameRate;
            int vSync = QualitySettings.vSyncCount;
            if (vSync > 0) return $"VSync {vSync}";
            return target > 0 ? $"{target}" : "없음";
        }

        private void OnGUI()
        {
            if (!showOverlay) return;
            float scale = Screen.height / 1080f;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperRight };
                _style.normal.textColor = Color.white;
                _shadow = new GUIStyle(_style);
                _shadow.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
            }
            _style.fontSize = Mathf.Max(8, Mathf.RoundToInt(fontSize * scale));
            _shadow.fontSize = _style.fontSize;

            string text = $"제한 {LimitText()} · 실제 {_fps:0} fps (F9)";
            float width = 400f * scale;
            float height = _style.fontSize * 1.6f;
            var rect = new Rect(Screen.width - width - 8f * scale, 6f * scale, width, height);
            // 밝은 배경에서도 읽히게 그림자 한 번
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, _shadow);
            GUI.Label(rect, text, _style);
        }
    }
}
