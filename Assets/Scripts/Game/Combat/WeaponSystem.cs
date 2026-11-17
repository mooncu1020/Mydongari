using System;
using UnityEngine;
using MyDongari.Bridge;
using MyDongari.Core;
using MyDongari.Game.Combat.Skills;
using MyDongari.Game.Flow;
using MyDongari.Game.Heat;
using MyDongari.Game.Input;
using MyDongari.Game.Tuning;

namespace MyDongari.Game.Combat
{
    /// <summary>근접 구역 밖에서 공격 버튼이 무엇을 할지. 버나드 계열은 Lunge.</summary>
    public enum RangedMode
    {
        Lunge,
        Projectile
    }

    public class WeaponSystem : MonoBehaviour
    {
        [Header("원거리 모드")]
        [Tooltip("Lunge = 스러스터 런지(버나드 계열, 원거리 무기 없음), Projectile = 투사체 사격(사격 기체용)")]
        [SerializeField] private RangedMode rangedMode = RangedMode.Lunge;

        [Header("무기 설정 (Projectile 모드)")]
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private Transform firePoint;
        [SerializeField] private float heatPerShot = 10f;
        [SerializeField] private float fireRate = 0.3f;

        private LockOnSystem _lockOnSystem;
        private HeatGauge _heatGauge;
        private MeleeSystem _meleeSystem;
        private AnchorClaw _anchorClaw;
        private ImpactSlam _impactSlam;
        private InputReader _inputReader;
        private MechIdentity _identity;
        private ThrusterLunge _thrusterLunge;
        private MechStateMachine _stateMachine;
        private float _fireTimer = 0f;
        private readonly AttackInputBuffer _attackBuffer = new AttackInputBuffer();
        // 이번 프레임의 공격 입력을 버퍼에서 지울지 (런지 쿨다운 중 입력, V9f)
        private bool _discardAttackInput;
        private TuningRef _tuning;

        /// <summary>입력 버퍼로(누른 프레임보다 뒤에) 발동한 공격. 로그용.</summary>
        public event Action<AttackType> OnBufferedAttack;

        private void Awake()
        {
            _lockOnSystem = GetComponent<LockOnSystem>();
            _heatGauge = GetComponent<HeatGauge>();
            _meleeSystem = GetComponent<MeleeSystem>();
            _anchorClaw = GetComponent<AnchorClaw>();
            _impactSlam = GetComponent<ImpactSlam>();
            _inputReader = GetComponent<InputReader>();
            _identity = MechIdentity.GetOrAdd(gameObject);
            _stateMachine = GetComponent<MechStateMachine>();
            _thrusterLunge = GetComponent<ThrusterLunge>();
            if (rangedMode == RangedMode.Lunge && _thrusterLunge == null)
            {
                // 씬에 안 붙여 둔 경우 기본값으로라도 동작하게. 수치 조정하려면 인스펙터에서 직접 추가할 것
                _thrusterLunge = gameObject.AddComponent<ThrusterLunge>();
            }
        }

        // 지난 틱에 공중이었는지 (착지 순간 버퍼를 지우려고)
        private bool _wasAirborne;
        private int _lastAttackPressCount;
        private int _lastSkill1PressCount;
        private int _lastSkill2PressCount;

        /// <summary>버튼을 지난 틱 뒤에 새로 눌렀는지 (누름 횟수로 — 틱과 화면 프레임이 어긋나도 놓치지 않는다).</summary>
        private static bool ConsumePress(int count, ref int last)
        {
            bool pressed = count != last;
            last = count;
            return pressed;
        }

        /// <summary>공격 버튼을 지난 틱 뒤에 새로 눌렀는지 (읽기만 — 기상 대기의 선입력·연타 판정용).</summary>
        public bool PeekAttackPressed() => _inputReader != null && _inputReader.AttackPressCount != _lastAttackPressCount;

        /// <summary>공격 입력을 버퍼에 넣는다 (기상 대기 끝의 선입력 공격).</summary>
        public void BufferAttackPress() => _attackBuffer.Press();

        /// <summary>히트스톱 중인 틱: 입력만 읽어 둔다 (공격 버튼은 버퍼에 넣되 버퍼 시간은 흐르지 않는다). 스킬 입력은 버린다.</summary>
        public void CombatTickFrozen()
        {
            if (ConsumePress(_inputReader.AttackPressCount, ref _lastAttackPressCount)) _attackBuffer.Press();
            ConsumePress(_inputReader.Skill1PressCount, ref _lastSkill1PressCount);
            ConsumePress(_inputReader.Skill2PressCount, ref _lastSkill2PressCount);
        }

        /// <summary>전투 틱마다 (MeleeSystem이 부른다): 스킬·공격 버튼의 새 입력을 읽고 공격을 고른다.</summary>
        public void CombatTick(bool roundActive)
        {
            bool attackPressed = ConsumePress(_inputReader.AttackPressCount, ref _lastAttackPressCount);
            bool skill1Pressed = ConsumePress(_inputReader.Skill1PressCount, ref _lastSkill1PressCount);
            bool skill2Pressed = ConsumePress(_inputReader.Skill2PressCount, ref _lastSkill2PressCount);

            if (!roundActive)
            {
                // 카운트다운·메뉴 중에 눌러 둔 입력이 FIGHT 순간 튀어나가지 않게
                _attackBuffer.Clear();
                return;
            }

            _fireTimer += 1f / CombatTime.TickRate;

            // 넉다운·경직·가드 경직 중엔 공격·스킬 입력을 전혀 처리하지 않는다 (버퍼에도 안 남김)
            if (_meleeSystem != null && (_meleeSystem.IsKnockedDown || _meleeSystem.IsStaggered || _meleeSystem.IsGuardStaggered))
            {
                _attackBuffer.Clear();
                return;
            }

            // 공중: 공격 = 내려찍기 (점프 중 아무 때나, 점프 한 번에 한 번 — 우리 규칙). 스킬은 공중에서 못 쓴다.
            // 지금 못 나가면(공중 대시 중 등) 선입력 4f 동안만 기억한다
            if (_stateMachine != null && _stateMachine.IsAirborne)
            {
                _wasAirborne = true;
                if (attackPressed) _attackBuffer.Press();
                if (_attackBuffer.Tick(StyleOverrides.Current.EffectiveCombat.meleeBufferFrames, TryStartDiveStrike, out AttackType dive, out bool diveBuffered) && diveBuffered)
                    OnBufferedAttack?.Invoke(dive);
                return;
            }
            // 착지 순간: 공중에서 예약한 입력은 지운다 (전투 사양서 8장)
            if (_wasAirborne)
            {
                _wasAirborne = false;
                _attackBuffer.Clear();
            }
            // 착지 단계(12f): 지상 공격만 즉시 받아 착지를 취소한다. 스킬·점프·대시는 착지가 끝난 뒤
            if (_stateMachine != null && _stateMachine.IsLanding)
            {
                HandleAttackButton(attackPressed);
                return;
            }

            if (skill1Pressed && _anchorClaw != null) _anchorClaw.TryActivate();
            if (skill2Pressed && _impactSlam != null) _impactSlam.TryActivate();

            HandleAttackButton(attackPressed);
        }

        /// <summary>
        /// 공격 버튼은 "누른 순간"에만 공격을 낸다(누르고 있기만 하면 아무것도 안 나감, GAME_SPEC 4장).
        /// 지금 못 나가면 입력 버퍼(4틱) 동안 기억했다가 가능해지는 틱에 발동하고,
        /// 무엇을 낼지는 그 틱 상황으로 다시 정한다.
        /// 예외: 사격 기체(Projectile 모드)의 원거리 사격만 누르고 있으면 연사 (버나드 로케 범위 밖).
        /// </summary>
        private void HandleAttackButton(bool attackPressed)
        {
            if (attackPressed) _attackBuffer.Press();

            if (rangedMode == RangedMode.Projectile && IsRangedContext())
            {
                if (_inputReader.AttackHeld) TryFire();
                _attackBuffer.Clear();
                return;
            }

            // 공격 선입력 = 원본의 근접 변환 창 4f
            int bufferTime = StyleOverrides.Current.EffectiveCombat.meleeBufferFrames;
            _discardAttackInput = false;
            bool startedAttack = _attackBuffer.Tick(bufferTime, TryStartAttack, out AttackType started, out bool fromBuffer);
            // V9f: 런지 쿨다운 중에 누른 공격은 버린다 — 버퍼에 남겨 두면 그 사이 상대가 6m 안에 들어왔을 때 펀치로 바뀌어 나간다
            if (_discardAttackInput) _attackBuffer.Clear();
            if (startedAttack && fromBuffer) OnBufferedAttack?.Invoke(started);
        }

        /// <summary>원거리 상황(파일드라이버·질주·하드 락온이 아님)인지.</summary>
        private bool IsRangedContext()
        {
            if (_anchorClaw != null && _anchorClaw.IsPileDriverReady) return false;
            if (_stateMachine != null && _stateMachine.IsDashing) return false;
            return _lockOnSystem == null || !_lockOnSystem.IsHardLocked;
        }

        /// <summary>
        /// 지금 상황으로 공격 버튼 한 번의 동작을 고르고 시작한다. 우선순위:
        /// ① 앵커 클로로 끌어온 직후 → 파일 드라이버, ② 대시 중 → 대시 공격(48f 미만) / 차징 태클(이상),
        /// ③ 근접 구역 A 안 → 펀치 콤보, ④ 그 밖 → 스러스터 런지. 공중의 공격 버튼은 내려찍기다(TryStartDiveStrike — CombatTick의 공중 갈래).
        /// 시작했으면 그 종류, 지금 못 하면(공격 중·런지 중·경직 등) null.
        /// </summary>
        private AttackType? TryStartAttack()
        {
            // 스턴 중엔 어떤 공격도 시작하지 않는다 (펀치·런지는 각자도 막지만 파일드라이버까지 한 곳에서)
            if (_meleeSystem != null && _meleeSystem.IsStunned) return null;
            // 대시 시작 단계(0.27초)와 끝 단계 처음 0.05초는 공격 불가 (V6b·V6.1) — 버퍼에 남았다가 가능해지면 나간다
            if (_stateMachine != null && _stateMachine.IsDashStopping && !_stateMachine.DashEndAllowsAttack) return null;
            if (_stateMachine != null && _stateMachine.IsDashing && _stateMachine.ActiveDash.IsInStartup) return null;
            // 점프 이륙 준비(2f) 중에 누른 공격은 버퍼에 남겨 둔다 — 뜨는 틱에 내려찍기가 된다 (런지 쿨다운 입력으로 버려지지 않게)
            if (_stateMachine != null && _stateMachine.IsJumpTakeoff) return null;

            if (_anchorClaw != null && _anchorClaw.IsPileDriverReady)
            {
                return _anchorClaw.TryPileDriver() ? AttackType.PileDriver : (AttackType?)null;
            }

            // 펀치 중에 누른 공격 = 다음 타 예약 (② 단계에서만 받는다. 구역을 벗어났어도 콤보는 이어진다)
            if (_meleeSystem != null && _meleeSystem.IsMeleeActive)
            {
                return _meleeSystem.TryMeleeAttack() ? AttackType.Punch : (AttackType?)null;
            }

            if (_stateMachine != null && _stateMachine.IsDashing)
            {
                // ② 대시 중 공격 버튼 = 48f 미만 유도 대시 어택 / 이상 차징 태클
                if (!_stateMachine.ActiveDash.RequestTackle()) return null;
                return _stateMachine.ActiveDash.IsTackleCharged ? AttackType.Tackle : AttackType.DashAttack;
            }

            // ③·④ 근접 구역 A(원본: 바깥 70u·안쪽 원 12.5u·원뿔 42.2°·높이차 창) 안이면 펀치, 밖이면 런지
            if (_meleeSystem != null && _meleeSystem.IsOpponentInMeleeZone())
            {
                return _meleeSystem != null && _meleeSystem.TryMeleeAttack() ? AttackType.Punch : (AttackType?)null;
            }

            if (rangedMode == RangedMode.Lunge && _thrusterLunge != null)
            {
                // V9f: 쿨다운 중 런지 입력은 무시 (버퍼에도 안 남김)
                if (_thrusterLunge.IsOnCooldown)
                {
                    _discardAttackInput = true;
                    _thrusterLunge.NotifyCooldownDenied();
                    // 진단: 붙어 있는 것 같은데 아무것도 안 나갔다면 이 줄이 남는다 (구역 밖 + 런지 쿨다운)
                    if (_meleeSystem != null) _meleeSystem.TraceAttackPress(PunchTrace.LungeCooldown);
                    return null;
                }
                if (!_thrusterLunge.TryLunge()) return null;
                // 진단: 펀치를 기대했는데 런지가 나갔다면 이 줄이 남는다 (구역 밖)
                if (_meleeSystem != null) _meleeSystem.TraceAttackPress(PunchTrace.ZoneOutLunge);
                return AttackType.Lunge;
            }
            return null;
        }

        /// <summary>공중의 공격 버튼: 내려찍기를 시작한다. 지금 못 하면 null.</summary>
        private AttackType? TryStartDiveStrike()
        {
            return _meleeSystem != null && _meleeSystem.TryDiveStrike() ? AttackType.DiveStrike : (AttackType?)null;
        }

        private void TryFire()
        {
            if (!_heatGauge.CanFire()) return;
            if (_fireTimer < fireRate) return;

            _fireTimer = 0f;
            _heatGauge.AddHeat(heatPerShot);

            // 화면 분할이라 조준 기준은 반드시 "내" 카메라
            Transform aimCamera = _identity.CameraTransform;
            // 카메라가 없으면 상체 방향 (G2)
            Vector3 fireDirection = aimCamera != null ? aimCamera.forward : MechFacing.AttackForward(transform);
            Transform target = _lockOnSystem != null ? _lockOnSystem.Target : null;

            GameObject proj = Instantiate(projectilePrefab, firePoint.position, Quaternion.identity);
            Projectile projectile = proj.GetComponent<Projectile>();
            if (projectile != null)
            {
                projectile.Init(fireDirection, target, aimCamera, _identity);
            }
        }
    }
}