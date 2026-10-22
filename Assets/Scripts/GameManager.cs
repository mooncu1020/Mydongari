using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using MyDongari.Combat;
using MyDongari.Combat.Skills;
using MyDongari.Mech;
using MyDongari.Movement;
using MyDongari.Input;
using MyDongari.Feedback;

namespace MyDongari.Core
{
    /// <summary>매치 진행 단계. Countdown(입력 잠금) → Fight → RoundOver → 다음 라운드 Countdown … → MatchOver.</summary>
    public enum MatchPhase
    {
        Countdown,
        Fight,
        RoundOver,
        MatchOver
    }

    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        /// <summary>매치(전체 승부)가 끝났을 때 발생. 인자는 승리한 플레이어 번호(1 또는 2).
        /// HUD/게임오버 화면 등은 여기 구독해서 처리하면 됨.</summary>
        public event Action<int> OnMatchOver;

        /// <summary>라운드가 끝났을 때 발생. 인자는 라운드 승자(1 또는 2), 무승부면 0.</summary>
        public event Action<int> OnRoundOver;

        /// <summary>라운드 리셋 직후(카운트다운 시작) 발생. 인자는 라운드 번호(1부터, 무승부 재경기도 1 증가).</summary>
        public event Action<int> OnRoundStarted;

        /// <summary>카운트다운이 끝나고 입력이 풀리는 순간("FIGHT").</summary>
        public event Action OnFight;

        /// <summary>재매치로 새 매치가 시작될 때 (첫 매치 포함).</summary>
        public event Action OnMatchStarted;

        public bool IsMatchOver { get; private set; }
        public int MatchWinner { get; private set; }

        [Header("모드")]
        [Tooltip("켜면 2인 대전(P1 vs P2), 끄면 P1 vs 더미 1인 테스트. Dummy Opponent가 비어 있으면 무시")]
        [SerializeField] private bool versusMode = true;

        [Header("1인(더미) 모드")]
        [Tooltip("1인 모드에서 Player2 대신 상대가 될 더미. 2인 모드에선 꺼 둔다")]
        [SerializeField] private GameObject dummyOpponent;
        [Tooltip("2인 모드에서만 쓰는 오브젝트(P2 기체, P2 HUD 캔버스 등). 1인 모드에서 끈다")]
        [SerializeField] private GameObject[] versusOnlyObjects;
        [Tooltip("1인 모드에서 화면 전체를 쓰게 할 P1 카메라")]
        [SerializeField] private UnityEngine.Camera player1Camera;

        [Header("라운드 설정")]
        [SerializeField] private int roundsToWin = 2;

        [Header("라운드 연출")]
        [Tooltip("라운드 시작 카운트다운(초). 이 동안 두 기체 모두 입력을 받지 않는다")]
        [SerializeField] private float countdownDuration = 3f;
        [Tooltip("FIGHT 글자를 띄워 두는 시간(초). 입력은 FIGHT와 동시에 풀린다")]
        [SerializeField] private float fightBannerDuration = 1f;

        [Header("리스폰 설정")]
        [Tooltip("라운드가 끝나고 다음 라운드 카운트다운이 시작될 때까지(초)")]
        [SerializeField] private float respawnDelay = 3f;
        [SerializeField] private Transform player1SpawnPoint;
        [SerializeField] private Transform player2SpawnPoint;

        [Header("참조")]
        [SerializeField] private GameObject player1;
        [SerializeField] private GameObject player2;

        private int _player1Wins = 0;
        private int _player2Wins = 0;
        private bool _roundActive = false;
        public bool IsRoundActive => _roundActive;

        public MatchPhase Phase { get; private set; } = MatchPhase.Countdown;
        public int RoundNumber { get; private set; }
        /// <summary>직전 라운드 승자. 0이면 무승부.</summary>
        public int LastRoundWinner { get; private set; }
        /// <summary>현재 단계(카운트다운·라운드 종료 대기)의 남은 시간(초).</summary>
        public float PhaseTimeRemaining => _phaseTimer;
        public bool ShowFightBanner => Phase == MatchPhase.Fight && _fightBannerTimer > 0f;

        private float _phaseTimer = 0f;
        private float _fightBannerTimer = 0f;

        public int RoundsToWin => roundsToWin;
        public int Player1Wins => _player1Wins;
        public int Player2Wins => _player2Wins;
        public bool VersusMode => versusMode && dummyOpponent != null;
        /// <summary>현재 대결 중인 기체. 1인 모드면 Player2는 더미.</summary>
        public GameObject Player1 => player1;
        public GameObject Player2 => player2;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            ApplyMode();
            LinkOpponents();
            // 씬에 안 붙여 둔 경우에도 히트스톱이 동작하게 (수치 조정은 인스펙터에서 직접 추가해서)
            if (GetComponent<HitStop>() == null) gameObject.AddComponent<HitStop>();
            // 상태 피드백(팝업·틴트·효과음)도 같은 방식. 효과음 클립을 넣으려면 씬에 직접 붙여 둔다
            if (GetComponent<CombatFeedback>() == null) gameObject.AddComponent<CombatFeedback>();
            // 빌드 운영(ESC 길게 종료·커서·전체 화면). 에디터에선 기본으로 아무것도 안 한다
            if (GetComponent<AppRuntime>() == null) gameObject.AddComponent<AppRuntime>();
        }

        /// <summary>
        /// 2인/1인 모드 전환. 더미가 지정돼 있지 않으면(Versus 세팅 전 씬) 아무것도 바꾸지 않는다.
        /// </summary>
        private void ApplyMode()
        {
            if (dummyOpponent == null) return;

            if (versusMode)
            {
                dummyOpponent.SetActive(false);
                return;
            }

            if (versusOnlyObjects != null)
            {
                foreach (GameObject go in versusOnlyObjects)
                {
                    if (go != null) go.SetActive(false);
                }
            }
            dummyOpponent.SetActive(true);
            player2 = dummyOpponent;
            if (player1Camera != null) player1Camera.rect = new Rect(0f, 0f, 1f, 1f);
        }

        /// <summary>
        /// player1·player2를 서로의 상대로 연결한다. 전투 코드는 태그 대신 이 참조로 상대를 찾는다.
        /// 인스펙터에서 Opponent를 직접 지정해 둔 경우(더미 테스트 등)는 덮어쓰지 않는다.
        /// </summary>
        private void LinkOpponents()
        {
            if (player1 == null || player2 == null) return;

            MechIdentity id1 = MechIdentity.GetOrAdd(player1);
            MechIdentity id2 = MechIdentity.GetOrAdd(player2);
            id1.SetPlayerIndex(1);
            id2.SetPlayerIndex(2);
            if (id1.Opponent == null) id1.SetOpponent(id2);
            if (id2.Opponent == null) id2.SetOpponent(id1);
        }

        private void Start()
        {
            StartMatch();
        }

        /// <summary>승수·매치 상태를 비우고 1라운드부터. 재매치는 씬 리로드 없이 이걸로 한다.</summary>
        private void StartMatch()
        {
            _player1Wins = 0;
            _player2Wins = 0;
            IsMatchOver = false;
            MatchWinner = 0;
            LastRoundWinner = 0;
            RoundNumber = 0;
            OnMatchStarted?.Invoke();
            StartRound();
        }

        private void Update()
        {
            switch (Phase)
            {
                case MatchPhase.Countdown:
                    _phaseTimer -= Time.deltaTime;
                    if (_phaseTimer <= 0f) BeginFight();
                    break;

                case MatchPhase.Fight:
                    if (_fightBannerTimer > 0f) _fightBannerTimer -= Time.deltaTime;
                    CheckRoundEnd();
                    break;

                case MatchPhase.RoundOver:
                    _phaseTimer -= Time.deltaTime;
                    if (_phaseTimer <= 0f) StartRound();
                    break;

                case MatchPhase.MatchOver:
                    if (AnyStartPressed()) StartMatch();
                    break;
            }
        }

        private void CheckRoundEnd()
        {
            Health p1Health = player1 != null ? player1.GetComponent<Health>() : null;
            Health p2Health = player2 != null ? player2.GetComponent<Health>() : null;

            bool p1Dead = p1Health != null && p1Health.IsDead;
            bool p2Dead = p2Health != null && p2Health.IsDead;

            if (p1Dead && p2Dead)
            {
                // 같은 프레임 동시 사망: 무승부, 승수 없이 다시
                RoundEnd(winner: 0);
            }
            else if (p1Dead)
            {
                RoundEnd(winner: 2);
            }
            else if (p2Dead)
            {
                RoundEnd(winner: 1);
            }
        }

        private void StartRound()
        {
            if (IsMatchOver) return;

            RoundNumber++;
            _roundActive = false;
            Phase = MatchPhase.Countdown;
            _phaseTimer = countdownDuration;
            _fightBannerTimer = 0f;

            // 앵커 클로는 상대 쪽 잠금을 잡고 있을 수 있고, 런지는 상태 전환 시 "빗나감"으로 끝나므로
            // 두 기체 모두 진행 중인 기동부터 조용히 끊은 뒤 나머지를 리셋한다
            StopActiveMoves(player1);
            StopActiveMoves(player2);
            ResetMech(player1, player1SpawnPoint);
            ResetMech(player2, player2SpawnPoint);
            SetPlayersInputLocked(true);

            OnRoundStarted?.Invoke(RoundNumber);
        }

        private void BeginFight()
        {
            Phase = MatchPhase.Fight;
            _roundActive = true;
            _fightBannerTimer = fightBannerDuration;
            SetPlayersInputLocked(false);
            OnFight?.Invoke();
        }

        private void SetPlayersInputLocked(bool locked)
        {
            SetInputLocked(player1, locked);
            SetInputLocked(player2, locked);
        }

        private static void SetInputLocked(GameObject mech, bool locked)
        {
            if (mech == null) return;
            InputReader input = mech.GetComponent<InputReader>();
            if (input != null) input.SetInputLocked(locked);
        }

        /// <summary>매치 종료 화면에서 아무 플레이어의 시작 버튼 (각자 페어링된 패드 Start / 키보드 Enter).</summary>
        private bool AnyStartPressed()
        {
            return IsStartPressed(player1) || IsStartPressed(player2);
        }

        private static bool IsStartPressed(GameObject mech)
        {
            if (mech == null || !mech.activeInHierarchy) return false;
            InputReader input = mech.GetComponent<InputReader>();
            return input != null && input.StartPressed;
        }

        private static void StopActiveMoves(GameObject mech)
        {
            if (mech == null) return;
            AnchorClaw claw = mech.GetComponent<AnchorClaw>();
            if (claw != null) claw.ResetForRound();
            ThrusterLunge lunge = mech.GetComponent<ThrusterLunge>();
            if (lunge != null) lunge.ResetForRound();
        }

        /// <summary>
        /// 기체 하나를 라운드 시작 상태로 되돌린다. 스폰 포인트가 없으면 위치·회전만 그대로 두고 나머지는 리셋.
        /// 더미처럼 일부 컴포넌트가 없는 기체도 있으니 전부 null 체크.
        /// </summary>
        private static void ResetMech(GameObject mech, Transform spawnPoint)
        {
            if (mech == null) return;

            Rigidbody rb = mech.GetComponent<Rigidbody>();
            if (spawnPoint != null)
            {
                mech.transform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);
                if (rb != null)
                {
                    // 트랜스폼만 옮기면 다음 물리 스텝에 리지드바디가 예전 자세로 덮어쓸 수 있다
                    rb.position = spawnPoint.position;
                    rb.rotation = spawnPoint.rotation;
                }
            }
            if (rb != null && !rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            ExternalControlLock controlLock = mech.GetComponent<ExternalControlLock>();
            if (controlLock != null) controlLock.ResetLock();

            Health health = mech.GetComponent<Health>();
            if (health != null) health.ResetHealth();

            BoostGauge boost = mech.GetComponent<BoostGauge>();
            if (boost != null) boost.ResetForRound();

            HeatGauge heat = mech.GetComponent<HeatGauge>();
            if (heat != null) heat.ResetForRound();

            MeleeSystem melee = mech.GetComponent<MeleeSystem>();
            if (melee != null) melee.ResetForRound();

            ImpactSlam slam = mech.GetComponent<ImpactSlam>();
            if (slam != null) slam.ResetForRound();

            LockOnSystem lockOn = mech.GetComponent<LockOnSystem>();
            if (lockOn != null) lockOn.ResetForRound();

            MechStateMachine stateMachine = mech.GetComponent<MechStateMachine>();
            if (stateMachine != null) stateMachine.ResetForRound();

            // 카메라 시점도 스폰 방향으로 (안 하면 Idle이 기체를 예전 카메라 방향으로 돌려버림)
            MechIdentity identity = mech.GetComponent<MechIdentity>();
            MyDongari.Camera.MechCamera mechCamera = identity != null ? identity.MechCamera : null;
            if (mechCamera != null) mechCamera.ResetView(mech.transform.eulerAngles.y);
        }

        private void RoundEnd(int winner)
        {
            _roundActive = false;
            LastRoundWinner = winner;
            // 라운드가 끝난 뒤엔 살아남은 쪽도 멈춘다 (다음 라운드 리셋 전 추가 타격 방지)
            SetPlayersInputLocked(true);

            if (winner == 1) _player1Wins++;
            else if (winner == 2) _player2Wins++;
            // winner == 0: 무승부, 승수 증가 없이 다음 라운드

            OnRoundOver?.Invoke(winner);

            if (_player1Wins >= roundsToWin || _player2Wins >= roundsToWin)
            {
                IsMatchOver = true;
                MatchWinner = _player1Wins >= roundsToWin ? 1 : 2;
                Phase = MatchPhase.MatchOver;
                OnMatchOver?.Invoke(MatchWinner);
            }
            else
            {
                Phase = MatchPhase.RoundOver;
                _phaseTimer = respawnDelay;
            }
        }
    }
}