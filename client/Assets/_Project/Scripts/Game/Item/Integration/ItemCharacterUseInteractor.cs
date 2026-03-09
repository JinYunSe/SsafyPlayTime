using System;
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 캐릭터에서 아이템 사용 입력과 런타임 호출을 연결한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ItemCharacterUseInteractor : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private ItemRuntimeHost itemRuntimeHost;
        [SerializeField] private Transform ownerRoot;
        [SerializeField] private Camera aimingCamera;

        [Header("입력")]
        [SerializeField] private bool useLegacyInput = false;
        [SerializeField] private KeyCode useItemKey = KeyCode.Mouse0;

        [Header("조준")]
        [SerializeField] private bool useCameraRayAim = true;
        [SerializeField] private float defaultAimDistance = 6f;
        [SerializeField] private float maxAimDistance = 40f;
        [SerializeField] private LayerMask aimMask = ~0;

        [Header("디버그")]
        [SerializeField] private bool enableDebugLog;

        public event Action<string> ItemUseSucceeded;
        public event Action<string> ItemUseFailed;

        public string HeldItemId => itemRuntimeHost != null ? itemRuntimeHost.HeldItemId : string.Empty;

        private void Awake()
        {
            ResolveReferences();
        }

        private void Update()
        {
            if (!useLegacyInput)
            {
                return;
            }

            if (!Input.GetKeyDown(useItemKey))
            {
                return;
            }

            TryUseHeldItem(out _);
        }

        public void SetRuntimeHost(ItemRuntimeHost runtimeHost)
        {
            itemRuntimeHost = runtimeHost;
        }

        public void SetOwnerRoot(Transform root)
        {
            ownerRoot = root;
        }

        public void SetAimingCamera(Camera cameraRef)
        {
            aimingCamera = cameraRef;
        }

        public void SetUseLegacyInput(bool enabled)
        {
            useLegacyInput = enabled;
        }

        public void SetUseItemKey(KeyCode key)
        {
            useItemKey = key;
        }

        public bool TryUseHeldItem(out string reason)
        {
            reason = string.Empty;
            ResolveReferences();
            if (itemRuntimeHost == null)
            {
                reason = "ItemRuntimeHost missing.";
                RaiseFailed(reason);
                return false;
            }

            if (!itemRuntimeHost.IsReady && !itemRuntimeHost.Initialize())
            {
                reason = string.IsNullOrWhiteSpace(itemRuntimeHost.LastError)
                    ? "Item runtime is not ready."
                    : itemRuntimeHost.LastError;
                RaiseFailed(reason);
                return false;
            }

            if (string.IsNullOrWhiteSpace(itemRuntimeHost.HeldItemId))
            {
                reason = "No held item.";
                RaiseFailed(reason);
                return false;
            }

            var usingItemId = itemRuntimeHost.HeldItemId;
            var targetPosition = ResolveTargetPosition();
            if (!itemRuntimeHost.TryUseHeldItem(targetPosition, out reason))
            {
                RaiseFailed(reason);
                return false;
            }

            ItemUseSucceeded?.Invoke(usingItemId);
            DebugLog($"Use succeeded: {usingItemId}");
            return true;
        }

        private Vector3 ResolveTargetPosition()
        {
            var root = ownerRoot != null ? ownerRoot : transform;
            var fallback = root.position + root.forward * Mathf.Max(0.1f, defaultAimDistance);
            if (!useCameraRayAim)
            {
                return fallback;
            }

            var cam = aimingCamera != null ? aimingCamera : Camera.main;
            if (cam == null)
            {
                return fallback;
            }

            var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (Physics.Raycast(ray, out var hit, Mathf.Max(0.1f, maxAimDistance), aimMask, QueryTriggerInteraction.Ignore))
            {
                return hit.point;
            }

            return ray.origin + ray.direction * Mathf.Max(0.1f, defaultAimDistance);
        }

        private void ResolveReferences()
        {
            if (itemRuntimeHost == null)
            {
                itemRuntimeHost = GetComponent<ItemRuntimeHost>();
            }

            if (ownerRoot == null && itemRuntimeHost != null && itemRuntimeHost.OwnerTransform != null)
            {
                ownerRoot = itemRuntimeHost.OwnerTransform;
            }

            if (ownerRoot == null)
            {
                ownerRoot = transform;
            }
        }

        private void RaiseFailed(string reason)
        {
            ItemUseFailed?.Invoke(reason);
            DebugLog($"Use failed: {reason}");
        }

        private void DebugLog(string message)
        {
            if (!enableDebugLog)
            {
                return;
            }

            Debug.Log($"[ItemCharacterUseInteractor] {message}", this);
        }
    }
}
