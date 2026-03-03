using UnityEngine;

namespace SSAFYPlayTime
{
    [DisallowMultipleComponent]
    public sealed class PrototypeDamageDummy : MonoBehaviour
    {
        [SerializeField] private float maxHp = 300f;
        [SerializeField] private float currentHp = 300f;

        private int _hitCount;
        private float _totalDamageTaken;
        private float _lastDamageAmount;
        private string _lastDamageSource = "None";
        private float _lastHitTime;

        public float MaxHp => maxHp;
        public float CurrentHp => currentHp;
        public int HitCount => _hitCount;
        public float TotalDamageTaken => _totalDamageTaken;
        public float LastDamageAmount => _lastDamageAmount;
        public string LastDamageSource => _lastDamageSource;
        public float LastHitTime => _lastHitTime;

        public void SetMaxHp(float value)
        {
            maxHp = Mathf.Max(1f, value);
            currentHp = Mathf.Clamp(currentHp, 0f, maxHp);

            if (currentHp <= 0f)
            {
                currentHp = maxHp;
            }
        }

        public void ResetDummy()
        {
            currentHp = maxHp;
            _hitCount = 0;
            _totalDamageTaken = 0f;
            _lastDamageAmount = 0f;
            _lastDamageSource = "None";
            _lastHitTime = 0f;
        }

        public void ApplyDamage(float amount, string source)
        {
            var clamped = Mathf.Max(0f, amount);
            if (clamped <= 0f)
            {
                return;
            }

            currentHp = Mathf.Max(0f, currentHp - clamped);
            _hitCount++;
            _totalDamageTaken += clamped;
            _lastDamageAmount = clamped;
            _lastDamageSource = string.IsNullOrWhiteSpace(source) ? "Unknown" : source;
            _lastHitTime = Time.time;

            if (currentHp <= 0f)
            {
                // 테스트 루프가 끊기지 않도록 즉시 체력을 복구한다.
                currentHp = maxHp;
            }
        }
    }
}
