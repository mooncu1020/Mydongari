using UnityEngine;
using UnityEngine.SceneManagement;
using MyDongari.Combat;

namespace MyDongari.Core
{
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

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

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
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

            if (p1Health != null && p1Health.IsDead)
            {
                RoundEnd(winner: 2);
            }
            else if (p2Health != null && p2Health.IsDead)
            {
                RoundEnd(winner: 1);
            }
        }

        private void StartRound()
        {
            _roundActive = true;

            if (player1 != null && player1SpawnPoint != null)
            {
                player1.transform.position = player1SpawnPoint.position;
                Health h = player1.GetComponent<Health>();
                if (h != null) h.ResetHealth();
            }

            if (player2 != null && player2SpawnPoint != null)
            {
                player2.transform.position = player2SpawnPoint.position;
                Health h = player2.GetComponent<Health>();
                if (h != null) h.ResetHealth();
            }

            Debug.Log("라운드 시작");
        }

        private void RoundEnd(int winner)
        {
            _roundActive = false;

            if (winner == 1) _player1Wins++;
            else _player2Wins++;

            Debug.Log($"라운드 종료 — P{winner} 승리 / P1: {_player1Wins} P2: {_player2Wins}");

            if (_player1Wins >= roundsToWin)
            {
                Debug.Log("Player 1 WIN");
            }
            else if (_player2Wins >= roundsToWin)
            {
                Debug.Log("Player 2 WIN");
            }
            else
            {
                Invoke(nameof(StartRound), respawnDelay);
            }
        }
    }
}