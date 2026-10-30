using UnityEngine;
using MyDongari.Movement;
using MyDongari.Combat;

namespace MyDongari.Mech
{
    public class BoostGauge : MonoBehaviour
    {
        private TuningRef _tuning;
        private BernardTuning.BoostValues T => _tuning.Get(this).Boost;

        public float CurrentBoost { get; private set; }
        public float MaxBoost => T.maxBoost;
        public bool IsExhausted { get; private set; }

        private float _recoveryDelayTimer = 0f;
        private bool _isRecovering = false;
        private MechStateMachine _stateMachine;
        private MeleeSystem _meleeSystem;

        private void Awake()
        {
            CurrentBoost = T.maxBoost;
            _stateMachine = GetComponent<MechStateMachine>();
            _meleeSystem = GetComponent<MeleeSystem>();
        }

        private void Update()
        {
            if (_stateMachine != null && _stateMachine.IsDashing)
            {
                Drain(T.dashDrainRate * Time.deltaTime);
            }

            // V3: 땅에서 달리기·근접 이동(둘 다 Run 상태)·정지 중에 회복. 가드를 올리고 있으면 멈춘다 (가드 유지에 대가)
            // 대시·대시 끝 미끄러짐·사이드스텝·런지·넉다운 중엔 회복 안 함. 공중(점프)은 5단계에서 추가
            bool guarding = _meleeSystem != null && _meleeSystem.IsGuarding;
            // 상태 머신이 없는 기체(연습 봇)는 땅에서 걷기·정지만 하므로 가드 중이 아니면 회복
            bool calm = _stateMachine == null || _stateMachine.IsRunning || _stateMachine.IsIdle;
            bool canRecover = calm && !guarding;
            HandleRecovery(canRecover);
            CheckExhaust();
        }

        private void HandleRecovery(bool canRecover)
        {
            if (!canRecover)
            {
                _recoveryDelayTimer = 0f;
                _isRecovering = false;
                return;
            }

            _recoveryDelayTimer += Time.deltaTime;

            if (_recoveryDelayTimer >= T.recoveryDelay)
            {
                _isRecovering = true;
            }

            if (_isRecovering)
            {
                CurrentBoost = Mathf.Min(CurrentBoost + T.recoveryRate * Time.deltaTime, T.maxBoost);
            }
        }

        private void CheckExhaust()
        {
            if (CurrentBoost <= 0f)
            {
                CurrentBoost = 0f;
                IsExhausted = true;
            }
            else if (IsExhausted && CurrentBoost >= T.maxBoost * T.exhaustRecoverRatio)
            {
                // 끝까지 다 채울 때까지 묶어 두면 너무 길어서, 절반 정도에서 풀어 준다
                IsExhausted = false;
            }
        }

        private void Drain(float amount)
        {
            CurrentBoost = Mathf.Max(CurrentBoost - amount, 0f);
        }

        public bool TryConsume(float amount)
        {
            if (IsExhausted) return false;
            if (CurrentBoost < amount) return false;
            CurrentBoost -= amount;
            return true;
        }

        /// <summary>라운드 시작: 최대치, 탈진 해제, 회복 타이머 초기화.</summary>
        public void ResetForRound()
        {
            CurrentBoost = T.maxBoost;
            IsExhausted = false;
            _recoveryDelayTimer = 0f;
            _isRecovering = false;
        }

        public float GetSideStepCost() => T.sideStepCost;
    }
}