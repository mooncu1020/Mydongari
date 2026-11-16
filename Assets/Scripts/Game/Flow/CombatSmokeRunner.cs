using System;
using System.Text;
using UnityEngine;
using MyDongari.Bridge;
using MyDongari.Core;
using MyDongari.Game.Bot;
using MyDongari.Game.Combat;
using MyDongari.Game.Input;

namespace MyDongari.Game.Flow
{
    /// <summary>
    /// 전투 스모크 테스트 (메뉴 Tools/Combat Smoke Test가 빌드를 -combatSmoke로 실행하면 스스로 생긴다).
    /// PRACTICE 모드에서 플레이어 자리에 자동 입력(InputReader 스크립트 입력)을 넣고 연습 봇을 상대로 장면(PracticeScene)을 차례로 돌린다:
    /// 원투쓰리 / 가드에 펀치 / 동시 펀치(맞딜) / 런지 / 대시 공격 / 밀착 원투쓰리(0.8m, 40° 비스듬히 — 3타 모두 명중해야 한다)
    /// / 내려찍기 셋(정점에서 명중 · 하강 중 명중 · 상대가 옆 대시로 피해 빗나감 → 착지 경직 30f).
    /// 이어서 봇 장면 세 개(P1은 가만히 또는 가끔 펀치): 공격만 모드 12m / 패리 모드 3m / 섞어서 모드 6m — 봇이 이동기로 다가가 공격하는지 본다.
    /// 그다음 VERSUS 모드로 바꿔 1P·2P 둘 다 자동 입력인 장면(VersusSmokeScenes)을 60fps로 한 번, 30fps로 한 번 돌리고 두 결과가 같은지 본다.
    /// 장면마다 두 기체를 리셋하고 정해진 거리(기본 3m, 런지·대시는 그보다 멀리)에 마주 세운다.
    /// 끝나면 경직·다운·날아감·가드 경직·맞딜 발생 횟수, 예외·오류 로그 수, NaN, 두 기체가 겹친 틱 수, 맵 밖 이탈을 로그로 남기고 종료한다.
    /// 반응 횟수가 하나라도 0이거나 예외·NaN·맵 밖 이탈이 있으면 실패. 시간은 전부 물리 스텝(= 전투 틱) 수로 센다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class CombatSmokeRunner : MonoBehaviour
    {
        /// <summary>이 인자로 실행하면 전투 스모크 테스트를 돌린다.</summary>
        public const string Argument = "-combatSmoke";
        /// <summary>결과 로그 머리말 (에디터 쪽이 Player.log에서 이 줄들을 찾는다).</summary>
        public const string LogTag = "[CombatSmoke]";

        /// <summary>PRACTICE 장면. 도는 순서는 SceneOrder.</summary>
        private enum PracticeScene
        {
            Combo, Guard, Clash, Lunge, DashAttack,
            /// <summary>밀착 원투쓰리: 0.8m에 붙여 세우고 P1을 40° 비스듬히 돌린다 (예전 판정 구로는 빗나가던 배치).</summary>
            CloseCombo,
            /// <summary>내려찍기: 정점에서 눌러 명중 / 하강 중에 눌러 명중 / 봇이 옆 대시로 피해 빗나감.</summary>
            DiveApex, DiveFalling, DiveEvade,
            BotApproach, BotParry, BotMixed
        }

        private static readonly PracticeScene[] SceneOrder =
        {
            PracticeScene.Combo, PracticeScene.Guard, PracticeScene.Clash, PracticeScene.Lunge, PracticeScene.DashAttack,
            PracticeScene.CloseCombo, PracticeScene.DiveApex, PracticeScene.DiveFalling, PracticeScene.DiveEvade,
            PracticeScene.BotApproach, PracticeScene.BotParry, PracticeScene.BotMixed
        };

        // 장면 하나의 기본 길이(틱)
        private const int SceneTicks = 200;
        private static int TicksOf(PracticeScene scene) => SceneTicks;

        private const float BotApproachDistance = 12f;
        private const float BotMixedDistance = 6f;
        // 기본 거리(m)와 런지·대시 공격을 거는 거리(근접 구역 6.1m 밖)
        private const float CloseDistance = 3f;
        // 밀착 장면: 거리(m)와 P1을 상대에게서 돌려 세우는 각(°)
        private const float TouchDistance = 0.8f;
        private const float TouchAngle = 40f;
        // 내려찍기 장면: 명중 장면 거리 / 피하기 장면 거리(m), 하강 중 장면은 정점 뒤 이 틱에 누른다, 봇은 예비를 본 뒤 이 틱에 옆 대시
        private const float DiveHitDistance = 3f;
        private const float DiveEvadeDistance = 8f;
        private const int DiveFallingDelay = 20;
        private const int DiveEvadeReaction = 5;
        private const float LungeDistance = 9f;
        private const float DashDistance = 10f;
        // 시작한 자리에서 이만큼 벗어나거나 떨어지면 맵 밖
        private const float MaxHorizontal = 80f;
        private const float MaxFall = 5f;
        // 준비(모드 전환·카운트다운)를 이 틱 안에 못 끝내면 실패
        private const int ReadyTimeoutTicks = 900;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateIfRequested()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), Argument) < 0) return;
            var go = new GameObject("CombatSmokeRunner");
            DontDestroyOnLoad(go);
            go.AddComponent<CombatSmokeRunner>();
        }

        private GameManager _gm;
        private GameObject _p1;
        private GameObject _bot;
        private InputReader _reader;
        private MeleeSystem _p1Melee;
        private MeleeSystem _botMelee;
        private MeleeDummy _botBrain;
        // 봇의 가상 패드 (봇 두뇌가 Idle이면 아무것도 안 누르므로 장면이 대신 누른다)
        private InputReader _botPad;
        private Rigidbody _p1Rb;
        private Rigidbody _botRb;

        // VERSUS 단계: 0 = 아직(PRACTICE 중), 1 = 60fps 준비·진행, 2 = 30fps 준비·진행, 3 = 끝
        private int _versusStage;
        private int _versusWait;
        private VersusSmokeScenes _versus60;
        private VersusSmokeScenes _versus30;
        private VersusSmokeScenes _versusActive;
        private bool _practiceDone;
        private const int VersusFps = 60;
        private const int VersusSlowFps = 30;

        private bool _ready;
        private bool _finished;
        private bool _modeRequested;
        private int _waitTicks;
        private int _tick;
        private int _sceneIndex = -1;
        private PracticeScene _scene;
        private int _sceneTick;
        private bool _sceneDone;
        private bool IsBotScene => _sceneIndex >= 0 && _scene >= PracticeScene.BotApproach && _scene <= PracticeScene.BotMixed;
        // 밀착 원투쓰리 장면에서 명중한 콤보 단계 (비트 1·2·4)
        private int _closeComboHits;
        private bool _closeComboRan;

        /// <summary>내려찍기 장면 하나의 결과.</summary>
        private class DiveResult
        {
            public bool Ran;
            public bool Started;
            public float VyAtPress;
            public bool Landed;
            public bool Connected;
            public int LagFrames;
            public int LandingTicks;
            public string BotResult = "-";
            public int P1StopTicks, BotStopTicks;
            // 피하기 장면: 봇이 대시를 냈는지, 내리꽂는 동안 두 기체 중심의 가장 가까운 거리(m)
            public bool BotDashed;
            public float Closest = float.MaxValue;
        }
        private readonly DiveResult[] _dive = { new DiveResult(), new DiveResult(), new DiveResult() };
        private DiveResult Dive => _dive[_scene - PracticeScene.DiveApex];
        private bool IsDiveScene => _sceneIndex >= 0 && _scene >= PracticeScene.DiveApex && _scene <= PracticeScene.DiveEvade;
        private int _apexTick, _diveSeenTick;
        private bool _divePressed;
        private MechStateMachine _p1Sm;
        private Vector3 _origin;
        private Vector3 _forward;

        // 결과
        private int _stagger, _down, _blown, _guardStagger, _clash, _invulnOnly, _hits;
        private int _exceptions, _nanTicks, _overlapTicks, _outOfMapTicks;
        // 원투쓰리 장면이 어떤 반응으로 끝났는지 (다운이어야 한다)
        private Reaction _comboEnd = Reaction.None;
        // 봇 장면에서 본 봇 상태·행동 (장면별)
        private readonly System.Collections.Generic.HashSet<string>[] _botStates =
        {
            new System.Collections.Generic.HashSet<string>(), new System.Collections.Generic.HashSet<string>(), new System.Collections.Generic.HashSet<string>()
        };
        private readonly System.Collections.Generic.HashSet<string>[] _botOrders =
        {
            new System.Collections.Generic.HashSet<string>(), new System.Collections.Generic.HashSet<string>(), new System.Collections.Generic.HashSet<string>()
        };
        private int _botParries;
        private int _botHitsLanded;
        private readonly StringBuilder _firstProblems = new StringBuilder();

        private void OnEnable()
        {
            Application.logMessageReceived += OnLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLog;
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
            _exceptions++;
            if (_exceptions <= 3) _firstProblems.Append(" | ").Append(condition.Length > 160 ? condition.Substring(0, 160) : condition);
        }

        // ---------------- 준비 ----------------

        private void FixedUpdate()
        {
            if (_finished) return;
            if (!_ready)
            {
                _waitTicks++;
                if (TryGetReady()) return;
                if (_waitTicks > ReadyTimeoutTicks) Finish("PRACTICE 모드를 시작하지 못했습니다 (연습 봇이 씬에 없거나 라운드가 시작되지 않음)");
                return;
            }

            if (_practiceDone)
            {
                TickVersus();
                return;
            }
            Measure();
            if (_sceneIndex < 0 || _sceneTick >= TicksOf(_scene))
            {
                if (_sceneIndex + 1 >= SceneOrder.Length)
                {
                    _practiceDone = true;
                    _reader.SetScripted(false);
                    return;
                }
                BeginScene(_sceneIndex + 1);
            }
            RunScene();
            if (_sceneTick % 40 == 39) LogSceneState();
            _sceneTick++;
            _tick++;
        }

        /// <summary>PRACTICE 모드로 들어가 라운드가 시작될 때까지 기다린다. 준비가 끝난 틱에 true.</summary>
        private bool TryGetReady()
        {
            _gm = GameManager.Instance;
            if (_gm == null) return false;
            if (!_modeRequested)
            {
                if (!_gm.CanStartPractice) return false;
                _modeRequested = true;
                _gm.StartMode(GameMode.Practice);
                return false;
            }
            if (_gm.Mode != GameMode.Practice || !_gm.IsRoundActive) return false;

            _p1 = _gm.Player1;
            _bot = _gm.PracticeOpponent;
            _botBrain = _gm.PracticeBot;
            if (_p1 == null || _bot == null || _botBrain == null) return false;
            _reader = _p1.GetComponent<InputReader>();
            _p1Melee = _p1.GetComponent<MeleeSystem>();
            _botMelee = _bot.GetComponent<MeleeSystem>();
            _botPad = _bot.GetComponent<InputReader>();
            _p1Rb = _p1.GetComponent<Rigidbody>();
            _botRb = _bot.GetComponent<Rigidbody>();
            if (_reader == null || _p1Melee == null || _botMelee == null || _botPad == null || _p1Rb == null || _botRb == null) return false;

            _reader.SetScripted(true);
            _p1Melee.OnHitResolved += OnHit;
            _botMelee.OnHitResolved += OnHit;
            _p1Melee.OnPunchTraced += OnP1PunchTraced;
            _p1Melee.OnDiveStrikeLanded += OnP1DiveLanded;
            _p1Sm = _p1.GetComponent<MechStateMachine>();
            _ready = true;
            Debug.Log($"{LogTag} 시작: P1 {_p1.name} vs 봇 {_bot.name}, {SceneOrder.Length}장면");
            return true;
        }

        /// <summary>P1 펀치 진단 한 줄 (왜 안 맞았는지 로그만 보고 가릴 수 있게).</summary>
        private void OnP1PunchTraced(PunchTrace trace)
        {
            if (_sceneIndex < 0 || _practiceDone) return;
            Debug.Log($"{LogTag} 장면 {_scene} t{_sceneTick} P1 펀치 진단: {trace}");
        }

        private void OnP1DiveLanded(bool connected, int lagFrames)
        {
            if (!IsDiveScene || _practiceDone) return;
            Dive.Landed = true;
            Dive.Connected = connected;
            Dive.LagFrames = lagFrames;
        }

        private void OnHit(HitInfo info, HitResult result)
        {
            if (IsDiveScene && info.Attacker == _p1 && info.Type == AttackType.DiveStrike) Dive.BotResult = $"{result}/{info.Reaction}";
            // 봇 장면: P1이 때린 걸 봇이 패리 / 봇이 때려 P1에게 닿음
            if (IsBotScene && result == HitResult.Parried && info.Attacker == _p1) _botParries++;
            if (IsBotScene && info.Attacker == _bot && (result == HitResult.Hit || result == HitResult.Guarded)) _botHitsLanded++;
            if (_scene == PracticeScene.CloseCombo && info.Attacker == _p1 && info.Type == AttackType.Punch && result == HitResult.Hit && info.ComboStep >= 1)
                _closeComboHits |= 1 << (info.ComboStep - 1);
            if (result == HitResult.Clash)
            {
                _clash++;
                return;
            }
            if (result == HitResult.Hit || result == HitResult.Guarded || result == HitResult.GuardBroken) _hits++;
            switch (info.Reaction)
            {
                case Reaction.Stagger: _stagger++; break;
                case Reaction.Down:
                case Reaction.AirDown: _down++; break;
                case Reaction.Blown: _blown++; break;
                case Reaction.GuardStagger: _guardStagger++; break;
                case Reaction.InvulnOnly: _invulnOnly++; break;
            }
            // 넘어뜨렸으면 이 장면의 공격은 끝 (누운 상대에게 계속 휘두르지 않게)
            if (info.Reaction == Reaction.Down || info.Reaction == Reaction.AirDown || info.Reaction == Reaction.Blown)
            {
                _sceneDone = true;
                if (_scene == PracticeScene.Combo && _comboEnd == Reaction.None) _comboEnd = info.Reaction;
            }
        }

        // ---------------- 장면 ----------------

        private void BeginScene(int index)
        {
            _sceneIndex = index;
            _scene = SceneOrder[index];
            _sceneTick = 0;
            _sceneDone = false;
            switch (_scene)
            {
                case PracticeScene.BotApproach: Arrange(BotApproachDistance, BotMode.Approach); break;
                case PracticeScene.BotParry: Arrange(CloseDistance, BotMode.Parrier); break;
                case PracticeScene.BotMixed: Arrange(BotMixedDistance, BotMode.Mixed); break;
                case PracticeScene.Guard: Arrange(CloseDistance, BotMode.AlwaysGuard); break;
                case PracticeScene.Lunge: Arrange(LungeDistance, BotMode.Idle); break;
                case PracticeScene.DashAttack: Arrange(DashDistance, BotMode.Idle); break;
                case PracticeScene.DiveApex:
                case PracticeScene.DiveFalling:
                case PracticeScene.DiveEvade:
                    Arrange(_scene == PracticeScene.DiveEvade ? DiveEvadeDistance : DiveHitDistance, BotMode.Idle);
                    Dive.Ran = true;
                    _apexTick = -1;
                    _diveSeenTick = -1;
                    _divePressed = false;
                    break;
                case PracticeScene.CloseCombo:
                    // 붙어서, 상대를 정면에서 40° 벗어나게 두고 친다 — 안쪽 원이라 추적 회전도 없다
                    Arrange(TouchDistance, BotMode.Idle, TouchAngle);
                    _closeComboRan = true;
                    break;
                default: Arrange(CloseDistance, BotMode.Idle); break;
            }
        }

        /// <summary>두 기체를 라운드 시작 상태로 되돌리고, P1 스폰 자리에서 정면으로 distance(m) 떨어뜨려 마주 세운다.</summary>
        private void Arrange(float distance, BotMode mode, float p1YawOffset = 0f)
        {
            _reader.ScriptState(Vector2.zero, false, false, false);
            _gm.ResetPracticeNow();
            if (_sceneIndex == 0 || _forward == Vector3.zero)
            {
                _origin = _p1Rb.position;
                Vector3 forward = _p1Rb.rotation * Vector3.forward;
                forward.y = 0f;
                _forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            }
            float botY = _botRb.position.y;
            Vector3 botPosition = _origin + _forward * distance;
            botPosition.y = botY;
            Place(_p1, _p1Rb, _origin, Quaternion.Euler(0f, p1YawOffset, 0f) * Quaternion.LookRotation(_forward, Vector3.up));
            Place(_bot, _botRb, botPosition, Quaternion.LookRotation(-_forward, Vector3.up));
            _botBrain.SetMode(mode);
        }

        /// <summary>
        /// 기체를 옮긴다. 리셋이 방금 트랜스폼을 스폰 자리로 옮겨 놨으므로 트랜스폼도 같이 옮겨야 한다
        /// (리지드바디만 옮기면 다음 물리 스텝에 트랜스폼 값으로 되돌아간다).
        /// </summary>
        private static void Place(GameObject mech, Rigidbody rb, Vector3 position, Quaternion rotation)
        {
            mech.transform.SetPositionAndRotation(position, rotation);
            MotionBridge.Teleport(rb, position, rotation);
        }

        private void RunScene()
        {
            int t = _sceneTick;
            switch (_scene)
            {
                case PracticeScene.Combo:
                case PracticeScene.CloseCombo:
                    // 원투쓰리: 공격을 3틱마다 눌러 ② 단계에 걸리게 한다 (① 중의 입력은 4f 버퍼 뒤 버려진다)
                    if (!_sceneDone && t >= 20 && t <= 170 && t % 3 == 0) _reader.ScriptPressAttack();
                    break;
                case PracticeScene.Guard:
                    // 가드: 봇이 가드를 올린 뒤(패리 창 9f가 지난 뒤) 펀치 한 번
                    if (t == 60) _reader.ScriptPressAttack();
                    break;
                case PracticeScene.Clash:
                    // 맞딜: 같은 틱에 둘 다 펀치
                    if (t == 30)
                    {
                        _reader.ScriptPressAttack();
                        _botPad.ScriptPressAttack();
                    }
                    break;
                case PracticeScene.Lunge:
                    // 런지: 근접 구역 밖에서 공격
                    if (t == 20) _reader.ScriptPressAttack();
                    break;
                case PracticeScene.DashAttack:
                    // 대시 공격: 앞 대시(○ + 스틱 앞) → 지속 단계에서 공격
                    if (t == 10)
                    {
                        _reader.ScriptState(Vector2.up, true, false, false);
                        _reader.ScriptPressDash();
                    }
                    if (!_sceneDone && t >= 28 && t <= 60 && t % 3 == 0) _reader.ScriptPressAttack();
                    if (t == 90) _reader.ScriptState(Vector2.zero, false, false, false);
                    break;
                case PracticeScene.BotApproach:
                case PracticeScene.BotMixed:
                    // 봇 장면: P1은 가만히 (봇이 다가와 공격). 섞어서 장면은 P1이 가끔 펀치를 내 봇의 대응을 본다
                    if (_scene == PracticeScene.BotMixed && t % 50 == 25) _reader.ScriptPressAttack();
                    RecordBot();
                    break;
                case PracticeScene.BotParry:
                    // 패리 장면: P1이 일정 간격으로 펀치
                    if (t % 60 == 20) _reader.ScriptPressAttack();
                    RecordBot();
                    break;
                case PracticeScene.DiveApex:
                case PracticeScene.DiveFalling:
                case PracticeScene.DiveEvade:
                    RunDiveScene(t);
                    break;
            }
        }

        /// <summary>
        /// 내려찍기 장면: △로 뛰고, 정점(상승이 끝나 하강으로 바뀐 첫 틱) 또는 그 뒤 DiveFallingDelay틱에 공격을 누른다.
        /// 피하기 장면은 봇이 P1의 예비 동작을 본 뒤 DiveEvadeReaction틱에 옆 대시를 낸다 (봇 두뇌는 Idle — 장면이 가상 패드를 대신 누른다.
        /// 봇이 틱마다 패드를 떼므로 누르고 있는 값은 틱마다 다시 넣는다).
        /// </summary>
        private void RunDiveScene(int t)
        {
            DiveResult r = Dive;
            if (t == 20)
            {
                _reader.ScriptState(Vector2.zero, false, true, false);
                _reader.ScriptPressJump();
            }
            if (t == 30) _reader.ScriptState(Vector2.zero, false, false, false);

            bool falling = _p1Sm.CurrentState is Core.VirtualOn.States.VoJumpState jump && jump.IsAirState && !jump.IsRising;
            if (falling && _apexTick < 0 && t > 20) _apexTick = t;
            int pressAt = _scene == PracticeScene.DiveFalling ? _apexTick + DiveFallingDelay : _apexTick;
            if (_apexTick >= 0 && !_divePressed && t >= pressAt)
            {
                _divePressed = true;
                r.VyAtPress = _p1Sm.Motor.Vy;
                _reader.ScriptPressAttack();
            }
            if (_p1Sm.IsDiveStrike)
            {
                r.Started = true;
                if (_diveSeenTick < 0) _diveSeenTick = t;
            }
            if (_p1Sm.IsDiveStrike) r.Closest = Mathf.Min(r.Closest, Vector3.Distance(_p1Rb.position, _botRb.position));
            MechStateMachine botSm = _bot.GetComponent<MechStateMachine>();
            if (botSm != null && botSm.IsDashing) r.BotDashed = true;
            if (_p1Sm.IsLanding) r.LandingTicks++;
            if (_p1Melee.IsHitStopped) r.P1StopTicks++;
            if (_botMelee.IsHitStopped) r.BotStopTicks++;

            if (_scene == PracticeScene.DiveEvade && _diveSeenTick >= 0)
            {
                int since = t - _diveSeenTick;
                if (since == DiveEvadeReaction) _botPad.ScriptPressDash();
                bool hold = since >= DiveEvadeReaction && since < DiveEvadeReaction + 40;
                _botPad.ScriptState(hold ? Vector2.right : Vector2.zero, hold, false, false);
            }
        }

        private void RecordBot()
        {
            int i = _scene - PracticeScene.BotApproach;
            MechStateMachine sm = _bot.GetComponent<MechStateMachine>();
            if (sm != null) _botStates[i].Add(sm.CurrentStateName.Replace("State", "").Replace("Vo", ""));
            _botOrders[i].Add(_botBrain.CurrentOrder.ToString());
        }

        /// <summary>장면 진행 상황 한 줄 (실패 원인을 로그만 보고 찾을 수 있게): 거리, P1 상태, 근접 구역, 두 기체 체력.</summary>
        private void LogSceneState()
        {
            MechStateMachine sm = _p1.GetComponent<MechStateMachine>();
            Health h1 = _p1.GetComponent<Health>();
            Health h2 = _bot.GetComponent<Health>();
            Vector3 offset = _botRb.position - _p1Rb.position;
            float dy = offset.y;
            offset.y = 0f;
            Debug.Log($"{LogTag} 장면 {_sceneIndex + 1} {_scene} t{_sceneTick + 1}: 거리 {offset.magnitude:0.00}m 높이차 {dy:0.00}m, P1 {(sm != null ? sm.CurrentStateName : "-")}" +
                      $" 구역 {_p1Melee.IsOpponentInMeleeZone()} 펀치중 {_p1Melee.IsMeleeActive} 경직 {_p1Melee.IsStunned}, 봇 펀치중 {_botMelee.IsMeleeActive} 가드 {_botMelee.IsGuarding}" +
                      $" 넉다운 {_botMelee.IsKnockedDown}, 체력 {(h1 != null ? h1.CurrentHealth : -1f):0.0}/{(h2 != null ? h2.CurrentHealth : -1f):0.0}");
        }

        // ---------------- VERSUS ----------------

        /// <summary>PRACTICE 장면이 끝난 뒤: VERSUS로 바꾸고 60fps → 30fps로 같은 장면을 돌린다.</summary>
        private void TickVersus()
        {
            if (_versusStage == 0)
            {
                if (!_gm.CanStartVersus)
                {
                    Finish("VERSUS를 시작할 수 없습니다 (2인 기체 없음)");
                    return;
                }
                _versusStage = 1;
                BeginVersusStage(VersusFps);
                return;
            }
            if (_versusActive == null)
            {
                // 라운드 시작(카운트다운)을 기다린다
                _versusWait++;
                if (_versusWait > ReadyTimeoutTicks)
                {
                    Finish("VERSUS 라운드가 시작되지 않았습니다");
                    return;
                }
                if (_gm.Mode != GameMode.Versus || !_gm.IsRoundActive) return;
                var scenes = new VersusSmokeScenes();
                if (!scenes.Setup(_gm, _versusStage == 1 ? VersusFps + "fps" : VersusSlowFps + "fps"))
                {
                    Finish("VERSUS 기체 부품을 찾지 못했습니다");
                    return;
                }
                _versusActive = scenes;
                if (_versusStage == 1) _versus60 = scenes;
                else _versus30 = scenes;
                Debug.Log($"{LogTag} VERSUS {scenes.Label} 시작: {VersusSmokeScenes.SceneCount}장면 × {VersusSmokeScenes.SceneTicks}틱");
                return;
            }
            _versusActive.Tick();
            Measure();
            if (!_versusActive.Done) return;
            _versusActive.Release();
            _versusActive = null;
            if (_versusStage == 1)
            {
                _versusStage = 2;
                BeginVersusStage(VersusSlowFps);
                return;
            }
            _versusStage = 3;
            Finish(null);
        }

        private void BeginVersusStage(int fps)
        {
            // 화면 프레임만 바꾼다 (전투 틱은 60Hz 고정). 30fps면 한 프레임에 물리 스텝 두 번
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = fps;
            _versusWait = 0;
            _gm.StartMode(GameMode.Versus);
        }

        // ---------------- 측정 ----------------

        private void Measure()
        {
            // VERSUS 단계에선 지금 상대(2P)로 잰다
            GameObject other = _practiceDone && _gm != null ? _gm.Player2 : _bot;
            Rigidbody otherRb = other != null ? other.GetComponent<Rigidbody>() : null;
            if (otherRb == null) return;
            Vector3 a = _p1Rb.position;
            Vector3 b = otherRb.position;
            if (!IsFinite(a) || !IsFinite(b))
            {
                _nanTicks++;
                return;
            }
            if (IsOutOfMap(a) || IsOutOfMap(b)) _outOfMapTicks++;

            // 겹침: 두 캡슐의 수평 거리가 반지름 합보다 1cm 넘게 작고 높이도 겹친다
            float ra = RadiusOf(_p1), rb = RadiusOf(other);
            Vector3 flat = a - b;
            float dy = Mathf.Abs(flat.y);
            flat.y = 0f;
            if (flat.magnitude < ra + rb - 0.01f && dy < HeightOf(_p1) * 0.5f + HeightOf(other) * 0.5f) _overlapTicks++;
        }

        private bool IsOutOfMap(Vector3 position)
        {
            Vector3 offset = position - _origin;
            if (offset.y < -MaxFall) return true;
            offset.y = 0f;
            return offset.magnitude > MaxHorizontal;
        }

        /// <summary>내려찍기 세 장면의 검사. 문제가 없으면 null.</summary>
        private string DiveProblems()
        {
            var sb = new StringBuilder();
            int missLag = StyleOverrides.Current.diveStrikeMissLandingFrames;
            int hitLag = StyleOverrides.Current.Effective.landingFrames;
            string[] names = { "정점", "하강 중", "피함" };
            for (int i = 0; i < _dive.Length; i++)
            {
                DiveResult d = _dive[i];
                bool evade = i == 2;
                if (!d.Ran || !d.Started) { sb.Append($" [{names[i]}: 내려찍기가 안 나감]"); continue; }
                if (!d.Landed) { sb.Append($" [{names[i]}: 착지를 못 함]"); continue; }
                if (evade)
                {
                    if (!d.BotDashed) sb.Append($" [{names[i]}: 봇이 옆 대시를 못 냄]");
                    if (d.Connected || d.BotResult != "-") sb.Append($" [{names[i]}: 빗나가야 하는데 {d.BotResult}]");
                    if (d.LagFrames != missLag || d.LandingTicks != missLag) sb.Append($" [{names[i]}: 착지 경직 {d.LagFrames}f(잰 값 {d.LandingTicks}) ≠ {missLag}]");
                }
                else
                {
                    if (!d.Connected || !d.BotResult.StartsWith("Hit/Down")) sb.Append($" [{names[i]}: 명중·다운이어야 하는데 {d.BotResult}]");
                    if (d.LagFrames != hitLag || d.LandingTicks != hitLag) sb.Append($" [{names[i]}: 착지 경직 {d.LagFrames}f(잰 값 {d.LandingTicks}) ≠ {hitLag}]");
                    if (d.P1StopTicks == 0 || d.BotStopTicks == 0) sb.Append($" [{names[i]}: 양쪽 히트스톱이어야 함 (P1 {d.P1StopTicks} / 봇 {d.BotStopTicks})]");
                }
            }
            return sb.Length > 0 ? sb.ToString().Trim() : null;
        }

        private string Mark(int bit) => (_closeComboHits & bit) != 0 ? "명중" : "빗나감";

        private static bool IsFinite(Vector3 v) =>
            !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
            !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);

        private static float RadiusOf(GameObject mech)
        {
            CapsuleCollider capsule = mech.GetComponent<CapsuleCollider>();
            return capsule != null ? capsule.radius : 0.5f;
        }

        private static float HeightOf(GameObject mech)
        {
            CapsuleCollider capsule = mech.GetComponent<CapsuleCollider>();
            return capsule != null ? capsule.height : 2f;
        }

        // ---------------- 결과 ----------------

        private void Finish(string setupFailure)
        {
            _finished = true;
            if (_reader != null) _reader.SetScripted(false);
            if (_versusActive != null) _versusActive.Release();
            if (_p1Melee != null) _p1Melee.OnHitResolved -= OnHit;
            if (_botMelee != null) _botMelee.OnHitResolved -= OnHit;
            if (_p1Melee != null) _p1Melee.OnPunchTraced -= OnP1PunchTraced;
            if (_p1Melee != null) _p1Melee.OnDiveStrikeLanded -= OnP1DiveLanded;

            var missing = new StringBuilder();
            if (_stagger == 0) missing.Append(" 경직");
            if (_down == 0) missing.Append(" 다운");
            if (_blown == 0) missing.Append(" 날아감");
            if (_guardStagger == 0) missing.Append(" 가드경직");
            if (_clash == 0) missing.Append(" 맞딜");
            // 원투쓰리는 날아감이 아니라 다운으로 끝나야 한다
            bool comboOk = _comboEnd == Reaction.Down;
            // 봇(공격만 모드)은 이동기로 다가가(걷기·대시) 공격 동작(펀치·뛰어 베기·런지·대시 공격)을 해야 한다
            var approach = _botStates[0];
            bool botMoved = approach.Contains("Walk") || approach.Contains("GroundDash");
            bool botAttacked = approach.Contains("Melee") || approach.Contains("DiveStrike") || approach.Contains("Lunge") || _botHitsLanded > 0;
            bool botOk = botMoved && botAttacked;
            // VERSUS: 장면별 검사 + 60fps·30fps 결과가 같아야 한다
            string versus60 = _versus60 != null ? _versus60.Problems() : "60fps 장면을 안 돌림";
            string versus30 = _versus30 != null ? _versus30.Problems() : "30fps 장면을 안 돌림";
            string fpsDiff = _versus60 != null && _versus30 != null ? VersusSmokeScenes.Compare(_versus60, _versus30) : "비교할 결과 없음";
            bool versusOk = versus60 == null && versus30 == null && fpsDiff == null;
            // 밀착 원투쓰리: 세 타 모두 명중해야 한다
            bool closeOk = _closeComboRan && _closeComboHits == 7;
            // 내려찍기: 정점·하강 중 = 명중(다운) + 양쪽 히트스톱 + 착지 경직 12f / 피하기 = 빗나감 + 착지 경직 30f
            string diveProblems = DiveProblems();
            bool pass = setupFailure == null && missing.Length == 0 && comboOk && closeOk && diveProblems == null && botOk && versusOk && _exceptions == 0 && _nanTicks == 0 && _outOfMapTicks == 0;

            Debug.Log($"{LogTag} 반응: 경직 {_stagger} / 다운 {_down} / 날아감 {_blown} / 가드경직 {_guardStagger} / 맞딜 {_clash} (무적만 {_invulnOnly}, 명중 {_hits})");
            Debug.Log($"{LogTag} 안정성: 예외·오류 로그 {_exceptions} / 좌표 깨짐 틱 {_nanTicks} / 겹친 틱 {_overlapTicks} / 맵 밖 틱 {_outOfMapTicks} / 돌린 틱 {_tick}");
            Debug.Log($"{LogTag} 원투쓰리 끝 반응: {_comboEnd} ({(comboOk ? "다운 — 정상" : "다운이어야 함")})");
            Debug.Log($"{LogTag} 밀착 원투쓰리({TouchDistance:0.0}m, {TouchAngle:0}° 비스듬히): 원 {Mark(1)} / 투 {Mark(2)} / 쓰리 {Mark(4)} ({(closeOk ? "3타 명중 — 정상" : "3타 모두 명중해야 함")})");
            string[] diveNames = { "정점", "하강 중", "옆 대시로 피함" };
            for (int i = 0; i < _dive.Length; i++)
            {
                DiveResult d = _dive[i];
                Debug.Log($"{LogTag} 내려찍기 {diveNames[i]}: 발동 {d.Started}(누를 때 vy {d.VyAtPress:0.00}u/f) / 상대 {d.BotResult} / 착지 {(d.Landed ? (d.Connected ? "맞힘" : "빗나감") : "안 함")}" +
                          $" 경직 {d.LagFrames}f(잰 값 {d.LandingTicks}틱) / 히트스톱 P1 {d.P1StopTicks}틱·봇 {d.BotStopTicks}틱 / 봇 대시 {d.BotDashed} / 가장 가까운 거리 {d.Closest:0.00}m");
            }
            Debug.Log($"{LogTag} 내려찍기: {diveProblems ?? "정상"}");
            string[] names = { "공격만", "패리", "섞어서" };
            for (int i = 0; i < 3; i++)
            {
                Debug.Log($"{LogTag} 봇 {names[i]}: 상태 [{string.Join(", ", _botStates[i])}] / 행동 [{string.Join(", ", _botOrders[i])}]");
            }
            Debug.Log($"{LogTag} 봇: 명중·가드됨 {_botHitsLanded} / 패리 {_botParries} ({(botOk ? "다가가 공격함 — 정상" : "공격만 모드가 다가가 공격해야 함")})");
            Debug.Log($"{LogTag} VERSUS 60fps: {versus60 ?? "정상"}");
            Debug.Log($"{LogTag} VERSUS 30fps: {versus30 ?? "정상"}");
            Debug.Log($"{LogTag} VERSUS 60fps·30fps 비교: {fpsDiff ?? "반응·최종 체력 같음"}");
            if (setupFailure != null) Debug.Log($"{LogTag} 준비 실패: {setupFailure}");
            if (missing.Length > 0) Debug.Log($"{LogTag} 횟수 0인 반응:{missing}");
            if (_firstProblems.Length > 0) Debug.Log($"{LogTag} 첫 오류 로그{_firstProblems}");
            Debug.Log($"{LogTag} RESULT {(pass ? "PASS" : "FAIL")}");

            if (!Application.isEditor) Application.Quit(pass ? 0 : 3);
        }
    }
}
