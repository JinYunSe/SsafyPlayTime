/*
 * 파일 개요:
 * - ItemCharacterMeleeSwingHandler 스크립트가 들어 있는 파일이다.
 * - Character 계층에서 캐릭터와 아이템 시스템의 결합 지점을 담당한다.
 * - 입력, 손 장착, 근접 판정, 버프 반영 같은 캐릭터 쪽 연결만 여기서 다루고, 실제 상태 전이는 Runtime 계층에서 유지한다.
 */
using System;
using System.Collections.Generic;
using System.Reflection;
using Fusion;
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 수박검 사용 요청을 받아 손 장착 비주얼에 근접 타격 콜라이더를 생성/운용한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ItemCharacterMeleeSwingHandler : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private ItemRuntimeHost itemRuntimeHost;
        [SerializeField] private ItemCharacterHeldItemPresenter heldItemPresenter;
        [SerializeField] private Transform ownerRoot;

        [Header("타격 기본값")]
        [SerializeField] private float defaultSwingDurationSec = 0.22f;
        [SerializeField] private float defaultHealthDamage = 5f;
        [SerializeField] private float defaultStunDamage = 5f;

        [Header("콜라이더 형태(수박검 로컬 기준)")]
        [SerializeField] private Vector3 hitboxLocalCenter = new Vector3(0f, 0.45f, 0f);
        [SerializeField] private float hitboxRadius = 0.12f;
        [SerializeField] private float hitboxHeight = 0.9f;
        [SerializeField] private int hitboxDirection = 1;
        [SerializeField] private bool requireStateAuthority = true;

        [Header("디버그")]
        [SerializeField] private bool enableDebugLog;

        private readonly HashSet<int> _hitTargetRootIds = new();
        private readonly Dictionary<Type, MethodInfo> _applyDamageMethodCache = new();
        private readonly Dictionary<Type, MethodInfo> _takeDamageMethodCache = new();

        private Transform _hitboxRoot;
        private CapsuleCollider _hitboxCollider;
        private ItemMeleeDamageHitbox _hitboxRelay;

        private bool _isSwingActive;
        private float _swingEndAtSec;
        private float _currentHealthDamage;
        private float _currentStunDamage;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            BindEvents();
        }

        private void OnDisable()
        {
            UnbindEvents();
            StopSwing();
        }

        private void Update()
        {
            if (!_isSwingActive)
            {
                return;
            }

            if (Time.time >= _swingEndAtSec)
            {
                StopSwing();
            }
        }

        public void SetRuntimeHost(ItemRuntimeHost runtimeHost)
        {
            if (itemRuntimeHost == runtimeHost)
            {
                return;
            }

            UnbindEvents();
            itemRuntimeHost = runtimeHost;
            BindEvents();
        }

        public void SetHeldItemPresenter(ItemCharacterHeldItemPresenter presenter)
        {
            heldItemPresenter = presenter;
        }

        public void SetOwnerRoot(Transform root)
        {
            ownerRoot = root;
        }

        internal void HandleHitCollider(Collider other)
        {
            if (!_isSwingActive || other == null)
            {
                return;
            }

            var root = ResolveTargetRoot(other);
            if (root == null || IsSelfRoot(root))
            {
                return;
            }

            var targetId = root.GetInstanceID();
            if (!_hitTargetRootIds.Add(targetId))
            {
                return;
            }

            ApplyDamage(root);
        }

        private void BindEvents()
        {
            if (itemRuntimeHost == null)
            {
                return;
            }

            itemRuntimeHost.MeleeSwingRequested -= OnMeleeSwingRequested;
            itemRuntimeHost.MeleeSwingRequested += OnMeleeSwingRequested;
            itemRuntimeHost.HeldItemChanged -= OnHeldItemChanged;
            itemRuntimeHost.HeldItemChanged += OnHeldItemChanged;
        }

        private void UnbindEvents()
        {
            if (itemRuntimeHost == null)
            {
                return;
            }

            itemRuntimeHost.MeleeSwingRequested -= OnMeleeSwingRequested;
            itemRuntimeHost.HeldItemChanged -= OnHeldItemChanged;
        }

        private void OnHeldItemChanged(string heldItemId)
        {
            if (string.Equals(heldItemId, ItemIds.WaterMelonSword, StringComparison.Ordinal))
            {
                return;
            }

            StopSwing();
        }

        private void OnMeleeSwingRequested(MeleeSwingRequest request)
        {
            if (!string.Equals(request.ItemId, ItemIds.WaterMelonSword, StringComparison.Ordinal))
            {
                return;
            }

            ResolveReferences();
            if (!HasDamageAuthority())
            {
                return;
            }

            if (!EnsureHitbox())
            {
                DebugLog("Swing skipped: held watermelon sword visual is missing.");
                return;
            }

            var buffApplier = ownerRoot != null ? ownerRoot.GetComponent<ItemCharacterBuffApplier>() : null;
            var outgoingStunMultiplier = buffApplier != null
                ? Mathf.Max(0.05f, buffApplier.CurrentOutgoingStunDamageMultiplier)
                : 1f;

            _currentHealthDamage = request.HealthDamage > 0f ? request.HealthDamage : Mathf.Max(0f, defaultHealthDamage);
            _currentStunDamage = (request.StunDamage > 0f ? request.StunDamage : Mathf.Max(0f, defaultStunDamage)) *
                                 outgoingStunMultiplier;
            var duration = request.ActiveDurationSec > 0f ? request.ActiveDurationSec : Mathf.Max(0.05f, defaultSwingDurationSec);

            _isSwingActive = true;
            _swingEndAtSec = Time.time + duration;
            _hitTargetRootIds.Clear();
            if (_hitboxCollider != null)
            {
                _hitboxCollider.enabled = true;
            }

            DebugLog($"Watermelon sword swing start: duration={duration:F2}, hp={_currentHealthDamage:F1}, stun={_currentStunDamage:F1}");
        }

        private bool EnsureHitbox()
        {
            var visualRoot = heldItemPresenter != null ? heldItemPresenter.CurrentHeldVisualRoot : null;
            if (visualRoot == null)
            {
                return false;
            }

            if (_hitboxRoot != null && _hitboxRoot.parent != visualRoot)
            {
                Destroy(_hitboxRoot.gameObject);
                _hitboxRoot = null;
                _hitboxCollider = null;
                _hitboxRelay = null;
            }

            if (_hitboxRoot == null)
            {
                var hitboxObject = new GameObject("WatermelonSword_Hitbox");
                _hitboxRoot = hitboxObject.transform;
                _hitboxRoot.SetParent(visualRoot, false);
                _hitboxRoot.localPosition = Vector3.zero;
                _hitboxRoot.localRotation = Quaternion.identity;
                _hitboxRoot.localScale = Vector3.one;

                var body = hitboxObject.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                body.interpolation = RigidbodyInterpolation.None;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

                _hitboxCollider = hitboxObject.AddComponent<CapsuleCollider>();
                _hitboxCollider.isTrigger = true;
                _hitboxRelay = hitboxObject.AddComponent<ItemMeleeDamageHitbox>();
                _hitboxRelay.SetOwner(this);
            }

            if (_hitboxCollider == null)
            {
                return false;
            }

            _hitboxCollider.center = hitboxLocalCenter;
            _hitboxCollider.radius = Mathf.Max(0.01f, hitboxRadius);
            _hitboxCollider.height = Mathf.Max(_hitboxCollider.radius * 2f, hitboxHeight);
            _hitboxCollider.direction = Mathf.Clamp(hitboxDirection, 0, 2);
            _hitboxCollider.enabled = false;
            return true;
        }

        private void StopSwing()
        {
            _isSwingActive = false;
            _swingEndAtSec = 0f;
            _hitTargetRootIds.Clear();
            if (_hitboxCollider != null)
            {
                _hitboxCollider.enabled = false;
            }
        }

        private bool HasDamageAuthority()
        {
            if (!requireStateAuthority)
            {
                return true;
            }

            var root = ownerRoot != null ? ownerRoot : transform;
            var networkObject = root.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                return true;
            }

            return networkObject.HasStateAuthority;
        }

        private bool IsSelfRoot(Transform targetRoot)
        {
            if (targetRoot == null)
            {
                return true;
            }

            var root = ownerRoot != null ? ownerRoot : transform;
            return targetRoot == root || targetRoot.IsChildOf(root);
        }

        private static Transform ResolveTargetRoot(Collider other)
        {
            if (other == null)
            {
                return null;
            }

            var networkPlayer = other.GetComponentInParent<NetworkPlayer>();
            if (networkPlayer != null)
            {
                return networkPlayer.transform;
            }

            var playerStats = other.GetComponentInParent<PlayerStats>();
            if (playerStats != null)
            {
                return playerStats.transform;
            }

            if (other.attachedRigidbody != null)
            {
                return other.attachedRigidbody.transform.root;
            }

            return other.transform.root;
        }

        private void ApplyDamage(Transform targetRoot)
        {
            if (targetRoot == null)
            {
                return;
            }

            var healthApplied = false;
            var stunApplied = false;
            var healthInt = Mathf.Max(0, Mathf.RoundToInt(_currentHealthDamage));
            var stunValue = Mathf.Max(0f, _currentStunDamage);
            var attackerVelocity = ResolveOwnerVelocity();

            var networkPlayer = targetRoot.GetComponentInParent<NetworkPlayer>();
            if (networkPlayer != null && stunValue > 0f)
            {
                // 부위 배율/충격량은 우선 기본값으로 적용한다.
                networkPlayer.ApplyStunDamage(stunValue, 1f, attackerVelocity, 0f);
                stunApplied = true;
            }

            var playerStats = targetRoot.GetComponentInParent<PlayerStats>();
            if (playerStats != null && healthInt > 0)
            {
                playerStats.TakeDamage(healthInt);
                healthApplied = true;
            }

            if (!healthApplied && healthInt > 0)
            {
                healthApplied = TryInvokeTakeDamage(targetRoot, healthInt);
            }

            var pendingHealth = healthApplied ? 0f : Mathf.Max(0f, _currentHealthDamage);
            var pendingStun = stunApplied ? 0f : stunValue;
            if (pendingHealth > 0f || pendingStun > 0f)
            {
                TryInvokeApplyDamage(targetRoot, pendingHealth, pendingStun, "WaterMelonSword");
            }
        }

        private bool TryInvokeApplyDamage(Transform root, float damage, float stunDamage, string source)
        {
            var components = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null)
                {
                    continue;
                }

                var method = GetApplyDamageMethod(component.GetType());
                if (method == null)
                {
                    continue;
                }

                try
                {
                    method.Invoke(component, new object[] { damage, stunDamage, source });
                    return true;
                }
                catch (Exception ex)
                {
                    DebugLog($"ApplyDamage invoke failed: {component.GetType().Name} - {ex.Message}");
                }
            }

            return false;
        }

        private bool TryInvokeTakeDamage(Transform root, int damage)
        {
            var components = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null)
                {
                    continue;
                }

                var method = GetTakeDamageMethod(component.GetType());
                if (method == null)
                {
                    continue;
                }

                try
                {
                    var parameterType = method.GetParameters()[0].ParameterType;
                    if (parameterType == typeof(int))
                    {
                        method.Invoke(component, new object[] { damage });
                    }
                    else
                    {
                        method.Invoke(component, new object[] { (float)damage });
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    DebugLog($"TakeDamage invoke failed: {component.GetType().Name} - {ex.Message}");
                }
            }

            return false;
        }

        private MethodInfo GetApplyDamageMethod(Type type)
        {
            if (type == null)
            {
                return null;
            }

            if (_applyDamageMethodCache.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var method = FindApplyDamageMethod(type);
            _applyDamageMethodCache[type] = method;
            return method;
        }

        private MethodInfo GetTakeDamageMethod(Type type)
        {
            if (type == null)
            {
                return null;
            }

            if (_takeDamageMethodCache.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var method = FindTakeDamageMethod(type);
            _takeDamageMethodCache[type] = method;
            return method;
        }

        private static MethodInfo FindApplyDamageMethod(Type type)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var methods = current.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                for (var i = 0; i < methods.Length; i++)
                {
                    var method = methods[i];
                    if (method == null ||
                        method.IsGenericMethod ||
                        !string.Equals(method.Name, "ApplyDamage", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var parameters = method.GetParameters();
                    if (parameters.Length == 3 &&
                        parameters[0].ParameterType == typeof(float) &&
                        parameters[1].ParameterType == typeof(float) &&
                        parameters[2].ParameterType == typeof(string))
                    {
                        return method;
                    }
                }
            }

            return null;
        }

        private static MethodInfo FindTakeDamageMethod(Type type)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var methods = current.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                for (var i = 0; i < methods.Length; i++)
                {
                    var method = methods[i];
                    if (method == null ||
                        method.IsGenericMethod ||
                        !string.Equals(method.Name, "TakeDamage", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var parameters = method.GetParameters();
                    if (parameters.Length == 1 &&
                        (parameters[0].ParameterType == typeof(int) || parameters[0].ParameterType == typeof(float)))
                    {
                        return method;
                    }
                }
            }

            return null;
        }

        private float ResolveOwnerVelocity()
        {
            var root = ownerRoot != null ? ownerRoot : transform;
            var body = root.GetComponent<Rigidbody>();
            if (body == null)
            {
                body = root.GetComponentInChildren<Rigidbody>();
            }

            return body != null ? body.velocity.magnitude : 0f;
        }

        private void ResolveReferences()
        {
            if (itemRuntimeHost == null)
            {
                itemRuntimeHost = GetComponent<ItemRuntimeHost>();
            }

            if (heldItemPresenter == null)
            {
                heldItemPresenter = GetComponent<ItemCharacterHeldItemPresenter>();
            }

            if (ownerRoot == null)
            {
                ownerRoot = itemRuntimeHost != null && itemRuntimeHost.OwnerTransform != null
                    ? itemRuntimeHost.OwnerTransform
                    : transform;
            }
        }

        private void DebugLog(string message)
        {
            if (!enableDebugLog)
            {
                return;
            }

            Debug.Log($"[ItemCharacterMeleeSwingHandler] {message}", this);
        }
    }
}

