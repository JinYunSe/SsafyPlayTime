using UnityEngine;

namespace S14P21D104.Character
{
    public class DummyHealth : MonoBehaviour
    {
        [Header("Health Settings")]
        public float maxHp = 100f;
        public float currentHp;

        // 화상 데미지 변수들
        private bool _isBurning = false;
        private float _burnTimer = 0f;
        private float _burnDamagePerTick; // 1초당 데미지
        private float _lastBurnTickTime = 0f; // 마지막 틱 데미지 들어간 시간 기록
        private float _timeSinceLastDirectHit = 100f; // 마지막으로 직접 화염을 맞은 시간으로부터 경과한 시간

        [Header("Burn Effect Settings")]
        [Tooltip("불탈 때 캐릭터를 감싸는 이펙트 프리팹 (예: ElementalAuraFire)")]
        public GameObject burnEffectPrefab;
        private GameObject _activeBurnEffect;

        private void Start()
        {
            currentHp = maxHp;
        }

        private void Update()
        {
            _timeSinceLastDirectHit += Time.deltaTime;

            if (_isBurning && currentHp > 0)
            {
                // 화염방사기에서 직격으로 맞고 있지 않을 때(마지막 피격 후 0.5초 이상 경과)만 화상 딜이 들어갑니다.
                if (_timeSinceLastDirectHit > 0.5f)
                {
                    _burnTimer -= Time.deltaTime;

                    // 1초 단위로 틱 데미지 부여
                    if (Time.time - _lastBurnTickTime >= 1f)
                    {
                        _lastBurnTickTime = Time.time;
                        TakeDamage(_burnDamagePerTick, true); // 화상 피해
                    }

                    if (_burnTimer <= 0)
                    {
                        _isBurning = false;
                        Debug.Log($"[{gameObject.name}] 불이 꺼졌습니다.");

                        // 화상 이펙트 제거
                        if (_activeBurnEffect != null)
                        {
                            Destroy(_activeBurnEffect);
                            _activeBurnEffect = null;
                        }
                    }
                }
                else
                {
                    // 직격 중일 때는 화상 타이머를 멈추고(혹은 갱신된 상태로 유지), 
                    // 다음 화상 틱이 직격 종료 1초 후에 들어가도록 타이머를 동기화합니다.
                    _lastBurnTickTime = Time.time;
                }
            }
        }

        public void TakeDamage(float amount, bool isBurnAttack = false)
        {
            if (currentHp <= 0)
                return; // 이미 죽음

            currentHp -= amount;

            if (isBurnAttack)
                Debug.Log($"[{gameObject.name}] 화상 틱 데미지! {amount} 받음! (남은 체력: {currentHp})");
            else
                Debug.Log($"[{gameObject.name}] 앗 뜨거! 화염 피해 {amount} 받음! (남은 체력: {currentHp})");

            if (currentHp <= 0)
            {
                currentHp = 0;
                Debug.Log($"[{gameObject.name}] 체력이 0이 되어 파괴되었습니다!");

                // 죽을 때 이펙트도 같이 제거
                if (_activeBurnEffect != null)
                {
                    Destroy(_activeBurnEffect);
                }

                Destroy(gameObject);
            }
        }

        // 화상 효과 부여 메서드 (화염방사기 직격 시마다 호출됨)
        public void ApplyBurn(float duration, float damagePerSecond)
        {
            if (currentHp <= 0)
                return;

            // 직격을 맞았으므로 타이머 리셋
            _timeSinceLastDirectHit = 0f;

            // 불타는 시간 갱신
            _burnTimer = duration;
            _burnDamagePerTick = damagePerSecond;

            if (!_isBurning)
            {
                _isBurning = true;
                Debug.Log($"[{gameObject.name}] 불이 붙었습니다! 직격이 끝나면 {duration}초 동안 초당 {damagePerSecond}의 화상 피해를 받습니다.");

                // 화상 이펙트 띄우기 (캐릭터를 감싸게 캐릭터의 자식으로 지정)
                if (burnEffectPrefab != null && _activeBurnEffect == null)
                {
                    // 부모 스케일에 영향을 받아 찌그러지거나 에러나는 것을 막기 위해 오프셋과 스케일 초기화
                    _activeBurnEffect = Instantiate(burnEffectPrefab, transform);
                    _activeBurnEffect.transform.localPosition = new Vector3(0f, 0.5f, 0f); // 바닥에 파묻히지 않게 살짝 위로 띄움
                    _activeBurnEffect.transform.localRotation = Quaternion.identity;
                    _activeBurnEffect.transform.localScale = Vector3.one; // 크기 초기화
                }
            }
        }
    }
}
