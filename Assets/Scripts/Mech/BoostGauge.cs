using UnityEngine;
using MyDongari.Movement;

namespace MyDongari.Mech
{
    public class BoostGauge : MonoBehaviour
    {
        [Header("부스트 설정")]
        [SerializeField] private float maxBoost = 100f;
        [SerializeField] private float recoveryDelay = 0.5f;
        [SerializeField] private float recoveryRate = 25f;
        [SerializeField] private float sprintDrainRate = 5f;

        [Header("소모량")]
        [SerializeField] private float sideStepCost = 10f;
        [SerializeField] private float quickTurnCost = 10f;
        [SerializeField] private float slideCost = 10f;

        public float CurrentBoost { get; private set; }
        public float MaxBoost => maxBoost;
        public bool IsOverheated { get; private set; }

        private float _recoveryDelayTimer = 0f;
        private bool _isRecovering = false;
        private MechStateMachine _stateMachine;

        private void Awake()
        {
            CurrentBoost = maxBoost;
            _stateMachine = GetComponent<MechStateMachine>();
        }

        private void Update()
        {
            if (_stateMachine == null) return;

            if (_stateMachine.IsSprinting)
            {
                Drain(sprintDrainRate * Time.deltaTime);
            }

            bool canRecover = _stateMachine.IsWalking || _stateMachine.IsIdle;
            HandleRecovery(canRecover);
            CheckOverheat();

            Debug.Log("부스트: " + CurrentBoost + " 오버히트: " + IsOverheated);
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
                if (CurrentBoost >= maxBoost)
                {
                    IsOverheated = false;
                }
            }
        }

        private void CheckOverheat()
        {
            if (CurrentBoost <= 0f)
            {
                CurrentBoost = 0f;
                IsOverheated = true;
            }
        }

        private void Drain(float amount)
        {
            CurrentBoost = Mathf.Max(CurrentBoost - amount, 0f);
        }

        public bool TryConsume(float amount)
        {
            if (IsOverheated) return false;
            if (CurrentBoost < amount) return false;
            CurrentBoost -= amount;
            return true;
        }

        public float GetSideStepCost() => sideStepCost;
        public float GetQuickTurnCost() => quickTurnCost;
        public float GetSlideCost() => slideCost;
    }
}