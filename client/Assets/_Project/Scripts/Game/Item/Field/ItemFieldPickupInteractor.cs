using System;
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 플레이어 근처 필드 아이템을 우클릭으로 획득한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ItemFieldPickupInteractor : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private ItemRuntimeHost itemRuntimeHost;
        [SerializeField] private Transform interactorRoot;

        [Header("입력")]
        [SerializeField] private bool useLegacyInput = true;
        [SerializeField] private KeyCode pickupKey = KeyCode.Mouse1;

        [Header("판정")]
        [SerializeField] private float pickupRadius = 2.2f;
        [SerializeField] private LayerMask pickupMask = ~0;
        [SerializeField] private bool includeTriggerColliders = true;

        [Header("디버그")]
        [SerializeField] private bool enableDebugLog = true;

        private readonly Collider[] _overlapBuffer = new Collider[64];

        public event Action<string, ItemFieldDrop> FieldItemPickedUp;

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

            if (!Input.GetKeyDown(pickupKey))
            {
                return;
            }

            TryPickupNearest(out _, out _);
        }

        private void ResolveReferences()
        {
            if (itemRuntimeHost == null)
            {
                itemRuntimeHost = GetComponent<ItemRuntimeHost>();
            }

            if (itemRuntimeHost == null)
            {
                itemRuntimeHost = FindObjectOfType<ItemRuntimeHost>();
            }
        }

        public bool TryPickupNearest(out string pickedItemId, out string reason)
        {
            pickedItemId = string.Empty;
            reason = string.Empty;
            if (itemRuntimeHost == null)
            {
                reason = "ItemRuntimeHost missing.";
                DebugLog($"Pickup failed: {reason}");
                return false;
            }

            if (!EnsureRuntimeReady())
            {
                reason = string.IsNullOrWhiteSpace(itemRuntimeHost.LastError)
                    ? "Item runtime is not ready."
                    : itemRuntimeHost.LastError;
                DebugLog($"Pickup failed: {reason}");
                return false;
            }

            var origin = ResolveInteractorPosition();
            var triggerMode = includeTriggerColliders
                ? QueryTriggerInteraction.Collide
                : QueryTriggerInteraction.Ignore;
            var hitCount = Physics.OverlapSphereNonAlloc(
                origin,
                Mathf.Max(0.1f, pickupRadius),
                _overlapBuffer,
                pickupMask,
                triggerMode);

            ItemFieldDrop nearestDrop = null;
            var nearestDistance = float.MaxValue;
            for (var i = 0; i < hitCount; i++)
            {
                var collider = _overlapBuffer[i];
                if (collider == null)
                {
                    continue;
                }

                var drop = collider.GetComponentInParent<ItemFieldDrop>();
                if (drop == null || !drop.CanBePickedUp())
                {
                    continue;
                }

                var sqrDistance = (drop.transform.position - origin).sqrMagnitude;
                if (sqrDistance < nearestDistance)
                {
                    nearestDrop = drop;
                    nearestDistance = sqrDistance;
                }
            }

            if (nearestDrop == null)
            {
                reason = "No nearby field item.";
                DebugLog($"Pickup failed: {reason}");
                return false;
            }

            if (!itemRuntimeHost.TryPickup(nearestDrop.ItemId, out reason))
            {
                DebugLog($"Pickup failed: {reason}");
                return false;
            }

            pickedItemId = nearestDrop.ItemId;
            nearestDrop.MarkPickedUp();
            FieldItemPickedUp?.Invoke(nearestDrop.ItemId, nearestDrop);
            DebugLog($"Picked up: {pickedItemId}");
            return true;
        }

        public void SetRuntimeHost(ItemRuntimeHost runtimeHost)
        {
            itemRuntimeHost = runtimeHost;
        }

        private void OnValidate()
        {
            pickupRadius = Mathf.Max(0.1f, pickupRadius);
        }

        private bool EnsureRuntimeReady()
        {
            return itemRuntimeHost.IsReady || itemRuntimeHost.Initialize();
        }

        private Vector3 ResolveInteractorPosition()
        {
            if (itemRuntimeHost != null && itemRuntimeHost.OwnerTransform != null)
            {
                return itemRuntimeHost.OwnerTransform.position;
            }

            if (interactorRoot != null)
            {
                return interactorRoot.position;
            }

            return transform.position;
        }

        private void DebugLog(string message)
        {
            if (!enableDebugLog)
            {
                return;
            }

            Debug.Log($"[ItemFieldPickupInteractor] {message}", this);
        }
    }
}
