using UnityEngine;
using MyDongari.Core.VirtualOn;
using MyDongari.Game.Combat;
using MyDongari.Game.Tuning;

namespace MyDongari.Bridge
{
    /// <summary>
    /// 지상 대시의 대시 어택·차징 태클 진행 (틱 단위).
    /// 대시 경과 48f(0.8초) 미만 공격 = 유도 대시 어택(25m 안이면 초당 120°까지 꺾어 따라감), 이상 = 차징 태클(직선, 96f에 최대).
    /// 발생 시간 뒤 activeFrames 동안 돌진하며 닿으면 판정, 끝까지 못 닿으면 빗나감. 끝나면 경직(대시 어택은 방향별 12~18f).
    /// 판정은 틱 끝(CollectHits — 모든 기체가 움직인 뒤)에 상대 기록에 쌓고, 다음 틱에 대시를 끝낸다.
    /// </summary>
    public class DashAttackRunner : IVoDashAttack
    {
        private readonly MechStateMachine _sm;
        private int _chargeTimer;
        private bool _requested;
        private int _attackElapsed = -1;
        private AttackType _type;
        private float _power;
        private bool _homing;
        // 틱 끝 판정에 쓸 이번 틱의 진로
        private Vector3 _heading;
        // 틱 끝에 상대 기록에 쌓았다 — 다음 틱에 멈추고 끝낸다
        private bool _landed;

        private MechController Controller => _sm.MechController;
        private BernardTuning.TackleValues Tackle => Controller.Tuning.Tackle;
        private BernardTuning.DashAttackValues DashAttack => Controller.Tuning.DashAttack;

        public DashAttackRunner(MechStateMachine sm)
        {
            _sm = sm;
        }

        public bool IsAttacking => _requested || _attackElapsed >= 0;
        public bool IsTackleCharged => _chargeTimer >= Tackle.dashChargeStartFrames;
        public float TackleChargePower
        {
            get
            {
                int span = Tackle.dashFullChargeFrames - Tackle.dashChargeStartFrames;
                if (span <= 0) return IsTackleCharged ? 1f : 0f;
                return Mathf.Clamp01((_chargeTimer - Tackle.dashChargeStartFrames) / (float)span);
            }
        }

        public void Reset()
        {
            _chargeTimer = 0;
            _requested = false;
            _attackElapsed = -1;
            _landed = false;
        }

        /// <summary>공격 중이 아니면 차징 시간을 센다 (대시 경과 틱).</summary>
        public void TickCharge()
        {
            if (!IsAttacking) _chargeTimer++;
        }

        /// <summary>공격 버튼 요청. 받아들이면 true (이번 틱 메인에서 시작).</summary>
        public bool Request()
        {
            if (_requested) return true;
            MeleeSystem melee = Controller.MeleeSystem;
            if (_attackElapsed >= 0 || melee == null || !melee.CanTackle) return false;
            _requested = true;
            return true;
        }

        /// <summary>
        /// 한 틱. 요청·차징 접촉이면 시작, 공격 중이면 진행. heading은 유도가 바꾼다. stop = 이번 틱 멈춤(닿았거나 빗나감).
        /// Finished면 경직까지 넣었고 대시를 끝내야 한다.
        /// </summary>
        public VoDashAttackResult Tick(ref Vector3 heading, float speedMps, float snappedAngle, out bool stop)
        {
            stop = false;
            MeleeSystem melee = Controller.MeleeSystem;
            if (_attackElapsed < 0)
            {
                bool requested = _requested;
                _requested = false;
                if (!requested && !(IsTackleCharged && IsOpponentInContact(heading))) return VoDashAttackResult.None;
                if (melee == null || !melee.CanTackle) return VoDashAttackResult.None;
                AttackType type = IsTackleCharged ? AttackType.Tackle : AttackType.DashAttack;
                if (!melee.BeginTackle(type)) return VoDashAttackResult.None;
                _attackElapsed = 0;
                _landed = false;
                _type = type;
                _power = TackleChargePower;
                _homing = type == AttackType.DashAttack && IsOpponentWithin(DashAttack.homingRange);
                _heading = heading;
                // 대시 중 공격: 몸을 상대 쪽으로 계속 (지속 조준)
                if (_sm.Motor != null) _sm.Motor.SetAutoTurn(VoMotor.AutoTurnMode.SustainAim);
                return VoDashAttackResult.Running;
            }

            // 지난 틱 끝에 닿아 상대 기록에 쌓았다 → 멈추고 끝낸다
            if (_landed)
            {
                stop = true;
                Finish(melee, snappedAngle);
                return VoDashAttackResult.Finished;
            }

            _attackElapsed++;
            if (_homing) UpdateHoming(ref heading);
            _heading = heading;
            if (melee == null) return VoDashAttackResult.Running;

            if (_attackElapsed >= melee.TackleStartupFrames(_type) + ActiveFrames(_type))
            {
                stop = true;
                Finish(melee, snappedAngle);
                return VoDashAttackResult.Finished;
            }
            return VoDashAttackResult.Running;
        }

        /// <summary>틱 끝 (모든 기체가 움직인 뒤): 발생 시간이 지났고 상대가 판정 거리·부채꼴 안이면 상대 기록에 쌓는다.</summary>
        public void CollectHits()
        {
            if (_attackElapsed < 0 || _landed) return;
            MeleeSystem melee = Controller.MeleeSystem;
            if (melee == null || _attackElapsed < melee.TackleStartupFrames(_type)) return;
            if (!IsOpponentWithin(HitDistance(_type))) return;
            if (melee.QueueTackleHit(_power, _type, _heading)) _landed = true;
        }

        /// <summary>공격이 끝남(명중·빗나감): 공격자 경직. 패리·맞딜당했으면 그 경직은 결과가 올 때(다음 틱) 풀린다.</summary>
        private void Finish(MeleeSystem melee, float snappedAngle)
        {
            _attackElapsed = -1;
            _landed = false;
            if (_sm.Motor != null) _sm.Motor.SetAutoTurn(VoMotor.AutoTurnMode.Default);
            if (melee == null) return;
            int recovery = _type == AttackType.DashAttack
                ? Mathf.RoundToInt(Controller.Tuning.Movement.dashAttackRecoveryFrames.Evaluate(snappedAngle))
                : melee.TackleRecoveryFrames;
            melee.ApplyStun(recovery);
        }

        /// <summary>대시가 공격 도중에 끝났으면(스턴·넉백·라운드 리셋) 판정 없이 취소.</summary>
        public void CancelIfAttacking()
        {
            _requested = false;
            if (_attackElapsed < 0) return;
            _attackElapsed = -1;
            // 이미 상대 기록에 쌓은 타격은 취소가 아니다 (명중으로 끝난 것)
            bool landed = _landed;
            _landed = false;
            MeleeSystem melee = Controller.MeleeSystem;
            if (melee != null && !landed) melee.CancelTackle(_type);
        }

        /// <summary>맞딜로 소진 (경직 없음).</summary>
        public void ConsumeByClash()
        {
            _attackElapsed = -1;
            _requested = false;
            _landed = false;
        }

        private void UpdateHoming(ref Vector3 heading)
        {
            if (!TryGetToOpponent(out Vector3 toOpponent, out float distance)) return;
            if (distance > DashAttack.homingRange) return;
            if (Vector3.Dot(heading, toOpponent) <= 0f)
            {
                _homing = false;
                return;
            }
            float maxRadians = CombatTime.Seconds(DashAttack.homingTurnRate) * Mathf.Deg2Rad;
            heading = Vector3.RotateTowards(heading, toOpponent, maxRadians, 0f);
            heading.y = 0f;
            heading.Normalize();
        }

        private float HitDistance(AttackType type) => type == AttackType.Tackle ? Tackle.hitDistance : DashAttack.hitDistance;
        private int ActiveFrames(AttackType type) => type == AttackType.Tackle ? Tackle.activeFrames : DashAttack.activeFrames;

        private bool TryGetToOpponent(out Vector3 direction, out float distance)
        {
            direction = Vector3.zero;
            distance = float.MaxValue;
            MechIdentity opponent = Controller.Identity != null ? Controller.Identity.Opponent : null;
            if (opponent == null || !opponent.gameObject.activeInHierarchy) return false;
            Vector3 self = _sm.Motor != null ? _sm.Motor.Position : Controller.Rb.position;
            Vector3 offset = VoMotor.OpponentPosition(opponent.transform) - self;
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
