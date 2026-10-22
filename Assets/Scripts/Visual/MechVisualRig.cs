using System;
using UnityEngine;
using MyDongari.Combat;
using MyDongari.Combat.Skills;
using MyDongari.Mech;

namespace MyDongari.Visual
{
    /// <summary>리그 슬롯 하나: 움직일 부품(관절 위치에 피벗)과 축 보정.</summary>
    [Serializable]
    public class RigSlot
    {
        [Tooltip("움직일 부품. 피벗이 관절(어깨·팔꿈치·손목·엉덩이·목·허리)에 있어야 한다")]
        public Transform target;
        [Tooltip("모델 부품의 축이 기준(X 오른쪽, Y 위, Z 앞)과 다를 때 맞추는 회전(°). 임시 상자는 0")]
        public Vector3 rotationCorrection;
    }

    /// <summary>
    /// 리깅 없이 코드로 부품을 움직이는 절차적 애니메이션. 콜라이더·리지드바디는 루트에 그대로 두고
    /// 시각 전용 부품만 LateUpdate에서 움직인다. 전투 코드는 이 컴포넌트를 모른다 —
    /// 기존 이벤트(공격 시작, 피격 결과, 잡기, 파일드라이버, 슬램)를 구독하고 상태(가드·스턴·런지·속도)를 읽기만 한다.
    /// 모든 부품은 쉴 때 자세(rest) 위에 오프셋을 얹고, 그 오프셋은 감쇠 스프링으로 목표를 따라간다.
    /// 수치는 BernardTuning의 Visual 묶음.
    /// </summary>
    public class MechVisualRig : MonoBehaviour
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

        [Header("동작")]
        [SerializeField] private bool animate = true;

        [Header("몸통")]
        [SerializeField] private RigSlot body = new RigSlot();
        [SerializeField] private RigSlot head = new RigSlot();
        [Header("왼팔")]
        [SerializeField] private RigSlot armLUpper = new RigSlot();
        [SerializeField] private RigSlot armLLower = new RigSlot();
        [SerializeField] private RigSlot fistL = new RigSlot();
        [Header("오른팔")]
        [SerializeField] private RigSlot armRUpper = new RigSlot();
        [SerializeField] private RigSlot armRLower = new RigSlot();
        [SerializeField] private RigSlot fistR = new RigSlot();
        [Header("다리")]
        [SerializeField] private RigSlot legL = new RigSlot();
        [SerializeField] private RigSlot legR = new RigSlot();

        /// <summary>슬롯 하나의 런타임 상태: 쉴 때 자세 + 스프링(위치·회전 오프셋).</summary>
        private class Joint
        {
            public RigSlot Slot;
            public Transform Target;
            public Vector3 RestPosition;
            public Quaternion RestRotation;
            public Vector3 Position;
            public Vector3 PositionVelocity;
            public Vector3 Rotation;          // 오일러(°), 기준 축 기준
            public Vector3 RotationVelocity;
            public Vector3 TargetPosition;
            public Vector3 TargetRotation;
        }

        private Joint _body, _head, _armLUpper, _armLLower, _fistL, _armRUpper, _armRLower, _fistR, _legL, _legR;
        private Joint[] _joints;

        private TuningRef _tuning;
        private BernardTuning.VisualValues V => _tuning.Get(this).Visual;

        private Rigidbody _rb;
        private MeleeSystem _melee;
        private ThrusterLunge _lunge;
        private AnchorClaw _claw;
        private ImpactSlam _slam;

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

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _melee = GetComponent<MeleeSystem>();
            BindJoints();
        }

        private void Start()
        {
            // ThrusterLunge 등은 다른 컴포넌트 Awake에서 런타임에 붙기도 해서 Start에서 찾는다
            _lunge = GetComponent<ThrusterLunge>();
            _claw = GetComponent<AnchorClaw>();
            _slam = GetComponent<ImpactSlam>();

            if (_melee != null)
            {
                _melee.OnAttackStarted += OnAttackStarted;
                _melee.OnHitResolved += OnHitResolved;
            }
            if (_claw != null)
            {
                _claw.OnGrabbed += OnClawGrabbed;
                _claw.OnPileDriverStarted += OnPileDriver;
            }
            if (_slam != null) _slam.OnSlamStarted += OnSlam;
        }

        private void OnDestroy()
        {
            if (_melee != null)
            {
                _melee.OnAttackStarted -= OnAttackStarted;
                _melee.OnHitResolved -= OnHitResolved;
            }
            if (_claw != null)
            {
                _claw.OnGrabbed -= OnClawGrabbed;
                _claw.OnPileDriverStarted -= OnPileDriver;
            }
            if (_slam != null) _slam.OnSlamStarted -= OnSlam;
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
            _joints = new[] { _body, _head, _armLUpper, _armLLower, _fistL, _armRUpper, _armRLower, _fistR, _legL, _legR };
        }

        private static Joint Bind(RigSlot slot)
        {
            var joint = new Joint { Slot = slot, Target = slot != null ? slot.target : null };
            if (joint.Target != null)
            {
                joint.RestPosition = joint.Target.localPosition;
                joint.RestRotation = joint.Target.localRotation;
            }
            return joint;
        }

        // ---------------- 이벤트 ----------------

        private void OnAttackStarted(AttackType type)
        {
            switch (type)
            {
                case AttackType.Punch:
                    // 원투: 직전 펀치가 comboWindow 안이면 반대 팔, 아니면 오른팔부터
                    _punchRight = _clock - _lastPunchTime > V.comboWindow || !_punchRight;
                    _punchStart = _clock;
                    _lastPunchTime = _clock;
                    break;
                case AttackType.DashAttack:
                case AttackType.Tackle:
                    _tackleStart = _clock;
                    break;
            }
        }

        private void OnHitResolved(HitInfo info, HitResult result)
        {
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
            if (!animate || _joints == null) return;

            float dt = Time.deltaTime;
            _clock += dt;
            BernardTuning.VisualValues v = V;

            foreach (Joint joint in _joints)
            {
                joint.TargetPosition = Vector3.zero;
                joint.TargetRotation = Vector3.zero;
            }

            PoseMovement(v, dt);
            PoseGuard(v);
            PoseActions(v);
            PoseStun(v);

            // 스프링: 프레임이 튀어도 터지지 않게 dt 상한
            float step = Mathf.Min(dt, 1f / 30f);
            foreach (Joint joint in _joints)
            {
                if (joint.Target == null) continue;
                Spring(ref joint.Position, ref joint.PositionVelocity, joint.TargetPosition, v, step);
                Spring(ref joint.Rotation, ref joint.RotationVelocity, joint.TargetRotation, v, step);
                Apply(joint);
            }
        }

        private static void Spring(ref Vector3 x, ref Vector3 velocity, Vector3 target, BernardTuning.VisualValues v, float dt)
        {
            if (dt <= 0f) return;
            float omega = 2f * Mathf.PI * Mathf.Max(0.1f, v.springFrequency);
            // 반암시 오일러: 감쇠 스프링 a = ω²(목표 - x) - 2ζω·v
            Vector3 acceleration = omega * omega * (target - x) - 2f * v.springDamping * omega * velocity;
            velocity += acceleration * dt;
            x += velocity * dt;
        }

        private static void Apply(Joint joint)
        {
            // 오프셋은 기준 축(X 오른쪽, Y 위, Z 앞)으로 정의 → 슬롯 보정으로 부품 축에 맞춘 뒤 쉴 때 자세 위에 얹는다
            Quaternion correction = Quaternion.Euler(joint.Slot.rotationCorrection);
            Quaternion offset = correction * Quaternion.Euler(joint.Rotation) * Quaternion.Inverse(correction);
            joint.Target.localRotation = joint.RestRotation * offset;
            joint.Target.localPosition = joint.RestPosition + joint.Position;
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

            // 걷기: 이동 거리에 맞춰 다리 교차 + 몸통 상하
            float amount = v.walkReferenceSpeed > 0f ? Mathf.Clamp01(speed / v.walkReferenceSpeed) : 0f;
            _walkPhase += speed * v.stepsPerMeter * 2f * Mathf.PI * dt;
            float swing = Mathf.Sin(_walkPhase) * v.strideAngle * amount;
            // 다리를 앞으로 = X- (아래를 향한 부품을 앞으로 돌림)
            AddRotation(_legL, new Vector3(-swing, 0f, 0f));
            AddRotation(_legR, new Vector3(swing, 0f, 0f));
            AddPosition(_body, new Vector3(0f, Mathf.Abs(Mathf.Sin(_walkPhase)) * v.bobHeight * amount, 0f));
        }

        private void PoseGuard(BernardTuning.VisualValues v)
        {
            if (_melee == null || !_melee.IsGuarding) return;
            // 양팔을 올려 얼굴 앞에 모은다 (펀치 중인 팔은 PoseActions가 덮어씀)
            AddRotation(_armLUpper, new Vector3(v.guardArmAngle, 0f, v.guardInward));
            AddRotation(_armRUpper, new Vector3(v.guardArmAngle, 0f, -v.guardInward));
            AddRotation(_armLLower, new Vector3(v.guardElbowAngle, 0f, 0f));
            AddRotation(_armRLower, new Vector3(v.guardElbowAngle, 0f, 0f));
        }

        private void PoseActions(BernardTuning.VisualValues v)
        {
            // 펀치: 예비 동작(뒤로) → 뻗기 → 복귀. 판정은 이벤트 프레임에 이미 끝났다
            float t = _clock - _punchStart;
            if (t >= 0f && t < v.anticipationTime + v.punchExtendTime + v.punchHoldTime)
            {
                Joint upper = _punchRight ? _armRUpper : _armLUpper;
                Joint lower = _punchRight ? _armRLower : _armLLower;
                float twist = _punchRight ? -v.punchBodyTwist : v.punchBodyTwist;
                if (t < v.anticipationTime)
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

            // 런지: 상체 숙이고 양팔 뒤로
            if (_lunge != null && _lunge.IsLunging)
            {
                AddRotation(_body, new Vector3(v.lungeLean, 0f, 0f));
                SetRotation(_armLUpper, new Vector3(v.lungeArmBack, 0f, 0f));
                SetRotation(_armRUpper, new Vector3(v.lungeArmBack, 0f, 0f));
            }

            // 대시 어택·태클: 예비 동작 뒤 오른 어깨 앞으로
            t = _clock - _tackleStart;
            if (t >= 0f && t < v.anticipationTime + v.tackleTime)
            {
                if (t < v.anticipationTime)
                {
                    AddRotation(_body, new Vector3(0f, -v.tackleTwist * 0.3f, 0f));
                }
                else
                {
                    AddRotation(_body, new Vector3(v.tackleLean, v.tackleTwist, 0f));
                    SetRotation(_armRUpper, new Vector3(0f, 0f, -20f));
                }
            }

            // 앵커 클로: 왼팔 뻗기
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
