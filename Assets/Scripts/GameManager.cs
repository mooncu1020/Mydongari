using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using MyDongari.Combat;

namespace MyDongari.Core
{
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        /// <summary>매치(전체 승부)가 끝났을 때 발생. 인자는 승리한 플레이어 번호(1 또는 2).
        /// HUD/게임오버 화면 등은 여기 구독해서 처리하면 됨.</summary>
        public event Action<int> OnMatchOver;

        public bool IsMatchOver { get; private set; }
        public int MatchWinner { get; private set; }

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
            if (IsMatchOver) return;

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
        }

        private void RoundEnd(int winner)
        {
            _roundActive = false;

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