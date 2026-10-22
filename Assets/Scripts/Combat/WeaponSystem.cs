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
            if (GameManager.Instance != null && !GameManager.Instance.IsRoundActive) return;

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
        /// 공격 버튼 하나의 우선순위 (GAME_SPEC 4장):
        /// 1 파일드라이버 준비 → 파일드라이버, 2 전력질주 → 대시 어택, 3 하드 락온 → 펀치, 4 그 외 → 런지(또는 사격)
        /// 펀치·사격은 누르고 있으면 쿨다운마다 반복, 런지·대시 어택은 누른 순간 한 번.
        /// </summary>
        private void HandleAttackButton()
        {
            bool held = _inputReader.AttackHeld;
            bool pressed = _inputReader.AttackPressedThisFrame;
            if (!held && !pressed) return;

            if (_anchorClaw != null && _anchorClaw.IsPileDriverReady)
            {
                _anchorClaw.TryPileDriver();
                return;
            }

            if (_stateMachine != null && _stateMachine.IsSprinting)
            {
                // 대시 어택은 B4에서 연결
                return;
            }

            // 근접 전환 거리는 하드 락온 진입 거리 하나로 통일 (거리 값이 두 군데 있으면 서로 어긋남)
            if (_lockOnSystem != null && _lockOnSystem.IsHardLocked)
            {
                _meleeSystem.TryMeleeAttack();
                return;
            }

            if (rangedMode == RangedMode.Lunge)
            {
                if (pressed && _thrusterLunge != null) _thrusterLunge.TryLunge();
            }
            else
            {
                TryFire();
            }
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