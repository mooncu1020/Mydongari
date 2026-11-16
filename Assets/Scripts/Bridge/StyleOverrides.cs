using System;
using UnityEngine;
using UnityEngine.Serialization;
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

        /// <summary>
        /// 공격 캡슐 하나: 손 높이에서 기체 중심 앞 start(m)부터 range(m)까지 정면으로 이어진 축에 반지름 radius(m).
        /// 원본의 "칼 밑동 ~ 칼끝이 쓸고 간 면"을 흉내 낸 것 — 사거리 끝의 구 하나였을 땐 1m 안에 붙은 상대가 비스듬히 서면 빠졌다.
        /// </summary>
        [Serializable]
        public struct AttackCapsule
        {
            [Tooltip("기체 중심에서 정면으로 캡슐 축이 시작하는 거리(m) — 몸 바로 앞")]
            public float start;
            [Tooltip("기체 중심에서 정면으로 캡슐 축이 끝나는 거리(m) — 사거리 끝")]
            public float range;
            [Tooltip("캡슐 반지름(m)")]
            public float radius;
            public AttackCapsule(float start, float range, float radius) { this.start = start; this.range = range; this.radius = radius; }
        }

        [Header("명중 판정 — 우리 근사 (원본: 칼끝이 쓸고 간 면 vs 피격 구 7개, 골격 길이 미확정)")]
        [Tooltip("원·투의 공격 캡슐. 지난 틱 위치 → 이번 틱 위치로 쓸어서 상대 피격 캡슐과 겹치면 명중")]
        [FormerlySerializedAs("punchSphere")]
        public AttackCapsule punchCapsule = new AttackCapsule(0.3f, 1.8f, 0.5f);
        [Tooltip("쓰리의 공격 캡슐")]
        [FormerlySerializedAs("thirdSphere")]
        public AttackCapsule thirdCapsule = new AttackCapsule(0.3f, 2.0f, 0.6f);
        [Tooltip("뛰어 베기의 공격 캡슐")]
        [FormerlySerializedAs("jumpSlashSphere")]
        public AttackCapsule jumpSlashCapsule = new AttackCapsule(0.3f, 2.0f, 0.7f);
        [Tooltip("공격 캡슐 높이 = 발 + 기체 키 × 이 비율 (손 높이)")]
        [Range(0f, 1f)] public float attackHandHeightRatio = 0.6f;
        [Tooltip("점프 상승 중 피격 캡슐의 높이·반지름 배율 (원본: 피격 구 7개 → 3개)")]
        [Range(0.1f, 1f)] public float jumpRisingHurtScale = 0.6f;
        [Tooltip("점프 하강 중 피격 캡슐의 높이·반지름 배율 (원본: 피격 구 2개)")]
        [Range(0.1f, 1f)] public float jumpFallingHurtScale = 0.45f;

        [Header("피해 (원본: 템진 근접 피해 — A1 1타 290 / 2타 130 / C1 380 / 점프 근접 340 / 대시 근접 275 / A2 435 / C2 460)")]
        [Tooltip("원본 피해 → 우리 체력 스케일 배율. 기술 사이의 비율은 VoCombatSpec의 원본값 그대로이고 이 값 하나로 전체 크기를 맞춘다. " +
                 "체력 100에서 원(A1 1타 290)이 10이 되도록 10 ÷ 290. 끄면 원본 스케일(1)이라 한 방에 죽는다")]
        public FloatOverride damageScale = new FloatOverride(true, 10f / 290f);

        [Header("충격치 (원본: A1 1타 500 → 경직, A1 2타 1100 → 다운)")]
        [Tooltip("원의 충격치. 우리 값 280 = 경직만 (원 + 투를 연달아 맞아도 다운 문턱 600 아래). 끄면 원본 500")]
        public FloatOverride punch1Impact = new FloatOverride(true, 280f);
        [Tooltip("투의 충격치. 우리 값 280 = 경직만. 끄면 원본 1100(다운)")]
        public FloatOverride punch2Impact = new FloatOverride(true, 280f);
        [Tooltip("쓰리의 충격치. 우리 값 620 = 단독으로 다운(문턱 600)이고, 원·투를 다 맞힌 뒤(누적 최대 560)에도 날아감 문턱 1200을 넘지 않아 다운으로 끝난다. 끄면 원본 C1 1100(원투쓰리가 날아감이 된다)")]
        public FloatOverride punch3Impact = new FloatOverride(true, 620f);
        [Tooltip("임팩트 슬램의 충격치 (원본에 대응 없음 — 일반 근접, 가드 가능, 다운급)")]
        public float slamImpact = 1100f;

        [Header("뛰어 베기 — [미상] 원본 미확인이라 우리 값 (원본 확정: vy 3.80u/f, 추적 2560 단위/f, 점프 입력 후 8f)")]
        [Tooltip("[미상] 기준 속력(u/f). 정면으로 파고드는 빠르기")]
        public FloatOverride jumpMeleeSpeed = new FloatOverride(true, 4.0f);
        [Tooltip("[미상] ① 파고들기 길이(틱)")]
        public IntOverride jumpMeleeApproachFrames = new IntOverride(true, 12);
        [Tooltip("[미상] ② 공격 길이(틱). 끝나면 일반 낙하")]
        public IntOverride jumpMeleeAttackFrames = new IntOverride(true, 10);
        [Tooltip("[미상] 판정 창 시작(② 단계 틱 번호)")]
        public IntOverride jumpMeleeHitStart = new IntOverride(true, 2);
        [Tooltip("[미상] 판정 창 끝(이 번호 직전까지 = 2~7f)")]
        public IntOverride jumpMeleeHitEnd = new IntOverride(true, 8);
        [Tooltip("[미상] 뛰어 베기 중 중력(u/f²). 점프 중력(0.074)이면 vy 3.80으로 6m까지 솟아 지상의 상대에게 안 닿아서, 다운 상태의 중력(0.451)을 빌려 썼다 — 정점 약 1.4m, 약 17틱 뒤 착지")]
        public FloatOverride jumpMeleeGravity = new FloatOverride(true, 0.451f);
        [Tooltip("[미상] 뛰어 베기 중 낙하 상한(u/f)")]
        public FloatOverride jumpMeleeFallMaxVy = new FloatOverride(true, 4.001f);

        // ---- 유효값 ----
        [NonSerialized] private VoSpec _effective;
        [NonSerialized] private VoCombatSpec _effectiveCombat;

        /// <summary>전투 원본 사본에 켜진 칸만 덮어쓴 전투 수치.</summary>
        public VoCombatSpec EffectiveCombat
        {
            get
            {
                if (_effectiveCombat == null) _effectiveCombat = BuildCombat();
                return _effectiveCombat;
            }
        }

        private VoCombatSpec BuildCombat()
        {
            VoCombatSpec c = VoCombatSpec.Original();
            damageScale.Apply(ref c.damageScale);
            punch1Impact.Apply(ref c.impactA1First);
            punch2Impact.Apply(ref c.impactA1Second);
            punch3Impact.Apply(ref c.impactC1);
            jumpMeleeSpeed.Apply(ref c.jumpMelee.baseSpeed);
            jumpMeleeApproachFrames.Apply(ref c.jumpMelee.approachFrames);
            jumpMeleeAttackFrames.Apply(ref c.jumpMelee.attackFrames);
            jumpMeleeHitStart.Apply(ref c.jumpMelee.hitStart);
            jumpMeleeHitEnd.Apply(ref c.jumpMelee.hitEnd);
            jumpMeleeGravity.Apply(ref c.jumpMeleeGravity);
            jumpMeleeFallMaxVy.Apply(ref c.jumpMeleeFallMaxVy);
            return c;
        }

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
            _effectiveCombat = null;
        }

        private void OnEnable()
        {
            _effective = null;
            _effectiveCombat = null;
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
