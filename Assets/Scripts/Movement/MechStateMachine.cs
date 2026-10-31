using System;
using UnityEngine;
using MyDongari.Input;
using MyDongari.Mech;

namespace MyDongari.Movement
{
    /// <summary>
    /// 이동 상태 (V3): Idle(정지) / Run(항상 달리기, 하드락 안이면 근접 이동) / Dash(○, 8방향 고정, 땅·공중) / DashStop(대시 끝 미끄러짐) /
    /// Air(△ 점프·낙하) / Landing(착지 경직) / SideStep / Lunge / Knockdown.
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
        public DashState DashState { get; private set; }
        public DashStopState DashStopState { get; private set; }
        public AirState AirState { get; private set; }
        public LandingState LandingState { get; private set; }
        public SideStepState SideStepState { get; private set; }
        public LungeState LungeState { get; private set; }
        public KnockdownState KnockdownState { get; private set; }

        /// <summary>○ 대시 중 (끝 미끄러짐 제외). 가드 불가·히트 냉각 감소·호버 활주 외형의 기준.</summary>
        public bool IsDashing => _currentState is DashState;
        /// <summary>대시 끝 단계 중 (V6b: 0.27초 선형 감속, 공중 대시 끝 포함). 대시 불가, 점프는 내내·공격은 0.05초 뒤부터 (V6.1, 땅만).</summary>
        public bool IsDashStopping => _currentState is DashStopState;
        /// <summary>공중 (점프·낙하·공중 대시). 가드·패리·앵커 클로·공격 불가, 외형 접지 보정 끔. 대공 판정의 "공중"도 이것.</summary>
        public bool IsAirborne => _currentState is AirState ||
                                  (_currentState is DashState && DashState.IsAirborne) ||
                                  (_currentState is DashStopState && DashStopState.IsAirborne);
        /// <summary>착지 경직 중. 이동·공격·대시·점프·가드 불가.</summary>
        public bool IsLanding => _currentState is LandingState;
        /// <summary>점프 락온으로 몸을 상대 쪽으로 돌리는 0.2초 (카메라가 몸에 바로 붙는다).</summary>
        public bool IsJumpTurning => _currentState is AirState && AirState.IsJumpTurning;

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
        public bool CanStartLunge => _currentState is IdleState || _currentState is RunState ||
                                     (_currentState is DashStopState && DashStopState.AllowsAttack);

        /// <summary>
        /// 하드 락온 몸통 자동 회전을 허용하는 상태인지.
        /// 런지·넉다운은 진행 방향이 정해진 기동이라 몸을 돌리면 안 된다.
        /// 대시는 허용 — V3.1: 대시 방향은 고정이고 몸 정면은 따로라, 하드락이면 상대를 정면에 둔 채 옆으로 빠진다.
        /// </summary>
        public bool AllowsHardLockFacing =>
            !IsJumpTurning &&
            !(_currentState is LungeState) &&
            !(_currentState is KnockdownState);

        public string CurrentStateName => _currentState != null ? _currentState.GetType().Name : "None";

        // R스틱·마우스로 모은 몸 회전량(°). 입력은 Update에서, 회전은 물리 스텝에서 (마우스 델타가 프레임마다라 그대로 모아 둔다)
        private float _pendingTurn;

        /// <summary>
        /// R스틱 몸 회전을 받는 상태인지 (V3). 하드락 몸통 회전 중(하드락 안에서는 R스틱 꺼짐), 스턴, 외부 제어,
        /// 대시(카메라·정면 고정)·사이드스텝·런지·넉다운·공중 중엔 받지 않는다. 땅의 대시 끝 미끄러짐은 받는다.
        /// </summary>
        public bool AllowsTurnInput =>
            (_currentState is IdleState || _currentState is RunState ||
             (_currentState is DashStopState && !DashStopState.IsAirborne)) &&
            !IsStunned && !MechController.IsExternallyControlled && !MechController.IsHardLockFacing;

        private void Awake()
        {
            InputReader = GetComponent<InputReader>();
            MechController = GetComponent<MechController>();

            IdleState = new IdleState(this);
            RunState = new RunState(this);
            DashState = new DashState(this);
            DashStopState = new DashStopState(this);
            AirState = new AirState(this);
            LandingState = new LandingState(this);
            SideStepState = new SideStepState(this);
            LungeState = new LungeState(this);
            KnockdownState = new KnockdownState(this);
        }

        private void Start()
        {
            ChangeState(IdleState);
            // 공중에서 맞으면 그 자리에서 떨어져 착지 경직 (V5). 넉백은 수평만 지운다
            if (MechController.MeleeSystem != null) MechController.MeleeSystem.OnHitResolved += OnHitResolved;
        }

        private void OnDestroy()
        {
            if (MechController != null && MechController.MeleeSystem != null) MechController.MeleeSystem.OnHitResolved -= OnHitResolved;
        }

        private void OnHitResolved(Combat.HitInfo info, Combat.HitResult result)
        {
            if (!IsAirborne) return;
            if (result != Combat.HitResult.Hit && result != Combat.HitResult.GuardBroken) return;
            Rigidbody rb = MechController.Rb;
            rb.linearVelocity = new Vector3(0f, Mathf.Min(0f, rb.linearVelocity.y), 0f);
            if (_currentState is AirState) AirState.MarkLandingLag();
            else
            {
                // 공중 대시 중이었으면 대시를 끊고 떨어진다
                AirState.PrepareFallWithLandingLag();
                ChangeState(AirState);
            }
        }

        /// <summary>라운드 시작: 사망으로 꺼졌던 상태머신을 켜고 Idle로.</summary>
        public void ResetForRound()
        {
            enabled = true;
            _pendingTurn = 0f;
            ChangeState(IdleState);
        }

        /// <summary>
        /// ○를 이번 프레임에 눌렀으면 대시로 넘어간다(방향은 DashState가 누른 순간 L스틱으로 정함 — 뒤 대시가 예전 백대시를 대신한다).
        /// Idle·Run이 부른다. 넘어갔으면 true. 탈진(0%)·스턴이면 안 나간다.
        /// </summary>
        public bool TryStartDash()
        {
            if (InputReader == null || !InputReader.DashPressedThisFrame) return false;
            if (IsStunned || MechController.BoostGauge.IsExhausted) return false;
            // 공중에서 누르면 공중 대시 (V6c: 점프 상승 중에만. 같은 시간표, 상승 중 중력 절반)
            bool airborne = _currentState is AirState;
            if (airborne && !AirState.CanAirDash) return false;
            DashState.PrepareAirborne(airborne);
            ChangeState(DashState);
            return true;
        }

        /// <summary>
        /// △를 이번 프레임에 눌렀으면 점프 (V5). Idle·Run·대시 지속 단계·대시 끝 단계(땅)가 부른다. 부스트 jumpCost(15)를 한 번 쓰고, 탈진·스턴·부족이면 안 나간다.
        /// </summary>
        public bool TryStartJump()
        {
            if (InputReader == null || !InputReader.JumpPressedThisFrame) return false;
            BoostGauge boost = MechController.BoostGauge;
            if (IsStunned || boost.IsExhausted || !boost.TryConsume(boost.GetJumpCost())) return false;
            AirState.PrepareJump();
            ChangeState(AirState);
            return true;
        }

        /// <summary>땅을 coyoteTime보다 오래 못 밟으면(발판 끝에서 떨어짐) 공중으로. Idle·Run·DashStop이 부른다.</summary>
        public bool CheckFall()
        {
            if (MechController.IsGrounded || MechController.IsExternallyControlled) return false;
            if (MechController.TimeSinceGrounded < MechController.Tuning.Movement.coyoteTime) return false;
            ChangeState(AirState);
            return true;
        }

        /// <summary>
        /// 착지: 경직이 필요하면(점프·공중 대시·공중 피격) landingLag, 부스트 0%면 exhaustedLandingLag 동안 LandingState.
        /// 그냥 떨어진 착지는 경직 없이 바로 달리기·정지 (먼지·흔들림은 둘 다).
        /// </summary>
        public void Land(bool needsLag)
        {
            BernardTuning.MovementValues m = MechController.Tuning.Movement;
            BoostGauge boost = MechController.BoostGauge;
            bool exhausted = boost != null && (boost.CurrentBoost <= 0.01f || boost.IsExhausted);
            float duration = needsLag ? (exhausted ? m.exhaustedLandingLag : m.landingLag) : 0f;
            LandingState.Prepare(duration, exhausted && needsLag);
            if (duration > 0f) ChangeState(LandingState);
            else ChangeState(InputReader != null && InputReader.IsMoveInputActive ? (MechState)RunState : IdleState);
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
