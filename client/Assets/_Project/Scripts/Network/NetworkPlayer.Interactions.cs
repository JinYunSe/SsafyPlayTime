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
            if (handler == null || !handler.IsHoldingThrowableTarget)
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
        if (!TryPrepareItemInteractionService(out _))
            return false;

        return _itemFieldInteractionService.TryUseHeldItem(out _, out _, out _);
    }

    private bool TryPickupNearestFieldItemByKey()
    {
        if (!TryPrepareItemInteractionService(out _))
            return false;

        if (!_itemFieldInteractionService.TryPickupNearest(out var pickedItemId, out var pickedDropInstanceId, out var pickupOrigin, out _))
            return false;

        BroadcastPickedFieldDrop(pickedItemId, pickedDropInstanceId, pickupOrigin);
        return true;
    }

    private bool TryDropHeldItemByKey()
    {
        if (!TryPrepareItemInteractionService(out var runtimeHost))
            return false;

        if (!_itemFieldInteractionService.TryDropHeldItem(out var droppedItemId, out var dropSpawnPosition, out _))
            return false;

        var dropInstanceId = CreateFieldDropReplicaId();
        EnsureFieldDropReplicaForDrop(droppedItemId, runtimeHost, dropSpawnPosition, dropInstanceId);
        BroadcastDroppedFieldItem(droppedItemId, dropSpawnPosition, dropInstanceId);
        return true;
    }

    private bool TryPrepareItemInteractionService(out ItemRuntimeHost runtimeHost)
    {
        runtimeHost = ResolveItemRuntimeHostForCharacter();
        if (_itemFieldInteractionService == null)
            _itemFieldInteractionService = GetComponent<ItemFieldInteractionService>();

        if (_itemFieldInteractionService == null)
            return false;

        if (runtimeHost == null)
            return false;

        _itemFieldInteractionService.SetRuntimeHost(runtimeHost);
        _itemFieldInteractionService.SetOwnerTransform(transform);
        return true;
    }
}
