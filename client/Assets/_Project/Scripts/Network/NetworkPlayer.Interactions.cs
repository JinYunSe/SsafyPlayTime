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
            TryProcessThrow();

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
            if (handler == null || !handler.IsHoldingThrowableTarget)
                continue;

            handler.Throw();
            didThrow = true;
        }

        if (didThrow && animator != null)
            animator.SetTrigger(H_Throw);
    }

    private void ResetInteractionTriggers()
    {
        _dropTriggered = false;
        _throwTriggered = false;
    }

    private bool IsAnyHandHoldingThrowableTarget()
    {
        foreach (var handler in _handGrabHandlers)
        {
            if (handler != null && handler.IsHoldingThrowableTarget)
                return true;
        }

        return false;
    }

    private void UpdateGrabbingAnimatorFlag()
    {
        if (animator == null)
            return;

        var isCarrying = IsAnyHandHoldingThrowableTarget();
        var isGrabbing = _isGrabActive && !isCarrying;

        animator.SetBool(H_IsGrabbing, isGrabbing);
        animator.SetBool("isCarrying", isCarrying);
    }

    private bool TryUseHeldItemByPrimaryClick()
    {
        var runtimeHost = ResolveItemRuntimeHostForCharacter();

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

    private bool TryDropHeldItemByKey()
    {
        var runtimeHost = ResolveItemRuntimeHostForCharacter();
        if (runtimeHost == null)
            return false;

        if (!runtimeHost.IsReady && !runtimeHost.Initialize())
            return false;

        if (string.IsNullOrWhiteSpace(runtimeHost.HeldItemId))
            return false;

        var droppedItemId = runtimeHost.HeldItemId;
        var beforeDropCount = CountFieldDropsByItemId(droppedItemId);
        if (!runtimeHost.TryDropHeldItem(out _))
            return false;

        EnsureFieldDropSpawnFallback(droppedItemId, runtimeHost, beforeDropCount);
        return true;
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
}
