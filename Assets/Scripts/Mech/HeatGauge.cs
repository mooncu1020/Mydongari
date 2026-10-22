using MyDongari.Movement;
using UnityEngine;

namespace MyDongari.Mech
{
    public class HeatGauge : MonoBehaviour
    {
        [Header("히트 설정")]
        [SerializeField] private float maxHeat = 100f;
        [SerializeField] private float coolingRate = 20f;
        [SerializeField] private float sprintCoolingPenalty = 0.2f;

        public float CurrentHeat { get; private set; }
        public bool IsOverheated { get; private set; }
        public bool IsSkillActive { get; set; }

        private MechStateMachine _stateMachine;

        private void Awake()
        {
            CurrentHeat = 0f;
            _stateMachine = GetComponent<MechStateMachine>();
        }

        private void Update()
        {
            if (_stateMachine == null) return;

            HandleCooling();
            CheckOverheat();
        }

        private void HandleCooling()
        {
            if (IsSkillActive) return;

            if (IsOverheated)
            {
                float currentCoolingRate = coolingRate;
                if (_stateMachine.IsSprinting)
                {
                    currentCoolingRate *= (1f - sprintCoolingPenalty);
                }
                CurrentHeat = Mathf.Max(CurrentHeat - currentCoolingRate * Time.deltaTime, 0f);
                if (CurrentHeat <= 0f)
                {
                    IsOverheated = false;
                }
                return;
            }

            if (CurrentHeat >= maxHeat) return;

            float normalCoolingRate = coolingRate;
            if (_stateMachine.IsSprinting)
            {
                normalCoolingRate *= (1f - sprintCoolingPenalty);
            }

            CurrentHeat = Mathf.Max(CurrentHeat - normalCoolingRate * Time.deltaTime, 0f);
        }

        private void CheckOverheat()
        {
            if (CurrentHeat >= maxHeat)
            {
                CurrentHeat = maxHeat;
                IsOverheated = true;
            }
        }

        public void AddHeat(float amount)
        {
            CurrentHeat = Mathf.Min(CurrentHeat + amount, maxHeat);
        }


        public bool CanFire() => !IsOverheated;

        /// <summary>라운드 시작: 히트 0, 과열 해제.</summary>
        public void ResetForRound()
        {
            CurrentHeat = 0f;
            IsOverheated = false;
            IsSkillActive = false;
        }

        /// <summary>과열(100%) 중엔 펀치 불가. 해제는 0%까지 식었을 때.</summary>
        public bool CanPunch() => !IsOverheated;
    }
}