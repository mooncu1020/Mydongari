using UnityEngine;
using MyDongari.Movement;
using MyDongari.Combat;

namespace MyDongari.Mech
{
    public class BoostGauge : MonoBehaviour
    {
        [Header("부스트 설정")]
        [SerializeField] private float maxBoost = 100f;
        [SerializeField] private float recoveryDelay = 0.5f;
        [SerializeField] private float recoveryRate = 25f;
        [SerializeField] private float sprintDrainRate = 5f;
        [Tooltip("탈진(0%) 후 걷기/정지로 회복해 최대치의 이 비율에 도달하면 탈진 해제")]
        [Range(0f, 1f)]
        [SerializeField] private float exhaustRecoverRatio = 0.5f;

        [Header("소모량")]
        [SerializeField] private float sideStepCost = 10f;
        [SerializeField] private float quickTurnCost = 10f;
        [SerializeField] private float slideCost = 10f;

        public float CurrentBoost { get; private set; }
        public float MaxBoost => maxBoost;
        public bool IsExhausted { get; private set; }

        private float _recoveryDelayTimer = 0f;
        private bool _isRecovering = false;
        private MechStateMachine _stateMachine;
        private MeleeSystem _meleeSystem;

        private void Awake()
        {
            CurrentBoost = maxBoost;
            _stateMachine = GetComponent<MechStateMachine>();
            _meleeSystem = GetComponent<MeleeSystem>();
        }

        private void Update()
        {
            if (_stateMachine == null) return;

            if (_stateMachine.IsSprinting)
            {
                Drain(sprintDrainRate * Time.deltaTime);
            }

            // 가드를 올리고 있으면 걷기/정지여도 회복하지 않는다 (가드 유지에 대가를 주기 위함)
            bool guarding = _meleeSystem != null && _meleeSystem.IsGuarding;
            bool canRecover = (_stateMachine.IsWalking || _stateMachine.IsIdle) && !guarding;
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

            if (_recoveryDelayTimer >= recoveryDelay)
            {
                _isRecovering = true;
            }

            if (_isRecovering)
            {
                CurrentBoost = Mathf.Min(CurrentBoost + recoveryRate * Time.deltaTime, maxBoost);
            }
        }

        private void CheckExhaust()
        {
            if (CurrentBoost <= 0f)
            {
                CurrentBoost = 0f;
                IsExhausted = true;
            }
            else if (IsExhausted && CurrentBoost >= maxBoost * exhaustRecoverRatio)
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
            CurrentBoost = maxBoost;
            IsExhausted = false;
            _recoveryDelayTimer = 0f;
            _isRecovering = false;
        }

        public float GetSideStepCost() => sideStepCost;
        public float GetQuickTurnCost() => quickTurnCost;
        public float GetSlideCost() => slideCost;
    }
}