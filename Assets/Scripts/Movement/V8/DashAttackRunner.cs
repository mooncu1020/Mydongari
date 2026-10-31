using UnityEngine;
using MyDongari.Combat;
using MyDongari.Core;
using MyDongari.Mech;

namespace MyDongari.Movement.V8
{
    /// <summary>
    /// V8 지상 대시의 대시 어택·차징 태클 진행 (예전 DashState의 공격 부분과 같은 규칙 — 예전 코드는 그대로 두고 V8용으로 옮김).
    /// 대시 경과 0.8초 미만 공격 = 유도 대시 어택(25m 안이면 초당 120°까지 꺾어 따라감), 이상 = 차징 태클(직선, 1.6초 최대).
    /// 발생 시간 뒤 activeTime 동안 돌진하며 닿으면 판정, 끝까지 못 닿으면 빗나감. 끝나면 경직(대시 어택은 방향별 0.2~0.3초).
    /// </summary>
    public class DashAttackRunner
    {
        public enum Result { None, Running, Finished }

        private readonly MechStateMachine _sm;
        private float _chargeTimer;
        private bool _requested;
        private float _attackElapsed = -1f;
        private AttackType _type;
        private float _power;
        private bool _homing;

        private MechController Controller => _sm.MechController;
        private BernardTuning.TackleValues Tackle => Controller.Tuning.Tackle;
        private BernardTuning.DashAttackValues DashAttack => Controller.Tuning.DashAttack;

        public DashAttackRunner(MechStateMachine sm)
        {
            _sm = sm;
        }

        public bool IsAttacking => _requested || _attackElapsed >= 0f;
        public bool IsTackleCharged => _chargeTimer >= Tackle.dashChargeStartTime;
        public float TackleChargePower
        {
            get
            {
                float span = Tackle.dashFullChargeTime - Tackle.dashChargeStartTime;
                if (span <= 0f) return IsTackleCharged ? 1f : 0f;
                return Mathf.Clamp01((_chargeTimer - Tackle.dashChargeStartTime) / span);
            }
        }

        public void Reset()
        {
            _chargeTimer = 0f;
            _requested = false;
            _attackElapsed = -1f;
        }

        /// <summary>공격 중이 아니면 차징 시간을 센다 (대시 경과).</summary>
        public void TickCharge(float dt)
        {
            if (!IsAttacking) _chargeTimer += dt;
        }

        /// <summary>공격 버튼 요청. 받아들이면 true (이번 틱 메인에서 시작).</summary>
        public bool Request()
        {
            if (_requested) return true;
            MeleeSystem melee = Controller.MeleeSystem;
            if (_attackElapsed >= 0f || melee == null || !melee.CanTackle) return false;
            _requested = true;
            return true;
        }

        /// <summary>
        /// 한 틱. 요청·차징 접촉이면 시작, 공격 중이면 진행. heading은 유도가 바꾼다. stop = 이번 틱 멈춤(닿아서 판정·빗나감).
        /// Finished면 경직까지 넣었고 대시를 끝내야 한다.
        /// </summary>
        public Result Tick(float dt, ref Vector3 heading, float speedMps, float snappedAngle, out bool stop)
        {
            stop = false;
            MeleeSystem melee = Controller.MeleeSystem;
            if (_attackElapsed < 0f)
            {
                bool requested = _requested;
                _requested = false;
                if (!requested && !(IsTackleCharged && IsOpponentInContact(heading))) return Result.None;
                if (melee == null || !melee.CanTackle) return Result.None;
                AttackType type = IsTackleCharged ? AttackType.Tackle : AttackType.DashAttack;
                if (!melee.BeginTackle(type)) return Result.None;
                _attackElapsed = 0f;
                _type = type;
                _power = TackleChargePower;
                _homing = type == AttackType.DashAttack && IsOpponentWithin(DashAttack.homingRange);
                // 대시 중 공격: 몸을 상대 쪽으로 계속 (지속 조준)
                if (_sm.Motor != null) _sm.Motor.SetAutoTurn(MechMotor.AutoTurnMode.SustainAim);
                return Result.Running;
            }

            _attackElapsed += dt;
            if (_homing) UpdateHoming(ref heading, dt);
            if (melee == null) return Result.Running;

            float startup = melee.TackleStartup(_type);
            if (_attackElapsed < startup) return Result.Running;
            bool expired = _attackElapsed >= startup + ActiveTime(_type);

            if (IsOpponentWithin(HitDistance(_type)))
            {
                float elapsed = _attackElapsed;
                // 판정하는 동안엔 공격 중이 아닌 것으로: 패리 반사·맞딜 넉백이 대시를 끝내도 취소로 알리지 않게
                _attackElapsed = -1f;
                HitResult? result = melee.ResolveTackle(_power, _type, heading);
                if (result != null)
                {
                    stop = true;
                    Finish(melee, result, snappedAngle);
                    return Result.Finished;
                }
                _attackElapsed = elapsed;
            }

            if (expired)
            {
                stop = true;
                Finish(melee, null, snappedAngle);
                return Result.Finished;
            }
            return Result.Running;
        }

        private void Finish(MeleeSystem melee, HitResult? result, float snappedAngle)
        {
            _attackElapsed = -1f;
            if (_sm.Motor != null) _sm.Motor.SetAutoTurn(MechMotor.AutoTurnMode.Default);
            if (result == HitResult.Parried || result == HitResult.Clash) return;
            float recovery = _type == AttackType.DashAttack
                ? Controller.Tuning.Movement.dashAttackRecovery.Evaluate(snappedAngle)
                : melee.TackleRecoveryStun;
            melee.ApplyStun(recovery);
        }

        /// <summary>대시가 공격 도중에 끝났으면(스턴·넉백·라운드 리셋) 판정 없이 취소.</summary>
        public void CancelIfAttacking()
        {
            _requested = false;
            if (_attackElapsed < 0f) return;
            _attackElapsed = -1f;
            MeleeSystem melee = Controller.MeleeSystem;
            if (melee != null) melee.CancelTackle(_type);
        }

        /// <summary>맞딜로 소진 (경직 없음).</summary>
        public void ConsumeByClash()
        {
            _attackElapsed = -1f;
            _requested = false;
        }

        public bool WouldHitWithin(float seconds, Vector3 heading, float speedMps)
        {
            if (_attackElapsed < 0f) return false;
            MeleeSystem melee = Controller.MeleeSystem;
            if (melee == null) return false;
            if (melee.TackleStartup(_type) - _attackElapsed > seconds) return false;
            return IsOpponentWithin(HitDistance(_type) + speedMps * seconds) && melee.IsOpponentInTackleArc(heading);
        }

        private void UpdateHoming(ref Vector3 heading, float dt)
        {
            if (!TryGetToOpponent(out Vector3 toOpponent, out float distance)) return;
            if (distance > DashAttack.homingRange) return;
            if (Vector3.Dot(heading, toOpponent) <= 0f)
            {
                _homing = false;
                return;
            }
            float maxRadians = DashAttack.homingTurnRate * Mathf.Deg2Rad * dt;
            heading = Vector3.RotateTowards(heading, toOpponent, maxRadians, 0f);
            heading.y = 0f;
            heading.Normalize();
        }

        private float HitDistance(AttackType type) => type == AttackType.Tackle ? Tackle.hitDistance : DashAttack.hitDistance;
        private float ActiveTime(AttackType type) => type == AttackType.Tackle ? Tackle.activeTime : DashAttack.activeTime;

        private bool TryGetToOpponent(out Vector3 direction, out float distance)
        {
            direction = Vector3.zero;
            distance = float.MaxValue;
            MechIdentity opponent = Controller.Identity != null ? Controller.Identity.Opponent : null;
            if (opponent == null || !opponent.gameObject.activeInHierarchy) return false;
            Vector3 self = _sm.Motor != null ? _sm.Motor.Position : Controller.Rb.position;
            Vector3 offset = MechMotor.OpponentPosition(opponent) - self;
            distance = offset.magnitude;
            offset.y = 0f;
            if (offset.sqrMagnitude < 0.0001f) return false;
            direction = offset.normalized;
            return true;
        }

        private bool IsOpponentWithin(float range)
        {
            TryGetToOpponent(out _, out float distance);
            return distance <= range;
        }

        private bool IsOpponentInContact(Vector3 heading)
        {
            MeleeSystem melee = Controller.MeleeSystem;
            if (melee == null || !melee.IsOpponentInTackleArc(heading)) return false;
            Collider[] hits = Physics.OverlapSphere(Controller.transform.position, Tackle.contactRadius);
            foreach (Collider hit in hits)
            {
                if (Controller.Identity.IsOpponentCollider(hit)) return true;
            }
            return false;
        }
    }
}
