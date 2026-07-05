using UnityEngine;
using MyDongari.Camera;

namespace MyDongari.Combat
{
    public class LockOnSystem : MonoBehaviour
    {
        [Header("소프트 락온")]
        [SerializeField] private float lockOnRange = 50f;
        [SerializeField] private float lockOnAngle = 30f;

        [Header("하드 락온")]
        [SerializeField] private float hardLockEnterRange = 5f;
        [SerializeField] private float hardLockExitRange = 7f;
        [SerializeField] private float hardLockMaxAngle = 120f;
        [SerializeField] private float hardLockTrackSpeed = 5f;

        private Transform _camera;
        private MechCamera _mechCamera;

        public Transform Target { get; private set; }
        public bool IsHardLocked { get; private set; }

        private void Awake()
        {
            _camera = GameObject.FindWithTag("MainCamera").transform;
            _mechCamera = _camera.GetComponent<MechCamera>();
        }

        private void Update()
        {
            DetectTarget();
            UpdateHardLock();
        }

        private void DetectTarget()
        {
            Collider[] hits = Physics.OverlapSphere(_camera.position, lockOnRange);
            Transform bestTarget = null;
            float bestAngle = lockOnAngle;

            foreach (Collider hit in hits)
            {
                if (!hit.CompareTag("Enemy")) continue;

                Vector3 targetCenter = hit.bounds.center;
                Vector3 dirToTarget = (targetCenter - _camera.position).normalized;
                float angle = Vector3.Angle(_camera.forward, dirToTarget);

                if (angle >= bestAngle) continue;

                RaycastHit rayHit;
                if (Physics.Raycast(_camera.position, dirToTarget, out rayHit, lockOnRange))
                {
                    if (!rayHit.collider.CompareTag("Enemy")) continue;
                }

                bestAngle = angle;
                bestTarget = hit.transform;
            }

            Target = bestTarget;
        }

        private void UpdateHardLock()
        {
            if (Target == null)
            {
                IsHardLocked = false;
                return;
            }

            float dist = Vector3.Distance(_camera.position, Target.position);

            if (!IsHardLocked)
            {
                if (dist <= hardLockEnterRange)
                {
                    IsHardLocked = true;
                }
            }
            else
            {
                if (dist > hardLockExitRange)
                {
                    IsHardLocked = false;
                    if (_mechCamera != null) _mechCamera.StopTracking();
                    return;
                }

                Vector3 dirToTarget = (Target.position - _camera.position).normalized;
                float angle = Vector3.Angle(_camera.forward, dirToTarget);

                if (angle > hardLockMaxAngle * 0.5f)
                {
                    IsHardLocked = false;
                    if (_mechCamera != null) _mechCamera.StopTracking();
                    return;
                }

                if (_mechCamera != null)
                {
                    _mechCamera.TrackTarget(Target, hardLockTrackSpeed);
                }
            }
        }

        public void ForceRelease()
        {
            IsHardLocked = false;
            if (_mechCamera != null) _mechCamera.StopTracking();
            Target = null;
        }
    }
}