using UnityEngine;
using MyDongari.Camera;

namespace MyDongari.Combat
{
    public class LockOnSystem : MonoBehaviour
    {
        [Header("락온 설정")]
        [SerializeField] private float lockOnRange = 50f;
        [SerializeField] private float lockOnAngle = 30f;

        private Transform _camera;
        public Transform Target { get; private set; }

        private void Awake()
        {
            _camera = GameObject.FindWithTag("MainCamera").transform;
            Debug.Log("카메라: " + (_camera != null ? _camera.name : "없음"));
        }

        private void Update()
        {
            Debug.Log("카메라 forward: " + _camera.forward);
            DetectTarget();
        }
        private void DetectTarget()
        {
            Collider[] hits = Physics.OverlapSphere(_camera.position, lockOnRange);
            Transform bestTarget = null;
            float bestAngle = lockOnAngle;

            foreach (Collider hit in hits)
            {
                if (!hit.CompareTag("Enemy")) continue;

                Vector3 dirToTarget = (hit.transform.position - _camera.position).normalized;
                float angle = Vector3.Angle(_camera.forward, dirToTarget);

                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    bestTarget = hit.transform;
                }
            }

            Target = bestTarget;
            Debug.Log("락온 타겟: " + (Target != null ? Target.name : "없음"));
        }
    }
}