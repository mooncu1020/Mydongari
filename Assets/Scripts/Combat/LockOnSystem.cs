using UnityEngine;
using MyDongari.Core;
using MyDongari.Mech;

namespace MyDongari.Combat
{
    public class LockOnSystem : MonoBehaviour
    {
        private TuningRef _tuning;
        private BernardTuning.LockOnValues T => _tuning.Get(this).LockOn;

        private Transform _camera;
        private MechIdentity _identity;

        /// <summary>하드 락온 중이면 상대, 아니면 소프트 락온(카메라 정면 부채꼴·가려짐 없음)이 잡은 상대. 앵커 클로·HUD 마커가 쓴다.</summary>
        public Transform Target { get; private set; }
        public bool IsHardLocked { get; private set; }

        // 하드 락온 금지: 내 런지 돌진 중(플래그) + 런지 빗나감 직후(타이머)
        private bool _hardLockSuppressed;
        private float _hardLockBlockTimer;

        public bool IsHardLockBlocked => _hardLockSuppressed || _hardLockBlockTimer > 0f;

        private void Awake()
        {
            // 화면 분할: 각 기체는 자기 카메라만 기준으로 조준한다
            _identity = MechIdentity.GetOrAdd(gameObject);
            _camera = _identity.CameraTransform;
        }

        private void Update()
        {
            if (_hardLockBlockTimer > 0f) _hardLockBlockTimer -= Time.deltaTime;

            // V3: 하드 락온은 거리만 보므로 카메라가 없어도 판정한다
            UpdateHardLock();
            DetectTarget();
        }

        private void DetectTarget()
        {
            MechIdentity opponent = _identity.Opponent;
            if (IsHardLocked)
            {
                Target = opponent.transform;
                return;
            }
            // 1대1이라 후보는 상대 하나뿐. 주변 콜라이더를 훑을 필요가 없다.
            Target = _camera != null && CanSoftLock(opponent) ? opponent.transform : null;
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

        /// <summary>
        /// V3 하드 락온: 상대가 기체 기준 3D 거리 hardLockEnterRange(10m) 안이면 걸리고 hardLockExitRange(12m) 밖이면 풀린다.
        /// 시야 각도·가려짐은 보지 않는다 — 뒤로 돌아 들어온 상대도 10m 안이면 몸이 돌아 마주 본다.
        /// </summary>
        private void UpdateHardLock()
        {
            MechIdentity opponent = _identity.Opponent;
            if (opponent == null || !opponent.gameObject.activeInHierarchy || IsHardLockBlocked)
            {
                ReleaseHardLock();
                return;
            }

            // 거리는 기체 기준: 카메라 기준이면 카메라 오프셋만큼 흔들린다
            float dist = Vector3.Distance(transform.position, opponent.transform.position);
            if (!IsHardLocked)
            {
                if (dist <= T.hardLockEnterRange) IsHardLocked = true;
            }
            else if (dist > T.hardLockExitRange)
            {
                ReleaseHardLock();
            }
        }

        private void ReleaseHardLock()
        {
            // V3: 카메라는 몸을 따라가고 하드락이면 몸이 상대를 본다 — 카메라 추적을 따로 끊을 필요가 없다
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

        /// <summary>라운드 시작: 락온 해제 + 금지 타이머 초기화.</summary>
        public void ResetForRound()
        {
            _hardLockSuppressed = false;
            _hardLockBlockTimer = 0f;
            ForceRelease();
        }

        public void ForceRelease()
        {
            IsHardLocked = false;
            Target = null;
        }
    }
}