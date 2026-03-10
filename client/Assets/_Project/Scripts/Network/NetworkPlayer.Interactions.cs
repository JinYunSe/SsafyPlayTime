using Fusion;
using SSAFYPlayTime.Gameplay.Items;
using UnityEngine;

public sealed partial class NetworkPlayer
{
    private void ProcessInteractions(PlayerNetworkInput input)
    {
        if (_handGrabHandlers == null || !_isActiveRagdoll)
            return;

        var dropRequested = input.Drop || _dropTriggered;
        var throwRequested = input.Throw || _throwTriggered;
        var anyHolding = IsAnyHandHoldingObject();

        if (input.Punch && !_isGrabActive)
            TryProcessPrimaryAction(anyHolding);

        if (_isGrabActive && !anyHolding)
            TryProcessGrab();

        if (dropRequested)
            TryProcessDrop();

        if (throwRequested)
            TryProcessSecondaryAction(anyHolding);

        ResetInteractionTriggers();
        UpdateGrabbingAnimatorFlag();
    }

    private bool IsAnyHandHoldingObject()
    {
        foreach (var handler in _handGrabHandlers)
        {
            if (handler.IsHolding)
                return true;
        }

        return false;
    }

    private void TryProcessPrimaryAction(bool anyHolding)
    {
        if (!TryUseHeldItemByPrimaryClick() && !anyHolding && animator != null)
            animator.SetTrigger(H_Punch);
    }

    private void TryProcessGrab()
    {
        foreach (var handler in _handGrabHandlers)
        {
            if (handler.IsHolding)
                continue;

            handler.TryGrab();
            if (handler.IsHolding)
                break;
        }
    }

    private void TryProcessDrop()
    {
        var droppedPhysicsHold = false;
        foreach (var handler in _handGrabHandlers)
        {
            if (handler == null || !handler.IsHolding)
                continue;

            handler.Drop();
            droppedPhysicsHold = true;
            break;
        }

        if (!droppedPhysicsHold)
            TryDropHeldItemByKey();
    }

    private void TryProcessThrow()
    {
        var didThrow = false;
        foreach (var handler in _handGrabHandlers)
        {
            if (!handler.IsHolding)
                continue;

            handler.Throw();
            didThrow = true;
        }

        if (didThrow && animator != null)
            animator.SetTrigger(H_Throw);
    }

    private void TryProcessSecondaryAction(bool anyHolding)
    {
        if (anyHolding)
        {
            TryProcessThrow();
            return;
        }

        TryPickupNearestFieldItemByKey();
    }

    private void ResetInteractionTriggers()
    {
        _dropTriggered = false;
        _throwTriggered = false;
    }

    private void UpdateGrabbingAnimatorFlag()
    {
        if (animator != null)
            animator.SetBool(H_IsGrabbing, IsAnyHandHoldingObject());
    }

    private bool TryUseHeldItemByPrimaryClick()
    {
        var runtimeHost = ResolveItemRuntimeHostForCharacter();
        if (_itemFieldInteractionService == null)
            _itemFieldInteractionService = GetComponent<ItemFieldInteractionService>();

        if (_itemFieldInteractionService != null)
        {
            if (runtimeHost != null)
                _itemFieldInteractionService.SetRuntimeHost(runtimeHost);
            _itemFieldInteractionService.SetOwnerTransform(transform);

            return _itemFieldInteractionService.TryUseHeldItem(out _, out _);
        }

        if (_itemUseInteractor == null)
            _itemUseInteractor = GetComponent<ItemCharacterUseInteractor>();

        if (_itemUseInteractor != null)
        {
            if (runtimeHost != null)
                _itemUseInteractor.SetRuntimeHost(runtimeHost);

            if (string.IsNullOrWhiteSpace(_itemUseInteractor.HeldItemId))
                return false;

            return _itemUseInteractor.TryUseHeldItem(out _);
        }

        if (runtimeHost == null)
            return false;

        if (!runtimeHost.IsReady && !runtimeHost.Initialize())
            return false;

        if (string.IsNullOrWhiteSpace(runtimeHost.HeldItemId))
            return false;

        var targetPosition = transform.position + transform.forward * 6f;
        return runtimeHost.TryUseHeldItem(targetPosition, out _);
    }

    private bool TryPickupNearestFieldItemByKey()
    {
        var runtimeHost = ResolveItemRuntimeHostForCharacter();
        if (_itemFieldInteractionService == null)
            _itemFieldInteractionService = GetComponent<ItemFieldInteractionService>();

        if (_itemFieldInteractionService != null)
        {
            if (runtimeHost != null)
                _itemFieldInteractionService.SetRuntimeHost(runtimeHost);
            _itemFieldInteractionService.SetOwnerTransform(transform);

            if (!string.IsNullOrWhiteSpace(runtimeHost != null ? runtimeHost.HeldItemId : string.Empty))
                return false;

            if (!_itemFieldInteractionService.TryPickupNearest(out var pickedItemId, out _))
                return false;

            BroadcastPickedFieldDrop(pickedItemId, transform.position);
            return true;
        }

        if (_itemPickupInteractor == null)
            _itemPickupInteractor = GetComponent<ItemFieldPickupInteractor>();

        if (_itemPickupInteractor == null)
            return false;

        if (runtimeHost != null)
            _itemPickupInteractor.SetRuntimeHost(runtimeHost);

        _itemPickupInteractor.SetInteractorRoot(transform);

        if (!string.IsNullOrWhiteSpace(runtimeHost != null ? runtimeHost.HeldItemId : string.Empty))
            return false;

        if (!_itemPickupInteractor.TryPickupNearest(out var fallbackPickedItemId, out _))
            return false;

        BroadcastPickedFieldDrop(fallbackPickedItemId, transform.position);
        return true;
    }

    private bool TryDropHeldItemByKey()
    {
        var runtimeHost = ResolveItemRuntimeHostForCharacter();
        if (_itemFieldInteractionService == null)
            _itemFieldInteractionService = GetComponent<ItemFieldInteractionService>();

        if (_itemFieldInteractionService != null)
        {
            if (runtimeHost != null)
                _itemFieldInteractionService.SetRuntimeHost(runtimeHost);
            _itemFieldInteractionService.SetOwnerTransform(transform);

            if (string.IsNullOrWhiteSpace(runtimeHost != null ? runtimeHost.HeldItemId : string.Empty))
                return false;

            var serviceDroppedItemId = runtimeHost.HeldItemId;
            var beforeDropCount = CountFieldDropsByItemId(serviceDroppedItemId);
            if (!_itemFieldInteractionService.TryDropHeldItem(out _, out _))
                return false;

            EnsureFieldDropSpawnFallback(serviceDroppedItemId, runtimeHost, beforeDropCount);
            BroadcastDroppedFieldItem(serviceDroppedItemId);
            return true;
        }

        if (runtimeHost == null)
            return false;

        if (!runtimeHost.IsReady && !runtimeHost.Initialize())
            return false;

        if (string.IsNullOrWhiteSpace(runtimeHost.HeldItemId))
            return false;

        var droppedItemId = runtimeHost.HeldItemId;
        var fallbackBeforeDropCount = CountFieldDropsByItemId(droppedItemId);
        if (!runtimeHost.TryDropHeldItem(out _))
            return false;

        EnsureFieldDropSpawnFallback(droppedItemId, runtimeHost, fallbackBeforeDropCount);
        BroadcastDroppedFieldItem(droppedItemId);
        return true;
    }

    private void BroadcastPickedFieldDrop(string itemId, Vector3 origin)
    {
        if (Runner == null || !HasStateAuthority || string.IsNullOrWhiteSpace(itemId))
            return;

        RPC_MarkNearestFieldDropPicked(itemId, origin);
    }

    private void BroadcastDroppedFieldItem(string itemId)
    {
        if (Runner == null || !HasStateAuthority || string.IsNullOrWhiteSpace(itemId))
            return;

        var drop = FindNearestFieldDropByItemId(itemId, transform.position, 6f);
        if (drop == null)
            return;

        RPC_SpawnFieldDropReplica(itemId, drop.transform.position);
    }

    private void EnsureFieldDropSpawnFallback(string droppedItemId, ItemRuntimeHost runtimeHost, int beforeDropCount)
    {
        if (string.IsNullOrWhiteSpace(droppedItemId))
            return;

        var afterDropCount = CountFieldDropsByItemId(droppedItemId);
        if (afterDropCount > beforeDropCount)
            return;

        var spawners = FindObjectsOfType<ItemFieldDropSpawner>(true);
        ItemFieldDropSpawner boundSpawner = null;
        ItemFieldDropSpawner fallbackSpawner = null;

        for (var i = 0; i < spawners.Length; i++)
        {
            var spawner = spawners[i];
            if (spawner == null)
                continue;

            if (fallbackSpawner == null && spawner.RuntimeHost == null)
                fallbackSpawner = spawner;

            if (spawner.RuntimeHost == runtimeHost)
                boundSpawner = spawner;
        }

        if (boundSpawner != null)
            fallbackSpawner = boundSpawner;
        else if (fallbackSpawner == null)
        {
            var spawnRoot = runtimeHost != null ? runtimeHost.gameObject : gameObject;
            fallbackSpawner = spawnRoot.GetComponent<ItemFieldDropSpawner>();
            if (fallbackSpawner == null)
                fallbackSpawner = spawnRoot.AddComponent<ItemFieldDropSpawner>();
        }

        fallbackSpawner.SetRuntimeHost(runtimeHost);
        var spawnPosition = transform.position + transform.forward * 0.9f + Vector3.up * 0.4f;
        if (!fallbackSpawner.TrySpawnItem(droppedItemId, spawnPosition, out _))
            Debug.LogWarning($"[NetworkPlayer] 드롭 폴백 스폰 실패: {droppedItemId}", this);
    }

    private static int CountFieldDropsByItemId(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return 0;

        var drops = FindObjectsOfType<ItemFieldDrop>(true);
        var count = 0;
        for (var i = 0; i < drops.Length; i++)
        {
            var drop = drops[i];
            if (drop == null || drop.IsPickedUp)
                continue;

            if (string.Equals(drop.ItemId, itemId, System.StringComparison.Ordinal))
                count++;
        }

        return count;
    }

    private static ItemFieldDrop FindNearestFieldDropByItemId(string itemId, Vector3 origin, float maxDistance)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return null;

        var drops = FindObjectsOfType<ItemFieldDrop>(true);
        ItemFieldDrop nearest = null;
        var bestDistance = Mathf.Max(0.01f, maxDistance) * Mathf.Max(0.01f, maxDistance);
        for (var i = 0; i < drops.Length; i++)
        {
            var drop = drops[i];
            if (drop == null || drop.IsPickedUp || !string.Equals(drop.ItemId, itemId, System.StringComparison.Ordinal))
                continue;

            var distance = (drop.transform.position - origin).sqrMagnitude;
            if (distance > bestDistance)
                continue;

            bestDistance = distance;
            nearest = drop;
        }

        return nearest;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_MarkNearestFieldDropPicked(string itemId, Vector3 origin)
    {
        if (HasStateAuthority)
            return;

        var drop = FindNearestFieldDropByItemId(itemId, origin, 3f);
        if (drop != null)
            drop.MarkPickedUp();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_SpawnFieldDropReplica(string itemId, Vector3 worldPosition)
    {
        if (HasStateAuthority || string.IsNullOrWhiteSpace(itemId))
            return;

        var spawners = FindObjectsOfType<ItemFieldDropSpawner>(true);
        ItemFieldDropSpawner spawner = null;
        for (var i = 0; i < spawners.Length; i++)
        {
            if (spawners[i] == null)
                continue;

            spawner = spawners[i];
            if (spawner.RuntimeHost == _itemRuntimeHost)
                break;
        }

        if (spawner == null)
            return;

        var existing = FindNearestFieldDropByItemId(itemId, worldPosition, 0.6f);
        if (existing != null)
            return;

        spawner.TrySpawnItem(itemId, worldPosition, out _);
    }
}
