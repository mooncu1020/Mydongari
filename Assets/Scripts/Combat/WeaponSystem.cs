using UnityEngine;
using MyDongari.Mech;
using MyDongari.Combat.Skills;
using MyDongari.Input;
using MyDongari.Core;

namespace MyDongari.Combat
{
    public class WeaponSystem : MonoBehaviour
    {
        [Header("무기 설정")]
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

            if (_inputReader.AttackHeld)
            {
                if (_anchorClaw != null && _anchorClaw.IsPileDriverReady)
                {
                    _anchorClaw.TryPileDriver();
                }
                // 근접 전환 거리는 하드 락온 진입 거리 하나로 통일 (거리 값이 두 군데 있으면 서로 어긋남)
                else if (_lockOnSystem != null && _lockOnSystem.IsHardLocked)
                {
                    _meleeSystem.TryMeleeAttack();
                }
                else
                {
                    TryFire();
                }
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