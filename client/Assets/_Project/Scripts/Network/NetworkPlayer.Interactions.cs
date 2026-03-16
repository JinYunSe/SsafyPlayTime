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

        if (input.Punch && (HasHeldRuntimeItem() || !_isGrabActive))
            TryProcessPrimaryAction(anyHolding);

        if (_isGrabActive)
            TryProcessGrab();

        if (dropRequested)
            TryProcessDrop();

        if (throwRequested)
            TryProcessSecondaryAction(anyHolding);

        ResetInteractionTriggers();
        UpdateGrabbingAnimatorFlag();
    }

    /// <summary>
    /// PartyMonsterAnimationDriver에서 그랩 애니메이션 동기화에 사용.
    /// 원격 클라이언트에서는 Networked 속성을 참조한다.
    /// </summary>
    public bool IsAnyHandHolding
    {
        get
        {
            if (Runner != null && Object != null && Object.IsValid && !HasStateAuthority)
                return NetworkedLeftGrabHolding || NetworkedRightGrabHolding;
            return IsAnyHandHoldingObject();
        }
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
        if (!TryUseHeldItemByPrimaryClick() && !anyHolding)
        {
            RaiseAnimationEvent(AnimationEventType.Punch, H_Punch);
            ExecutePunchHitDetection();
        }
    }

    private void TryProcessGrab()
    {
        foreach (var handler in _handGrabHandlers)
        {
            if (handler.IsHolding)
                continue;

            if (!IsHandGrabActive(handler.Side))
                continue;

            handler.TryGrab();
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

        if (didThrow)
            RaiseAnimationEvent(AnimationEventType.Throw, H_Throw);
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

        var itemIdBeforeUse = _itemRuntimeHost?.HeldItemId ?? string.Empty;
        if (!_itemFieldInteractionService.TryUseHeldItem(out _, out _, out _))
            return false;

        BroadcastItemUsed(itemIdBeforeUse);
        return true;
    }

    private void BroadcastItemUsed(string itemId)
    {
        if (Runner == null || !HasStateAuthority || string.IsNullOrWhiteSpace(itemId))
            return;

        RPC_NotifyItemUsed(itemId);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_NotifyItemUsed(string itemId)
    {
        // StateAuthority에서는 이미 로컬에서 처리됨
        if (HasStateAuthority)
            return;

        // 원격 클라이언트에서 들고 있는 아이템 시각 표현 제거
        _lastReplicatedHeldItemId = string.Empty;
        _heldItemPresenter?.SetReplicatedHeldItemId(string.Empty);
    }

    private bool HasHeldRuntimeItem()
    {
        return _itemRuntimeHost != null && !string.IsNullOrWhiteSpace(_itemRuntimeHost.HeldItemId);
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
        TrackDroppedFieldItem(dropInstanceId);
        return true;
    }

    /// <summary>
    /// HandGrabHandler에서 손 물리 그랩으로 필드 아이템을 주웠을 때 호출.
    /// 키 기반 픽업과 동일한 네트워크 브로드캐스트 경로를 탄다.
    /// </summary>
    internal void NotifyHandGrabPickedFieldDrop(string itemId, string dropInstanceId, Vector3 origin)
    {
        BroadcastPickedFieldDrop(itemId, dropInstanceId, origin);
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
