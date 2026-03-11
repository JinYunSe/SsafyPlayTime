/*
 * 파일 개요:
 * - ItemFieldInteractionService 스크립트가 들어 있는 파일이다.
 * - Character 계층에서 캐릭터와 아이템 시스템의 결합 지점을 담당한다.
 * - 입력, 손 장착, 근접 판정, 버프 반영 같은 캐릭터 쪽 연결만 여기서 다루고, 실제 상태 전이는 Runtime 계층에서 유지한다.
 */
using Fusion;
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 필드 드랍 생성/획득/사용 흐름을 게임 로직 관점에서 묶는 서비스다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ItemFieldInteractionService : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private ItemRuntimeHost itemRuntimeHost;
        [SerializeField] private ItemFieldDropSpawner itemFieldDropSpawner;
        [SerializeField] private ItemFieldPickupInteractor itemFieldPickupInteractor;
        [SerializeField] private ItemCharacterUseInteractor itemCharacterUseInteractor;
        [SerializeField] private Transform ownerTransform;
        [SerializeField] private Camera aimingCamera;

        [Header("자동 연결")]
        [SerializeField] private bool autoCreateMissingComponents = true;
        [SerializeField] private bool disableLegacyInputOnInteractors = true;

        [Header("스폰 위치")]
        [SerializeField] private float spawnForwardDistance = 2.2f;
        [SerializeField] private float spawnHeightOffset = 0.2f;

        [Header("권한")]
        [SerializeField] private bool requireServerAuthorityWhenRunnerExists = true;
        [SerializeField] private float runnerLookupIntervalSec = 1f;

        [Header("디버그")]
        [SerializeField] private bool enableDebugLog = true;

        private NetworkRunner _runnerCache;
        private float _nextRunnerLookupTime;

        private void Awake()
        {
            ResolveAndWire(out _);
        }

        public void SetRuntimeHost(ItemRuntimeHost runtimeHost)
        {
            itemRuntimeHost = runtimeHost;
            ResolveAndWire(out _);
        }

        public void SetOwnerTransform(Transform owner)
        {
            ownerTransform = owner;
            ResolveAndWire(out _);
        }

        public bool TrySpawnItemInFront(string itemId, out ItemFieldDrop spawnedDrop, out string reason)
        {
            spawnedDrop = null;
            if (!ResolveAndWire(out reason))
            {
                return false;
            }

            if (!HasSpawnAuthority())
            {
                reason = "Host authority required.";
                DebugLog($"Spawn failed: {reason}");
                return false;
            }

            var owner = ResolveOwnerTransform();
            var forward = owner.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            var position = owner.position + forward.normalized * Mathf.Max(0.2f, spawnForwardDistance);
            position.y += spawnHeightOffset;
            return TrySpawnItemAt(itemId, position, out spawnedDrop, out reason);
        }

        public bool TrySpawnItemAt(string itemId, Vector3 worldPosition, out ItemFieldDrop spawnedDrop, out string reason)
        {
            spawnedDrop = null;
            if (!ResolveAndWire(out reason))
            {
                return false;
            }

            if (itemFieldDropSpawner == null)
            {
                reason = "ItemFieldDropSpawner missing.";
                DebugLog($"Spawn failed: {reason}");
                return false;
            }

            if (string.IsNullOrWhiteSpace(itemId))
            {
                reason = "Item ID is empty.";
                DebugLog($"Spawn failed: {reason}");
                return false;
            }

            if (!itemFieldDropSpawner.TrySpawnItem(itemId, worldPosition, out spawnedDrop))
            {
                reason = $"Failed to spawn field item: {itemId}";
                DebugLog($"Spawn failed: {reason}");
                return false;
            }

            reason = string.Empty;
            DebugLog($"Spawned field item: {itemId}");
            return true;
        }

        public bool TryPickupNearest(out string pickedItemId, out string reason)
        {
            return TryPickupNearest(out pickedItemId, out _, out reason);
        }

        public bool TryPickupNearest(out string pickedItemId, out Vector3 pickupOrigin, out string reason)
        {
            pickedItemId = string.Empty;
            pickupOrigin = Vector3.zero;
            if (!ResolveAndWire(out reason))
            {
                return false;
            }

            if (itemRuntimeHost == null)
            {
                reason = "ItemRuntimeHost missing.";
                DebugLog($"Pickup failed: {reason}");
                return false;
            }

            if (!EnsureRuntimeReady(out reason))
            {
                DebugLog($"Pickup failed: {reason}");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(itemRuntimeHost.HeldItemId))
            {
                reason = "Held item already exists.";
                return false;
            }

            if (itemFieldPickupInteractor == null)
            {
                reason = "ItemFieldPickupInteractor missing.";
                DebugLog($"Pickup failed: {reason}");
                return false;
            }

            pickupOrigin = ResolveOwnerTransform().position;
            return itemFieldPickupInteractor.TryPickupNearest(out pickedItemId, out reason);
        }

        public bool TryUseHeldItem(out string usedItemId, out string reason)
        {
            return TryUseHeldItem(out usedItemId, out _, out reason);
        }

        public bool TryUseHeldItem(out string usedItemId, out Vector3 targetPosition, out string reason)
        {
            usedItemId = string.Empty;
            targetPosition = Vector3.zero;
            if (!ResolveAndWire(out reason))
            {
                return false;
            }

            if (itemRuntimeHost == null)
            {
                reason = "ItemRuntimeHost missing.";
                DebugLog($"Use failed: {reason}");
                return false;
            }

            if (!EnsureRuntimeReady(out reason))
            {
                DebugLog($"Use failed: {reason}");
                return false;
            }

            if (itemCharacterUseInteractor == null)
            {
                reason = "ItemCharacterUseInteractor missing.";
                DebugLog($"Use failed: {reason}");
                return false;
            }

            usedItemId = itemRuntimeHost.HeldItemId;
            if (string.IsNullOrWhiteSpace(usedItemId))
            {
                reason = "No held item.";
                DebugLog($"Use failed: {reason}");
                return false;
            }

            targetPosition = itemCharacterUseInteractor.GetTargetPosition();
            if (!itemRuntimeHost.TryUseHeldItem(targetPosition, out reason))
            {
                DebugLog($"Use failed: {reason}");
                return false;
            }

            DebugLog($"Use succeeded: {usedItemId}");
            return true;
        }

        public bool TryDropHeldItem(out string droppedItemId, out string reason)
        {
            return TryDropHeldItem(out droppedItemId, out _, out reason);
        }

        public bool TryDropHeldItem(out string droppedItemId, out Vector3 dropSpawnPosition, out string reason)
        {
            droppedItemId = string.Empty;
            dropSpawnPosition = Vector3.zero;
            if (!ResolveAndWire(out reason))
            {
                return false;
            }

            if (itemRuntimeHost == null)
            {
                reason = "ItemRuntimeHost missing.";
                DebugLog($"Drop failed: {reason}");
                return false;
            }

            if (!EnsureRuntimeReady(out reason))
            {
                DebugLog($"Drop failed: {reason}");
                return false;
            }

            droppedItemId = itemRuntimeHost.HeldItemId;
            if (string.IsNullOrWhiteSpace(droppedItemId))
            {
                reason = "No held item.";
                DebugLog($"Drop failed: {reason}");
                return false;
            }

            dropSpawnPosition = ResolveDefaultDropSpawnPosition();
            var dropped = itemRuntimeHost.TryDropHeldItem(out reason);
            if (!dropped)
            {
                DebugLog($"Drop failed: {reason}");
                return false;
            }

            DebugLog($"Drop succeeded: {droppedItemId}");
            return true;
        }

        private bool ResolveAndWire(out string reason)
        {
            reason = string.Empty;

            if (itemRuntimeHost == null)
            {
                itemRuntimeHost = GetComponent<ItemRuntimeHost>();
            }

            if (itemRuntimeHost == null)
            {
                reason = "ItemRuntimeHost missing.";
                return false;
            }

            if (ownerTransform == null)
            {
                ownerTransform = itemRuntimeHost.OwnerTransform != null
                    ? itemRuntimeHost.OwnerTransform
                    : transform;
            }
            itemRuntimeHost.SetOwnerTransform(ownerTransform);

            EnsureReferences();
            WireDependencies();
            return true;
        }

        private bool EnsureRuntimeReady(out string reason)
        {
            if (itemRuntimeHost != null && (itemRuntimeHost.IsReady || itemRuntimeHost.Initialize()))
            {
                reason = string.Empty;
                return true;
            }

            reason = itemRuntimeHost == null || string.IsNullOrWhiteSpace(itemRuntimeHost.LastError)
                ? "Item runtime is not ready."
                : itemRuntimeHost.LastError;
            return false;
        }

        private void EnsureReferences()
        {
            if (itemFieldDropSpawner == null)
            {
                itemFieldDropSpawner = GetComponent<ItemFieldDropSpawner>();
                if (itemFieldDropSpawner == null && autoCreateMissingComponents)
                {
                    itemFieldDropSpawner = gameObject.AddComponent<ItemFieldDropSpawner>();
                }
            }

            if (itemFieldPickupInteractor == null)
            {
                itemFieldPickupInteractor = GetComponent<ItemFieldPickupInteractor>();
                if (itemFieldPickupInteractor == null && autoCreateMissingComponents)
                {
                    itemFieldPickupInteractor = gameObject.AddComponent<ItemFieldPickupInteractor>();
                }
            }

            if (itemCharacterUseInteractor == null)
            {
                itemCharacterUseInteractor = GetComponent<ItemCharacterUseInteractor>();
                if (itemCharacterUseInteractor == null && autoCreateMissingComponents)
                {
                    itemCharacterUseInteractor = gameObject.AddComponent<ItemCharacterUseInteractor>();
                }
            }

            if (aimingCamera == null)
            {
                aimingCamera = Camera.main;
            }
        }

        private void WireDependencies()
        {
            if (itemFieldDropSpawner != null)
            {
                itemFieldDropSpawner.SetRuntimeHost(itemRuntimeHost);
            }

            if (itemFieldPickupInteractor != null)
            {
                itemFieldPickupInteractor.SetRuntimeHost(itemRuntimeHost);
                itemFieldPickupInteractor.SetInteractorRoot(ResolveOwnerTransform());
                if (disableLegacyInputOnInteractors)
                {
                    itemFieldPickupInteractor.SetUseLegacyInput(false);
                }
            }

            if (itemCharacterUseInteractor != null)
            {
                itemCharacterUseInteractor.SetRuntimeHost(itemRuntimeHost);
                itemCharacterUseInteractor.SetOwnerRoot(ResolveOwnerTransform());
                itemCharacterUseInteractor.SetAimingCamera(aimingCamera);
                if (disableLegacyInputOnInteractors)
                {
                    itemCharacterUseInteractor.SetUseLegacyInput(false);
                }
            }
        }

        private Transform ResolveOwnerTransform()
        {
            if (ownerTransform != null)
            {
                return ownerTransform;
            }

            if (itemRuntimeHost != null && itemRuntimeHost.OwnerTransform != null)
            {
                return itemRuntimeHost.OwnerTransform;
            }

            return transform;
        }

        private Vector3 ResolveDefaultDropSpawnPosition()
        {
            var owner = ResolveOwnerTransform();
            var forward = owner != null ? owner.forward : transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();
            return (owner != null ? owner.position : transform.position) + forward * 0.9f + Vector3.up * 0.4f;
        }

        private bool HasSpawnAuthority()
        {
            if (!requireServerAuthorityWhenRunnerExists)
            {
                return true;
            }

            if (!TryGetRunner(out var runner))
            {
                return true;
            }

            return runner.IsServer;
        }

        private bool TryGetRunner(out NetworkRunner runner)
        {
            if (_runnerCache != null && _runnerCache.IsRunning)
            {
                runner = _runnerCache;
                return true;
            }

            if (Time.unscaledTime < _nextRunnerLookupTime)
            {
                runner = null;
                return false;
            }

            _nextRunnerLookupTime = Time.unscaledTime + Mathf.Max(0.1f, runnerLookupIntervalSec);
            _runnerCache = FindObjectOfType<NetworkRunner>(true);
            if (_runnerCache != null && _runnerCache.IsRunning)
            {
                runner = _runnerCache;
                return true;
            }

            runner = null;
            return false;
        }

        private void OnValidate()
        {
            spawnForwardDistance = Mathf.Max(0.2f, spawnForwardDistance);
            runnerLookupIntervalSec = Mathf.Max(0.1f, runnerLookupIntervalSec);
        }

        private void DebugLog(string message)
        {
            if (!enableDebugLog)
            {
                return;
            }

            Debug.Log($"[ItemFieldInteractionService] {message}", this);
        }
    }
}

