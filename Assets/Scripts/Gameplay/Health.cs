using UnityEngine;

namespace ShiftFour
{
    public sealed class Health : MonoBehaviour, IDamageable
    {
        [SerializeField] private float maximum = 100f;
        [SerializeField] private bool regenerate;
        [SerializeField] private float recoveryDelay = 3f;
        [SerializeField] private float recoveryPerSecond = 8f;

        private float current;
        private float lastHitTime = float.NegativeInfinity;

        public float Current => current;
        public float Maximum => maximum;
        public bool IsAlive => current > 0f;
        public event System.Action<Health> Died;

        public void Configure(float max, bool playerRecovery)
        {
            maximum = Mathf.Max(1f, max);
            regenerate = playerRecovery;
            current = maximum;
        }

        private void Awake() => current = maximum;

        private void Update()
        {
            if (regenerate && IsAlive && Time.time - lastHitTime >= recoveryDelay)
                current = Mathf.Min(maximum, current + recoveryPerSecond * Time.deltaTime);
        }

        public void TakeDamage(float amount)
        {
            if (!IsAlive || amount <= 0f) return;
            current = Mathf.Max(0f, current - amount);
            lastHitTime = Time.time;
            if (!IsAlive) Died?.Invoke(this);
        }
    }
}
