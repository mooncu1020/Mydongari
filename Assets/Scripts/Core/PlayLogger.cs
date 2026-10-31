using System;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Text;
using System.Globalization;
using MyDongari.Combat;
using MyDongari.Combat.Skills;
using MyDongari.Mech;
using MyDongari.Movement;
using MyDongari.Input;
using MyDongari.Visual;

namespace MyDongari.Core
{
    /// <summary>
    /// 로케테스트 로그. 세 파일을 남긴다.
    /// - playlog_*.csv: 0.1초 간격 프레임 로그 (기존 + 라운드·거리 구간)
    /// - events_*.csv: 전투 이벤트 (시간·라운드·플레이어·이벤트·거리·상세)
    /// - summary_*.csv: 매치마다 플레이어별 이벤트 카운트 + 거리 구간 체류 시간
    /// 전투 코드는 로거를 모른다. 여기서 각 시스템의 이벤트를 구독만 한다.
    /// </summary>
    public class PlayLogger : MonoBehaviour
    {
        [Header("설정")]
        [SerializeField] private float logInterval = 0.1f;
        [SerializeField] private string fileName = "playlog";
        [SerializeField] private string eventFileName = "events";
        [SerializeField] private string summaryFileName = "summary";
        [Tooltip("로그 폴더 이름. 에디터: 프로젝트 루트/이 폴더, 빌드: 실행 파일 옆/이 폴더")]
        [SerializeField] private string logFolderName = "Logs";
        [Tooltip("Logs 안의 하위 폴더. 에디터에선 Logs/에 Unity 자체 로그도 쌓이므로 섞이지 않게 나눈다. 비우면 Logs 바로 아래")]
        [SerializeField] private string logSubfolderName = "PlayLogs";

        [Header("거리 구간 (m)")]
        [Tooltip("이 거리 미만 = near (하드 락온·펀치 거리)")]
        [SerializeField] private float nearRange = 6f;
        [Tooltip("이 거리 이하 = mid, 초과 = far")]
        [SerializeField] private float midRange = 12f;

        [Header("P1 참조 (비우면 GameManager의 현재 P1)")]
        [SerializeField] private GameObject player1;

        [Header("P2 참조 (비우면 GameManager의 현재 P2 — 1인 모드면 더미)")]
        [SerializeField] private GameObject player2;

        // 요약에 0이어도 항상 나오게 할 스펙 9장 항목 (순서 고정)
        private static readonly string[] SummaryKeys =
        {
            "punch_start", "punch_landed",
            // 원투쓰리 콤보 단계별 시작 수 (N2)
            "combo1_start", "combo2_start", "combo3_start",
            "lunge_start", "lunge_hit", "lunge_miss", "lunge_parried",
            "tackle_start", "tackle_landed", "dash_attack_start", "dash_attack_landed",
            "guard_block", "parry", "guard_break", "clash",
            "sidestep", "anchor_hit", "slam_landed",
            // 입력 버퍼로(누른 프레임보다 뒤에) 발동한 공격 수. 종류별은 buffered_punch 등으로 따로
            "buffered_attack",
            // ○ 대시(V3.1): 진입 수와 끝난 이유별 수 (나머지 이유는 0이 아닐 때만 추가로 나온다)
            "dash_enter", "dash_exit_canceled", "dash_exit_exhausted", "dash_exit_bump", "dash_exit_wall", "dash_exit_timeout",
            // 점프(V5): 점프 수, 공중 대시 수, 착지 경직 수 (짧게·부스트 0% 길게)
            "jump", "air_dash", "landing_lag", "landing_lag_long"
        };
        private static readonly string[] BandNames = { "near", "mid", "far" };

        private float _logTimer = 0f;
        private StringBuilder _sb;
        private StringBuilder _eventSb;
        private StringBuilder _summarySb;
        private string _filePath;
        private string _eventFilePath;
        private string _summaryFilePath;
        private bool _isLogging = false;
        private bool _dirty = false;

        private readonly Dictionary<string, int>[] _counts = { new Dictionary<string, int>(), new Dictionary<string, int>() };
        private readonly float[] _bandTime = new float[3];
        private int _matchIndex = 0;
        // 요약의 match 칸에 붙이는 모드 이름 (versus / practice)
        private string _matchMode = "versus";
        private bool _matchSummaryWritten = false;
        private readonly List<Action> _unsubscribers = new List<Action>();

        private void Start()
        {
            GameManager gm = GameManager.Instance;
            // GameManager가 1인/2인 모드에 따라 정한 실제 대결 기체를 우선한다
            if (gm != null)
            {
                if (gm.Player1 != null) player1 = gm.Player1;
                if (gm.Player2 != null) player2 = gm.Player2;
            }

            string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string folder = LogFolder();
            // 예외 기록(errors.txt)도 같은 폴더에
            ErrorLog.Folder = folder;
            _filePath = Path.Combine(folder, fileName + "_" + timestamp + ".csv");
            _eventFilePath = Path.Combine(folder, eventFileName + "_" + timestamp + ".csv");
            _summaryFilePath = Path.Combine(folder, summaryFileName + "_" + timestamp + ".csv");

            _sb = new StringBuilder();
            _sb.AppendLine("time,round,p1_hp,p1_boost,p1_heat,p1_state,p1_posX,p1_posZ,p1_moveX,p1_moveY,p2_hp,p2_boost,p2_heat,p2_state,p2_posX,p2_posZ,p2_moveX,p2_moveY,distance,dist_band");
            _eventSb = new StringBuilder();
            _eventSb.AppendLine("time,round,player,event,distance,detail");
            _summarySb = new StringBuilder();
            _summarySb.AppendLine("match,player,metric,value");

            SubscribeMech(player1, 1);
            if (gm != null)
            {
                // 모드에 따라 P2가 2인용 기체 또는 연습 봇으로 바뀐다. 둘 다 P2로 구독 (꺼진 쪽은 이벤트가 안 난다)
                foreach (GameObject mech in gm.AllMechs)
                {
                    if (mech != player1) SubscribeMech(mech, 2);
                }
            }
            else
            {
                SubscribeMech(player2, 2);
            }
            SubscribeGameManager(gm);

            _isLogging = true;
            _matchIndex = 1;
            // GameManager.Start가 먼저 돌았으면 1라운드 시작 이벤트를 놓쳤으니 지금 남긴다
            if (gm != null && gm.RoundNumber > 0) LogEvent(0, "round_start", gm.RoundNumber.ToString());
        }

        private void OnDestroy()
        {
            foreach (Action unsubscribe in _unsubscribers) unsubscribe();
            _unsubscribers.Clear();
            SaveLog();
        }

        private void OnApplicationQuit()
        {
            SaveLog();
        }

        // ---------------- 구독 ----------------

        private void SubscribeMech(GameObject mech, int index)
        {
            if (mech == null) return;

            MeleeSystem melee = mech.GetComponent<MeleeSystem>();
            if (melee != null)
            {
                Action<AttackType> onStart = type => ErrorLog.Run(() => OnAttackStarted(index, type), "PlayLogger.onStart");
                // 원투쓰리 콤보 단계 (N2): combo1_start / combo2_start / combo3_start
                Action<int> onCombo = step => ErrorLog.Run(() => Count(index, "combo" + step + "_start"), "PlayLogger.onCombo");
                melee.OnComboStep += onCombo;
                MeleeSystem comboMelee = melee;
                _unsubscribers.Add(() => comboMelee.OnComboStep -= onCombo);
                Action<HitInfo, HitResult> onHit = (info, result) => ErrorLog.Run(() => OnHitResolved(index, info, result), "PlayLogger.onHit");
                // 발생 시간 중 스턴 등으로 취소된 공격 (punch_canceled 등)
                Action<AttackType> onCancel = type => ErrorLog.Run(() => Count(index, AttackKey(type) + "_canceled"), "PlayLogger.onCancel");
                melee.OnAttackStarted += onStart;
                melee.OnAttackCanceled += onCancel;
                melee.OnHitResolved += onHit;
                _unsubscribers.Add(() => { melee.OnAttackStarted -= onStart; melee.OnAttackCanceled -= onCancel; melee.OnHitResolved -= onHit; });
            }

            // 버퍼 발동은 플레이어면 WeaponSystem, 연습 봇이면 MeleeDummy가 알린다
            Action<AttackType> onBuffered = type => ErrorLog.Run(() =>
            {
                Count(index, "buffered_attack", AttackKey(type));
                CountSilent(index, "buffered_" + AttackKey(type));
            }, "PlayLogger.onBuffered");
            WeaponSystem weapon = mech.GetComponent<WeaponSystem>();
            if (weapon != null)
            {
                weapon.OnBufferedAttack += onBuffered;
                _unsubscribers.Add(() => weapon.OnBufferedAttack -= onBuffered);
            }
            MeleeDummy bot = mech.GetComponent<MeleeDummy>();
            if (bot != null)
            {
                bot.OnBufferedAttack += onBuffered;
                _unsubscribers.Add(() => bot.OnBufferedAttack -= onBuffered);
            }

            // 넉다운 (R1): 시작·벽 충돌·끝(수평 이동 거리·최고 높이 — 30fps 비교용)
            KnockdownController knockdown = mech.GetComponent<KnockdownController>();
            if (knockdown != null)
            {
                Action onKnockStart = () => ErrorLog.Run(() => Count(index, "knockdown"), "PlayLogger.onKnockdown");
                Action<Vector3> onKnockWall = point => ErrorLog.Run(() => Count(index, "knockdown_wall"), "PlayLogger.onKnockdownWall");
                Action<float, float, bool> onKnockEnd = (distance, height, wall) => ErrorLog.Run(
                    () => Count(index, "knockdown_end", $"{distance:0.00}m h{height:0.00}m{(wall ? " wall" : "")}"), "PlayLogger.onKnockdownEnd");
                knockdown.OnKnockdownStarted += onKnockStart;
                knockdown.OnWallHit += onKnockWall;
                knockdown.OnRecovered += onKnockEnd;
                _unsubscribers.Add(() =>
                {
                    knockdown.OnKnockdownStarted -= onKnockStart;
                    knockdown.OnWallHit -= onKnockWall;
                    knockdown.OnRecovered -= onKnockEnd;
                });
            }

            ThrusterLunge lunge = mech.GetComponent<ThrusterLunge>();
            if (lunge != null)
            {
                Action onLungeStart = () => ErrorLog.Run(() => Count(index, "lunge_start"), "PlayLogger.onLungeStart");
                Action<HitResult?> onLungeEnd = result => ErrorLog.Run(() => OnLungeEnded(index, result), "PlayLogger.onLungeEnd");
                lunge.OnLungeStarted += onLungeStart;
                lunge.OnLungeEnded += onLungeEnd;
                _unsubscribers.Add(() => { lunge.OnLungeStarted -= onLungeStart; lunge.OnLungeEnded -= onLungeEnd; });
            }

            AnchorClaw claw = mech.GetComponent<AnchorClaw>();
            if (claw != null)
            {
                Action<GameObject> onGrab = _ => ErrorLog.Run(() => Count(index, "anchor_hit"), "PlayLogger.onGrab");
                Action onFire = () => ErrorLog.Run(() => Count(index, "anchor_fire"), "PlayLogger.onClawFire");
                Action onMiss = () => ErrorLog.Run(() => Count(index, "anchor_miss"), "PlayLogger.onClawMiss");
                claw.OnGrabbed += onGrab;
                claw.OnFired += onFire;
                claw.OnMissed += onMiss;
                _unsubscribers.Add(() =>
                {
                    claw.OnGrabbed -= onGrab;
                    claw.OnFired -= onFire;
                    claw.OnMissed -= onMiss;
                });
            }

            // 접지 보정량을 가끔(튜닝 groundLogInterval) 이벤트로. 카운트는 안 올린다
            MechVisualRig rig = mech.GetComponent<MechVisualRig>();
            if (rig != null)
            {
                Action<float> onGround = offset => ErrorLog.Run(
                    () => LogEvent(index, "ground_offset", offset.ToString("F3", CultureInfo.InvariantCulture)), "PlayLogger.onGround");
                rig.OnGroundSample += onGround;
                _unsubscribers.Add(() => rig.OnGroundSample -= onGround);
            }

            MechStateMachine sm = mech.GetComponent<MechStateMachine>();
            if (sm != null)
            {
                Action<MechState> onState = state => ErrorLog.Run(() =>
                {
                    if (state is SideStepState) Count(index, "sidestep");
                    else if (state is DashState && sm.IsAirborne) Count(index, "air_dash");
                }, "PlayLogger.onState");
                sm.OnStateChanged += onState;
                _unsubscribers.Add(() => sm.OnStateChanged -= onState);

                // 점프·착지 경직 (V5)
                Action onJump = () => ErrorLog.Run(() => Count(index, "jump"), "PlayLogger.onJump");
                sm.AirState.OnJumped += onJump;
                Action<float, bool> onLand = (lag, exhausted) => ErrorLog.Run(() =>
                {
                    if (lag > 0f) Count(index, exhausted ? "landing_lag_long" : "landing_lag", lag.ToString("F2", CultureInfo.InvariantCulture));
                }, "PlayLogger.onLand");
                sm.LandingState.OnLanded += onLand;
                _unsubscribers.Add(() =>
                {
                    sm.AirState.OnJumped -= onJump;
                    sm.LandingState.OnLanded -= onLand;
                });

                DashState dash = sm.DashState;
                if (dash != null)
                {
                    Action onDashStart = () => ErrorLog.Run(
                        () => Count(index, "dash_enter"), "PlayLogger.onDashStart");
                    Action<DashExitReason> onDashEnd = reason => ErrorLog.Run(
                        () => Count(index, "dash_exit_" + DashExitKey(reason)), "PlayLogger.onDashEnd");
                    dash.OnDashStarted += onDashStart;
                    dash.OnDashEnded += onDashEnd;
                    _unsubscribers.Add(() =>
                    {
                        dash.OnDashStarted -= onDashStart;
                        dash.OnDashEnded -= onDashEnd;
                    });
                }
            }
        }

        private void SubscribeGameManager(GameManager gm)
        {
            if (gm == null) return;

            Action<int> onRoundStarted = round => ErrorLog.Run(() => LogEvent(0, "round_start", round.ToString()), "PlayLogger.onRoundStarted");
            Action onFight = () => ErrorLog.Run(() => LogEvent(0, "fight", ""), "PlayLogger.onFight");
            Action<int> onRoundOver = winner => ErrorLog.Run(() => LogEvent(0, "round_over", winner == 0 ? "draw" : "P" + winner), "PlayLogger.onRoundOver");
            Action<int> onMatchOver = winner => ErrorLog.Run(() =>
            {
                LogEvent(0, "match_over", "P" + winner);
                WriteMatchSummary(false);
                SaveLog();
            }, "PlayLogger.onMatchOver");
            Action onMatchStarted = () => ErrorLog.Run(() =>
            {
                // 첫 매치는 Start에서 이미 준비됨. 재매치 때만 카운트를 비우고 매치 번호를 올린다
                if (_matchSummaryWritten)
                {
                    _matchIndex++;
                    ResetMatchStats();
                }
                player2 = gm.Player2;
                _matchMode = gm.Mode == GameMode.Practice ? "practice" : "versus";
                LogEvent(0, "match_start", _matchIndex + " " + _matchMode);
            }, "PlayLogger.onMatchStarted");
            Action onReturnedToTitle = () => ErrorLog.Run(() =>
            {
                // 매치 도중에 나갔거나 연습을 끝냈으면 지금까지를 요약으로 남긴다 (2인 매치 도중이면 미완료 표시)
                if (!_matchSummaryWritten && HasAnyStats())
                {
                    WriteMatchSummary(_matchMode == "versus" && !gm.IsMatchOver);
                    SaveLog();
                }
                LogEvent(0, "title", "");
            }, "PlayLogger.onReturnedToTitle");

            gm.OnRoundStarted += onRoundStarted;
            gm.OnFight += onFight;
            gm.OnRoundOver += onRoundOver;
            gm.OnMatchOver += onMatchOver;
            gm.OnMatchStarted += onMatchStarted;
            gm.OnReturnedToTitle += onReturnedToTitle;
            _unsubscribers.Add(() =>
            {
                gm.OnRoundStarted -= onRoundStarted;
                gm.OnFight -= onFight;
                gm.OnRoundOver -= onRoundOver;
                gm.OnMatchOver -= onMatchOver;
                gm.OnMatchStarted -= onMatchStarted;
                gm.OnReturnedToTitle -= onReturnedToTitle;
            });
        }

        // ---------------- 이벤트 해석 ----------------

        private void OnAttackStarted(int attacker, AttackType type)
        {
            Count(attacker, AttackKey(type) + "_start");
        }

        /// <summary>피격자(defender) 쪽 MeleeSystem에서 결과가 정해졌을 때.</summary>
        private void OnHitResolved(int defender, HitInfo info, HitResult result)
        {
            int attacker = ResolveAttackerIndex(info, defender);
            string resultKey = ResultKey(result);

            // 런지 결과는 ThrusterLunge.OnLungeEnded가 빗나감까지 포함해 남기므로 공격자 쪽은 여기서 생략
            if (info.Type != AttackType.Lunge)
            {
                string attackKey = AttackKey(info.Type);
                // 펀치는 콤보 단계를 같이 남긴다 (예: "25 combo2")
                string detail = info.ComboStep > 0 ? info.Damage.ToString("F0") + " combo" + info.ComboStep : info.Damage.ToString("F0");
                Count(attacker, attackKey + "_" + resultKey, detail);
                if (info.Type == AttackType.Punch && info.ComboStep > 0) CountSilent(attacker, "combo" + info.ComboStep + "_" + resultKey);
                if (result == HitResult.Hit || result == HitResult.Guarded || result == HitResult.GuardBroken)
                {
                    CountSilent(attacker, attackKey + "_landed");
                }
            }

            switch (result)
            {
                case HitResult.Guarded: Count(defender, "guard_block", AttackKey(info.Type)); break;
                case HitResult.Parried: Count(defender, "parry", AttackKey(info.Type)); break;
                // 넉다운 기상 무적 중 맞은 공격 (R1)
                case HitResult.Invulnerable: Count(defender, "invulnerable_hit", AttackKey(info.Type)); break;
                case HitResult.GuardBroken: Count(defender, "guard_break", AttackKey(info.Type)); break;
                case HitResult.Clash:
                    // 맞딜은 양쪽 모두의 사건
                    Count(defender, "clash");
                    Count(attacker, "clash");
                    break;
            }
        }

        private void OnLungeEnded(int attacker, HitResult? result)
        {
            if (result == null) Count(attacker, "lunge_miss");
            else if (result == HitResult.Parried) Count(attacker, "lunge_parried");
            else if (result == HitResult.Invulnerable) Count(attacker, "lunge_invulnerable");
            else Count(attacker, "lunge_hit", ResultKey(result.Value));
        }

        private int ResolveAttackerIndex(HitInfo info, int defender)
        {
            if (info.Attacker != null)
            {
                if (info.Attacker == player1) return 1;
                if (info.Attacker == player2) return 2;
            }
            return defender == 1 ? 2 : 1;
        }

        private static string AttackKey(AttackType type)
        {
            switch (type)
            {
                case AttackType.Punch: return "punch";
                case AttackType.DashAttack: return "dash_attack";
                case AttackType.Tackle: return "tackle";
                case AttackType.Lunge: return "lunge";
                case AttackType.Slam: return "slam";
                case AttackType.PileDriver: return "piledriver";
                default: return "projectile";
            }
        }

        private static string DashExitKey(DashExitReason reason)
        {
            switch (reason)
            {
                case DashExitReason.Canceled: return "canceled";
                case DashExitReason.Wall: return "wall";
                case DashExitReason.Landed: return "landed";
                case DashExitReason.Exhausted: return "exhausted";
                case DashExitReason.Stun: return "stun";
                case DashExitReason.Tackle: return "tackle";
                case DashExitReason.Bump: return "bump";
                case DashExitReason.Timeout: return "timeout";
                case DashExitReason.Jump: return "jump";
                default: return "other";
            }
        }

        private static string ResultKey(HitResult result)
        {
            switch (result)
            {
                case HitResult.Hit: return "hit";
                case HitResult.Guarded: return "guarded";
                case HitResult.GuardBroken: return "guard_broken";
                case HitResult.Parried: return "parried";
                case HitResult.Invulnerable: return "invulnerable";
                default: return "clash";
            }
        }

        // ---------------- 기록 ----------------

        /// <summary>카운트를 올리고 이벤트 로그에도 한 줄 남긴다.</summary>
        private void Count(int player, string key, string detail = "")
        {
            CountSilent(player, key);
            LogEvent(player, key, detail);
        }

        /// <summary>요약용 카운트만 올린다 (이벤트 로그엔 이미 상세 이벤트가 남는 집계 항목).</summary>
        private void CountSilent(int player, string key)
        {
            if (player < 1 || player > 2) return;
            Dictionary<string, int> counts = _counts[player - 1];
            counts.TryGetValue(key, out int value);
            counts[key] = value + 1;
        }

        private void LogEvent(int player, string evt, string detail)
        {
            if (_eventSb == null) return;
            GameManager gm = GameManager.Instance;
            int round = gm != null ? gm.RoundNumber : 0;
            _eventSb.AppendLine(string.Format("{0:F2},{1},{2},{3},{4:F2},{5}",
                Time.time, round, player == 0 ? "-" : "P" + player, evt, CurrentDistance(), detail));
            _dirty = true;
        }

        private void Update()
        {
            // 예외가 나도 게임(과 이 이벤트를 부른 전투 코드)이 멈추지 않게 기록만 하고 넘어간다
            try
            {
                UpdateLogic();
            }
            catch (Exception e)
            {
                ErrorLog.Report(e, "PlayLogger.Update");
            }
        }

        private void UpdateLogic()
        {
            if (!_isLogging) return;
            // 현재 상대(모드 전환으로 바뀔 수 있음)
            if (GameManager.Instance != null && GameManager.Instance.Player2 != null) player2 = GameManager.Instance.Player2;
            if (GameManager.Instance != null && !GameManager.Instance.IsRoundActive) return;

            // 거리 구간 체류 시간은 실제 싸우는 시간(Fight 단계)만 센다
            if (player1 != null && player2 != null)
            {
                _bandTime[DistanceBand(CurrentDistance())] += Time.deltaTime;
            }

            _logTimer += Time.deltaTime;
            if (_logTimer < logInterval) return;
            _logTimer = 0f;

            LogFrame();
        }

        private float CurrentDistance()
        {
            if (player1 == null || player2 == null) return 0f;
            return Vector3.Distance(player1.transform.position, player2.transform.position);
        }

        private int DistanceBand(float distance)
        {
            if (distance < nearRange) return 0;
            if (distance <= midRange) return 1;
            return 2;
        }

        private void LogFrame()
        {
            float time = Time.time;
            GameManager gm = GameManager.Instance;
            int round = gm != null ? gm.RoundNumber : 0;

            ReadMech(player1, out float p1Hp, out float p1Boost, out float p1Heat, out string p1State,
                out float p1PosX, out float p1PosZ, out float p1MoveX, out float p1MoveY);
            ReadMech(player2, out float p2Hp, out float p2Boost, out float p2Heat, out string p2State,
                out float p2PosX, out float p2PosZ, out float p2MoveX, out float p2MoveY);

            float distance = CurrentDistance();
            string band = player1 != null && player2 != null ? BandNames[DistanceBand(distance)] : "none";

            _sb.AppendLine(string.Format("{0:F2},{1},{2:F1},{3:F1},{4:F1},{5},{6:F2},{7:F2},{8:F2},{9:F2},{10:F1},{11:F1},{12:F1},{13},{14:F2},{15:F2},{16:F2},{17:F2},{18:F2},{19}",
                time, round, p1Hp, p1Boost, p1Heat, p1State, p1PosX, p1PosZ, p1MoveX, p1MoveY,
                p2Hp, p2Boost, p2Heat, p2State, p2PosX, p2PosZ, p2MoveX, p2MoveY, distance, band));
            _dirty = true;
        }

        private static void ReadMech(GameObject mech, out float hp, out float boost, out float heat, out string state,
            out float posX, out float posZ, out float moveX, out float moveY)
        {
            hp = 0f; boost = 0f; heat = 0f; state = "none";
            posX = 0f; posZ = 0f; moveX = 0f; moveY = 0f;
            if (mech == null) return;

            Health h = mech.GetComponent<Health>();
            BoostGauge bg = mech.GetComponent<BoostGauge>();
            HeatGauge hg = mech.GetComponent<HeatGauge>();
            MechStateMachine sm = mech.GetComponent<MechStateMachine>();
            InputReader ir = mech.GetComponent<InputReader>();

            if (h != null) hp = h.CurrentHealth;
            if (bg != null) boost = bg.CurrentBoost;
            if (hg != null) heat = hg.CurrentHeat;
            if (sm != null) state = sm.CurrentStateName;
            if (ir != null)
            {
                moveX = ir.MoveInput.x;
                moveY = ir.MoveInput.y;
            }
            posX = mech.transform.position.x;
            posZ = mech.transform.position.z;
        }

        private void WriteMatchSummary(bool incomplete)
        {
            if (_summarySb == null) return;
            string match = _matchIndex + (_matchMode == "practice" ? "_practice" : "") + (incomplete ? "_incomplete" : "");

            for (int p = 0; p < 2; p++)
            {
                Dictionary<string, int> counts = _counts[p];
                foreach (string key in SummaryKeys)
                {
                    counts.TryGetValue(key, out int value);
                    _summarySb.AppendLine($"{match},P{p + 1},{key},{value}");
                }
                // 고정 목록 밖의 세부 항목(punch_guarded, slam_parried 등)도 뒤에 붙인다
                List<string> extras = new List<string>();
                foreach (string key in counts.Keys)
                {
                    if (Array.IndexOf(SummaryKeys, key) < 0) extras.Add(key);
                }
                extras.Sort(StringComparer.Ordinal);
                foreach (string key in extras)
                {
                    _summarySb.AppendLine($"{match},P{p + 1},{key},{counts[key]}");
                }
            }

            for (int b = 0; b < BandNames.Length; b++)
            {
                _summarySb.AppendLine($"{match},-,time_{BandNames[b]}_sec,{_bandTime[b]:F1}");
            }

            _matchSummaryWritten = true;
            _dirty = true;
        }

        private void ResetMatchStats()
        {
            _counts[0].Clear();
            _counts[1].Clear();
            for (int b = 0; b < _bandTime.Length; b++) _bandTime[b] = 0f;
            _matchSummaryWritten = false;
        }

        private void SaveLog()
        {
            if (!_isLogging || !_dirty) return;

            // 매치 도중 종료면 지금까지의 요약을 미완료로 남긴다 (연습은 끝이 없으니 미완료 표시 없이)
            bool matchInProgress = GameManager.Instance == null || !GameManager.Instance.IsMatchOver;
            if (matchInProgress && !_matchSummaryWritten && HasAnyStats())
            {
                WriteMatchSummary(_matchMode != "practice");
            }

            // 매치 종료마다 전체를 다시 쓴다 (도중에 꺼져도 마지막 매치까지는 남게)
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath));
                File.WriteAllText(_filePath, _sb.ToString());
                File.WriteAllText(_eventFilePath, _eventSb.ToString());
                File.WriteAllText(_summaryFilePath, _summarySb.ToString());
                _dirty = false;
            }
            catch (Exception e)
            {
                // 쓰기 권한 없는 폴더(예: Program Files)에 빌드를 두면 여기로 온다. 게임은 계속 돌게 하고 기록만
                ErrorLog.Report(e, "PlayLogger.SaveLog " + _filePath);
            }
        }

        /// <summary>
        /// 에디터: 프로젝트 루트/Logs/PlayLogs (Assets 밖이라 임포트되지 않고 .gitignore 대상).
        /// Windows 빌드: dataPath가 "실행 파일 폴더/이름_Data"라 그 부모 = 실행 파일 옆/Logs/PlayLogs.
        /// </summary>
        private string LogFolder()
        {
            DirectoryInfo parent = Directory.GetParent(Application.dataPath);
            string root = parent != null ? parent.FullName : Application.persistentDataPath;
            string folder = Path.Combine(root, logFolderName);
            return string.IsNullOrEmpty(logSubfolderName) ? folder : Path.Combine(folder, logSubfolderName);
        }

        private bool HasAnyStats()
        {
            if (_counts[0].Count > 0 || _counts[1].Count > 0) return true;
            foreach (float t in _bandTime)
            {
                if (t > 0f) return true;
            }
            return false;
        }
    }
}
