using UnityEngine;
using MyDongari.Camera;
using MyDongari.Core;
using MyDongari.Mech;

namespace MyDongari.Combat
{
    public class LockOnSystem : MonoBehaviour
    {
        private TuningRef _tuning;
        private BernardTuning.LockOnValues T => _tuning.Get(this).LockOn;

        private Transform _camera;
        private MechCamera _mechCamera;
        private MechIdentity _identity;

        public Transform Target { get; private set; }
        public bool IsHardLocked { get; private set; }

        // 하드 락온 금지: 내 런지 돌진 중(플래그) + 런지 빗나감 직후(타이머)
        private bool _hardLockSuppressed;
        private float _hardLockBlockTimer;
        // 락온 전체(소프트 포함) 금지: 상대가 슬라이딩으로 내 락온을 끊었을 때
        private float _allLockBlockTimer;

        public bool IsHardLockBlocked => _hardLockSuppressed || _hardLockBlockTimer > 0f || _allLockBlockTimer > 0f;

        private void Awake()
        {
            // 화면 분할: 각 기체는 자기 카메라만 기준으로 조준한다
            _identity = MechIdentity.GetOrAdd(gameObject);
            _camera = _identity.CameraTransform;
            _mechCamera = _identity.MechCamera;
        }

        private void Update()
        {
            if (_hardLockBlockTimer > 0f) _hardLockBlockTimer -= Time.deltaTime;
            if (_allLockBlockTimer > 0f) _allLockBlockTimer -= Time.deltaTime;

            if (_camera == null) return;
            DetectTarget();
            UpdateHardLock();
        }

        private void DetectTarget()
        {
            if (_allLockBlockTimer > 0f)
            {
                Target = null;
                return;
            }
            // 1대1이라 후보는 상대 하나뿐. 주변 콜라이더를 훑을 필요가 없다.
            Target = CanSoftLock(_identity.Opponent) ? _identity.Opponent.transform : null;
        }

        private bool CanSoftLock(MechIdentity opponent)
        {
            if (opponent == null || !opponent.gameObject.activeInHierarchy) return false;

            Vector3 targetCenter = GetTargetCenter(opponent);
            Vector3 toTarget = targetCenter - _camera.position;
            float distance = toTarget.magnitude;
            if (distance > T.softRange) return false;

            Vector3 dirToTarget = toTarget.normalized;
            if (Vector3.Angle(_camera.forward, dirToTarget) >= T.softAngle) return false;

            // 가려짐 판정: 카메라에서 상대 사이에 처음 걸리는 것이 상대여야 한다.
            // 카메라가 내 기체에 붙어 있어서 내 콜라이더가 먼저 걸릴 수 있으니 그건 건너뛴다.
            RaycastHit[] rayHits = Physics.RaycastAll(_camera.position, dirToTarget, distance + 1f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            System.Array.Sort(rayHits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit rayHit in rayHits)
            {
                if (_identity.IsOwnCollider(rayHit.collider)) continue;
                return _identity.IsOpponentCollider(rayHit.collider);
            }
            // 아무것도 안 걸렸으면(콜라이더 경계 바로 밖 등) 가려진 건 아니다
            return true;
        }

        private static Vector3 GetTargetCenter(MechIdentity opponent)
        {
            Collider col = opponent.GetComponent<Collider>();
            return col != null ? col.bounds.center : opponent.transform.position;
        }

        private void UpdateHardLock()
        {
            if (Target == null || IsHardLockBlocked)
            {
                ReleaseHardLock();
                return;
            }

            // 거리는 기체 기준: 카메라 기준이면 카메라 오프셋만큼 펀치 전환 거리가 흔들린다
            float dist = Vector3.Distance(transform.position, Target.position);

            if (!IsHardLocked)
            {
                if (dist <= T.hardEnterRange)
                {
                    IsHardLocked = true;
                }
            }
            else
            {
                if (dist > T.hardExitRange)
                {
                    ReleaseHardLock();
                    return;
                }

                // 각도는 "내가 보고 있는 방향" 기준이라 카메라 정면 유지
                Vector3 dirToTarget = (Target.position - _camera.position).normalized;
                float angle = Vector3.Angle(_camera.forward, dirToTarget);

                if (angle > T.hardMaxAngle * 0.5f)
                {
                    ReleaseHardLock();
                    return;
                }
            }

            if (IsHardLocked && _mechCamera != null)
            {
                _mechCamera.TrackTarget(Target, T.hardTrackSpeed);
            }
        }

        private void ReleaseHardLock()
        {
            // 목표를 잃었을 때도 카메라 추적을 반드시 끊는다 (안 끊으면 사라진 목표를 계속 바라봄)
            if (IsHardLocked && _mechCamera != null) _mechCamera.StopTracking();
            IsHardLocked = false;
        }

        /// <summary>내 런지 돌진 동안 켠다. 켜져 있는 동안 하드 락온이 걸리지 않는다.</summary>
        public void SetHardLockSuppressed(bool suppressed)
        {
            _hardLockSuppressed = suppressed;
            if (suppressed) ReleaseHardLock();
        }

        /// <summary>일정 시간 하드 락온 금지 (런지 빗나감 후 반격 틈).</summary>
        public void BlockHardLockFor(float seconds)
        {
            _hardLockBlockTimer = Mathf.Max(_hardLockBlockTimer, seconds);
            ReleaseHardLock();
        }

        /// <summary>일정 시간 소프트·하드 락온 모두 금지 (상대의 슬라이딩).</summary>
        public void BlockAllLockFor(float seconds)
        {
            _allLockBlockTimer = Mathf.Max(_allLockBlockTimer, seconds);
            ReleaseHardLock();
            Target = null;
        }

        /// <summary>라운드 시작: 락온 해제 + 금지 타이머 초기화.</summary>
        public void ResetForRound()
        {
            _hardLockSuppressed = false;
            _hardLockBlockTimer = 0f;
            _allLockBlockTimer = 0f;
            ForceRelease();
        }

        public void ForceRelease()
        {
            IsHardLocked = false;
            if (_mechCamera != null) _mechCamera.StopTracking();
            Target = null;
        }
    }
}