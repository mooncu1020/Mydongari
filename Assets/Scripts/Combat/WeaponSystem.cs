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
        [SerializeField] private float meleeAutoRange = 5f;

        private LockOnSystem _lockOnSystem;
        private HeatGauge _heatGauge;
        private MeleeSystem _meleeSystem;
        private AnchorClaw _anchorClaw;
        private ImpactSlam _impactSlam;
        private InputReader _inputReader;
        private float _fireTimer = 0f;

        private void Awake()
        {
            _lockOnSystem = GetComponent<LockOnSystem>();
            _heatGauge = GetComponent<HeatGauge>();
            _meleeSystem = GetComponent<MeleeSystem>();
            _anchorClaw = GetComponent<AnchorClaw>();
            _impactSlam = GetComponent<ImpactSlam>();
            _inputReader = GetComponent<InputReader>();
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

            if (UnityEngine.InputSystem.Mouse.current.leftButton.isPressed)
            {
                if (_anchorClaw != null && _anchorClaw.IsPileDriverReady)
                {
                    _anchorClaw.TryPileDriver();
                }
                else if (_lockOnSystem != null && _lockOnSystem.IsHardLocked && IsEnemyInMeleeRange())
                {
                    _meleeSystem.TryMeleeAttack();
                }
                else
                {
                    TryFire();
                }
            }
        }

        private bool IsEnemyInMeleeRange()
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, meleeAutoRange);
            foreach (Collider hit in hits)
            {
                if (hit.CompareTag("Enemy")) return true;
            }
            return false;
        }

        private void TryFire()
        {
            if (!_heatGauge.CanFire()) return;
            if (_fireTimer < fireRate) return;

            _fireTimer = 0f;
            _heatGauge.AddHeat(heatPerShot);

            Vector3 fireDirection = GameObject.FindWithTag("MainCamera").transform.forward;
            Transform target = _lockOnSystem != null ? _lockOnSystem.Target : null;

            GameObject proj = Instantiate(projectilePrefab, firePoint.position, Quaternion.identity);
            Projectile projectile = proj.GetComponent<Projectile>();
            if (projectile != null)
            {
                projectile.Init(fireDirection, target);
            }
        }
    }
}