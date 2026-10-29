using MyDongari.Core;
using System;
using UnityEngine;
using MyDongari.Combat;
using MyDongari.Combat.Skills;
using MyDongari.Mech;
using MyDongari.Movement;

namespace MyDongari.Visual
{
    /// <summary>리그 슬롯 하나: 움직일 부품(관절 위치에 피벗)과 축 보정.</summary>
    [Serializable]
    public class RigSlot
    {
        [Tooltip("움직일 부품. 피벗이 관절(어깨·팔꿈치·손목·엉덩이·목·허리)에 있어야 한다")]
        public Transform target;
        [Tooltip("추가 회전 보정(°). 축은 처음 자세의 뼈 방향으로 자동으로 맞추므로 보통 0. 동작 방향이 여전히 어긋날 때만")]
        public Vector3 rotationCorrection;
    }

    /// <summary>
    /// 리깅 없이 코드로 부품을 움직이는 절차적 애니메이션. 콜라이더·리지드바디는 루트에 그대로 두고
    /// 시각 전용 부품만 LateUpdate에서 움직인다. 전투 코드는 이 컴포넌트를 모른다 —
    /// 기존 이벤트(공격 시작, 피격 결과, 잡기, 파일드라이버, 슬램)를 구독하고 상태(가드·스턴·런지·속도)를 읽기만 한다.
    /// 모든 부품은 기준 자세 위에 오프셋을 얹고, 그 오프셋은 감쇠 스프링으로 목표를 따라간다.
    /// 기준 자세 = Animator가 있으면 그 프레임 Animator가 만든 자세(반복 동작 키프레임), 없거나 그 뼈를 안 건드렸으면 쉴 때 자세(rest).
    /// 오프셋 각도는 기체 축(X 오른쪽, Y 위, Z 앞) 기준이고, 처음 자세에서 잰 뼈 방향으로 각 부품 축에 맞춰 바꿔 넣는다
    /// (임포트된 뼈의 축·롤이 제각각이어도 같은 각도가 같은 방향으로 움직이게).
    /// 수치는 BernardTuning의 Visual 묶음.
    /// </summary>
    public partial class MechVisualRig : MonoBehaviour, IUpperBodyFacing
    {
        /// <summary>슬롯 이름 = 모델 부품 이름 (E2 모델 교체 메뉴가 이 이름으로 찾는다).</summary>
        public static readonly string[] SlotNames =
        {
            "Body", "Head",
            "Arm_L_Upper", "Arm_L_Lower", "Fist_L",
            "Arm_R_Upper", "Arm_R_Lower", "Fist_R",
            "Leg_L", "Leg_R"
        };

        /// <summary>SlotNames와 같은 순서의 직렬화 필드 이름 (에디터 메뉴용).</summary>
        public static readonly string[] SlotFieldNames =
        {
            "body", "head",
            "armLUpper", "armLLower", "fistL",
            "armRUpper", "armRLower", "fistR",
            "legL", "legR"
        };

        /// <summary>
        /// 없어도 되는 슬롯. 주먹 뼈가 없는 모델이 많아서, 없으면 아래팔 끝을 펀치 지점으로 쓴다 (GetPunchPoint).
        /// 모델 교체 메뉴는 이 슬롯을 못 찾아도 경고하지 않는다.
        /// </summary>
        public static readonly string[] OptionalSlotNames = { "Fist_L", "Fist_R" };

        public static bool IsOptionalSlot(string slotName) => Array.IndexOf(OptionalSlotNames, slotName) >= 0;

        [Header("동작")]
        [SerializeField] private bool animate = true;

        [Header("키프레임 반복 동작 (선택)")]
        [Tooltip("리깅된 모델의 Animator (Idle/Walk/Run/Sprint). 비워 두면 모델 아래에서 자동으로 찾는다. 없으면 코드 동작만")]
        [SerializeField] private Animator animator;

        /// <summary>반복 동작 Animator Controller 파라미터·상태 이름 (에디터 메뉴와 같이 쓴다).</summary>
        public const string SpeedParameter = "Speed";
        public const string MoveStateParameter = "MoveState";
        public static readonly string[] LocomotionStates = { "Idle", "Walk", "Run", "Sprint" };

        [Header("몸통")]
        [SerializeField] private RigSlot body = new RigSlot();
        [SerializeField] private RigSlot head = new RigSlot();
        [Header("왼팔")]
        [SerializeField] private RigSlot armLUpper = new RigSlot();
        [SerializeField] private RigSlot armLLower = new RigSlot();
        [Tooltip("선택 사항. 비워 두면 아래팔 끝을 펀치 지점으로 쓴다")]
        [SerializeField] private RigSlot fistL = new RigSlot();
        [Header("오른팔")]
        [SerializeField] private RigSlot armRUpper = new RigSlot();
        [SerializeField] private RigSlot armRLower = new RigSlot();
        [Tooltip("선택 사항. 비워 두면 아래팔 끝을 펀치 지점으로 쓴다")]
        [SerializeField] private RigSlot fistR = new RigSlot();
        [Header("다리")]
        [SerializeField] private RigSlot legL = new RigSlot();
        [SerializeField] private RigSlot legR = new RigSlot();
        [Header("무릎 (선택 — 비우면 모델에서 이름으로 찾고, 없으면 무시)")]
        [Tooltip("왼 정강이 뼈. 비워 두면 기체 아래에서 'Shin.L'을 찾는다")]
        [SerializeField] private RigSlot shinL = new RigSlot();
        [Tooltip("오른 정강이 뼈. 비워 두면 기체 아래에서 'Shin.R'을 찾는다")]
        [SerializeField] private RigSlot shinR = new RigSlot();
        [Header("발목 (선택 — 비우면 이름으로 찾고, 없으면 다리 끝으로 접지)")]
        [Tooltip("왼 발목 뼈. 비워 두면 기체 아래에서 'Ankle.L'을 찾는다")]
        [SerializeField] private RigSlot ankleL = new RigSlot();
        [Tooltip("오른 발목 뼈. 비워 두면 기체 아래에서 'Ankle.R'을 찾는다")]
        [SerializeField] private RigSlot ankleR = new RigSlot();
        [Header("하체·스커트 (선택 — 비우면 이름으로 찾고, 없으면 무시. 임시 상자엔 없음)")]
        [Tooltip("하체(골반) 뼈. 비워 두면 'Pelvis'. 있으면 상하체 분리(G2)·착지 꺼짐(G3)을 쓴다")]
        [SerializeField] private RigSlot pelvis = new RigSlot();
        [Tooltip("왼 엉덩이 뼈. 비워 두면 'Hip.L'")]
        [SerializeField] private RigSlot hipL = new RigSlot();
        [Tooltip("오른 엉덩이 뼈. 비워 두면 'Hip.R'")]
        [SerializeField] private RigSlot hipR = new RigSlot();
        [Tooltip("비워 두면 'Skirt_Front'")]
        [SerializeField] private RigSlot skirtFront = new RigSlot();
        [Tooltip("비워 두면 'Skirt_Back'")]
        [SerializeField] private RigSlot skirtBack = new RigSlot();
        [Tooltip("비워 두면 'Skirt_Side.L'")]
        [SerializeField] private RigSlot skirtSideL = new RigSlot();
        [Tooltip("비워 두면 'Skirt_Side.R'")]
        [SerializeField] private RigSlot skirtSideR = new RigSlot();

        /// <summary>무릎 뼈 이름. 필수 슬롯이 아니라 SlotNames(모델 교체 메뉴 경고 대상)에 넣지 않는다.</summary>
        public const string ShinLName = "Shin.L";
        public const string ShinRName = "Shin.R";
        /// <summary>하체 뼈 이름. bernard 원본 계층(Root → Body → Pelvis)을 시작할 때 Root → Pelvis → Body로 바꾸는 데 쓴다 (H1).</summary>
        public const string PelvisName = "Pelvis";
        public const string AnkleLName = "Ankle.L";
        public const string AnkleRName = "Ankle.R";
        public const string HipLName = "Hip.L";
        public const string HipRName = "Hip.R";
        public const string SkirtFrontName = "Skirt_Front";
        public const string SkirtBackName = "Skirt_Back";
        public const string SkirtSideLName = "Skirt_Side.L";
        public const string SkirtSideRName = "Skirt_Side.R";

        /// <summary>슬롯 하나의 런타임 상태: 쉴 때 자세 + 스프링(위치·회전 오프셋).</summary>
        private class Joint
        {
            public RigSlot Slot;
            public Transform Target;
            public Vector3 RestPosition;
            public Quaternion RestRotation;
            // 처음 자세에서 잰 "기체 기준 → 이 부품 기준" 회전 (회전 오프셋용)
            public Quaternion BoneFromMech = Quaternion.identity;
            // 이번 프레임 기준 자세, 지난 프레임에 직접 써 넣은 값 (Animator가 안 건드린 뼈 구분용)
            public Vector3 BasePosition;
            public Quaternion BaseRotation;
            public Vector3 WrittenPosition;
            public Quaternion WrittenRotation;
            public bool HasWritten;
            public Vector3 Position;
            public Vector3 PositionVelocity;
            public Vector3 Rotation;          // 오일러(°), 기준 축 기준
            public Vector3 RotationVelocity;
            public Vector3 TargetPosition;
            public Vector3 TargetRotation;
            // 수평 회전(°, 기체 위 축). 상하체 분리·머리 돌리기(G2)가 스프링 없이 직접 넣는다
            public float Yaw;
            // 이번 프레임 전신 모션(M1)이 이 관절을 맡은 비율. 스프링 우회에 쓴다 (매 프레임 0으로)
            public float MotionWeight;
            // 스프링 없이 이번 프레임에만 더하는 위치 오프셋(m, 기체 축). 피스톤·막음 반동처럼 정확한 시간이 필요한 것 (매 프레임 0으로)
            public Vector3 DirectPosition;
        }

        private Joint _body, _head, _armLUpper, _armLLower, _fistL, _armRUpper, _armRLower, _fistR, _legL, _legR, _shinL, _shinR, _ankleL, _ankleR,
            _pelvis, _hipL, _hipR, _skirtFront, _skirtBack, _skirtSideL, _skirtSideR;
        /// <summary>하체(Pelvis) 뼈가 있는 모델인지. 상하체 분리·무게감 걸음(G2~G4)은 이 구조에서만 쓴다 (임시 상자는 예전 동작).</summary>
        private bool HasLowerBody => _pelvis != null && _pelvis.Target != null;
        private Joint[] _joints;

        private TuningRef _tuning;
        private BernardTuning.VisualValues V => _tuning.Get(this).Visual;
        private BernardTuning.AttackStartupValues Startup => _tuning.Get(this).Startup;

        // 예비 동작 길이 = 그 공격의 발생 시간(버튼 → 판정). 발생 시간이 0(즉시 판정)이면 Visual의 기본 예비 동작 길이
        private float PunchWindup => Startup.punch > 0f ? Startup.punch : V.anticipationTime;
        private float TackleWindup
        {
            get
            {
                float startup = _tackleType == AttackType.Tackle ? Startup.chargedTackle : Startup.dashAttack;
                return startup > 0f ? startup : V.anticipationTime;
            }
        }
        private AttackType _tackleType = AttackType.DashAttack;

        private Rigidbody _rb;
        private MechStateMachine _stateMachine;
        private int _moveState;
        private MeleeSystem _melee;
        private ThrusterLunge _lunge;
        private AnchorClaw _claw;
        private ImpactSlam _slam;
        private LockOnSystem _lockOn;
        private MechIdentity _identity;

        // 리그 자체 시계(scaled). 히트스톱(timeScale 0) 동안 동작도 같이 멈춘다
        private float _clock;
        private float _walkPhase;
        private const float None = -999f;
        private float _punchStart = None;
        private bool _punchRight = true;
        private float _lastPunchTime = None;
        private float _tackleStart = None;
        private float _clawStart = None;
        private float _pileStart = None;
        private float _slamStart = None;
        // 숨쉬기 세기(0~1). 움직이거나 다른 동작 중이면 breathFadeTime에 걸쳐 0으로
        private float _breathWeight = 1f;
        // 걸음 동작 세기(0~1, G3). 0이면 대기 자세 다리가 그대로 나온다
        private float _gaitWeight;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _melee = GetComponent<MeleeSystem>();
            _stateMachine = GetComponent<MechStateMachine>();
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            if (animator != null)
            {
                // 모델 애니메이션이 기체(물리)를 움직이면 안 된다
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                // 컨트롤러가 없는 Animator(FBX가 자동으로 붙인 것)는 할 일이 없으니 꺼서 뼈를 절대 건드리지 않게 한다
                if (animator.runtimeAnimatorController == null) animator.enabled = false;
            }
            // 계층을 먼저 바꾸고 나서 원래 자세를 잰다 (바꾼 뒤의 로컬 값이 기준 자세)
            RestructureLowerBody();
            BindJoints();
        }

        /// <summary>
        /// bernard 원본 계층은 Root → Body → Pelvis(→ 다리)라서 상체(Body)를 기울이거나 올리면 다리까지 같이 움직인다.
        /// 시작할 때 Pelvis를 Root(= Body의 원래 부모) 자식으로, Body를 Pelvis 자식으로 옮긴다 (월드 위치·회전·배율 유지).
        /// Pelvis가 없거나(임시 상자) Body 아래에 있지 않으면(이미 바뀐 구조) 아무것도 안 한다.
        /// </summary>
        private void RestructureLowerBody()
        {
            Transform bodyBone = OwnBone(body != null ? body.target : null);
            if (bodyBone == null) return;
            Transform pelvisBone = OwnBone(pelvis != null && pelvis.target != null ? pelvis.target : FindBone(PelvisName));
            if (pelvisBone == null || pelvisBone == bodyBone || !pelvisBone.IsChildOf(bodyBone)) return;
            Transform root = bodyBone.parent;
            if (root == null) return;

            pelvisBone.SetParent(root, true);
            bodyBone.SetParent(pelvisBone, true);
        }

        /// <summary>
        /// 슬롯 뼈가 이 기체 아래에 있는지 검사한다 (J1). 다른 기체(복제 전 원본 등)의 뼈를 가리키면 두 기체가 같은 뼈를
        /// 서로 덮어써서 파츠가 튄다 — 에러를 남기고 그 슬롯은 비운다(움직이지 않음).
        /// </summary>
        private Transform OwnBone(Transform bone)
        {
            if (bone == null) return null;
            if (bone == transform || !bone.IsChildOf(transform))
            {
                Debug.LogError($"[MechVisualRig] {name}: 슬롯 뼈 '{bone.name}'({bone.root.name} 아래)가 이 기체 아래가 아니라 무시합니다. " +
                               "Tools/Apply Bernard Model을 다시 실행하세요.", this);
                return null;
            }
            return bone;
        }

        /// <summary>기체 아래에서 이름으로 뼈를 찾는다. 같은 이름의 뼈와 메시가 둘 다 있으면 뼈(메시 없는 Transform)를 고른다.</summary>
        private Transform FindBone(string boneName)
        {
            Transform found = null;
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name != boneName) continue;
                if (t.GetComponent<Renderer>() == null && t.GetComponent<MeshFilter>() == null) return t;
                if (found == null) found = t;
            }
            return found;
        }

        private void Start()
        {
            // ThrusterLunge 등은 다른 컴포넌트 Awake에서 런타임에 붙기도 해서 Start에서 찾는다
            _lunge = GetComponent<ThrusterLunge>();
            _claw = GetComponent<AnchorClaw>();
            _slam = GetComponent<ImpactSlam>();
            _lockOn = GetComponent<LockOnSystem>();
            _identity = GetComponent<MechIdentity>();

            if (_melee != null)
            {
                _melee.OnAttackStarted += OnAttackStarted;
                _melee.OnAttackCanceled += OnAttackCanceled;
                _melee.OnHitResolved += OnHitResolved;
            }
            if (_claw != null)
            {
                _claw.OnGrabbed += OnClawGrabbed;
                _claw.OnPileDriverStarted += OnPileDriver;
            }
            if (_slam != null) _slam.OnSlamStarted += OnSlam;
            if (_lunge != null)
            {
                _lunge.OnLungeStarted += OnLungeStartedVisual;
                _lunge.OnLungePlant += OnLungePlantVisual;
                _lunge.OnLungeEnded += OnLungeEndedVisual;
            }
        }

        private void OnDestroy()
        {
            if (_melee != null)
            {
                _melee.OnAttackStarted -= OnAttackStarted;
                _melee.OnAttackCanceled -= OnAttackCanceled;
                _melee.OnHitResolved -= OnHitResolved;
            }
            if (_claw != null)
            {
                _claw.OnGrabbed -= OnClawGrabbed;
                _claw.OnPileDriverStarted -= OnPileDriver;
            }
            if (_slam != null) _slam.OnSlamStarted -= OnSlam;
            if (_lunge != null)
            {
                _lunge.OnLungeStarted -= OnLungeStartedVisual;
                _lunge.OnLungePlant -= OnLungePlantVisual;
                _lunge.OnLungeEnded -= OnLungeEndedVisual;
            }
        }

        private void OnDisable()
        {
            // 꺼질 때 부품을 쉴 때 자세로 돌려 둔다 (에디터에서 끄고 보면 원래 모양)
            if (_joints == null) return;
            foreach (Joint joint in _joints)
            {
                if (joint == null || joint.Target == null) continue;
                joint.Target.localPosition = joint.RestPosition;
                joint.Target.localRotation = joint.RestRotation;
            }
            ResetGrounding();
        }

        /// <summary>
        /// 슬롯 대상이 바뀌었을 때(모델 교체) 쉴 때 자세를 다시 잡는다. 대상의 현재 로컬 자세가 rest가 된다.
        /// </summary>
        public void BindJoints()
        {
            _body = Bind(body);
            _head = Bind(head);
            _armLUpper = Bind(armLUpper);
            _armLLower = Bind(armLLower);
            _fistL = Bind(fistL);
            _armRUpper = Bind(armRUpper);
            _armRLower = Bind(armRLower);
            _fistR = Bind(fistR);
            _legL = Bind(legL);
            _legR = Bind(legR);
            // 무릎은 선택: 칸이 비어 있으면 모델 아래에서 이름으로 찾는다 (모델 교체 메뉴는 이 칸을 안 채운다)
            _shinL = Bind(ResolveOptionalSlot(shinL, ShinLName));
            _shinR = Bind(ResolveOptionalSlot(shinR, ShinRName));
            _ankleL = Bind(ResolveOptionalSlot(ankleL, AnkleLName));
            _ankleR = Bind(ResolveOptionalSlot(ankleR, AnkleRName));
            _pelvis = Bind(ResolveOptionalSlot(pelvis, PelvisName));
            _hipL = Bind(ResolveOptionalSlot(hipL, HipLName));
            _hipR = Bind(ResolveOptionalSlot(hipR, HipRName));
            _skirtFront = Bind(ResolveOptionalSlot(skirtFront, SkirtFrontName));
            _skirtBack = Bind(ResolveOptionalSlot(skirtBack, SkirtBackName));
            _skirtSideL = Bind(ResolveOptionalSlot(skirtSideL, SkirtSideLName));
            _skirtSideR = Bind(ResolveOptionalSlot(skirtSideR, SkirtSideRName));
            // 순서: 발목 평행 보정이 위쪽 사슬(골반·엉덩이·다리·정강이)의 이번 프레임 각도를 쓰므로 그 뒤에 발목
            _joints = new[]
            {
                _pelvis, _body, _head, _armLUpper, _armLLower, _fistL, _armRUpper, _armRLower, _fistR,
                _hipL, _hipR, _legL, _legR, _shinL, _shinR, _ankleL, _ankleR,
                _skirtFront, _skirtBack, _skirtSideL, _skirtSideR
            };
            BindPoseJoints();
            SetupGrounding();
            SetupGait();
        }

        /// <summary>칸이 비어 있으면 같은 이름의 Transform을 찾아 런타임 슬롯을 만든다 (뼈 우선, 없으면 그대로 빈 칸).</summary>
        private RigSlot ResolveOptionalSlot(RigSlot slot, string boneName)
        {
            if (slot != null && slot.target != null) return slot;
            return new RigSlot { target = FindBone(boneName), rotationCorrection = slot != null ? slot.rotationCorrection : Vector3.zero };
        }

        private Joint Bind(RigSlot slot)
        {
            var joint = new Joint { Slot = slot, Target = OwnBone(slot != null ? slot.target : null) };
            if (joint.Target != null)
            {
                joint.RestPosition = joint.Target.localPosition;
                joint.RestRotation = joint.Target.localRotation;
                Quaternion mechInverse = Quaternion.Inverse(transform.rotation);
                joint.BoneFromMech = Quaternion.Inverse(mechInverse * joint.Target.rotation);
            }
            return joint;
        }

        // ---------------- 펀치 지점 ----------------

        /// <summary>
        /// 펀치 지점(월드). 주먹 슬롯이 있으면 주먹(손목) 위치, 없으면 아래팔 끝, 아래팔도 없으면 기체 루트.
        /// 슬롯 Transform을 매번 직접 읽으므로 플레이 전(에디터)에도 맞는 값이 나온다.
        /// </summary>
        public Vector3 GetPunchPoint(bool right)
        {
            RigSlot fist = right ? fistR : fistL;
            if (fist != null && fist.target != null) return fist.target.position;
            RigSlot lower = right ? armRLower : armLLower;
            RigSlot upper = right ? armRUpper : armLUpper;
            if (lower != null && lower.target != null) return LowerArmEnd(lower.target, upper != null ? upper.target : null);
            return transform.position;
        }

        /// <summary>
        /// 아래팔 끝(손목) 추정. 뼈는 시작점(팔꿈치)만 있고 끝점이 Transform에 없어서:
        /// 1) 메시 없는 자식(손 뼈, Blender의 _end 끝 뼈)이 있으면 그중 가장 먼 것
        /// 2) 없으면 위팔과 같은 축 규칙이라 보고, 어깨→팔꿈치 벡터를 위팔 기준으로 잰 그대로 아래팔 기준에 얹는다
        ///    (아래팔이 위팔과 같은 길이라고 보는 근사. 임시 상자는 0.42 vs 0.38로 거의 같다)
        /// 3) 위팔도 없으면 팔꿈치 위치
        /// </summary>
        private static Vector3 LowerArmEnd(Transform lower, Transform upper)
        {
            Transform farthest = null;
            float farthestSqr = 0f;
            foreach (Transform child in lower)
            {
                if (child.GetComponent<Renderer>() != null || child.GetComponent<MeshFilter>() != null) continue;
                float sqr = (child.position - lower.position).sqrMagnitude;
                if (sqr > farthestSqr)
                {
                    farthest = child;
                    farthestSqr = sqr;
                }
            }
            if (farthest != null) return farthest.position;

            if (upper != null)
            {
                Vector3 upperBoneLocal = Quaternion.Inverse(upper.rotation) * (lower.position - upper.position);
                return lower.position + lower.rotation * upperBoneLocal;
            }
            return lower.position;
        }

        private void OnDrawGizmosSelected()
        {
            // 펀치 지점 확인용: 주먹이 없는 모델에서 아래팔 끝이 제대로 잡혔는지 씬 뷰에서 본다
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(GetPunchPoint(false), 0.08f);
            Gizmos.DrawWireSphere(GetPunchPoint(true), 0.08f);
        }

        // ---------------- 이벤트 ----------------

        private void OnAttackStarted(AttackType type)
        {
            // 예외가 나도 게임(과 이 이벤트를 부른 전투 코드)이 멈추지 않게 기록만 하고 넘어간다
            try
            {
                OnAttackStartedLogic(type);
            }
            catch (Exception e)
            {
                ErrorLog.Report(e, "MechVisualRig.OnAttackStarted");
            }
        }

        private void OnAttackStartedLogic(AttackType type)
        {
            switch (type)
            {
                case AttackType.Punch:
                    // 원투: 직전 펀치가 comboWindow 안이면 반대 팔, 아니면 오른팔부터
                    _punchRight = _clock - _lastPunchTime > V.comboWindow || !_punchRight;
                    _punchStart = _clock;
                    _lastPunchTime = _clock;
                    // 달리며 친 펀치는 앞발 디딤을 더한다 (M3, 시작 순간 걸음 세기만큼)
                    _punchGait = _gaitWeight;
                    break;
                case AttackType.DashAttack:
                case AttackType.Tackle:
                    _tackleStart = _clock;
                    _tackleType = type;
                    break;
            }
        }

        /// <summary>발생 중 스턴 등으로 공격이 취소되면 뻗는 동작도 하지 않는다.</summary>
        private void OnAttackCanceled(AttackType type)
        {
            if (type == AttackType.Punch) _punchStart = None;
            else if (type == AttackType.DashAttack || type == AttackType.Tackle) _tackleStart = None;
        }

        private void OnHitResolved(HitInfo info, HitResult result)
        {
            // 예외가 나도 게임(과 이 이벤트를 부른 전투 코드)이 멈추지 않게 기록만 하고 넘어간다
            try
            {
                OnHitResolvedLogic(info, result);
            }
            catch (Exception e)
            {
                ErrorLog.Report(e, "MechVisualRig.OnHitResolved");
            }
        }

        private void OnHitResolvedLogic(HitInfo info, HitResult result)
        {
            // 전신 모션 모델은 막음·패리·가드 브레이크를 포즈 모션으로 (M5)
            if (UseMotions)
            {
                OnDefenseResolved(info, result);
                return;
            }
            BernardTuning.VisualValues v = V;
            if (result == HitResult.Parried)
            {
                // 패리: 양팔을 위로 튕기고 상체를 살짝 젖힌다
                Kick(_armLUpper, new Vector3(-v.parryKick, 0f, 0f));
                Kick(_armRUpper, new Vector3(-v.parryKick, 0f, 0f));
                Kick(_body, new Vector3(-v.parryKick * 0.15f, 0f, 0f));
            }
            else if (result == HitResult.GuardBroken)
            {
                // 가드 깨짐: 팔이 바깥·뒤로 튕겨 벌어진다
                Kick(_armLUpper, new Vector3(v.guardBreakFling * 0.5f, 0f, -v.guardBreakFling));
                Kick(_armRUpper, new Vector3(v.guardBreakFling * 0.5f, 0f, v.guardBreakFling));
                Kick(_body, new Vector3(-v.guardBreakFling * 0.1f, 0f, 0f));
            }
        }

        private void OnClawGrabbed(GameObject target) => _clawStart = _clock;
        private void OnPileDriver() => _pileStart = _clock;
        private void OnSlam() => _slamStart = _clock;

        private static void Kick(Joint joint, Vector3 angularVelocity)
        {
            if (joint != null && joint.Target != null) joint.RotationVelocity += angularVelocity;
        }

        // ---------------- 매 프레임 ----------------

        private void LateUpdate()
        {
            // 예외가 나도 게임(과 이 이벤트를 부른 전투 코드)이 멈추지 않게 기록만 하고 넘어간다
            try
            {
                LateUpdateLogic();
            }
            catch (Exception e)
            {
                ErrorLog.Report(e, "MechVisualRig.LateUpdate");
            }
        }

        private void LateUpdateLogic()
        {
            if (!animate || _joints == null) return;

            // 포즈 캡처 중(에디터 도구): 사용자가 뼈를 직접 돌리는 동안 덮어쓰지 않는다
            if (PoseEditFreeze) return;

            float dt = Time.deltaTime;
            _clock += dt;
            BernardTuning.VisualValues v = V;

            bool animated = animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null;
            foreach (Joint joint in _joints)
            {
                joint.TargetPosition = Vector3.zero;
                joint.TargetRotation = Vector3.zero;
                joint.MotionWeight = 0f;
                joint.DirectPosition = Vector3.zero;
                if (joint.Target != null) CaptureBase(joint, animated);
            }
            _groundingHold = false;

            // 상하체 방향(G2)을 먼저 정하고 나머지 포즈를 얹는다
            UpdateFacing(v, dt);

            // 포즈 미리보기(에디터 도구): 그 포즈 하나만 그대로
            if (!string.IsNullOrEmpty(_previewPose))
            {
                PreviewLateUpdate(v, dt);
                return;
            }
            // 하체 뼈가 있는 모델은 무게감 걸음(G3), 없으면(임시 상자) 예전 걷기
            if (HasLowerBody) PoseGait(v, dt);
            else PoseMovement(v, dt);
            PoseIdle(v, dt);
            PoseGuard(v);
            PoseActions(v);
            PoseStun(v);

            // 스프링: 프레임이 튀어도 터지지 않게 dt 상한
            float step = Mathf.Min(dt, 1f / 30f);
            foreach (Joint joint in _joints)
            {
                if (joint.Target == null) continue;
                Spring(ref joint.Position, ref joint.PositionVelocity, joint.TargetPosition, v, step);
                // 스커트는 따로 느슨한 스프링으로 허벅지를 늦게 따라간다 (G3)
                if (IsSkirt(joint)) Spring(ref joint.Rotation, ref joint.RotationVelocity, joint.TargetRotation, v.skirtSpringFrequency, v.skirtSpringDamping, step);
                else Spring(ref joint.Rotation, ref joint.RotationVelocity, joint.TargetRotation, v, step);
                // 전신 모션이 맡은 관절은 스프링 지연 없이 목표로 (M1)
                ApplySpringBypass(joint);
                // 발목은 위쪽 사슬이 이번 프레임 각도로 정해진 뒤(배열에서 다리·정강이 다음) 평행 보정
                if (joint == _ankleL) LevelAnkle(joint, v, _pelvis, _hipL, _legL, _shinL);
                else if (joint == _ankleR) LevelAnkle(joint, v, _pelvis, _hipR, _legR, _shinR);
                Apply(joint);
            }

            // 포즈를 다 적용한 다음 발바닥 높이로 비주얼 루트 Y를 맞춘다 (H3)
            UpdateGrounding(v, dt);
        }

        private static void Spring(ref Vector3 x, ref Vector3 velocity, Vector3 target, BernardTuning.VisualValues v, float dt)
        {
            Spring(ref x, ref velocity, target, v.springFrequency, v.springDamping, dt);
        }

        private static void Spring(ref Vector3 x, ref Vector3 velocity, Vector3 target, float frequency, float damping, float dt)
        {
            if (dt <= 0f) return;
            float omega = 2f * Mathf.PI * Mathf.Max(0.1f, frequency);
            // 반암시 오일러: 감쇠 스프링 a = ω²(목표 - x) - 2ζω·v
            Vector3 acceleration = omega * omega * (target - x) - 2f * damping * omega * velocity;
            velocity += acceleration * dt;
            x += velocity * dt;
        }

        /// <summary>
        /// 이번 프레임 기준 자세. Animator가 이번 프레임에 이 뼈를 썼으면 그 자세, 아니면(클립 없음·안 움직이는 뼈)
        /// 지난 프레임에 우리가 써 둔 값이 그대로 남아 있으므로 쉴 때 자세로 — 오프셋이 누적되지 않게.
        /// </summary>
        private static void CaptureBase(Joint joint, bool animated)
        {
            Transform t = joint.Target;
            bool untouched = joint.HasWritten && SameExactly(t.localRotation, joint.WrittenRotation) && SameExactly(t.localPosition, joint.WrittenPosition);
            if (animated && !untouched)
            {
                joint.BasePosition = t.localPosition;
                joint.BaseRotation = t.localRotation;
            }
            else
            {
                joint.BasePosition = joint.RestPosition;
                joint.BaseRotation = joint.RestRotation;
            }
        }

        private static bool SameExactly(Quaternion a, Quaternion b)
        {
            // Quaternion·Vector3의 ==는 근사 비교라 정확히 같은지 성분으로 본다
            return a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
        }

        private static bool SameExactly(Vector3 a, Vector3 b)
        {
            return a.x == b.x && a.y == b.y && a.z == b.z;
        }

        private void Apply(Joint joint)
        {
            // 오프셋은 기체 축(X 오른쪽, Y 위, Z 앞)으로 정의 → 슬롯 추가 보정 → 처음 자세의 뼈 방향으로 부품 축에 맞춰
            // 기준 자세(원래 회전) 위에 얹는다: 원래 회전 * 오프셋 회전
            Quaternion correction = Quaternion.Euler(joint.Slot.rotationCorrection);
            Vector3 euler = joint.Rotation;
            euler.y += joint.Yaw;
            Quaternion mechOffset = correction * Quaternion.Euler(euler) * Quaternion.Inverse(correction);
            Quaternion localOffset = joint.BoneFromMech * mechOffset * Quaternion.Inverse(joint.BoneFromMech);
            joint.Target.localRotation = joint.BaseRotation * localOffset;

            // 원래 위치 + 오프셋 위치. 오프셋은 기체 축의 미터 값이라 부모의 회전·배율로 부모 로컬 단위로 바꿔 더한다.
            // (예전엔 회전만 바꾸고 미터 값을 그대로 더해서, 부모 뼈 배율이 큰 FBX(bernard)는 숨쉬기 1.5cm가 수 m로 커져
            // 몸통이 공중으로 뜨고 메시가 늘어났다. 임시 상자는 부모 배율 1이라 결과가 같다)
            Vector3 worldOffset = transform.TransformDirection(joint.Position + joint.DirectPosition);
            Transform parent = joint.Target.parent;
            Vector3 localPositionOffset = parent != null ? parent.InverseTransformVector(worldOffset) : worldOffset;
            joint.Target.localPosition = joint.BasePosition + localPositionOffset;
            joint.WrittenRotation = joint.Target.localRotation;
            joint.WrittenPosition = joint.Target.localPosition;
            joint.HasWritten = true;
        }

        // ---------------- 자세 ----------------

        private void PoseMovement(BernardTuning.VisualValues v, float dt)
        {
            if (_rb == null) return;
            Vector3 local = transform.InverseTransformDirection(_rb.linearVelocity);
            local.y = 0f;
            float speed = local.magnitude;

            // 가는 방향으로 상체를 기울인다 (앞 = X+, 오른쪽 = Z-)
            AddRotation(_body, new Vector3(
                Mathf.Clamp(local.z * v.leanPerSpeed, -v.maxLean, v.maxLean), 0f,
                Mathf.Clamp(-local.x * v.leanPerSpeed, -v.maxLean, v.maxLean)));

            // 걷기: 이동 거리에 맞춰 다리 교차 + 몸통 상하.
            // Animator의 지금 상태에 키프레임 클립이 들어 있으면 그 클립이 맡으므로 코드는 끈다 (기울기는 계속)
            if (IsLocomotionClipPlaying()) return;
            float amount = v.walkReferenceSpeed > 0f ? Mathf.Clamp01(speed / v.walkReferenceSpeed) : 0f;
            _walkPhase += speed * v.stepsPerMeter * 2f * Mathf.PI * dt;
            float swing = Mathf.Sin(_walkPhase) * v.strideAngle * amount;
            // 다리를 앞으로 = X- (아래를 향한 부품을 앞으로 돌림)
            AddRotation(_legL, new Vector3(-swing, 0f, 0f));
            AddRotation(_legR, new Vector3(swing, 0f, 0f));
            AddPosition(_body, new Vector3(0f, Mathf.Abs(Mathf.Sin(_walkPhase)) * v.bobHeight * amount, 0f));
        }

        private bool IsLocomotionClipPlaying()
        {
            return animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null &&
                   animator.GetCurrentAnimatorClipInfoCount(0) > 0;
        }

        // ---------------- Animator 파라미터 ----------------

        private void Update()
        {
            // Animator가 이번 프레임을 계산하기 전에 파라미터를 넣는다 (LateUpdate에선 한 프레임 늦음)
            try
            {
                UpdateAnimatorParameters();
            }
            catch (Exception e)
            {
                ErrorLog.Report(e, "MechVisualRig.Update");
            }
        }

        private void UpdateAnimatorParameters()
        {
            if (!animate || animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null) return;

            float speed = 0f;
            if (_rb != null)
            {
                Vector3 velocity = _rb.linearVelocity;
                velocity.y = 0f;
                speed = velocity.magnitude;
            }

            // 상태 → 0 Idle, 1 Walk, 2 Run, 3 Sprint. 제동·슬라이딩·사이드스텝·퀵턴·런지는 직전 반복 동작 유지
            if (_stateMachine != null)
            {
                if (_stateMachine.IsIdle) _moveState = 0;
                else if (_stateMachine.IsWalking) _moveState = 1;
                else if (_stateMachine.IsRunning) _moveState = 2;
                else if (_stateMachine.IsSprinting) _moveState = 3;
            }
            else
            {
                // 상태 머신이 없는 기체(연습 봇)는 속도로만
                _moveState = speed > 0.5f ? 1 : 0;
            }

            animator.SetFloat(SpeedParameter, speed);
            animator.SetInteger(MoveStateParameter, _moveState);
        }

        /// <summary>
        /// 대기 자세 + 숨쉬기 (F3). 가드·공격·스턴 중엔 대기 자세를 빼서 기존 포즈를 그대로 둔다.
        /// 숨쉬기는 걷기 이상으로 움직이거나 그런 동작 중이면 breathFadeTime에 걸쳐 줄여 없앤다.
        /// </summary>
        private void PoseIdle(BernardTuning.VisualValues v, float dt)
        {
            bool acting = IsActionPoseActive(v);
            bool moving;
            if (_stateMachine != null) moving = !_stateMachine.IsIdle;
            else
            {
                Vector3 velocity = _rb != null ? _rb.linearVelocity : Vector3.zero;
                velocity.y = 0f;
                moving = velocity.magnitude >= v.breathStopSpeed;
            }

            float breathTarget = acting || moving ? 0f : 1f;
            // 움직이면 breathFadeTime에 걸쳐 끄고, 멈추면 stopBlendTime에 걸쳐 대기 자세와 같이 돌아온다 (G3)
            float breathFade = breathTarget > _breathWeight ? v.stopBlendTime : v.breathFadeTime;
            _breathWeight = breathFade > 0f
                ? Mathf.MoveTowards(_breathWeight, breathTarget, dt / breathFade)
                : breathTarget;

            if (UseMotions)
            {
                // 전신 모션 모델(M2): 팔·상체·무릎은 기본 자세 포즈(로우 가드/돌격)가 맡고, 발 벌림·엇갈림만 대기 다리에서.
                // 공격·가드 모션이 이 위에서 섞이므로 동작 중에도 깔아 둔다
                PoseStance(v, dt);
                PoseIdleLegs(v, 1f - _gaitWeight, includeKnee: false);
            }
            else if (!acting)
            {
                // 어깨 바깥(L = Z-, R = Z+), 위팔 앞으로(X-), 팔꿈치 더 굽힘(X-), 상체 앞으로(X+), 무릎 굽힘(정강이 뒤로 X+)
                AddRotation(_armLUpper, new Vector3(-v.idleArmForward, 0f, -v.idleShoulderSpread));
                AddRotation(_armRUpper, new Vector3(-v.idleArmForward, 0f, v.idleShoulderSpread));
                AddRotation(_armLLower, new Vector3(-v.idleElbowBend, 0f, 0f));
                AddRotation(_armRLower, new Vector3(-v.idleElbowBend, 0f, 0f));
                AddRotation(_body, new Vector3(v.idleLean, 0f, 0f));
                // 다리 자세는 걷는 동안 걸음 동작에 자리를 내준다 (걸음 세기만큼 줄임)
                PoseIdleLegs(v, 1f - _gaitWeight);
            }

            if (_breathWeight <= 0f || v.breathPeriod <= 0f) return;
            float omega = 2f * Mathf.PI / v.breathPeriod;
            float breath = Mathf.Sin(_clock * omega) * _breathWeight;
            // 팔은 몸통보다 breathArmLag만큼 늦은 위상으로 출렁인다
            float armBreath = Mathf.Sin((_clock - v.breathArmLag) * omega) * _breathWeight;
            AddPosition(_body, new Vector3(0f, breath * v.breathHeight * 0.5f, 0f));
            // 들숨(올라갈 때) 가슴이 들린다 = 상체를 살짝 뒤로(X-)
            AddRotation(_body, new Vector3(-breath * v.breathChestAngle, 0f, 0f));
            AddRotation(_armLUpper, new Vector3(armBreath * v.breathArmAngle, 0f, 0f));
            AddRotation(_armRUpper, new Vector3(armBreath * v.breathArmAngle, 0f, 0f));
            // 날숨에 무릎이 아주 살짝 굽고 들숨에 펴진다 (접지 보정이 발을 붙여 두므로 몸이 같이 가라앉는다)
            AddRotation(_shinL, new Vector3(-breath * v.breathKneeAngle, 0f, 0f));
            AddRotation(_shinR, new Vector3(-breath * v.breathKneeAngle, 0f, 0f));
        }

        /// <summary>
        /// 대기 자세 다리 (H4): 11자 대신 바깥으로 벌림(L = Z-, R = Z+), 무릎 굽힘(정강이 X+),
        /// 왼발을 앞으로(앞뒤 엇갈림 idleFootStagger m 상당 각도, 다리 앞으로 = X-). 발목 평행 보정은 스프링 뒤 LevelAnkle.
        /// </summary>
        private void PoseIdleLegs(BernardTuning.VisualValues v, float weight, bool includeKnee = true)
        {
            if (weight <= 0f) return;
            float stagger = _legLengthL > 0.01f ? Mathf.Atan2(v.idleFootStagger, _legLengthL) * Mathf.Rad2Deg : 0f;
            AddRotation(_legL, new Vector3(-stagger, 0f, -v.idleLegSpread) * weight);
            AddRotation(_legR, new Vector3(0f, 0f, v.idleLegSpread) * weight);
            // 기본 자세 포즈가 무릎을 굽히는 모델(M2)은 무릎을 여기서 또 굽히지 않는다
            if (!includeKnee) return;
            AddRotation(_shinL, new Vector3(v.idleKneeBend, 0f, 0f) * weight);
            AddRotation(_shinR, new Vector3(v.idleKneeBend, 0f, 0f) * weight);
        }

        /// <summary>
        /// 발목 평행 보정: 위쪽 사슬(골반·엉덩이·다리·정강이)의 앞뒤(X)·좌우(Z) 기울기를 발목에서 되돌려 발바닥을 지면과 평행하게.
        /// 오프셋 각도가 모두 기체 축 기준이라 작은 각도에선 더해서 빼는 근사로 충분하다. 스프링을 거친 실제 각도로 계산한다.
        /// </summary>
        private static void LevelAnkle(Joint ankle, BernardTuning.VisualValues v, Joint a, Joint b, Joint c = null, Joint d = null)
        {
            if (ankle == null || ankle.Target == null) return;
            // 매 프레임 배열을 만들지 않게 사슬은 인자 넷까지
            Vector3 sum = ChainRotation(a) + ChainRotation(b) + ChainRotation(c) + ChainRotation(d);
            float w = v.ankleLevelWeight;
            ankle.Rotation = new Vector3(-sum.x * w, ankle.Rotation.y, -sum.z * w);
            ankle.RotationVelocity = Vector3.zero;
        }

        private static Vector3 ChainRotation(Joint j) => j != null && j.Target != null ? j.Rotation : Vector3.zero;

        /// <summary>가드·스턴·펀치·런지·태클·앵커 클로·파일드라이버·슬램 포즈가 나오는 중인지 (대기 자세·숨쉬기보다 우선).</summary>
        private bool IsActionPoseActive(BernardTuning.VisualValues v)
        {
            if (_melee != null && (_melee.IsGuarding || _melee.IsStunned)) return true;
            if (_lunge != null && _lunge.IsLunging) return true;
            if (UseMotions && _lungeStart != None) return true;
            if (UseMotions && IsDefenseMotionActive()) return true;
            if (_claw != null && _claw.Phase != AnchorClaw.ClawPhase.Ready) return true;
            if (InWindow(_punchStart, PunchVisualLength(v))) return true;
            if (InWindow(_tackleStart, TackleWindup + v.tackleTime)) return true;
            if (InWindow(_clawStart, v.clawHoldTime)) return true;
            float smash = v.smashRaiseTime + v.smashDownTime;
            return InWindow(_pileStart, smash) || InWindow(_slamStart, smash);
        }

        private bool InWindow(float start, float length)
        {
            float t = _clock - start;
            return t >= 0f && t < length;
        }

        private void PoseGuard(BernardTuning.VisualValues v)
        {
            if (UseMotions)
            {
                PoseDefense(Time.deltaTime);
                return;
            }
            if (_melee == null || !_melee.IsGuarding) return;
            // 양팔을 올려 얼굴 앞에 모은다 (펀치 중인 팔은 PoseActions가 덮어씀)
            AddRotation(_armLUpper, new Vector3(v.guardArmAngle, 0f, v.guardInward));
            AddRotation(_armRUpper, new Vector3(v.guardArmAngle, 0f, -v.guardInward));
            AddRotation(_armLLower, new Vector3(v.guardElbowAngle, 0f, 0f));
            AddRotation(_armRLower, new Vector3(v.guardElbowAngle, 0f, 0f));
        }

        private void PoseActions(BernardTuning.VisualValues v)
        {
            // 전신 모션 모델은 바뀐 기술을 Moves 쪽에서 (M3~M7). 아래는 임시 상자·모션 끔일 때의 예전 동작
            bool motions = UseMotions;
            if (motions) PosePunch();

            // 펀치: 예비 동작(뒤로) → 뻗기 → 복귀. 판정은 이벤트 프레임에 이미 끝났다
            float t = _clock - _punchStart;
            float punchWindup = PunchWindup;
            if (!motions && t >= 0f && t < punchWindup + v.punchExtendTime + v.punchHoldTime)
            {
                Joint upper = _punchRight ? _armRUpper : _armLUpper;
                Joint lower = _punchRight ? _armRLower : _armLLower;
                float twist = _punchRight ? -v.punchBodyTwist : v.punchBodyTwist;
                if (t < punchWindup)
                {
                    SetRotation(upper, new Vector3(v.anticipationPullback, 0f, 0f));
                    AddRotation(_body, new Vector3(0f, -twist * 0.3f, 0f));
                }
                else
                {
                    SetRotation(upper, new Vector3(v.punchArmAngle, 0f, 0f));
                    SetRotation(lower, new Vector3(v.punchElbowAngle, 0f, 0f));
                    AddRotation(_body, new Vector3(0f, twist, 0f));
                }
            }

            // 런지: 상체 숙이고 양팔 뒤로 (전신 모션은 PoseLunge)
            if (motions) PoseLunge();
            else if (_lunge != null && _lunge.IsLunging)
            {
                AddRotation(_body, new Vector3(v.lungeLean, 0f, 0f));
                SetRotation(_armLUpper, new Vector3(v.lungeArmBack, 0f, 0f));
                SetRotation(_armRUpper, new Vector3(v.lungeArmBack, 0f, 0f));
            }

            // 대시 어택·태클: 예비 동작 뒤 오른 어깨 앞으로
            t = _clock - _tackleStart;
            float tackleWindup = TackleWindup;
            if (t >= 0f && t < tackleWindup + v.tackleTime)
            {
                if (t < tackleWindup)
                {
                    AddRotation(_body, new Vector3(0f, -v.tackleTwist * 0.3f, 0f));
                }
                else
                {
                    AddRotation(_body, new Vector3(v.tackleLean, v.tackleTwist, 0f));
                    SetRotation(_armRUpper, new Vector3(0f, 0f, -20f));
                }
            }

            // 앵커 클로 발사: 예비 동작 = 왼팔을 뒤로 당김, 날아가는·감겨 오는 동안 = 왼팔 뻗기
            if (_claw != null)
            {
                AnchorClaw.ClawPhase phase = _claw.Phase;
                if (phase == AnchorClaw.ClawPhase.Windup)
                {
                    SetRotation(_armLUpper, new Vector3(v.anticipationPullback, 0f, 0f));
                }
                else if (phase == AnchorClaw.ClawPhase.Flying || phase == AnchorClaw.ClawPhase.Retracting)
                {
                    SetRotation(_armLUpper, new Vector3(v.clawArmAngle, 0f, 0f));
                    SetRotation(_armLLower, new Vector3(v.punchElbowAngle, 0f, 0f));
                }
            }

            // 앵커 클로 명중: 왼팔 뻗기
            t = _clock - _clawStart;
            if (t >= 0f && t < v.clawHoldTime)
            {
                SetRotation(_armLUpper, new Vector3(v.clawArmAngle, 0f, 0f));
                SetRotation(_armLLower, new Vector3(v.punchElbowAngle, 0f, 0f));
            }

            // 파일드라이버: 오른팔 들었다 내리찍기 / 슬램: 양팔
            t = _clock - _pileStart;
            if (t >= 0f && t < v.smashRaiseTime + v.smashDownTime) Smash(v, t, _armRUpper, null);
            t = _clock - _slamStart;
            if (t >= 0f && t < v.smashRaiseTime + v.smashDownTime) Smash(v, t, _armRUpper, _armLUpper);
        }

        private void Smash(BernardTuning.VisualValues v, float t, Joint first, Joint second)
        {
            bool raising = t < v.smashRaiseTime;
            float angle = raising ? v.smashRaiseAngle : v.smashDownAngle;
            SetRotation(first, new Vector3(angle, 0f, 0f));
            SetRotation(second, new Vector3(angle, 0f, 0f));
            if (!raising) AddRotation(_body, new Vector3(v.smashLean, 0f, 0f));
        }

        private void PoseStun(BernardTuning.VisualValues v)
        {
            if (_melee == null || !_melee.IsStunned) return;
            // 가드 브레이크 휘청(M5)이 따로 흔들므로 겹치지 않게
            if (UseMotions && InWindow(_breakStart, _breakLength)) return;
            float phase = _clock * v.stunShakeFrequency * 2f * Mathf.PI;
            AddRotation(_body, new Vector3(Mathf.Sin(phase) * v.stunShakeAngle, 0f, Mathf.Sin(phase * 1.3f + 1f) * v.stunShakeAngle));
            AddRotation(_head, new Vector3(Mathf.Sin(phase * 0.7f + 2f) * v.stunShakeAngle * 2f, Mathf.Sin(phase * 0.9f) * v.stunShakeAngle * 2f, 0f));
        }

        private static void AddRotation(Joint joint, Vector3 euler)
        {
            if (joint != null) joint.TargetRotation += euler;
        }

        /// <summary>그 부품의 다른 자세(가드 등)를 덮어쓴다 — 공격 동작이 가드보다 우선.</summary>
        private static void SetRotation(Joint joint, Vector3 euler)
        {
            if (joint != null) joint.TargetRotation = euler;
        }

        private static void AddPosition(Joint joint, Vector3 offset)
        {
            if (joint != null) joint.TargetPosition += offset;
        }
    }
}
