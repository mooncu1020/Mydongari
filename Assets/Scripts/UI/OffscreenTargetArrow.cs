using UnityEngine;
using MyDongari.Camera;
using MyDongari.Core;
using UnityCamera = UnityEngine.Camera;

namespace MyDongari.UI
{
    /// <summary>
    /// 화면 밖 상대 방향 화살표 (V3 "카메라와 시점"). 카메라 하나에 하나 붙어서, 그 카메라 화면(분할된 자기 영역) 가장자리에
    /// 상대 쪽을 가리키는 삼각형을 그린다. MechCamera가 시작할 때 스스로 붙이므로 씬·프리팹 배선이 없다.
    /// 캔버스 대신 OnGUI를 쓰는 이유: 카메라 영역(pixelRect)을 그대로 쓰면 화면 분할 좌우가 저절로 맞고, 텍스처도 코드로 만들 수 있다.
    /// </summary>
    [RequireComponent(typeof(UnityCamera))]
    public class OffscreenTargetArrow : MonoBehaviour
    {
        [Header("표시")]
        [Tooltip("화살표 한 변 크기(픽셀, 1080p 기준 — 화면 높이에 비례해 커지고 작아진다)")]
        [SerializeField] private float arrowSize = 44f;
        [Tooltip("화면(자기 영역) 가장자리에서 안쪽으로 띄우는 거리(픽셀, 1080p 기준)")]
        [SerializeField] private float edgeMargin = 36f;
        [Tooltip("상대가 화면 안이어도 가장자리에서 이 비율(0~0.5) 안쪽에 있으면 화면 밖으로 본다. 0이면 정확히 화면 밖일 때만")]
        [SerializeField, Range(0f, 0.2f)] private float viewportInset = 0.02f;
        [SerializeField] private Color arrowColor = new Color(1f, 0.35f, 0.2f, 0.9f);
        [Tooltip("상대 기체 위치에서 이만큼(m) 위를 가리킨다 (발밑이 아니라 몸통)")]
        [SerializeField] private float targetHeight = 1.5f;

        private const int TextureSize = 64;
        private UnityCamera _camera;
        private MechCamera _mechCamera;
        private MechIdentity _owner;
        private Texture2D _texture;

        private void Awake()
        {
            _camera = GetComponent<UnityCamera>();
            _mechCamera = GetComponent<MechCamera>();
            _texture = CreateArrowTexture();
        }

        private void OnDestroy()
        {
            if (_texture != null) Destroy(_texture);
        }

        /// <summary>오른쪽(+x)을 가리키는 흰 삼각형. 색은 그릴 때 GUI.color로 입힌다.</summary>
        private static Texture2D CreateArrowTexture()
        {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color32[TextureSize * TextureSize];
            float half = TextureSize * 0.5f;
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    // 왼쪽 변(x = 4)에서 오른쪽 꼭짓점(x = size - 4)으로 갈수록 세로 폭이 줄어드는 삼각형
                    float u = (x + 0.5f - 4f) / (TextureSize - 8f);
                    float allowed = (1f - u) * (half - 4f);
                    bool inside = u >= 0f && u <= 1f && Mathf.Abs(y + 0.5f - half) <= allowed;
                    pixels[y * TextureSize + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private MechIdentity Opponent
        {
            get
            {
                if (_owner == null && _mechCamera != null && _mechCamera.Target != null)
                {
                    _owner = _mechCamera.Target.GetComponent<MechIdentity>();
                }
                return _owner != null ? _owner.Opponent : null;
            }
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (_camera == null || !_camera.isActiveAndEnabled || _texture == null) return;
            MechIdentity opponent = Opponent;
            if (opponent == null || !opponent.gameObject.activeInHierarchy) return;

            Vector3 world = opponent.transform.position + Vector3.up * targetHeight;
            Vector3 viewport = _camera.WorldToViewportPoint(world);
            bool behind = viewport.z < 0f;
            float min = viewportInset, max = 1f - viewportInset;
            bool onScreen = !behind && viewport.x >= min && viewport.x <= max && viewport.y >= min && viewport.y <= max;
            if (onScreen) return;

            // 화면 가운데에서 상대 쪽 방향(뷰포트 기준, y 위). 카메라 뒤면 투영이 뒤집히므로 반대로
            Vector2 dir = new Vector2(viewport.x - 0.5f, viewport.y - 0.5f);
            if (behind) dir = -dir;
            // 정확히 뒤(가운데로 투영)면 방향이 없다 — 아래를 가리킨다
            if (dir.sqrMagnitude < 0.000001f) dir = Vector2.down;

            Rect area = _camera.pixelRect;
            // 화면 비율 그대로 방향을 픽셀로 (뷰포트 1 = 영역 폭/높이)
            Vector2 pixelDir = new Vector2(dir.x * area.width, dir.y * area.height).normalized;

            float scale = Screen.height / 1080f;
            float size = arrowSize * scale;
            float margin = edgeMargin * scale + size * 0.5f;
            float halfW = Mathf.Max(1f, area.width * 0.5f - margin);
            float halfH = Mathf.Max(1f, area.height * 0.5f - margin);
            // 가운데에서 그 방향으로 영역 테두리(여백 안쪽 사각형)에 닿는 점
            float tx = Mathf.Abs(pixelDir.x) > 0.0001f ? halfW / Mathf.Abs(pixelDir.x) : float.MaxValue;
            float ty = Mathf.Abs(pixelDir.y) > 0.0001f ? halfH / Mathf.Abs(pixelDir.y) : float.MaxValue;
            float t = Mathf.Min(tx, ty);
            Vector2 centerPx = area.center + pixelDir * t;

            // 픽셀 좌표(아래가 0)를 GUI 좌표(위가 0)로
            Vector2 guiCenter = new Vector2(centerPx.x, Screen.height - centerPx.y);
            float angle = -Mathf.Atan2(pixelDir.y, pixelDir.x) * Mathf.Rad2Deg;

            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUIUtility.RotateAroundPivot(angle, guiCenter);
            GUI.color = arrowColor;
            GUI.DrawTexture(new Rect(guiCenter.x - size * 0.5f, guiCenter.y - size * 0.5f, size, size), _texture);
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }
    }
}
