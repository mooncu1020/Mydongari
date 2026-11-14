using UnityEngine;
using MyDongari.Bridge;
using MyDongari.Core;
using MyDongari.Game.Tuning;

namespace MyDongari.Game.Combat.Skills
{
    /// <summary>
    /// 임팩트 슬램: 전방 부채꼴 충격파. 쿨다운은 전투 틱으로 세고, 판정은 발동한 틱의 끝(모든 기체가 움직인 뒤)에 한다.
    /// </summary>
    public class ImpactSlam : MonoBehaviour
    {
        private TuningRef _tuning;
        private BernardTuning.ImpactSlamValues T => _tuning.Get(this).ImpactSlam;

        /// <summary>슬램을 발동한 순간(맞든 빗나가든). 외형·로그용.</summary>
        public event System.Action OnSlamStarted;

        // 마지막 발동 뒤 지난 틱
        private int _cooldownTimer;
        // 이번 틱 끝에 판정할지
        private bool _strikePending;
        private MechIdentity _identity;
        private MeleeSystem _melee;

        private void Awake()
        {
            _identity = MechIdentity.GetOrAdd(gameObject);
            _melee = GetComponent<MeleeSystem>();
            _cooldownTimer = T.cooldownFrames;
        }

        /// <summary>라운드 시작: 쿨타임을 비워 바로 쓸 수 있게.</summary>
        public void ResetForRound()
        {
            _cooldownTimer = T.cooldownFrames;
            _strikePending = false;
        }

        /// <summary>전투 틱마다 (MeleeSystem이 부른다).</summary>
        public void CombatTick()
        {
            if (_cooldownTimer < T.cooldownFrames) _cooldownTimer++;
        }

        /// <summary>발동. 쿨다운이면 false. 판정은 이 틱의 끝에 하고, 결과는 다음 틱 상대의 정산에서 정해진다.</summary>
        public bool TryActivate()
        {
            if (_cooldownTimer < T.cooldownFrames) return false;
            _cooldownTimer = 0;
            _strikePending = true;
            // 맞든 빗나가든 내리찍는 순간 (외형·로그용)
            OnSlamStarted?.Invoke();
            return true;
        }

        /// <summary>틱 끝: 전방 부채꼴 안에 상대가 있으면 상대 기록에 쌓는다. 가드(감쇄)·패리(무효)는 상대의 판단에서 적용된다.</summary>
        public void CollectHits()
        {
            if (!_strikePending) return;
            _strikePending = false;
            BernardTuning.ImpactSlamValues t = T;

            MechIdentity opponent = _identity.Opponent;
            if (opponent == null) return;

            Vector3 dirToTarget = (opponent.transform.position - transform.position).normalized;
            // 부채꼴 기준 = 상체 방향 (G2)
            float angle = Vector3.Angle(MechFacing.AttackForward(transform), dirToTarget);
            if (angle > t.angle * 0.5f) return;

            Collider[] hits = Physics.OverlapSphere(transform.position, t.range);
            foreach (Collider hit in hits)
            {
                if (!_identity.IsOpponentCollider(hit)) continue;

                HitInfo info = _melee != null
                    ? _melee.NewHit(AttackType.Slam, HitKind.Melee, t.damage, opponent.gameObject)
                    : new HitInfo { Type = AttackType.Slam, Kind = HitKind.Melee, Damage = t.damage, Attacker = gameObject, Direction = dirToTarget };
                info.KnockbackForce = t.knockback;
                // 상대 콜라이더가 여러 개여도 한 번만 맞는다
                MeleeSystem.QueueHit(opponent.gameObject, info);
                return;
            }
        }
    }
}
