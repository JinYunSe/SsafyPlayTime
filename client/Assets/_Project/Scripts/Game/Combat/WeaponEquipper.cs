using Fusion;
using UnityEngine;

namespace S14P21D104.Character
{
    public class WeaponEquipper : MonoBehaviour
    {
        [Header("Weapon Settings")]
        [Tooltip("손에 쥐어줄 무기 조립체 텍스처 프리팹 (예: 화면에 보일 무기)")]
        public GameObject weaponPrefab;

        [Tooltip("!!!필수!!! 무기를 장착할 실제 '오른손' 오브젝트를 드래그 앤 드롭 하세요. (비워두면 자동 검색하지만, 오류가 발생할 수 있습니다.)")]
        public Transform targetHandTransform;

        [Tooltip("자동 검색용 이름 필터")]
        public string[] searchHandNames = { "RightHand", "Hand_R", "R_Hand", "Right Hand", "mixamorig:RightHand" };

        [Tooltip("오른손 중심으로부터 무기가 들릴 위치 오프셋")]
        public Vector3 positionOffset = new Vector3(0.05f, 0.05f, 0.05f);

        [Tooltip("오른손 중심으로부터 무기 기본 각도 오프셋")]
        public Vector3 rotationOffset = new Vector3(-90f, -90f, 90f);

        [Header("Effect Settings")]
        [Tooltip("총구 발사 이펙트 프리팹 (예: Flamethrower)")]
        public GameObject muzzleEffectPrefab;

        [Tooltip("무기 중심으로부터 총구 이펙트가 붙을 위치 오프셋")]
        public Vector3 effectPositionOffset = new Vector3(0f, 0f, 0.5f);

        [Tooltip("총구 이펙트 특유의 기본 각도 오프셋")]
        public Vector3 effectRotationOffset = new Vector3(0f, 0f, 0f);

        [Header("Combat Output")]
        public float maxUsageTime = 5f;
        public float damageTickInterval = 0.25f;
        public float damagePerTick = 5f;
        public float attackRange = 7f;
        public float attackRadius = 1.5f;

        [Header("Aim Lock Settings")]
        [Tooltip("달리기 혹은 점프 시 무기가 흔들려도 강제로 캐릭터 앞(정면)을 보게 고정할지 여부")]
        public bool forceForwardRotation = true;

        [Tooltip("강제 고정 시 무기의 세부 각도 보정값")]
        public Vector3 forwardRotationOffset = new Vector3(-90f, -90f, 90f);

        // 내부 변수들
        private float _currentUsageTime = 0f;
        private float _lastDamageTime = 0f;

        private Animator _animator;
        private GameObject _equippedWeapon;
        private GameObject _equippedEffect;
        private Transform _anchorHand; // 최적화를 위해 캐싱해둔 손 뼈대

        public bool IsWeaponEquipped => _equippedWeapon != null;

        private void Start()
        {
            _animator = GetComponentInChildren<Animator>();

            if (targetHandTransform == null)
            {
                Debug.LogWarning("WeaponEquipper: [targetHandTransform]이 비어있어 코드가 오른손을 자동 추적합니다. 가능하면 인스펙터에서 직접 뼈대를 할당해주세요.");
            }

            EquipWeapon();
        }

        private Transform GetResolvingHand()
        {
            if (targetHandTransform != null)
                return targetHandTransform;

            Transform[] children = transform.GetComponentsInChildren<Transform>(true);
            foreach (var t in children)
            {
                foreach (var expected in searchHandNames)
                {
                    if (string.Equals(t.name, expected, System.StringComparison.OrdinalIgnoreCase))
                        return t;
                }
            }

            if (_animator != null)
            {
                Transform bone = _animator.GetBoneTransform(HumanBodyBones.RightHand);
                if (bone != null) return bone;
            }

            return transform;
        }

        private void EquipWeapon()
        {
            if (weaponPrefab == null) return;

            _anchorHand = GetResolvingHand();

            // 부모 자식 관계로 1차 할당
            _equippedWeapon = Instantiate(weaponPrefab, _anchorHand);
            
            // 최초에 한 번 로컬 기준 오프셋 셋업 (LateUpdate에서 월드로 덮어씌움)
            _equippedWeapon.transform.localPosition = positionOffset;
            _equippedWeapon.transform.localRotation = Quaternion.Euler(rotationOffset);

            Debug.Log($"WeaponEquipper: 무기를 [{_anchorHand.name}] 옵젝트에 장착했습니다!");

            if (muzzleEffectPrefab != null)
            {
                _equippedEffect = Instantiate(muzzleEffectPrefab, _equippedWeapon.transform);
                _equippedEffect.transform.localPosition = effectPositionOffset;
                _equippedEffect.transform.localRotation = Quaternion.Euler(effectRotationOffset);
                _equippedEffect.SetActive(false);
            }
        }

        private bool IsLocalPlayer()
        {
            var netObj = GetComponentInParent<NetworkObject>();
            return netObj == null || netObj.HasInputAuthority;
        }

        private void Update()
        {
            if (!IsLocalPlayer())
                return;

            if (_equippedWeapon != null)
            {
                if (Input.GetKeyDown(KeyCode.F))
                {
                    DropWeapon();
                    return;
                }

                if (_equippedEffect != null)
                {
                    bool isShooting = Input.GetMouseButton(0);

                    if (_equippedEffect.activeSelf != isShooting)
                        _equippedEffect.SetActive(isShooting);

                    if (isShooting)
                    {
                        _currentUsageTime += Time.deltaTime;

                        if (Time.time - _lastDamageTime >= damageTickInterval)
                        {
                            _lastDamageTime = Time.time;
                            DealDamage();
                        }

                        if (_currentUsageTime >= maxUsageTime)
                        {
                            Destroy(_equippedWeapon);
                            _equippedWeapon = null;
                            if (_equippedEffect != null) Destroy(_equippedEffect);
                            _currentUsageTime = 0f;
                            return;
                        }
                    }
                }
            }
            else
            {
                // Right click is intentionally reserved for a future kick input.
                // Leave weapon pickup unbound here so the core player control scheme stays consistent.
            }
        }

        private void DealDamage()
        {
            if (_equippedEffect == null) return;

            Vector3 origin = _equippedEffect.transform.position;
            Vector3 direction = _equippedEffect.transform.forward;

            Collider[] hits = Physics.OverlapCapsule(origin, origin + direction * attackRange, attackRadius);
            foreach (var col in hits)
            {
                if (col.transform.root == transform.root) continue;

                var dummy = col.GetComponentInParent<DummyHealth>();
                if (dummy != null)
                {
                    dummy.TakeDamage(damagePerTick);
                    dummy.ApplyBurn(5f, 2f);
                }
            }
        }

        private void DropWeapon()
        {
            if (_equippedWeapon == null) return;

            _equippedWeapon.transform.SetParent(null);

            var rb = _equippedWeapon.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = _equippedWeapon.AddComponent<Rigidbody>();
                rb.mass = 1f;
                rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            }

            var col = _equippedWeapon.GetComponent<Collider>();
            if (col == null)
            {
                _equippedWeapon.AddComponent<BoxCollider>();
            }

            var marker = _equippedWeapon.GetComponent<DroppedWeaponMarker>();
            if (marker == null) marker = _equippedWeapon.AddComponent<DroppedWeaponMarker>();
            marker.savedUsageTime = _currentUsageTime;

            if (_equippedEffect != null)
            {
                Destroy(_equippedEffect);
                _equippedEffect = null;
            }

            _equippedWeapon = null;
            _anchorHand = null;
        }

        private void TryPickupWeapon()
        {
            Collider[] colliders = Physics.OverlapSphere(transform.position, 2f);
            foreach (var col in colliders)
            {
                var marker = col.GetComponent<DroppedWeaponMarker>();
                if (marker != null)
                {
                    _currentUsageTime = marker.savedUsageTime;
                    GameObject weaponObj = marker.gameObject;

                    Destroy(marker);
                    var rb = weaponObj.GetComponent<Rigidbody>();
                    if (rb != null) Destroy(rb);
                    var bCol = weaponObj.GetComponent<BoxCollider>();
                    if (bCol != null) Destroy(bCol);

                    _anchorHand = GetResolvingHand();
                    weaponObj.transform.SetParent(_anchorHand);
                    
                    _equippedWeapon = weaponObj;
                    _equippedWeapon.transform.localPosition = positionOffset;
                    _equippedWeapon.transform.localRotation = Quaternion.Euler(rotationOffset);

                    if (muzzleEffectPrefab != null)
                    {
                        _equippedEffect = Instantiate(muzzleEffectPrefab, _equippedWeapon.transform);
                        _equippedEffect.transform.localPosition = effectPositionOffset;
                        _equippedEffect.transform.localRotation = Quaternion.Euler(effectRotationOffset);
                        _equippedEffect.SetActive(false);
                    }

                    break;
                }
            }
        }

        // 컴포넌트 업데이트 주기가 끝난 후(애니메이션 갱신 후)에 무기의 회전을 강제로 덮어씌웁니다.
        private void LateUpdate()
        {
            if (_equippedWeapon != null && _anchorHand != null)
            {
                // 이전(오류버전)처럼 LateUpdate마다 localPosition을 덮어씌우면,
                // 리깅이 꼬인 뼈대에선 무기가 제멋대로 떨리거나 덜덜거리는 버그(지터링)가 발생합니다.
                
                // 손(anchorHand)의 월드 좌표 + 캐릭터 기준 위치 오프셋(TransformPoint)으로 무기의 절대(World) 위치를 꽂습니다.
                // 뼈대의 기괴한 로컬 스케일이나 회전 간섭을 원천 차단합니다.
                _equippedWeapon.transform.position = _anchorHand.TransformPoint(positionOffset);

                if (forceForwardRotation)
                {
                    // 무기의 방향을 캐릭터 기준 뒤쪽(-forward)으로 돌림
                    _equippedWeapon.transform.rotation = Quaternion.LookRotation(-transform.forward) * Quaternion.Euler(forwardRotationOffset);
                }
                else
                {
                    // 만약 강제정렬옵션이 꺼져있다면, 뼈대(손)의 회전축을 기준으로 오프셋만 돌립니다.
                    _equippedWeapon.transform.rotation = _anchorHand.rotation * Quaternion.Euler(rotationOffset);
                }
            }
        }

        [ContextMenu("Update Weapon Transform")]
        private void UpdateWeaponTransform()
        {
            if (_equippedWeapon != null && _anchorHand != null)
            {
                _equippedWeapon.transform.position = _anchorHand.TransformPoint(positionOffset);

                if (forceForwardRotation)
                    _equippedWeapon.transform.rotation = Quaternion.LookRotation(-transform.forward) * Quaternion.Euler(forwardRotationOffset);
                else
                    _equippedWeapon.transform.rotation = _anchorHand.rotation * Quaternion.Euler(rotationOffset);
                
                if (_equippedEffect != null)
                {
                    _equippedEffect.transform.localPosition = effectPositionOffset;
                    _equippedEffect.transform.localRotation = Quaternion.Euler(effectRotationOffset);
                }
            }
        }
    }

    public class DroppedWeaponMarker : MonoBehaviour
    {
        public float savedUsageTime = 0f;
    }
}
