using System;
using UnityEngine;
using MyDongari.Mech;
using MyDongari.Combat.Skills;
using MyDongari.Input;
using MyDongari.Core;
using MyDongari.Movement;

namespace MyDongari.Combat
{
    /// <summary>락온 밖(상대 6m 밖 또는 락온 없음)에서 공격 버튼이 무엇을 할지. 버나드 계열은 Lunge.</summary>
    public enum RangedMode
    {
        Lunge,
        Projectile
    }

    public class WeaponSystem : MonoBehaviour
    {
        [Header("원거리 모드")]
        [Tooltip("Lunge = 스러스터 런지(버나드 계열, 원거리 무기 없음), Projectile = 투사체 사격(사격 기체용)")]
        [SerializeField] private RangedMode rangedMode = RangedMode.Lunge;

        [Header("무기 설정 (Projectile 모드)")]
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private Transform firePoint;
        [SerializeField] private float heatPerShot = 10f;
        [SerializeField] private float fireRate = 0.3f;

        private LockOnSystem _lockOnSystem;
        private HeatGauge _heatGauge;
        private MeleeSystem _meleeSystem;
        private AnchorClaw _anchorClaw;
        private ImpactSlam _impactSlam;
        private InputReader _inputReader;
        private MechIdentity _identity;
        private ThrusterLunge _thrusterLunge;
        private MechStateMachine _stateMachine;
        private float _fireTimer = 0f;
        private readonly AttackInputBuffer _attackBuffer = new AttackInputBuffer();
        private TuningRef _tuning;

        /// <summary>입력 버퍼로(누른 프레임보다 뒤에) 발동한 공격. 로그용.</summary>
        public event Action<AttackType> OnBufferedAttack;

        private void Awake()
        {
            _lockOnSystem = GetComponent<LockOnSystem>();
            _heatGauge = GetComponent<HeatGauge>();
            _meleeSystem = GetComponent<MeleeSystem>();
            _anchorClaw = GetComponent<AnchorClaw>();
            _impactSlam = GetComponent<ImpactSlam>();
            _inputReader = GetComponent<InputReader>();
            _identity = MechIdentity.GetOrAdd(gameObject);
            _stateMachine = GetComponent<MechStateMachine>();
            _thrusterLunge = GetComponent<ThrusterLunge>();
            if (rangedMode == RangedMode.Lunge && _thrusterLunge == null)
            {
                // 씬에 안 붙여 둔 경우 기본값으로라도 동작하게. 수치 조정하려면 인스펙터에서 직접 추가할 것
                _thrusterLunge = gameObject.AddComponent<ThrusterLunge>();
            }
        }

        private void Update()
        {
            if (GameManager.Instance != null && !GameManager.Instance.IsRoundActive)
            {
                // 카운트다운·메뉴 중에 눌러 둔 입력이 FIGHT 순간 튀어나가지 않게
                _attackBuffer.Clear();
                return;
            }

            _fireTimer += Time.deltaTime;

            if (_inputReader.Skill1Input)
            {
                if (_anchorClaw != null)
                {
                    _anchorClaw.TryActivate();
                }
            }

            if (_inputReader.Skill2Input)
            {
                if (_impactSlam != null)
                {
                    _impactSlam.TryActivate();
                }
            }

            HandleAttackButton();
        }

        /// <summary>
        /// 공격 버튼은 "누른 순간"에만 공격을 낸다(누르고 있기만 하면 아무것도 안 나감, GAME_SPEC 4장).
        /// 지금 못 나가면 입력 버퍼(inputBuffer 초) 동안 기억했다가 가능해지는 프레임에 발동하고,
        /// 무엇을 낼지는 그 프레임 상황으로 다시 정한다.
        /// 예외: 사격 기체(Projectile 모드)의 원거리 사격만 누르고 있으면 연사 (버나드 로케 범위 밖).
        /// </summary>
        private void HandleAttackButton()
        {
            if (_inputReader.AttackPressedThisFrame) _attackBuffer.Press();

            if (rangedMode == RangedMode.Projectile && IsRangedContext())
            {
                if (_inputReader.AttackHeld) TryFire();
                _attackBuffer.Clear();
                return;
            }

            float bufferTime = _tuning.Get(this).Startup.inputBuffer;
            if (_attackBuffer.Tick(bufferTime, TryStartAttack, out AttackType started, out bool fromBuffer) && fromBuffer)
            {
                OnBufferedAttack?.Invoke(started);
            }
        }

        /// <summary>원거리 상황(파일드라이버·질주·하드 락온이 아님)인지.</summary>
        private bool IsRangedContext()
        {
            if (_anchorClaw != null && _anchorClaw.IsPileDriverReady) return false;
            if (_stateMachine != null && _stateMachine.IsSprinting) return false;
            return _lockOnSystem == null || !_lockOnSystem.IsHardLocked;
        }

        /// <summary>
        /// 지금 상황으로 공격 버튼 한 번의 동작을 고르고 시작한다. 우선순위 (GAME_SPEC 4장):
        /// 1 파일드라이버 준비 → 파일드라이버, 2 전력질주 → 대시 어택·태클, 3 하드 락온 → 펀치, 4 그 외 → 런지.
        /// 시작했으면 그 종류, 지금 못 하면(쿨다운·발생 중·런지 중·스턴 등) null.
        /// </summary>
        private AttackType? TryStartAttack()
        {
            // 스턴 중엔 어떤 공격도 시작하지 않는다 (펀치·런지는 각자도 막지만 파일드라이버까지 한 곳에서)
            if (_meleeSystem != null && _meleeSystem.IsStunned) return null;

            if (_anchorClaw != null && _anchorClaw.IsPileDriverReady)
            {
                return _anchorClaw.TryPileDriver() ? AttackType.PileDriver : (AttackType?)null;
            }

            if (_stateMachine != null && _stateMachine.IsSprinting)
            {
                // 질주 중 공격 버튼 = 어깨 태클 (1.5초 미만 대시 어택 / 이상 차징 태클)
                if (!_stateMachine.SprintState.RequestTackle()) return null;
                return _stateMachine.SprintState.IsTackleCharged ? AttackType.Tackle : AttackType.DashAttack;
            }

            // 근접 전환 거리는 하드 락온 진입 거리 하나로 통일 (거리 값이 두 군데 있으면 서로 어긋남)
            if (_lockOnSystem != null && _lockOnSystem.IsHardLocked)
            {
                return _meleeSystem != null && _meleeSystem.TryMeleeAttack() ? AttackType.Punch : (AttackType?)null;
            }

            if (rangedMode == RangedMode.Lunge && _thrusterLunge != null)
            {
                return _thrusterLunge.TryLunge() ? AttackType.Lunge : (AttackType?)null;
            }
            return null;
        }

        private void TryFire()
        {
            if (!_heatGauge.CanFire()) return;
            if (_fireTimer < fireRate) return;

            _fireTimer = 0f;
            _heatGauge.AddHeat(heatPerShot);

            // 화면 분할이라 조준 기준은 반드시 "내" 카메라
            Transform aimCamera = _identity.CameraTransform;
            Vector3 fireDirection = aimCamera != null ? aimCamera.forward : transform.forward;
            Transform target = _lockOnSystem != null ? _lockOnSystem.Target : null;

            GameObject proj = Instantiate(projectilePrefab, firePoint.position, Quaternion.identity);
            Projectile projectile = proj.GetComponent<Projectile>();
            if (projectile != null)
            {
                projectile.Init(fireDirection, target, aimCamera, _identity);
            }
        }
    }
}