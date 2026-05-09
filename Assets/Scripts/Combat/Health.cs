using UnityEngine;

namespace MyDongari.Combat
{
    public class Health : MonoBehaviour
    {
        [SerializeField] private float maxHealth = 100f;
        public float CurrentHealth { get; private set; }
        public bool IsDead { get; private set; }

        private void Awake()
        {
            CurrentHealth = maxHealth;
        }

        public void TakeDamage(float amount)
        {
            if (IsDead) return;
            CurrentHealth = Mathf.Max(CurrentHealth - amount, 0f);
            Debug.Log(gameObject.name + " HP: " + CurrentHealth);

            if (CurrentHealth <= 0f)
            {
                IsDead = true;
                Debug.Log(gameObject.name + " 사망");
                gameObject.SetActive(false);
            }
        }
    }
}