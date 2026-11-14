using System;
using UnityEngine;
using MyDongari.Core.VirtualOn;

namespace MyDongari.Bridge
{
    /// <summary>
    /// 우리 스타일 수치. 이동기 원본값(VoSpec, docs/REFERENCE_MOVEMENT.md)과 **다르게 쓰는 값은 전부 여기에만** 적는다.
    /// 칸마다 "덮어쓰기"를 끄면 그 값은 원본으로 돌아간다. 이동기는 원본 사본에 켜진 칸만 덮어쓴 결과(Effective)를 받는다.
    /// 두 기체가 같은 에셋(Resources/StyleOverrides)을 읽는다(미러전). 에셋이 없으면 아래 코드 기본값(= 지금 우리 값)으로 돈다.
    /// 원본과 다른 항목 전체 목록과 근거는 docs/PLAN_CLEANUP.md 3절.
    /// </summary>
    [CreateAssetMenu(menuName = "Mydongari/Style Overrides", fileName = "StyleOverrides")]
    public class StyleOverrides : ScriptableObject
    {
        /// <summary>Resources 폴더 기준 에셋 이름.</summary>
        public const string ResourcesName = "StyleOverrides";

        [Serializable]
        public struct FloatOverride
        {
            [Tooltip("켜면 아래 값으로 덮어쓴다. 끄면 원본값")]
            public bool enabled;
            public float value;
            public FloatOverride(bool enabled, float value) { this.enabled = enabled; this.value = value; }
            public void Apply(ref float target) { if (enabled) target = value; }
        }

        [Serializable]
        public struct IntOverride
        {
            [Tooltip("켜면 아래 값으로 덮어쓴다. 끄면 원본값")]
            public bool enabled;
            public int value;
            public IntOverride(bool enabled, int value) { this.enabled = enabled; this.value = value; }
            public void Apply(ref int target) { if (enabled) target = value; }
        }

        [Serializable]
        public struct BoolOverride
        {
            [Tooltip("켜면 아래 값으로 덮어쓴다. 끄면 원본값")]
            public bool enabled;
            public bool value;
            public BoolOverride(bool enabled, bool value) { this.enabled = enabled; this.value = value; }
            public void Apply(ref bool target) { if (enabled) target = value; }
        }

        [Header("선회 (원본: 사양서 2.2)")]
        [Tooltip("V9e: 빠른 '정면 밖' 선회율(759°/s)을 상대 쪽으로 도는 입력에만 쓴다 — 반대쪽으로 돌 때는 항상 '정면 안' 선회율. 원본은 방향을 가리지 않는다(끔)")]
        public BoolOverride fastTurnOnlyTowardOpponent = new BoolOverride(true, true);
        [Tooltip("V9e: 정면 안 → 밖 선회율이 33.75°에서 이 각도(°)까지 선형으로 올라간다(경계에서 훅 빨라지지 않게). 가드 자동 선회도 같은 구간. 원본은 33.75°에서 바로 바뀜")]
        public FloatOverride turnBlendEndAngle = new FloatOverride(true, 60f);

        [Header("조작 (원본: 사양서 2.1·4.6·4.8)")]
        [Tooltip("V8.1: 스틱 중립에서 ○를 누르면 앞 대시. 원본은 방향 잠금이 없어 안 나간다(끔)")]
        public BoolOverride neutralTurboDashesForward = new BoolOverride(true, true);
        [Tooltip("V8.2: 근접 구역 서기의 중립 ○ 앞 대시가 시작 후 이 프레임 안에 스틱이 들어오면 근접 스텝으로 바뀐다 (15f = 0.25초). 원본엔 없다(0)")]
        public IntOverride closeStepFromDashFrames = new IntOverride(true, 15);
        [Tooltip("V9a: 상승 중 △ 재입력(2단 점프). 원본엔 있다(켬) — 우리는 뺐다")]
        public BoolOverride risingRejump = new BoolOverride(true, false);
        [Tooltip("V8.1: L스틱이 이 기울기 이상이면 그 방향으로 민 것 — 걷기·점프는 즉시 방향별 최대 속도. 원본의 레버 누적값 모델(임계 64/48, 속력 × 0.8~1.2) 대신 쓰는 우리 입력 규칙이라 끌 수 없다")]
        [Range(0f, 1f)] public float stickThreshold = 0.3f;

        [Header("카메라 (원본: 사양서 5장)")]
        [Tooltip("V8.3: 기준점(시선 표적)이 매 틱 기체 위치와의 차이를 이 비율만큼 따라감 (35%). 작을수록 대시 때 기체가 화면에서 더 처진다. 원본은 지연 없음(1)")]
        public FloatOverride cameraPositionFollow = new FloatOverride(true, 0.35f);
        [Tooltip("V8.3: 기체가 화면 가로 중앙에서 벗어날 수 있는 최대 거리(화면 폭 대비 0.25 = 25%). 넘으면 기준점을 당겨 온다. 원본엔 없다(0.5 = 화면 끝)")]
        public FloatOverride cameraMaxScreenOffset = new FloatOverride(true, 0.25f);
        [Tooltip("시선 표적 기준 높이 = 발 + 키 × 이 비율. 원본 계산식을 확인하지 못해(사양서 5.2 '낮음') 정한 값. 끄면 코어의 중립값 0.5")]
        public FloatOverride cameraBaseHeightRatio = new FloatOverride(true, 0.5f);
        [Tooltip("세로 FOV(°). 사양서에 없는 우리 값 — 원본 카메라 거리 9.2m(78u, 기체 키 2.6m 기준)에서 기체가 화면 높이의 45%가 되도록 역산한 값: " +
                 "2 × atan((2.6 ÷ 0.45) ÷ (2 × 9.2)) ≈ 34.9° → 35°")]
        public float cameraFov = 35f;

        [Header("우리 규칙의 선회율 (원본에 대응 없음, 각 단위/f — 65536 = 360°)")]
        [Tooltip("가드 자동 선회 (V9a): 가드 중엔 R스틱 대신 몸이 상대 쪽으로 돈다. 상대가 정면 ±33.75° 안일 때 (400 = 132°/s)")]
        public float guardTurnInside = 400f;
        [Tooltip("가드 자동 선회: 상대가 정면 밖일 때 (1092 = 360°/s). 33.75° ~ 보간 끝 각도 사이는 두 값 사이를 선형으로")]
        public float guardTurnOutside = 1092f;
        [Tooltip("공격 사용 '사격 조준': 15f 동안 몸을 상대 쪽으로 돌리는 선회율. 원본은 무기별 80~2560 — 우리 공격(펀치·런지 등) 하나의 값 (2560 = 14.06°/f)")]
        public float attackAimRate = 2560f;

        // ---- 유효값 ----
        [NonSerialized] private VoSpec _effective;

        /// <summary>원본 사본에 켜진 칸만 덮어쓴 이동기 수치. 에셋을 고치면(OnValidate) 다음에 읽을 때 다시 만든다.</summary>
        public VoSpec Effective
        {
            get
            {
                if (_effective == null) _effective = Build();
                return _effective;
            }
        }

        private void OnValidate()
        {
            _effective = null;
        }

        private void OnEnable()
        {
            _effective = null;
        }

        private VoSpec Build()
        {
            VoSpec s = VoSpec.Original();
            fastTurnOnlyTowardOpponent.Apply(ref s.fastTurnOnlyTowardOpponent);
            turnBlendEndAngle.Apply(ref s.turnBlendEndAngle);
            neutralTurboDashesForward.Apply(ref s.neutralTurboDashesForward);
            closeStepFromDashFrames.Apply(ref s.closeStepFromDashFrames);
            risingRejump.Apply(ref s.risingRejump);
            cameraPositionFollow.Apply(ref s.cameraPositionFollow);
            cameraMaxScreenOffset.Apply(ref s.cameraMaxScreenOffset);
            cameraBaseHeightRatio.Apply(ref s.cameraBaseHeightRatio);
            return s;
        }

        private static StyleOverrides _current;

        /// <summary>쓸 에셋: Resources/StyleOverrides → 없으면 코드 기본값 인스턴스.</summary>
        public static StyleOverrides Current
        {
            get
            {
                if (_current == null)
                {
                    _current = Resources.Load<StyleOverrides>(ResourcesName);
                    if (_current == null)
                    {
                        _current = CreateInstance<StyleOverrides>();
                        _current.name = "StyleOverrides (코드 기본값)";
                        _current.hideFlags = HideFlags.DontSave;
                    }
                }
                return _current;
            }
        }
    }
}
