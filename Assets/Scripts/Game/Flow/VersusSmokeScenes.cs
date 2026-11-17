using System.Collections.Generic;
using System.Text;
using UnityEngine;
using MyDongari.Bridge;
using MyDongari.Core;
using MyDongari.Game.Combat;
using MyDongari.Game.Input;
using MyDongari.Game.Knockdown;

namespace MyDongari.Game.Flow
{
    /// <summary>
    /// 전투 스모크의 VERSUS 장면 (CombatSmokeRunner가 PRACTICE 장면 뒤에 돌린다). 1P·2P 둘 다 InputReader 스크립트 입력으로:
    /// ① 같은 틱 동시 펀치 → 맞딜이 대칭인지 (같은 자리로 세 번 — 장면 이력에 따라 밀림이 달라지는지 본다 — 그리고 1P·2P 자리를 바꿔 한 번)
    /// ② 1P 대시 공격이 맞는 동안 1P는 안 멈추는지 (기체별 히트스톱 — 화면 전체 timeScale도 1 그대로)
    /// ③ 벽 2m 앞에 선 2P를 런지로 날림 → 벽에 끼거나 뚫고 나가지 않는지 (장면이 벽을 잠깐 세운다)
    /// ④ 1P 대시 공격이 다가오면 2P가 점프 → 공중 다운 → 착지 → 기상 선입력 대시 (점프하는 거리를 세 가지로 — 하나라도 되면 됨).
    ///    런지는 강타라 공중이어도 날아감이 먼저다(전투 사양서 2.1 5번) — 그래서 강타가 아닌 대시 공격(근접 계열 2, 1100)을 쓴다.
    /// 맞딜 대칭은 맞딜이 난 뒤 밀린 거리로 본다. 그 전의 파고들기는 1P가 먼저 움직이고 2P가 1P의 이번 틱 위치를 보는
    /// 원본 순서(이동 사양서 1.1) 때문에 마지막 한 틱만큼 1P가 더 들어간다 — 그 차이는 로그에만 남긴다.
    /// 같은 장면을 60fps·30fps로 한 번씩 돌려 장면별 반응·최종 체력이 같고, 두 기체가 밀린 거리·끝난 자리가 1mm 안으로 같은지도 본다
    /// (전투는 틱으로만 돌아야 한다 — 화면 프레임률이 결과를 바꾸면 안 된다). 시간은 전부 물리 스텝(= 전투 틱) 수.
    /// </summary>
    public class VersusSmokeScenes
    {
        public const int SceneTicks = 300;
        // 장면 종류: 0 맞딜 A, 1 맞딜 B(자리 바꿈), 2 대시 공격 히트스톱, 3 벽 앞 런지, 4~6 공중 다운 + 기상 대시(점프 시점 셋)
        // 도는 순서 — 맞딜 A는 세 번 (첫 번째만 앞 단계의 이력을 물려받는다)
        private static readonly int[] Kinds = { 0, 0, 0, 1, 2, 3, 4, 5, 6 };
        public static int SceneCount => Kinds.Length;
        /// <summary>지금 장면의 종류.</summary>
        private int Kind => Kinds[Mathf.Clamp(_scene, 0, Kinds.Length - 1)];
        /// <summary>60fps·30fps 결과의 위치 허용 오차(m): 1mm.</summary>
        public const float FpsPositionTolerance = 0.001f;
        private const float ClashDistance = 3f;
        private const float DashDistance = 10f;
        private const float LungeDistance = 7f;
        private const float WallGap = 2f;
        private const float WallThickness = 1f;
        private const float WallWidth = 10f;
        private const float WallHeight = 5f;
        private const int AttackTick = 20;
        // 공중 다운 장면: 다가오는 1P가 이 거리(m) 안에 들어오면 2P가 점프
        private static readonly float[] JumpDistances = { 3.5f, 4.5f, 5.5f };
        // 허용 오차: 대칭 거리(m)·벽 파묻힘(m)·체력
        private const float SymmetryTolerance = 0.05f;
        private const float PenetrationTolerance = 0.05f;
        private const float HealthTolerance = 0.01f;
        // 기상 대시: 다운이 끝난 뒤 이 틱 안에 대시가 시작돼야 한다
        private const int WakeDashWindow = 5;

        private class Mech
        {
            public GameObject Go;
            public InputReader Reader;
            public MeleeSystem Melee;
            public Rigidbody Rb;
            public MechStateMachine Sm;
            public KnockdownController Knockdown;
            public Health Health;
            public float Radius;
            public readonly List<string> Events = new List<string>();
        }

        /// <summary>장면 하나의 결과 (60fps·30fps 비교용).</summary>
        public class SceneResult
        {
            public string Name;
            public string P1Events;
            public string P2Events;
            public float P1Health;
            public float P2Health;
            public string Problem;
            public bool AirDownWakeDash;
            // 첫 반응 뒤 밀린 거리(m)와 장면이 끝났을 때 자리(장면 시작 자리 기준, m) — 60fps·30fps 비교용
            public float P1Push, P2Push;
            public Vector3 P1End, P2End;
        }

        private readonly Mech _p1 = new Mech();
        private readonly Mech _p2 = new Mech();
        private GameManager _gm;
        private Vector3 _origin;
        private Vector3 _forward;
        private int _scene = -1;
        private int _tick;
        private GameObject _wall;
        private Vector3 _wallFace;

        // 장면 측정값
        private Vector3 _p1Start, _p2Start;
        // 지난 틱 위치와 첫 반응 직전 위치 (맞딜 뒤 밀린 거리용)
        private Vector3 _p1Prev, _p2Prev, _p1AtEvent, _p2AtEvent;
        private bool _eventSeen;
        private bool _p2Jumped;
        private int _timeScaleTicks;
        private float _p1MoveDuringP2Stop;
        private int _p2StopTicks, _p1StopTicksWhileP2Stop;
        private float _maxPenetration;
        private bool _passedThrough;
        private bool _p2WasDown;
        private int _ticksSinceRecover = -1;
        private bool _wakeDashSeen;
        private bool _airDownSeen;

        private System.Action<HitInfo, HitResult> _onP1, _onP2;

        public readonly List<SceneResult> Results = new List<SceneResult>();
        public bool Done => _scene >= SceneCount;
        public string Label { get; private set; }

        public bool Setup(GameManager gm, string label)
        {
            _gm = gm;
            Label = label;
            if (!Fill(_p1, gm.Player1) || !Fill(_p2, gm.Player2)) return false;
            _p1.Reader.SetScripted(true);
            _p2.Reader.SetScripted(true);
            _onP1 = (info, result) => Note(_p1, info, result);
            _onP2 = (info, result) => Note(_p2, info, result);
            _p1.Melee.OnHitResolved += _onP1;
            _p2.Melee.OnHitResolved += _onP2;
            Vector3 forward = _p1.Rb.rotation * Vector3.forward;
            forward.y = 0f;
            _forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            _scene = -1;
            _tick = 0;
            Results.Clear();
            return true;
        }

        private static bool Fill(Mech m, GameObject go)
        {
            if (go == null) return false;
            m.Go = go;
            m.Reader = go.GetComponent<InputReader>();
            m.Melee = go.GetComponent<MeleeSystem>();
            m.Rb = go.GetComponent<Rigidbody>();
            m.Sm = go.GetComponent<MechStateMachine>();
            m.Knockdown = go.GetComponent<KnockdownController>();
            m.Health = go.GetComponent<Health>();
            CapsuleCollider capsule = go.GetComponent<CapsuleCollider>();
            m.Radius = capsule != null ? capsule.radius : 0.5f;
            return m.Reader != null && m.Melee != null && m.Rb != null && m.Sm != null && m.Health != null;
        }

        private void Note(Mech m, HitInfo info, HitResult result)
        {
            if (_scene < 0 || Done) return;
            m.Events.Add(result == HitResult.Clash ? "맞딜" : $"{result}/{info.Reaction}");
            if (m == _p2 && info.Reaction == Reaction.AirDown) _airDownSeen = true;
        }

        public void Release()
        {
            if (_p1.Reader != null) _p1.Reader.SetScripted(false);
            if (_p2.Reader != null) _p2.Reader.SetScripted(false);
            if (_p1.Melee != null && _onP1 != null) _p1.Melee.OnHitResolved -= _onP1;
            if (_p2.Melee != null && _onP2 != null) _p2.Melee.OnHitResolved -= _onP2;
            DestroyWall();
        }

        /// <summary>물리 스텝마다 (전투 틱보다 먼저). 다 끝나면 Done.</summary>
        public void Tick()
        {
            if (Done) return;
            int scene = _tick / SceneTicks;
            if (scene != _scene)
            {
                if (_scene >= 0) EndScene();
                if (scene >= SceneCount)
                {
                    _scene = SceneCount;
                    DestroyWall();
                    return;
                }
                BeginScene(scene);
            }
            int t = _tick - _scene * SceneTicks;
            Drive(t);
            Measure();
            _tick++;
        }

        // ---------------- 장면 ----------------

        private void BeginScene(int scene)
        {
            _scene = scene;
            _p1.Events.Clear();
            _p2.Events.Clear();
            _p1.Reader.ScriptState(Vector2.zero, false, false, false);
            _p2.Reader.ScriptState(Vector2.zero, false, false, false);
            _gm.ResetMechsNow();
            if (_origin == Vector3.zero) _origin = _p1.Rb.position;
            DestroyWall();

            int kind = Kind;
            float distance = kind <= 1 ? ClashDistance : kind == 3 ? LungeDistance : DashDistance;
            // 맞딜 B는 자리를 바꾼다: 2P가 1P 자리에, 1P가 2P 자리에
            bool swapped = kind == 1;
            Mech near = swapped ? _p2 : _p1;
            Mech far = swapped ? _p1 : _p2;
            Vector3 farPos = _origin + _forward * distance;
            farPos.y = far.Rb.position.y;
            Vector3 nearPos = _origin;
            nearPos.y = near.Rb.position.y;
            Place(near, nearPos, Quaternion.LookRotation(_forward, Vector3.up));
            Place(far, farPos, Quaternion.LookRotation(-_forward, Vector3.up));
            if (kind == 3) BuildWall(farPos, far);

            _p1Start = _p1.Rb.position;
            _p2Start = _p2.Rb.position;
            _p1Prev = _p1Start;
            _p2Prev = _p2Start;
            _eventSeen = false;
            _p2Jumped = false;
            _timeScaleTicks = 0;
            _p1MoveDuringP2Stop = 0f;
            _p2StopTicks = 0;
            _p1StopTicksWhileP2Stop = 0;
            _maxPenetration = float.MinValue;
            _passedThrough = false;
            _p2WasDown = false;
            _ticksSinceRecover = -1;
            _wakeDashSeen = false;
            _airDownSeen = false;
        }

        private static void Place(Mech m, Vector3 position, Quaternion rotation)
        {
            m.Go.transform.SetPositionAndRotation(position, rotation);
            MotionBridge.Teleport(m.Rb, position, rotation);
        }

        /// <summary>2P 뒤(1P 반대쪽) WallGap 거리에 벽면이 오도록 상자 하나를 세운다. "Wall" 레이어가 있으면 그 레이어로.</summary>
        private void BuildWall(Vector3 victimPos, Mech victim)
        {
            _wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _wall.name = "CombatSmokeWall";
            int layer = LayerMask.NameToLayer(GroundLayers.WallLayerName);
            if (layer >= 0) _wall.layer = layer;
            float feet = victim.Rb.position.y - victim.Go.GetComponent<Collider>().bounds.extents.y;
            _wallFace = victimPos + _forward * WallGap;
            _wallFace.y = feet;
            _wall.transform.SetPositionAndRotation(_wallFace + _forward * (WallThickness * 0.5f) + Vector3.up * (WallHeight * 0.5f - 0.5f),
                Quaternion.LookRotation(_forward, Vector3.up));
            _wall.transform.localScale = new Vector3(WallWidth, WallHeight, WallThickness);
            Physics.SyncTransforms();
        }

        private void DestroyWall()
        {
            if (_wall != null) Object.Destroy(_wall);
            _wall = null;
        }

        private void Drive(int t)
        {
            switch (Kind)
            {
                case 0:
                case 1:
                    // 같은 틱에 둘 다 펀치
                    if (t == 30)
                    {
                        _p1.Reader.ScriptPressAttack();
                        _p2.Reader.ScriptPressAttack();
                    }
                    break;
                case 2:
                    // 1P: 앞 대시 → 지속 단계에서 공격 (2P는 가만히)
                    if (t == 10)
                    {
                        _p1.Reader.ScriptState(Vector2.up, true, false, false);
                        _p1.Reader.ScriptPressDash();
                    }
                    if (_p2.Events.Count == 0 && t >= 28 && t <= 60 && t % 3 == 0) _p1.Reader.ScriptPressAttack();
                    if (t == 90) _p1.Reader.ScriptState(Vector2.zero, false, false, false);
                    break;
                case 3:
                    // 벽 앞 런지
                    if (t == AttackTick) _p1.Reader.ScriptPressAttack();
                    break;
                default:
                {
                    // 공중 다운 + 기상 선입력 대시: 1P 앞 대시 → 대시 공격, 2P는 1P가 가까워지면 점프. 다운되면 ○ + 오른쪽을 누른 채 가드 연타
                    if (t == 10)
                    {
                        _p1.Reader.ScriptState(Vector2.up, true, false, false);
                        _p1.Reader.ScriptPressDash();
                    }
                    if (_p2.Events.Count == 0 && t >= 28 && t <= 60 && t % 3 == 0) _p1.Reader.ScriptPressAttack();
                    if (t == 90) _p1.Reader.ScriptState(Vector2.zero, false, false, false);
                    if (!_p2Jumped && t > 10 && Flat(_p1.Rb.position - _p2.Rb.position) < JumpDistances[Kind - 4])
                    {
                        _p2Jumped = true;
                        _jumpTick = t;
                        _p2.Reader.ScriptState(Vector2.zero, false, true, false);
                        _p2.Reader.ScriptPressJump();
                    }
                    if (_p2Jumped && t == _jumpTick + 8 && !(_p2.Knockdown != null && _p2.Knockdown.IsKnockedDown))
                        _p2.Reader.ScriptState(Vector2.zero, false, false, false);
                    bool down = _p2.Knockdown != null && _p2.Knockdown.IsKnockedDown;
                    if (down)
                    {
                        _p2.Reader.ScriptState(Vector2.right, true, false, false);
                        if (_p2.Knockdown.IsWaitingWake && t % 4 == 0) _p2.Reader.ScriptPressGuard();
                    }
                    else if (_p2WasDown && _ticksSinceRecover > 20)
                    {
                        _p2.Reader.ScriptState(Vector2.zero, false, false, false);
                    }
                    break;
                }
            }
        }

        private int _jumpTick;

        private void Measure()
        {
            if (!_eventSeen && (_p1.Events.Count > 0 || _p2.Events.Count > 0))
            {
                _eventSeen = true;
                _p1AtEvent = _p1Prev;
                _p2AtEvent = _p2Prev;
            }
            _p1Prev = _p1.Rb.position;
            _p2Prev = _p2.Rb.position;
            if (!Mathf.Approximately(Time.timeScale, 1f)) _timeScaleTicks++;
            bool p2Stop = _p2.Melee.IsHitStopped;
            bool p1Stop = _p1.Melee.IsHitStopped;
            if (Kind == 2 && p2Stop)
            {
                _p2StopTicks++;
                if (p1Stop) _p1StopTicksWhileP2Stop++;
                _p1MoveDuringP2Stop += MotionBridge.GetVelocity(_p1.Rb).magnitude / CombatTime.TickRate;
            }
            if (Kind == 3 && _wall != null)
            {
                Vector3 c = _p2.Rb.position;
                float d = Vector3.Dot(_wallFace - c, _forward);
                _maxPenetration = Mathf.Max(_maxPenetration, _p2.Radius - d);
                if (d < -_p2.Radius) _passedThrough = true;
            }
            if (Kind >= 4)
            {
                bool down = _p2.Knockdown != null && _p2.Knockdown.IsKnockedDown;
                if (down)
                {
                    _p2WasDown = true;
                    _ticksSinceRecover = -1;
                }
                else if (_p2WasDown)
                {
                    _ticksSinceRecover++;
                    if (_ticksSinceRecover <= WakeDashWindow && _p2.Sm.IsDashing) _wakeDashSeen = true;
                }
            }
        }

        private void EndScene()
        {
            var r = new SceneResult
            {
                Name = SceneName(_scene),
                P1Push = _eventSeen ? Flat(_p1.Rb.position - _p1AtEvent) : 0f,
                P2Push = _eventSeen ? Flat(_p2.Rb.position - _p2AtEvent) : 0f,
                P1End = _p1.Rb.position - _p1Start,
                P2End = _p2.Rb.position - _p2Start,
                P1Events = string.Join(",", _p1.Events),
                P2Events = string.Join(",", _p2.Events),
                P1Health = _p1.Health.CurrentHealth,
                P2Health = _p2.Health.CurrentHealth
            };
            var problem = new StringBuilder();
            if (_timeScaleTicks > 0) problem.Append($" timeScale≠1 {_timeScaleTicks}틱");
            switch (Kind)
            {
                case 0:
                case 1:
                {
                    // 맞딜 뒤 밀린 거리 (대칭이어야 함) / 그 전 파고든 거리 (1P→2P 순서 때문에 다를 수 있음 — 기록만)
                    float push1 = _eventSeen ? Flat(_p1.Rb.position - _p1AtEvent) : 0f;
                    float push2 = _eventSeen ? Flat(_p2.Rb.position - _p2AtEvent) : 0f;
                    float in1 = _eventSeen ? Flat(_p1AtEvent - _p1Start) : 0f;
                    float in2 = _eventSeen ? Flat(_p2AtEvent - _p2Start) : 0f;
                    if (!r.P1Events.Contains("맞딜") || !r.P2Events.Contains("맞딜")) problem.Append(" 둘 다 맞딜이어야 함");
                    if (r.P1Events != r.P2Events) problem.Append($" 반응 다름(1P {r.P1Events} / 2P {r.P2Events})");
                    if (Mathf.Abs(r.P1Health - r.P2Health) > HealthTolerance) problem.Append($" 체력 다름 {r.P1Health:0.0}/{r.P2Health:0.0}");
                    if (Mathf.Abs(push1 - push2) > SymmetryTolerance) problem.Append($" 맞딜 뒤 밀린 거리 다름 {push1:0.000}/{push2:0.000}m");
                    r.Name += $" (맞딜 뒤 밀림 1P {push1:0.000}m / 2P {push2:0.000}m, 파고듦 1P {in1:0.000}m / 2P {in2:0.000}m)";
                    break;
                }
                case 2:
                    if (!r.P2Events.Contains("Hit")) problem.Append(" 대시 공격이 안 맞음");
                    if (_p2StopTicks == 0) problem.Append(" 2P 히트스톱 없음");
                    if (_p1StopTicksWhileP2Stop > 0) problem.Append($" 2P 히트스톱 중 1P도 {_p1StopTicksWhileP2Stop}틱 멈춤");
                    if (_p2StopTicks > 0 && _p1MoveDuringP2Stop < 0.1f) problem.Append($" 2P 히트스톱 중 1P 이동 {_p1MoveDuringP2Stop:0.00}m");
                    r.Name += $" (2P 정지 {_p2StopTicks}틱 동안 1P 정지 {_p1StopTicksWhileP2Stop}틱·이동 {_p1MoveDuringP2Stop:0.00}m)";
                    break;
                case 3:
                    if (!r.P2Events.Contains("Blown") && !r.P2Events.Contains("Down")) problem.Append(" 런지로 안 날아감");
                    if (_maxPenetration > PenetrationTolerance) problem.Append($" 벽에 {_maxPenetration:0.000}m 파묻힘");
                    if (_passedThrough) problem.Append(" 벽을 뚫고 나감");
                    if (WallOverlap()) problem.Append(" 끝났을 때 벽에 끼어 있음");
                    r.Name += $" (최대 파묻힘 {Mathf.Max(0f, _maxPenetration):0.000}m)";
                    break;
                default:
                    r.AirDownWakeDash = _airDownSeen && _wakeDashSeen;
                    r.Name += $" (점프 {(_p2Jumped ? "t" + _jumpTick : "안 함")}, 공중 다운 {_airDownSeen}, 기상 대시 {_wakeDashSeen})";
                    break;
            }
            r.Problem = problem.Length > 0 ? problem.ToString().Trim() : null;
            if (Kind >= 2) r.Name += $" [첫 반응 뒤 밀림 1P {r.P1Push:0.000}m / 2P {r.P2Push:0.000}m]";
            Results.Add(r);
            Debug.Log($"{CombatSmokeRunner.LogTag} {Label} {r.Name}: 1P [{r.P1Events}] 체력 {r.P1Health:0.0} / 2P [{r.P2Events}] 체력 {r.P2Health:0.0}" +
                      (r.Problem != null ? $" — 문제: {r.Problem}" : ""));
        }

        private bool WallOverlap()
        {
            if (_wall == null) return false;
            Collider body = _p2.Go.GetComponent<Collider>();
            Collider wall = _wall.GetComponent<Collider>();
            if (body == null || wall == null) return false;
            return Physics.ComputePenetration(body, _p2.Rb.position, _p2.Rb.rotation, wall, wall.transform.position, wall.transform.rotation,
                       out _, out float depth) && depth > PenetrationTolerance;
        }

        private static float Flat(Vector3 v)
        {
            v.y = 0f;
            return v.magnitude;
        }

        private static string SceneName(int scene)
        {
            int kind = Kinds[scene];
            switch (kind)
            {
                case 0: return $"V{scene + 1} 맞딜 A";
                case 1: return $"V{scene + 1} 맞딜 B(자리 바꿈)";
                case 2: return $"V{scene + 1} 대시 공격 히트스톱";
                case 3: return $"V{scene + 1} 벽 앞 런지";
                default: return $"V{scene + 1} 공중 다운 + 기상 대시({JumpDistances[kind - 4]:0.0}m에서 점프)";
            }
        }

        /// <summary>장면별 검사 문제 + 공중 다운 장면 중 하나라도 성공했는지. 문제가 없으면 null.</summary>
        public string Problems()
        {
            var sb = new StringBuilder();
            bool airOk = false;
            foreach (SceneResult r in Results)
            {
                if (r.Problem != null) sb.Append($" [{r.Name}: {r.Problem}]");
                airOk |= r.AirDownWakeDash;
            }
            if (!airOk) sb.Append(" [공중 다운 → 기상 대시가 한 번도 안 됨]");
            if (Results.Count < SceneCount) sb.Append($" [장면 {Results.Count}/{SceneCount}만 끝남]");
            return sb.Length > 0 ? sb.ToString().Trim() : null;
        }

        /// <summary>두 번(60fps·30fps) 돌린 결과가 장면별로 같은지. 다르면 차이.</summary>
        public static string Compare(VersusSmokeScenes a, VersusSmokeScenes b)
        {
            var sb = new StringBuilder();
            int n = Mathf.Min(a.Results.Count, b.Results.Count);
            if (a.Results.Count != b.Results.Count) sb.Append($" 장면 수 {a.Results.Count}/{b.Results.Count}");
            for (int i = 0; i < n; i++)
            {
                SceneResult x = a.Results[i], y = b.Results[i];
                if (x.P1Events != y.P1Events || x.P2Events != y.P2Events) sb.Append($" [V{i + 1} 반응 1P {x.P1Events}|{y.P1Events} 2P {x.P2Events}|{y.P2Events}]");
                if (Mathf.Abs(x.P1Health - y.P1Health) > HealthTolerance || Mathf.Abs(x.P2Health - y.P2Health) > HealthTolerance)
                    sb.Append($" [V{i + 1} 체력 {x.P1Health:0.0}/{x.P2Health:0.0} | {y.P1Health:0.0}/{y.P2Health:0.0}]");
                // 밀린 거리와 끝난 자리: 화면 프레임률이 달라도 1mm 안으로 같아야 한다
                float push = Mathf.Max(Mathf.Abs(x.P1Push - y.P1Push), Mathf.Abs(x.P2Push - y.P2Push));
                float end = Mathf.Max((x.P1End - y.P1End).magnitude, (x.P2End - y.P2End).magnitude);
                if (push > FpsPositionTolerance || end > FpsPositionTolerance)
                    sb.Append($" [V{i + 1} 밀림 1P {x.P1Push:0.0000}|{y.P1Push:0.0000} 2P {x.P2Push:0.0000}|{y.P2Push:0.0000}m, 끝난 자리 차이 1P {(x.P1End - y.P1End).magnitude * 1000f:0.0}mm 2P {(x.P2End - y.P2End).magnitude * 1000f:0.0}mm]");
            }
            return sb.Length > 0 ? sb.ToString().Trim() : null;
        }

        /// <summary>두 번 돌린 결과에서 밀린 거리·끝난 자리가 가장 크게 다른 값(mm). 로그용.</summary>
        public static float MaxPositionDifferenceMm(VersusSmokeScenes a, VersusSmokeScenes b)
        {
            float worst = 0f;
            int n = Mathf.Min(a.Results.Count, b.Results.Count);
            for (int i = 0; i < n; i++)
            {
                SceneResult x = a.Results[i], y = b.Results[i];
                worst = Mathf.Max(worst, Mathf.Abs(x.P1Push - y.P1Push), Mathf.Abs(x.P2Push - y.P2Push));
                worst = Mathf.Max(worst, (x.P1End - y.P1End).magnitude, (x.P2End - y.P2End).magnitude);
            }
            return worst * 1000f;
        }
    }
}
