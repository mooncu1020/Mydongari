using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using MyDongari.Combat;
using MyDongari.Combat.Skills;
using MyDongari.Mech;
using MyDongari.Movement;

namespace MyDongari.Core
{
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        /// <summary>매치(전체 승부)가 끝났을 때 발생. 인자는 승리한 플레이어 번호(1 또는 2).
        /// HUD/게임오버 화면 등은 여기 구독해서 처리하면 됨.</summary>
        public event Action<int> OnMatchOver;

        /// <summary>라운드가 끝났을 때 발생. 인자는 라운드 승자(1 또는 2), 무승부면 0.</summary>
        public event Action<int> OnRoundOver;

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

        [Header("리스폰 설정")]
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
            StartRound();
        }

        private void Update()
        {
            if (!_roundActive) return;

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

            _roundActive = true;

            // 앵커 클로는 상대 쪽 잠금을 잡고 있을 수 있어서, 두 기체 모두 클로부터 먼저 놓게 한 뒤 나머지를 리셋한다
            ReleaseGrabs(player1);
            ReleaseGrabs(player2);
            ResetMech(player1, player1SpawnPoint);
            ResetMech(player2, player2SpawnPoint);
        }

        private static void ReleaseGrabs(GameObject mech)
        {
            if (mech == null) return;
            AnchorClaw claw = mech.GetComponent<AnchorClaw>();
            if (claw != null) claw.ResetForRound();
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
            if (lockOn != null) lockOn.ForceRelease();

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
            OnRoundOver?.Invoke(winner);

            if (winner == 0)
            {
                // 무승부: 승수 증가 없이 다음 라운드
                Invoke(nameof(StartRound), respawnDelay);
                return;
            }

            if (winner == 1) _player1Wins++;
            else _player2Wins++;

            if (_player1Wins >= roundsToWin || _player2Wins >= roundsToWin)
            {
                IsMatchOver = true;
                MatchWinner = _player1Wins >= roundsToWin ? 1 : 2;
                Debug.Log($"[GameManager] 매치 종료. 승자: Player{MatchWinner}");
                OnMatchOver?.Invoke(MatchWinner);
            }
            else
            {
                Invoke(nameof(StartRound), respawnDelay);
            }
        }
    }
}