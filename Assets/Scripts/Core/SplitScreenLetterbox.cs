using UnityEngine;

namespace MyDongari.Core
{
    /// <summary>
    /// 화면 분할 레터박스 (V6.1). 좌우 분할은 그대로 두고, 각 플레이어 화면을 반쪽 안에서 정해진 비율(16:9)로 줄여 가운데에 둔다.
    /// 1920×1080이면 반쪽 960×1080 안에 960×540 화면 + 위아래 검은 띠. 반쪽이 그 비율보다 넓으면(아주 넓은 모니터) 좌우 띠.
    /// HUD 캔버스는 각 카메라의 Screen Space - Camera라 카메라 영역을 따라 저절로 16:9 안에 들어간다.
    /// 띠 부분은 어떤 카메라도 안 그리면 이전 프레임이 남으므로, 가장 먼저 그리는 배경 카메라가 화면 전체를 검게 지운다.
    /// GameManager가 붙이고 SetLayout을 부른다. 창 크기가 바뀌면 다시 맞춘다.
    /// </summary>
    public class SplitScreenLetterbox : MonoBehaviour
    {
        private UnityEngine.Camera _player1;
        private UnityEngine.Camera _player2;
        private bool _versus;
        private bool _letterbox = true;
        private float _aspect = 16f / 9f;
        private UnityEngine.Camera _background;
        private int _screenWidth;
        private int _screenHeight;

        /// <summary>
        /// 배치를 정한다. versus면 P1 왼쪽·P2 오른쪽 반쪽(letterbox면 그 안의 aspect 영역), 아니면 P1 전체 화면.
        /// </summary>
        public void SetLayout(bool versus, UnityEngine.Camera player1, UnityEngine.Camera player2, bool letterbox, float aspect)
        {
            _versus = versus;
            _player1 = player1;
            _player2 = player2;
            _letterbox = letterbox;
            _aspect = Mathf.Max(0.1f, aspect);
            EnsureBackground();
            Apply();
        }

        private void LateUpdate()
        {
            if (Screen.width != _screenWidth || Screen.height != _screenHeight) Apply();
        }

        private void Apply()
        {
            _screenWidth = Screen.width;
            _screenHeight = Screen.height;
            if (_player1 == null) return;
            if (!_versus)
            {
                _player1.rect = new Rect(0f, 0f, 1f, 1f);
                return;
            }
            _player1.rect = HalfRect(0f);
            if (_player2 != null) _player2.rect = HalfRect(0.5f);
        }

        /// <summary>반쪽(왼쪽 x0 = 0, 오른쪽 0.5) 안에서 비율을 지킨 가운데 영역 (정규화 좌표).</summary>
        private Rect HalfRect(float x0)
        {
            if (!_letterbox || _screenWidth <= 0 || _screenHeight <= 0) return new Rect(x0, 0f, 0.5f, 1f);
            float halfWidthPx = _screenWidth * 0.5f;
            float heightPx = halfWidthPx / _aspect;
            if (heightPx <= _screenHeight)
            {
                // 위아래 띠
                float h = heightPx / _screenHeight;
                return new Rect(x0, (1f - h) * 0.5f, 0.5f, h);
            }
            // 반쪽이 비율보다 넓다 → 좌우 띠
            float w = _screenHeight * _aspect / _screenWidth;
            return new Rect(x0 + (0.5f - w) * 0.5f, 0f, w, 1f);
        }

        /// <summary>띠를 검게 지우는 배경 카메라. 플레이어 카메라보다 먼저(깊이 낮게) 그리고, 아무 레이어도 안 그린다.</summary>
        private void EnsureBackground()
        {
            if (_background != null || _player1 == null) return;
            var go = new GameObject("LetterboxBackgroundCamera");
            go.transform.SetParent(transform, false);
            _background = go.AddComponent<UnityEngine.Camera>();
            _background.clearFlags = CameraClearFlags.SolidColor;
            _background.backgroundColor = Color.black;
            _background.cullingMask = 0;
            _background.rect = new Rect(0f, 0f, 1f, 1f);
            float depth = _player1.depth;
            if (_player2 != null) depth = Mathf.Min(depth, _player2.depth);
            _background.depth = depth - 1f;
            _background.useOcclusionCulling = false;
            _background.allowHDR = false;
            _background.allowMSAA = false;
        }
    }
}
