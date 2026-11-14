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
    /// <summary>락온 밖(상대 6m 밖 또는 락온 없음)에서 공격 버튼이 무엇을 할지. 버나드 계열은 Lunge.</summary>
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

            // 넉다운 중엔 공격·스킬 입력을 무시한다 (버퍼에도 안 남김)
            if (_meleeSystem != null && _meleeSystem.IsKnockedDown)
            {
                _attackBuffer.Clear();
                return;
            }

            // V5: 공중·착지 경직 중엔 공격·스킬 입력 무시. 버퍼에도 안 남긴다
            if (_stateMachine != null && (_stateMachine.IsAirborne || _stateMachine.IsLanding))
            {
                _attackBuffer.Clear();
                return;
            }

            if (skill1Pressed && _anchorClaw != null) _anchorClaw.TryActivate();
            if (skill2Pressed && _impactSlam != null) _impactSlam.TryActivate();

            HandleAttackButton(attackPressed);
        }

        /// <summary>
        /// 공격 버튼은 "누른 순간"에만 공격을 낸다(누르고 있기만 하면 아무것도 안 나감, GAME_SPEC 4장).
        /// 지금 못 나가면 입력 버퍼(inputBuffer 초) 동안 기억했다가 가능해지는 프레임에 발동하고,
        /// 무엇을 낼지는 그 프레임 상황으로 다시 정한다.
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

            int bufferTime = CombatTime.Frames(_tuning.Get(this).Startup.inputBuffer);
            _discardAttackInput = false;
            bool startedAttack = _attackBuffer.Tick(bufferTime, TryStartAttack, out AttackType started, out bool fromBuffer);
            // V9f: 런지 쿨다운 중에 누른 공격은 버린다 — 버퍼에 남겨 두면 그 사이 상대가 6m 안에 들어왔을 때 펀치로 바뀌어 나간다
            if (_discardAttackInput) _attackBuffer.Clear();
            if (startedAttack)
            {
                if (fromBuffer) OnBufferedAttack?.Invoke(started);
                // V8: 공격을 쓰면 "사격 조준" — 15f 동안 몸을 상대 쪽으로 돌린다 (대시 중 공격은 대시가 지속 조준)
                MechController controller = _stateMachine != null ? _stateMachine.MechController : null;
                if (controller != null) controller.NotifyAttackStarted();
            }
        }

        /// <summary>원거리 상황(파일드라이버·질주·하드 락온이 아님)인지.</summary>
        private bool IsRangedContext()
        {
            if (_anchorClaw != null && _anchorClaw.IsPileDriverReady) return false;
            if (_stateMachine != null && _stateMachine.IsDashing) return false;
            return _lockOnSystem == null || !_lockOnSystem.IsHardLocked;
        }

        /// <summary>
        /// 지금 상황으로 공격 버튼 한 번의 동작을 고르고 시작한다. 우선순위 (GAME_SPEC_V3 "공격" 표):
        /// 1 앵커 클로로 끌어온 직후 → 파일 드라이버, 2 내가 공중 → 유도 내려찍기(6단계), 3 상대 공중 + 6m 안 → 대공 어퍼컷(6단계),
        /// 4 대시 0.8초 미만 → 유도 대시 어택, 5 대시 0.8초 이상 → 차징 태클, 6 상대 6m 안 → 펀치 콤보, 7 그 외 → 스러스터 런지.
        /// 시작했으면 그 종류, 지금 못 하면(쿨다운·발생 중·런지 중·스턴 등) null.
        /// </summary>
        private AttackType? TryStartAttack()
        {
            // 스턴 중엔 어떤 공격도 시작하지 않는다 (펀치·런지는 각자도 막지만 파일드라이버까지 한 곳에서)
            if (_meleeSystem != null && _meleeSystem.IsStunned) return null;
            // 대시 시작 단계(0.27초)와 끝 단계 처음 0.05초는 공격 불가 (V6b·V6.1) — 버퍼에 남았다가 가능해지면 나간다
            if (_stateMachine != null && _stateMachine.IsDashStopping && !_stateMachine.DashEndAllowsAttack) return null;
            if (_stateMachine != null && _stateMachine.IsDashing && _stateMachine.ActiveDash.IsInStartup) return null;

            if (_anchorClaw != null && _anchorClaw.IsPileDriverReady)
            {
                return _anchorClaw.TryPileDriver() ? AttackType.PileDriver : (AttackType?)null;
            }

            // 2) 내가 공중 → 유도 내려찍기(6단계, 지금은 공중 입력을 위 Update에서 무시), 3) 상대가 공중 + 6m 안 → 대공 어퍼컷(6단계): 자리만

            if (_stateMachine != null && _stateMachine.IsDashing)
            {
                // 4·5) 대시 중 공격 버튼 = 0.8초 미만 유도 대시 어택 / 이상 차징 태클
                if (!_stateMachine.ActiveDash.RequestTackle()) return null;
                return _stateMachine.ActiveDash.IsTackleCharged ? AttackType.Tackle : AttackType.DashAttack;
            }

            // 6·7) V3: 펀치/런지 전환은 하드 락온(10m)과 따로 meleeSwitchRange(6m). 6~10m는 하드락 안이어도 런지
            if (IsOpponentInMeleeRange())
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
                    return null;
                }
                return _thrusterLunge.TryLunge() ? AttackType.Lunge : (AttackType?)null;
            }
            return null;
        }

        /// <summary>상대가 펀치 전환 거리 안인지 (기체 기준 3D 거리).</summary>
        private bool IsOpponentInMeleeRange()
        {
            MechIdentity opponent = _identity != null ? _identity.Opponent : null;
            if (opponent == null || !opponent.gameObject.activeInHierarchy) return false;
            float range = _tuning.Get(this).LockOn.meleeSwitchRange;
            return Vector3.Distance(transform.position, opponent.transform.position) <= range;
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