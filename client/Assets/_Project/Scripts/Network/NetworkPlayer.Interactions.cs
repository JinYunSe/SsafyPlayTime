using Fusion;
using SSAFYPlayTime.Gameplay.Items;
using UnityEngine;

public sealed partial class NetworkPlayer
{
    private void ProcessInteractions(PlayerNetworkInput input)
    {
        if (_handGrabHandlers == null || !_isActiveRagdoll)
            return;

        var runtimeHost = ResolveCurrentRuntimeHost();
        characterGrabController?.RefreshNow();

        // Block combat actions while the recovery gate is active.
        if (_isRecovering || _isRecoverStabilizing)
            return;

        var dropRequested = input.Drop || _dropTriggered;
        var throwRequested = input.Throw || _throwTriggered;
        var anyHolding = IsAnyHandHoldingObject();
        var hasHeldRuntimeItem = HasHeldRuntimeItem(runtimeHost);
        var isHoldingFlamethrower = IsHoldingRuntimeItem(ItemIds.Flamethrower, runtimeHost);
        var isGrabTemporarilyDisabled = Time.time < _grabDisabledUntilTime;

        if (hasHeldRuntimeItem)
        {
            _isLeftGrabActive = false;
            _isRightGrabActive = false;
            _isGrabActive = false;

            if (input.PrimaryUseHold && throwRequested)
                TryThrowHeldItem();
            else if (dropRequested)
                TryProcessDrop();

            if (isHoldingFlamethrower)
            {
                ProcessFlamethrowerPrimaryHold(input.PrimaryUseHold);
            }
            else if (input.Punch)
            {
                TryUseHeldItemByPrimaryClick();
            }

            ResetInteractionTriggers();
            UpdateGrabbingAnimatorFlag();
            return;
        }

        if (isHoldingFlamethrower)
        {
            ProcessFlamethrowerPrimaryHold(input.PrimaryUseHold);
            if (input.PrimaryUseHold)
            {
                _isLeftGrabActive = false;
                _isGrabActive = _isRightGrabActive;
            }
        }

        if (!isHoldingFlamethrower && input.Headbutt)
        {
            if (TryProcessHeadbutt(anyHolding, hasHeldRuntimeItem))
            {
                ResetInteractionTriggers();
                UpdateGrabbingAnimatorFlag();
                return;
            }
        }

        if (!isHoldingFlamethrower && input.Punch && !_isGrabActive)
            TryProcessPrimaryAction(anyHolding);

        var shouldProcessGrab = !isGrabTemporarilyDisabled &&
            !isHoldingFlamethrower &&
            (characterGrabController != null ? characterGrabController.ShouldProcessGrabLoop() : _isGrabActive);
        if (shouldProcessGrab)
            TryProcessGrab();

        if (dropRequested)
            TryProcessDrop();

        if (throwRequested)
            TryProcessSecondaryAction(anyHolding, hasHeldRuntimeItem);

        ResetInteractionTriggers();
        UpdateGrabbingAnimatorFlag();
    }

    private void ProcessFlamethrowerPrimaryHold(bool isHoldingPrimaryUse)
    {
        var runtimeHost = ResolveCurrentRuntimeHost();
        if (runtimeHost == null)
            return;

        runtimeHost.TrySetFlamethrowerActive(isHoldingPrimaryUse, out _);
    }

    /// <summary>
    /// Used by PartyMonsterAnimationDriver to branch grab animation state.
    /// Remote peers read the replicated Networked state instead of local grab handlers.
    /// </summary>
    public bool IsAnyHandHolding
    {
        get
        {
            if (characterGrabController != null)
            {
                characterGrabController.RefreshNow();
                return characterGrabController.HasAnyHold();
            }

            if (Runner != null && Object != null && Object.IsValid && !HasStateAuthority)
                return NetworkedLeftGrabHolding || NetworkedRightGrabHolding;
            return IsAnyHandHoldingObject();
        }
    }

    private bool IsAnyHandHoldingObject()
    {
        if (characterGrabController != null)
        {
            characterGrabController.RefreshNow();
            return characterGrabController.HasAnyHold();
        }

        foreach (var handler in _handGrabHandlers)
        {
            if (handler.IsHolding)
                return true;
        }

        return false;
    }

    private void TryProcessPrimaryAction(bool anyHolding)
    {
        if (HasHeldRuntimeItem())
        {
            TryUseHeldItemByPrimaryClick();
            return;
        }

        if (!TryUseHeldItemByPrimaryClick() && !anyHolding)
        {
            // The host decides left/right punch order and records the replicated event.
            var isLeft = _hostNextPunchLeft;
            if (!TryBeginPunchHitDetection(isLeft))
                return;

            _hostNextPunchLeft = !_hostNextPunchLeft;

            if (IsNetworkReady)
                NetworkedPunchIsLeft = isLeft;

            var punchEvent = isLeft ? AnimationEventType.PunchLeft : AnimationEventType.PunchRight;
            RaiseAnimationEvent(punchEvent, H_Punch);
        }
    }

    private void TryProcessGrab()
    {
        foreach (var handler in _handGrabHandlers)
        {
            var shouldAttemptGrab = characterGrabController != null
                ? characterGrabController.ShouldAttemptGrab(handler)
                : !handler.IsHolding && IsHandGrabActive(handler.Side);
            if (!shouldAttemptGrab)
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

    private bool TryThrowHeldItem()
    {
        if (!TryPrepareItemInteractionService(out var runtimeHost))
            return false;

        if (!_itemFieldInteractionService.TryDropHeldItem(out var droppedItemId, out _, out _))
            return false;

        var handAnchor = ResolveHeldItemHandAnchor();
        var anchorTransform = handAnchor != null ? handAnchor : transform;
        var throwForward = anchorTransform.forward;
        if (throwForward.sqrMagnitude < 0.0001f)
            throwForward = transform.forward;

        throwForward.y = Mathf.Max(0.1f, throwForward.y);
        throwForward.Normalize();

        var spawnPosition = anchorTransform.position + throwForward * 0.22f + Vector3.up * 0.08f;
        if (!TrySpawnNetworkedFieldDrop(droppedItemId, spawnPosition, runtimeHost, false, out var spawnedDrop))
            return false;

        var spawnedBody = spawnedDrop != null ? spawnedDrop.GetComponent<Rigidbody>() : null;
        if (spawnedBody != null)
        {
            if (spawnedBody.isKinematic)
            {
                spawnedBody.isKinematic = false;
                spawnedBody.useGravity = true;
            }

            var throwVelocity = throwForward * 5.2f + Vector3.up * 1.6f;
            spawnedBody.velocity = throwVelocity;
            spawnedBody.angularVelocity = Vector3.zero;
        }

        BeginGrabDisableWindow();
        RefreshHeldItemPresentationImmediate();
        RaiseAnimationEvent(AnimationEventType.Throw, H_Throw);
        return true;
    }

    private bool TryProcessThrow()
    {
        var didThrow = false;
        Transform thrownStunnedTargetRoot = null;
        foreach (var handler in _handGrabHandlers)
        {
            if (handler == null || !handler.IsHoldingThrowableTarget)
                continue;

            var targetRoot = handler.GrabTargetRoot;
            var isStunnedTarget = handler.IsHoldingStunnedPlayer;
            if (thrownStunnedTargetRoot != null && targetRoot == thrownStunnedTargetRoot)
                continue;

            handler.Throw();
            didThrow = true;

            if (isStunnedTarget && targetRoot != null)
                thrownStunnedTargetRoot = targetRoot;
        }

        if (didThrow)
        {
            BeginGrabDisableWindow();
            RaiseAnimationEvent(AnimationEventType.Throw, H_Throw);
        }

        return didThrow;
    }

    private void BeginGrabDisableWindow(float duration = 1f)
    {
        _grabDisabledUntilTime = Mathf.Max(_grabDisabledUntilTime, Time.time + Mathf.Max(0f, duration));
    }

    private static bool ShouldAllowKickFallback(bool anyHolding, bool hasHeldRuntimeItem)
    {
        return !anyHolding && !hasHeldRuntimeItem;
    }

    private bool TryProcessHeadbutt(bool anyHolding, bool hasHeldRuntimeItem)
    {
        var hasReachPending = false;
        var hasAttachPending = false;
        if (characterGrabController != null)
        {
            characterGrabController.RefreshNow();
            hasReachPending = characterGrabController.IsAnyReachActive;
            hasAttachPending = characterGrabController.CurrentActionState ==
                               CharacterGrabController.GrabActionState.AttachPending;
        }

        var canPerformHeadbuttActions = _isActiveRagdoll && !_isRecovering && !_isRecoverStabilizing && !GetIsDeadState();
        var beingGrabbed = _beingGrabbedRefCount > 0 || IsGrabbedByOther || NetworkedIsBeingGrabbed;
        var dragged = _localIsDragged || (IsNetworkReady && !HasStateAuthority && NetworkedIsDragged);
        if (!ShouldAllowHeadbuttDecision(
                anyHolding,
                hasHeldRuntimeItem,
                _isGrabActive,
                hasReachPending,
                hasAttachPending,
                canPerformHeadbuttActions,
                beingGrabbed,
                dragged,
                _localInstability,
                GetPhysicalPhase()))
        {
            return false;
        }

        if (!TryBeginHeadbuttHitDetection())
            return false;

        BeginGrabDisableWindow(0.35f);
        RaiseAnimationEvent(AnimationEventType.Headbutt, 0);
        return true;
    }

    private bool ShouldAllowAerialKickDecision(bool anyHolding, bool hasHeldRuntimeItem)
    {
        var hasReachPending = false;
        var hasAttachPending = false;
        if (characterGrabController != null)
        {
            characterGrabController.RefreshNow();
            hasReachPending = characterGrabController.IsAnyReachActive;
            hasAttachPending = characterGrabController.CurrentActionState ==
                               CharacterGrabController.GrabActionState.AttachPending;
        }

        if (!ShouldAllowAerialKickDecision(
                _isGrounded,
                anyHolding,
                hasHeldRuntimeItem,
                _isGrabActive,
                hasReachPending,
                hasAttachPending,
                CanPerformCombatActions,
                GetPhysicalPhase()))
        {
            return false;
        }

        return ShouldAllowAerialKickAirborneStart(
            _isGrounded,
            Time.time - _lastGroundedTime,
            _coyoteTimeRemaining,
            AreFeetClearForAerialKickStart(),
            _isAerialKickMomentumActive || _activeAerialKickBallisticFallActive || _aerialKickSpringRestoreTimer > 0f);
    }

    private static bool ShouldAllowAerialKickDecision(
        bool isGrounded,
        bool anyHolding,
        bool hasHeldRuntimeItem,
        bool isGrabActive,
        bool hasReachPending,
        bool hasAttachPending,
        bool canPerformCombatActions,
        PhysicalPhase phase)
    {
        if (!canPerformCombatActions ||
            isGrounded ||
            anyHolding ||
            hasHeldRuntimeItem ||
            isGrabActive ||
            hasReachPending ||
            hasAttachPending)
        {
            return false;
        }

        switch (phase)
        {
            case PhysicalPhase.GrabIntent:
            case PhysicalPhase.Holding:
            case PhysicalPhase.BeingGrabbed:
            case PhysicalPhase.Dragged:
            case PhysicalPhase.Recovering:
            case PhysicalPhase.CarryingStunned:
            case PhysicalPhase.WeaponEquipped:
            case PhysicalPhase.BeingCarriedStunned:
            case PhysicalPhase.StunnedCollapse:
            case PhysicalPhase.DraggedStunned:
            case PhysicalPhase.SettledStunned:
            case PhysicalPhase.Stunned:
                return false;
            default:
                return true;
        }
    }

    private static bool ShouldAllowAerialKickAirborneStart(
        bool isGrounded,
        float airborneElapsed,
        float coyoteTimeRemaining,
        bool feetClear,
        bool hasActiveAerialKickState)
    {
        if (isGrounded ||
            coyoteTimeRemaining > 0f ||
            !feetClear ||
            hasActiveAerialKickState)
        {
            return false;
        }

        return airborneElapsed >= AerialKickMinimumAirborneTriggerTime;
    }

    private static bool ShouldAllowHeadbuttDecision(
        bool anyHolding,
        bool hasHeldRuntimeItem,
        bool isGrabActive,
        bool hasReachPending,
        bool hasAttachPending,
        bool canPerformHeadbuttActions,
        bool beingGrabbed,
        bool dragged,
        float instability,
        PhysicalPhase phase)
    {
        if (!canPerformHeadbuttActions ||
            anyHolding ||
            hasHeldRuntimeItem ||
            isGrabActive ||
            hasReachPending ||
            hasAttachPending ||
            beingGrabbed ||
            dragged ||
            instability >= UnstableEnterThreshold)
        {
            return false;
        }

        switch (phase)
        {
            case PhysicalPhase.GrabIntent:
            case PhysicalPhase.Holding:
            case PhysicalPhase.BeingGrabbed:
            case PhysicalPhase.Dragged:
            case PhysicalPhase.Unstable:
            case PhysicalPhase.Recovering:
            case PhysicalPhase.CarryingStunned:
            case PhysicalPhase.WeaponEquipped:
            case PhysicalPhase.BeingCarriedStunned:
            case PhysicalPhase.StunnedCollapse:
            case PhysicalPhase.DraggedStunned:
            case PhysicalPhase.SettledStunned:
            case PhysicalPhase.Stunned:
                return false;
            default:
                return true;
        }
    }

    private void TryProcessSecondaryAction(bool anyHolding, bool hasHeldRuntimeItem)
    {
        if (anyHolding)
        {
            if (TryProcessThrow())
                return;

            return;
        }

        if (hasHeldRuntimeItem)
            return;

        if (TryProcessAerialKick())
            return;

        if (ShouldAllowKickFallback(anyHolding, hasHeldRuntimeItem))
            TryProcessKick();
    }

    private void TryProcessKick()
    {
        var isLeft = _hostNextKickLeft;
        if (!TryBeginKickHitDetection(isLeft))
            return;

        _hostNextKickLeft = !_hostNextKickLeft;
        var kickEvent = isLeft ? AnimationEventType.KickLeft : AnimationEventType.KickRight;
        RaiseAnimationEvent(kickEvent, H_Punch);
    }

    private bool TryProcessAerialKick()
    {
        var anyHolding = IsAnyHandHoldingObject();
        var hasHeldRuntimeItem = HasHeldRuntimeItem();
        if (!ShouldAllowAerialKickDecision(anyHolding, hasHeldRuntimeItem))
            return false;

        if (!TryBeginAerialKickHitDetection())
            return false;

        RaiseAnimationEvent(AnimationEventType.AerialKick, H_Punch);
        return true;
    }

    private void ResetInteractionTriggers()
    {
        _dropTriggered = false;
        _throwTriggered = false;
    }

    private bool IsAnyHandHoldingThrowableTarget()
    {
        if (characterGrabController != null)
        {
            characterGrabController.RefreshNow();
            return characterGrabController.HasThrowableHold();
        }

        foreach (var handler in _handGrabHandlers)
        {
            if (handler != null && handler.IsHoldingThrowableTarget)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true when either hand is holding a stunned player.
    /// Also supports proxy-side checks from replicated grab/carry state.
    /// </summary>
    internal bool IsAnyHandHoldingStunnedPlayer
    {
        get
        {
            if (characterGrabController != null)
            {
                characterGrabController.RefreshNow();
                return characterGrabController.HasAnyStunnedHold();
            }

            if (_handGrabHandlers != null)
            {
                foreach (var h in _handGrabHandlers)
                {
                    if (h != null && h.IsHoldingStunnedPlayer)
                        return true;
                }
            }

            if (IsNetworkReady && !HasStateAuthority &&
                GetPhysicalPhase() == PhysicalPhase.CarryingStunned)
                return true;

            return false;
        }
    }

    /// <summary>
    /// Returns whether the requested hand is holding something.
    /// StateAuthority uses local hand state, while proxies read the authoritative
    /// LeftGrabConfirmed / RightGrabConfirmed checkpoints directly.
    /// </summary>
    internal bool IsHandHoldingNetworked(HandGrabHandler.HandSide side)
    {
        if (IsNetworkReady && !HasStateAuthority)
            return side == HandGrabHandler.HandSide.Left
                ? (bool)LeftGrabConfirmed
                : (bool)RightGrabConfirmed;

        if (HasStateAuthority)
        {
            if (_handGrabHandlers == null) return false;
            foreach (var h in _handGrabHandlers)
            {
                if (h != null && h.Side == side && h.IsHolding)
                    return true;
            }

            return false;
        }

        if (characterGrabController != null)
        {
            characterGrabController.RefreshNow();
            return characterGrabController.IsHandHolding(side);
        }

        return false;
    }

    /// <summary>
    /// Returns true when both hands are holding the same stunned player.
    /// Used as a gate for overhead carry / two-hand carry transitions.
    /// </summary>
    internal bool IsDualGrabbingStunnedPlayer
    {
        get
        {
            if (characterGrabController != null)
            {
                characterGrabController.RefreshNow();
                return characterGrabController.IsDualHandHoldingSameStunnedTarget();
            }

            if (HasStateAuthority)
            {
                if (_handGrabHandlers == null || _handGrabHandlers.Length < 2)
                    return false;

                HandGrabHandler left = null, right = null;
                foreach (var h in _handGrabHandlers)
                {
                    if (h == null) continue;
                    if (h.Side == HandGrabHandler.HandSide.Left) left = h;
                    else right = h;
                }

                if (left == null || right == null)
                    return false;

                return left.IsHoldingStunnedPlayer && right.IsHoldingStunnedPlayer
                    && left.GrabTargetRoot != null && left.GrabTargetRoot == right.GrabTargetRoot;
            }

            if (IsNetworkReady && LeftGrabConfirmed && RightGrabConfirmed)
                return LeftGrabTargetId.IsValid && LeftGrabTargetId == RightGrabTargetId;

            return false;
        }
    }

    private void UpdateGrabbingAnimatorFlag()
    {
        if (animator == null)
            return;

        var phase = GetPhysicalPhase();
        var isCarrying = (phase == PhysicalPhase.Holding && IsAnyHandHoldingThrowableTarget())
            || phase == PhysicalPhase.CarryingStunned;
        var isGrabbing = (phase == PhysicalPhase.GrabIntent || phase == PhysicalPhase.Holding || phase == PhysicalPhase.CarryingStunned) && !isCarrying;
        if (characterGrabController != null)
        {
            characterGrabController.RefreshNow();
            isCarrying = characterGrabController.ShouldUseCarryPresentation;
            isGrabbing = characterGrabController.ShouldUseGrabPresentation &&
                         !UsesPhysicsPosePresentation(phase);
        }

        var isWeaponEquipped = phase == PhysicalPhase.WeaponEquipped;

        animator.SetBool(H_IsGrabbing, isGrabbing || isWeaponEquipped);
        animator.SetBool("isCarrying", isCarrying);
    }

    // Local grab prediction timer for OwnerProxy presentation.
    private float _grabPredictionStart = -1f;
    private const float GRAB_PREDICTION_TIMEOUT = 0.4f;

    /// <summary>
    /// Sync replicated grab / carry state into the animator on non-authority peers.
    /// StateAuthority uses UpdateGrabbingAnimatorFlag() directly.
    /// OwnerProxy predicts grab immediately and rolls back if host confirmation never arrives.
    ///
    /// LeftGrabConfirmed / RightGrabConfirmed are the authoritative checkpoints.
    /// If they do not arrive in time, GRAB_PREDICTION_TIMEOUT rolls presentation back.
    /// This keeps owner feel responsive without desyncing remote presentation.
    /// </summary>
    internal void SyncGrabbingAnimatorFromNetwork()
    {
        if (animator == null) return;

        var phase = GetPhysicalPhase();
        var confirmedHolding = phase == PhysicalPhase.Holding;
        var showGrabFromState = phase == PhysicalPhase.GrabIntent || confirmedHolding;
        var isCarrying = confirmedHolding && IsConfirmedGrabTargetStunned();
        if (characterGrabController != null)
        {
            characterGrabController.RefreshNow();
            confirmedHolding = characterGrabController.ShouldLockFacingToHoldTarget;
            showGrabFromState = characterGrabController.ShouldPreserveGrabPose;
            isCarrying = characterGrabController.ShouldUseCarryPresentation;
        }

        // OwnerProxy prediction: reflect local grab instantly, then roll back if not confirmed.
        bool localPredicting = HasInputAuthority && !HasStateAuthority
            && ((_leftMouseDown && _leftMouseConsumedAsGrab) || (_rightMouseDown && _rightMouseConsumedAsGrab));

        bool showGrab;
        if (localPredicting && !confirmedHolding)
        {
            // Local player is trying to grab, but host has not confirmed it yet.
            if (_grabPredictionStart < 0f)
                _grabPredictionStart = Time.time;
            showGrab = Time.time - _grabPredictionStart < GRAB_PREDICTION_TIMEOUT;
        }
        else
        {
            _grabPredictionStart = -1f;
            showGrab = showGrabFromState || localPredicting;
        }

        bool isGrabbing = showGrab && !isCarrying && !UsesPhysicsPosePresentation(phase);

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
        if (!TryPrepareItemInteractionService(out var runtimeHost))
            return false;

        var itemIdBeforeUse = runtimeHost?.HeldItemId ?? string.Empty;
        if (!_itemFieldInteractionService.TryUseHeldItem(out _, out _, out _))
            return false;

        RefreshHeldItemPresentationImmediate();
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
        return HasHeldRuntimeItem(ResolveCurrentRuntimeHost());
    }

    private bool HasHeldRuntimeItem(ItemRuntimeHost runtimeHost)
    {
        if (runtimeHost != null && !string.IsNullOrWhiteSpace(runtimeHost.HeldItemId))
            return true;

        return !string.IsNullOrWhiteSpace(NetworkedHeldItemId.ToString()) ||
               !string.IsNullOrWhiteSpace(_lastReplicatedHeldItemId);
    }

    private bool IsHoldingRuntimeItem(string itemId)
    {
        return IsHoldingRuntimeItem(itemId, ResolveCurrentRuntimeHost());
    }

    private bool IsHoldingRuntimeItem(string itemId, ItemRuntimeHost runtimeHost)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return false;

        if (runtimeHost != null &&
            string.Equals(runtimeHost.HeldItemId, itemId, System.StringComparison.Ordinal))
            return true;

        var replicatedHeldItemId = NetworkedHeldItemId.ToString();
        return string.Equals(replicatedHeldItemId, itemId, System.StringComparison.Ordinal) ||
               string.Equals(_lastReplicatedHeldItemId, itemId, System.StringComparison.Ordinal);
    }

    private bool TryPickupNearestFieldItemByKey()
    {
        if (!TryPrepareItemInteractionService(out _))
            return false;

        if (!_itemFieldInteractionService.TryPickupNearest(out _, out _, out _, out _))
            return false;

        return true;
    }

    private bool TryDropHeldItemByKey()
    {
        if (!TryPrepareItemInteractionService(out var runtimeHost))
            return false;

        if (!_itemFieldInteractionService.TryDropHeldItem(out var droppedItemId, out var dropSpawnPosition, out _))
            return false;

        if (!TrySpawnNetworkedFieldDrop(droppedItemId, dropSpawnPosition, runtimeHost, false, out _))
            return false;

        RefreshHeldItemPresentationImmediate();
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
        runtimeHost = ResolveCurrentRuntimeHost();
        if (runtimeHost == null)
        {
            EnsureItemRuntimeIntegration();
            runtimeHost = ResolveCurrentRuntimeHost();
        }

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

    private ItemRuntimeHost ResolveCurrentRuntimeHost()
    {
        var runtimeHost = ResolveItemRuntimeHostForCharacter();
        if (runtimeHost != null)
        {
            _itemRuntimeHost = runtimeHost;
        }

        return _itemRuntimeHost;
    }

    internal void RefreshHeldItemPresentationImmediate()
    {
        SyncHeldItemNetworkState();
        ApplyReplicatedHeldItemPresentation();
    }
}
