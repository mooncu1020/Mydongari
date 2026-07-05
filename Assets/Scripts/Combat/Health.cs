using UnityEngine;
using MyDongari.Movement;

namespace MyDongari.Combat
{
    public class Health : MonoBehaviour
    {
        [SerializeField] private float maxHealth = 100f;
        public float CurrentHealth { get; private set; }
        public bool IsDead { get; private set; }

        private MechStateMachine _stateMachine;

        private void Awake()
        {
            CurrentHealth = maxHealth;
            _stateMachine = GetComponent<MechStateMachine>();
        }

        public void TakeDamage(float amount)
        {
            if (IsDead) return;
            CurrentHealth = Mathf.Max(CurrentHealth - amount, 0f);

            if (CurrentHealth <= 0f)
            {
                IsDead = true;
                OnDeath();
            }
        }

        private void OnDeath()
        {
            if (_stateMachine != null)
            {
                _stateMachine.ChangeState(_stateMachine.IdleState);
                _stateMachine.enabled = false;
            }
        }

        public void ResetHealth()
        {
            CurrentHealth = maxHealth;
            IsDead = false;
            gameObject.SetActive(true);
            if (_stateMachine != null)
            {
                _stateMachine.enabled = true;
                _stateMachine.ChangeState(_stateMachine.IdleState);
            }
        }
    }
}