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

    // ─── OwnerProxy grab 예측 타이머 ───
    private float _grabPredictionStart = -1f;
    private const float GRAB_PREDICTION_TIMEOUT = 0.4f;

    /// <summary>
    /// 비권한 클라이언트(OwnerProxy / RemoteProxy)에서
    /// 호스트가 확정한 grab/carry 상태를 animator에 반영한다.
    /// StateAuthority에서는 UpdateGrabbingAnimatorFlag()가 직접 처리하므로 호출 불필요.
    ///
    /// OwnerProxy는 로컬 grab 입력을 즉시 예측 반영하되,
    /// 호스트가 LeftGrabConfirmed/RightGrabConfirmed를 확정하지 않으면
    /// GRAB_PREDICTION_TIMEOUT 후 롤백한다.
    /// </summary>
    internal void SyncGrabbingAnimatorFromNetwork()
    {
        if (animator == null) return;

        bool confirmedHolding = NetworkedLeftGrabHolding || NetworkedRightGrabHolding;

        // OwnerProxy 예측: 로컬 입력 즉시 반영 → 호스트 미확정 시 타임아웃 롤백
        bool localPredicting = HasInputAuthority && !HasStateAuthority
            && ((_leftMouseDown && _leftMouseConsumedAsGrab) || (_rightMouseDown && _rightMouseConsumedAsGrab));

        bool showGrab;
        if (localPredicting && !confirmedHolding)
        {
            // 로컬에서 잡으려 하는데 호스트가 아직 확정하지 않음
            if (_grabPredictionStart < 0f)
                _grabPredictionStart = Time.time;
            showGrab = Time.time - _grabPredictionStart < GRAB_PREDICTION_TIMEOUT;
        }
        else
        {
            _grabPredictionStart = -1f;
            showGrab = confirmedHolding || localPredicting;
        }

        // Carry 판별: 확정된 grab 대상이 기절 상태이면 carrying
        bool isCarrying = showGrab && IsConfirmedGrabTargetStunned();
        bool isGrabbing = showGrab && !isCarrying;

        animator.SetBool(H_IsGrabbing, isGrabbing);
        animator.SetBool("isCarrying", isCarrying);
    }

    /// <summary>
    /// 네트워크 확정된 grab 대상(LeftGrabTargetId/RightGrabTargetId)을 조회하여
    /// 대상이 기절(stunned) 상태인지 판별한다.
    /// grab 관계 필드의 실질적 소비자 — carry vs grab 구분에 사용.
    /// </summary>
    private bool IsConfirmedGrabTargetStunned()
    {
        if (Runner == null) return false;

        if (LeftGrabConfirmed && LeftGrabTargetId.IsValid)
        {
            var targetObj = Runner.FindObject(LeftGrabTargetId);
            if (targetObj != null)
            {
                var targetPlayer = targetObj.GetComponent<NetworkPlayer>();
                if (targetPlayer != null && !targetPlayer.IsActiveRagdoll)
                    return true;
            }
        }

        if (RightGrabConfirmed && RightGrabTargetId.IsValid)
        {
            var targetObj = Runner.FindObject(RightGrabTargetId);
            if (targetObj != null)
            {
                var targetPlayer = targetObj.GetComponent<NetworkPlayer>();
                if (targetPlayer != null && !targetPlayer.IsActiveRagdoll)
                    return true;
            }
        }

        return false;
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
