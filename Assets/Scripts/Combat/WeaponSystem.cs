using UnityEngine;
using MyDongari.Mech;

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
        private float _fireTimer = 0f;

        private void Awake()
        {
            _lockOnSystem = GetComponent<LockOnSystem>();
            _heatGauge = GetComponent<HeatGauge>();
        }

        private void Update()
        {
            _fireTimer += Time.deltaTime;

            if (UnityEngine.InputSystem.Mouse.current.leftButton.isPressed)
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