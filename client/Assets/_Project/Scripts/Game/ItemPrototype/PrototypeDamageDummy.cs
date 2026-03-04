using UnityEngine;

namespace SSAFYPlayTime
{
    [DisallowMultipleComponent]
    public sealed class PrototypeDamageDummy : MonoBehaviour
    {
        [SerializeField] private float maxHp = 300f;
        [SerializeField] private float currentHp = 300f;
        [SerializeField] private float maxStunGauge = 100f;
        [SerializeField] private float currentStunGauge;
        [SerializeField] private float stunRecoveryPerSec = 6f;
        [SerializeField] private float stunDurationSec = 1.5f;
        [SerializeField] private bool isStunned;

        private int _hitCount;
        private float _totalDamageTaken;
        private float _totalStunTaken;
        private float _lastDamageAmount;
        private float _lastStunDamageAmount;
        private string _lastDamageSource = "None";
        private float _lastHitTime;
        private int _stunCount;
        private int _deathCount;
        private float _stunEndTime;

        public float MaxHp => maxHp;
        public float CurrentHp => currentHp;
        public float MaxStunGauge => maxStunGauge;
        public float CurrentStunGauge => currentStunGauge;
        public bool IsStunned => isStunned;
        public int HitCount => _hitCount;
        public float TotalDamageTaken => _totalDamageTaken;
        public float TotalStunTaken => _totalStunTaken;
        public float LastDamageAmount => _lastDamageAmount;
        public float LastStunDamageAmount => _lastStunDamageAmount;
        public string LastDamageSource => _lastDamageSource;
        public float LastHitTime => _lastHitTime;
        public int StunCount => _stunCount;
        public int DeathCount => _deathCount;

        public void SetMaxHp(float value)
        {
            maxHp = Mathf.Max(1f, value);
            currentHp = Mathf.Clamp(currentHp, 0f, maxHp);

            if (currentHp <= 0f)
            {
                currentHp = maxHp;
            }
        }

        public void SetMaxStunGauge(float value)
        {
            maxStunGauge = Mathf.Max(1f, value);
            currentStunGauge = Mathf.Clamp(currentStunGauge, 0f, maxStunGauge);
        }

        private void Update()
        {
            if (isStunned)
            {
                if (Time.time >= _stunEndTime)
                {
                    isStunned = false;
                    currentStunGauge = 0f;
                }

                return;
            }

            if (currentStunGauge <= 0f)
            {
                return;
            }

            currentStunGauge = Mathf.Max(0f, currentStunGauge - stunRecoveryPerSec * Time.deltaTime);
        }

        public void ResetDummy()
        {
            currentHp = maxHp;
            currentStunGauge = 0f;
            isStunned = false;
            _stunEndTime = 0f;
            _hitCount = 0;
            _totalDamageTaken = 0f;
            _totalStunTaken = 0f;
            _lastDamageAmount = 0f;
            _lastStunDamageAmount = 0f;
            _lastDamageSource = "None";
            _lastHitTime = 0f;
            _stunCount = 0;
            _deathCount = 0;
        }

        public void ApplyDamage(float amount, string source)
        {
            ApplyDamage(amount, 0f, source);
        }

        public void ApplyDamage(float hpDamage, float stunDamage, string source)
        {
            var clampedHp = Mathf.Max(0f, hpDamage);
            var clampedStun = Mathf.Max(0f, stunDamage);
            if (clampedHp <= 0f && clampedStun <= 0f)
            {
                return;
            }

            currentHp = Mathf.Max(0f, currentHp - clampedHp);
            currentStunGauge = Mathf.Min(maxStunGauge, currentStunGauge + clampedStun);
            _hitCount++;
            _totalDamageTaken += clampedHp;
            _totalStunTaken += clampedStun;
            _lastDamageAmount = clampedHp;
            _lastStunDamageAmount = clampedStun;
            _lastDamageSource = string.IsNullOrWhiteSpace(source) ? "Unknown" : source;
            _lastHitTime = Time.time;

            if (!isStunned && currentStunGauge >= maxStunGauge)
            {
                isStunned = true;
                _stunCount++;
                _stunEndTime = Time.time + Mathf.Max(0.1f, stunDurationSec);
            }

            if (currentHp <= 0f)
            {
                // 테스트 루프가 끊기지 않도록 즉시 체력을 복구한다.
                _deathCount++;
                currentHp = maxHp;
            }
        }
    }
}
