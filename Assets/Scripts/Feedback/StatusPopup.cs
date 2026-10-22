using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MyDongari.Feedback
{
    /// <summary>팝업 모양·움직임 수치. CombatFeedback 인스펙터에서 조정한다.</summary>
    [System.Serializable]
    public class PopupStyle
    {
        [Tooltip("떠 있는 시간(초, 실제 시간 — 히트스톱 중에도 흐른다)")]
        public float lifetime = 0.8f;
        [Tooltip("사라질 때까지 위로 떠오르는 거리(m)")]
        public float riseDistance = 1f;
        [Tooltip("같은 기체에 팝업이 겹칠 때 위로 쌓는 간격(m)")]
        public float stackSpacing = 0.7f;
        [Tooltip("글자 크기(월드). 기준 거리에서의 크기")]
        public float characterSize = 0.08f;
        public int fontSize = 64;
        [Tooltip("이 거리(m)보다 멀면 화면에서 너무 작아지지 않게 거리에 비례해 키운다")]
        public float referenceDistance = 10f;
        [Tooltip("거리 보정 최대 배율")]
        public float maxDistanceScale = 3f;
        [Tooltip("사라지기 시작하는 시점(수명 비율)")]
        [Range(0f, 1f)] public float fadeStart = 0.6f;
    }

    /// <summary>
    /// 기체 머리 위 짧은 월드 텍스트(PARRY, STUN …). 패키지 추가 없이 레거시 TextMesh + 기본 폰트로 그린다.
    /// 화면 분할에서 한 오브젝트가 양쪽 카메라에 다 정면으로 보이도록, 카메라마다 렌더 직전에
    /// 그 카메라를 향해 회전·크기를 다시 맞춘다 (URP: beginCameraRendering, 내장 파이프라인: Camera.onPreCull).
    /// </summary>
    public class StatusPopup : MonoBehaviour
    {
        private const string BuiltinFontName = "LegacyRuntime.ttf";

        private static readonly List<StatusPopup> Active = new List<StatusPopup>();
        private static Font _font;

        private TextMesh _text;
        private Transform _follow;
        private float _heightOffset;
        private float _age;
        private Color _color;
        private PopupStyle _style;

        public Transform Follow => _follow;

        /// <summary>follow 기체 머리 위(heightOffset)에 label을 띄운다. 같은 기체에 떠 있는 팝업이 있으면 그 위로 쌓는다.</summary>
        public static StatusPopup Spawn(Transform follow, float heightOffset, string label, Color color, PopupStyle style)
        {
            if (follow == null || string.IsNullOrEmpty(label)) return null;
            if (_font == null) _font = Resources.GetBuiltinResource<Font>(BuiltinFontName);

            int stack = 0;
            foreach (StatusPopup other in Active)
            {
                if (other._follow == follow) stack++;
            }

            GameObject go = new GameObject("Popup_" + label);
            MeshRenderer meshRenderer = go.AddComponent<MeshRenderer>();
            TextMesh text = go.AddComponent<TextMesh>();
            text.font = _font;
            if (_font != null) meshRenderer.sharedMaterial = _font.material;
            text.text = label;
            text.fontSize = style.fontSize;
            text.characterSize = style.characterSize;
            text.anchor = TextAnchor.LowerCenter;
            text.alignment = TextAlignment.Center;
            text.fontStyle = FontStyle.Bold;
            text.color = color;
            // 그림자·라이트와 무관한 UI성 오브젝트
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            StatusPopup popup = go.AddComponent<StatusPopup>();
            popup._text = text;
            popup._follow = follow;
            popup._heightOffset = heightOffset + stack * style.stackSpacing;
            popup._color = color;
            popup._style = style;
            popup.UpdatePosition();
            return popup;
        }

        private void OnEnable()
        {
            if (Active.Count == 0)
            {
                RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
                UnityEngine.Camera.onPreCull += FaceCamera;
            }
            Active.Add(this);
        }

        private void OnDisable()
        {
            Active.Remove(this);
            if (Active.Count == 0)
            {
                RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
                UnityEngine.Camera.onPreCull -= FaceCamera;
            }
        }

        private void Update()
        {
            _age += Time.unscaledDeltaTime;
            if (_follow == null || _age >= _style.lifetime)
            {
                Destroy(gameObject);
                return;
            }

            UpdatePosition();

            float t = _age / _style.lifetime;
            float alpha = t <= _style.fadeStart ? 1f : 1f - Mathf.InverseLerp(_style.fadeStart, 1f, t);
            Color c = _color;
            c.a *= alpha;
            _text.color = c;
        }

        private void UpdatePosition()
        {
            float rise = _style.lifetime > 0f ? _style.riseDistance * (_age / _style.lifetime) : 0f;
            transform.position = _follow.position + Vector3.up * (_heightOffset + rise);
        }

        private static void OnBeginCameraRendering(ScriptableRenderContext context, UnityEngine.Camera cam)
        {
            FaceCamera(cam);
        }

        /// <summary>렌더 직전 카메라 하나에 대해 모든 팝업을 그 카메라 정면으로 돌린다.</summary>
        private static void FaceCamera(UnityEngine.Camera cam)
        {
            if (cam == null) return;
            Transform camTransform = cam.transform;
            foreach (StatusPopup popup in Active)
            {
                Transform t = popup.transform;
                // 카메라와 같은 회전 = 화면에 평행. TextMesh는 +Z를 등지고 읽히므로 카메라 정면 방향이 그대로 맞다
                t.rotation = camTransform.rotation;
                float distance = Vector3.Distance(camTransform.position, t.position);
                float scale = popup._style.referenceDistance > 0f
                    ? Mathf.Clamp(distance / popup._style.referenceDistance, 1f, popup._style.maxDistanceScale)
                    : 1f;
                t.localScale = Vector3.one * scale;
            }
        }
    }
}
