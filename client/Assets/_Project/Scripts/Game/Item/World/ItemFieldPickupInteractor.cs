/*
 * 파일 개요:
 * - ItemFieldPickupInteractor 스크립트가 들어 있는 파일이다.
 * - World 계층에서 필드 드랍, 획득, 스폰, 배치, 프리팹 해석처럼 월드 오브젝트와 연결되는 책임을 맡는다.
 * - 필드 공통 규칙을 바꾸면 모든 아이템 획득 흐름에 영향이 가므로 개별 아이템 예외와 분리해서 수정해야 한다.
 */
using System;
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 한국어: 필드 아이템의 근접 습득 API와 임시 입력 처리를 담당한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ItemFieldPickupInteractor : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private ItemRuntimeHost itemRuntimeHost;
        [SerializeField] private Transform interactorRoot;

        [Header("입력")]
        [SerializeField] private bool useLegacyInput = false;
        [SerializeField] private KeyCode pickupKey = KeyCode.None;

        [Header("설정")]
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
            if (!useLegacyInput || pickupKey == KeyCode.None)
            {
                return;
            }

            if (!Input.GetKeyDown(pickupKey))
            {
                return;
            }

            ResolveReferences();
            if (itemRuntimeHost == null)
            {
                return;
            }

            // 한국어: 이미 아이템을 들고 있을 때는 기존 우클릭 동작을 유지한다.
            if (!string.IsNullOrWhiteSpace(itemRuntimeHost.HeldItemId))
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

        public void SetUseLegacyInput(bool enabled)
        {
            useLegacyInput = enabled;
        }

        public void SetInteractorRoot(Transform root)
        {
            interactorRoot = root;
        }

        public void SetPickupKey(KeyCode key)
        {
            pickupKey = key;
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

