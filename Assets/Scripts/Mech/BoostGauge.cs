using UnityEngine;
using MyDongari.Movement;
using MyDongari.Combat;

namespace MyDongari.Mech
{
    public class BoostGauge : MonoBehaviour
    {
        // 수치는 BernardTuning(부스트)으로 옮겼다. 숨긴 필드는 "Copy P1 Values To Tuning" 메뉴용, 런타임에서 읽지 않는다.
#pragma warning disable 0414
        [HideInInspector] [SerializeField] private float maxBoost = 100f;
        [HideInInspector] [SerializeField] private float recoveryDelay = 0.5f;
        [HideInInspector] [SerializeField] private float recoveryRate = 25f;
        [HideInInspector] [SerializeField] private float sprintDrainRate = 5f;
        [HideInInspector] [SerializeField] private float exhaustRecoverRatio = 0.5f;
        [HideInInspector] [SerializeField] private float sideStepCost = 10f;
        [HideInInspector] [SerializeField] private float quickTurnCost = 10f;
        [HideInInspector] [SerializeField] private float slideCost = 10f;
#pragma warning restore 0414

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
            if (_stateMachine == null) return;

            if (_stateMachine.IsSprinting)
            {
                Drain(T.sprintDrainRate * Time.deltaTime);
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
        public float GetQuickTurnCost() => T.quickTurnCost;
        public float GetSlideCost() => T.slideCost;
    }
}