using System;
using UnityEngine;
using MyDongari.Input;
using MyDongari.Mech;

namespace MyDongari.Movement
{
    /// <summary>
    /// 이동 상태 (V3): Idle(정지) / Run(항상 달리기, 하드락 안이면 근접 이동) / SideStep / Lunge / Knockdown,
    /// 그리고 3단계 ○ 대시 전까지 임시로 남긴 Sprint(키보드 Space만).
    /// 걷기·제동·슬라이딩·초신지선회·트윈스틱 전력질주는 V3에서 삭제됐다.
    /// </summary>
    public class MechStateMachine : MonoBehaviour
    {
        public InputReader InputReader { get; private set; }
        public MechController MechController { get; private set; }

        private MechState _currentState;

        /// <summary>상태가 실제로 바뀌었을 때(Enter에서 곧바로 다른 상태로 튕겨 나간 경우는 제외). 로그용.</summary>
        public event Action<MechState> OnStateChanged;

        public IdleState IdleState { get; private set; }
        public RunState RunState { get; private set; }
        public SprintState SprintState { get; private set; }
        public SideStepState SideStepState { get; private set; }
        public LungeState LungeState { get; private set; }
        public KnockdownState KnockdownState { get; private set; }

        public bool IsSprinting => _currentState is SprintState;

        public bool IsStunned => MechController.MeleeSystem != null && MechController.MeleeSystem.IsStunned;
        public bool IsIdle => _currentState is IdleState;

        public bool IsRunning => _currentState is RunState;
        public bool IsLunging => _currentState is LungeState;

        private Combat.KnockdownController _knockdown;
        /// <summary>파일 드라이버 넉다운 중 (R1). 컴포넌트는 MeleeSystem이 런타임에 붙이기도 해서 처음 쓸 때 찾는다.</summary>
        public bool IsKnockedDown
        {
            get
            {
                if (_knockdown == null) _knockdown = GetComponent<Combat.KnockdownController>();
                return _knockdown != null && _knockdown.IsKnockedDown;
            }
        }

        /// <summary>스러스터 런지를 시작할 수 있는 상태(정지·달리기). 다른 기동 중엔 불가.</summary>
        public bool CanStartLunge => _currentState is IdleState || _currentState is RunState;

        /// <summary>
        /// 하드 락온 몸통 자동 회전을 허용하는 상태인지.
        /// 전력질주·런지·넉다운은 진행 방향이 정해진 기동이라 몸을 돌리면 안 된다.
        /// </summary>
        public bool AllowsHardLockFacing =>
            !(_currentState is SprintState) &&
            !(_currentState is LungeState) &&
            !(_currentState is KnockdownState);

        public string CurrentStateName => _currentState != null ? _currentState.GetType().Name : "None";

        // R스틱·마우스로 모은 몸 회전량(°). 입력은 Update에서, 회전은 물리 스텝에서 (마우스 델타가 프레임마다라 그대로 모아 둔다)
        private float _pendingTurn;

        /// <summary>
        /// R스틱 몸 회전을 받는 상태인지 (V3). 하드락 몸통 회전 중(하드락 안에서는 R스틱 꺼짐), 스턴, 외부 제어,
        /// 진행 방향이 정해진 기동(사이드스텝·런지·넉다운) 중엔 받지 않는다.
        /// </summary>
        public bool AllowsTurnInput =>
            (_currentState is IdleState || _currentState is RunState || _currentState is SprintState) &&
            !IsStunned && !MechController.IsExternallyControlled && !MechController.IsHardLockFacing;

        private void Awake()
        {
            InputReader = GetComponent<InputReader>();
            MechController = GetComponent<MechController>();

            IdleState = new IdleState(this);
            RunState = new RunState(this);
            SprintState = new SprintState(this);
            SideStepState = new SideStepState(this);
            LungeState = new LungeState(this);
            KnockdownState = new KnockdownState(this);
        }

        private void Start()
        {
            ChangeState(IdleState);
        }

        /// <summary>라운드 시작: 사망으로 꺼졌던 상태머신을 켜고 Idle로.</summary>
        public void ResetForRound()
        {
            enabled = true;
            _pendingTurn = 0f;
            ChangeState(IdleState);
        }

        public void ChangeState(MechState newState)
        {
            _currentState?.Exit();
            _currentState = newState;
            _currentState.Enter();
            // Enter 안에서 부스트 부족 등으로 다른 상태로 넘어갔으면 그쪽 호출이 이미 알렸다
            if (_currentState == newState) OnStateChanged?.Invoke(newState);
        }

        private void Update()
        {
            // 넉다운이 시작되면 어느 상태에서든 넉다운 상태로 (입력 무시, 속도는 KnockdownController가)
            if (IsKnockedDown && !(_currentState is KnockdownState)) ChangeState(KnockdownState);
            _currentState?.Update();
            AccumulateTurnInput();

            if (InputTrace.ShouldLog(this, "4 MechStateMachine"))
            {
                InputTrace.Log("4 MechStateMachine", $"{gameObject.name} state={_currentState?.GetType().Name ?? "없음"} " +
                    $"reader={(InputReader != null ? InputReader.gameObject.name : "없음")} " +
                    $"move={(InputReader != null ? InputReader.MoveInput.ToString() : "-")} enabled={enabled}");
            }
        }

        /// <summary>R스틱 좌우(패드는 기울기 × 초당 각도 × 시간, 마우스는 이동량 × 감도)를 몸 회전량으로 모은다.</summary>
        private void AccumulateTurnInput()
        {
            if (InputReader == null || !AllowsTurnInput)
            {
                _pendingTurn = 0f;
                return;
            }
            BernardTuning.MovementValues m = MechController.Tuning.Movement;
            float x = InputReader.LookInput.x;
            _pendingTurn += InputReader.IsLookFromGamepad ? x * m.bodyTurnSpeed * Time.deltaTime : x * m.mouseTurnSensitivity;
        }

        private void FixedUpdate()
        {
            // 모인 회전량을 한 번에. 이번 프레임에 물리 스텝이 없으면 다음 스텝으로 넘어간다
            if (_pendingTurn != 0f)
            {
                if (AllowsTurnInput) MechController.ApplyTurn(_pendingTurn);
                _pendingTurn = 0f;
            }
            _currentState?.FixedUpdate();
            // 상태가 끝난 뒤에 돌려야 상태 쪽 회전과 겹치지 않는다 (하드락 몸통 회전 중엔 R스틱 회전이 꺼져 있다)
            MechController.ApplyHardLockFacing();
        }
    }
}
