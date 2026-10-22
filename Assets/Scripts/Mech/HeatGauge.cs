using MyDongari.Movement;
using UnityEngine;

namespace MyDongari.Mech
{
    public class HeatGauge : MonoBehaviour
    {
        // 수치는 BernardTuning(히트)으로 옮겼다. 숨긴 필드는 "Copy P1 Values To Tuning" 메뉴용, 런타임에서 읽지 않는다.
#pragma warning disable 0414
        [HideInInspector] [SerializeField] private float maxHeat = 100f;
        [HideInInspector] [SerializeField] private float coolingRate = 20f;
        [HideInInspector] [SerializeField] private float sprintCoolingReduction = 0.7f;
#pragma warning restore 0414

        private TuningRef _tuning;
        private BernardTuning.HeatValues T => _tuning.Get(this).Heat;

        public float CurrentHeat { get; private set; }
        public float MaxHeat => T.maxHeat;

        // 상태 머신이 없는 기체(연습 봇)는 질주하지 않으므로 항상 평소 냉각
        private bool IsSprinting => _stateMachine != null && _stateMachine.IsSprinting;
        public bool IsOverheated { get; private set; }

        private MechStateMachine _stateMachine;

        private void Awake()
        {
            CurrentHeat = 0f;
            _stateMachine = GetComponent<MechStateMachine>();
        }

        private void Update()
        {
            HandleCooling();
            CheckOverheat();
        }

        private void HandleCooling()
        {
            if (IsOverheated)
            {
                float currentCoolingRate = T.coolingRate;
                if (IsSprinting)
                {
                    currentCoolingRate *= (1f - T.sprintCoolingReduction);
                }
                CurrentHeat = Mathf.Max(CurrentHeat - currentCoolingRate * Time.deltaTime, 0f);
                if (CurrentHeat <= 0f)
                {
                    IsOverheated = false;
                }
                return;
            }

            if (CurrentHeat >= T.maxHeat) return;

            float normalCoolingRate = T.coolingRate;
            if (IsSprinting)
            {
                normalCoolingRate *= (1f - T.sprintCoolingReduction);
            }

            CurrentHeat = Mathf.Max(CurrentHeat - normalCoolingRate * Time.deltaTime, 0f);
        }

        private void CheckOverheat()
        {
            if (CurrentHeat >= T.maxHeat)
            {
                CurrentHeat = T.maxHeat;
                IsOverheated = true;
            }
        }

        public void AddHeat(float amount)
        {
            CurrentHeat = Mathf.Min(CurrentHeat + amount, T.maxHeat);
        }


        public bool CanFire() => !IsOverheated;

        /// <summary>라운드 시작: 히트 0, 과열 해제.</summary>
        public void ResetForRound()
        {
            CurrentHeat = 0f;
            IsOverheated = false;
        }

        /// <summary>과열(100%) 중엔 펀치 불가. 해제는 0%까지 식었을 때.</summary>
        public bool CanPunch() => !IsOverheated;
    }
}