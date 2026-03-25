using Fusion;
using RootMotion.Dynamics;
using UnityEngine;

public sealed partial class NetworkPlayer
{
    private float _localAccumulatedStun;
    private float _localStunTimeRemaining;
    private float _stunCollapseTimer;
    private float _stunnedFloorSettleTimer;
    private float _stunnedGroundContactTimer;
    private float _downedRecoverScale = 1f;
    private float _downedHitCooldownRemaining;

    // Coyote time + Jump buffer
    private float _coyoteTimeRemaining;
    private float _jumpBufferRemaining;
    private const float COYOTE_TIME = 0.1f;
    private const float JUMP_BUFFER_TIME = 0.1f;
    private const float InstabilityRiseSpeed = 3.5f;
    private const float InstabilityFallSpeed = 2.25f;
    private const float UnstableEnterThreshold = 0.48f;
    private const float UnstableExitThreshold = 0.26f;
    private const float GroggyEntryThresholdRatio = 0.65f;
    private const float GroggyStunFailRetainRatio = 0.85f;
    private const float DragPlanarSpeedThreshold = 1.75f;
    private const float DragAngularSpeedThreshold = 3.5f;
    private const float StunLaunchKnockbackScale = 0.45f;
    private const float StunEntryLinearKnockbackScale = 0.14f;
    private const float GroundedStunEntryLinearKnockbackScale = 0.035f;
    private const float StunEntryToppleKnockbackScale = 0.28f;
    private const float GroundedStunEntryToppleKnockbackScale = 0.34f;
    private const float DownedRepeatHitRootKnockbackScale = 0.05f;
    private const float StunEntryRootPlanarVelocityScale = 0.38f;
    private const float StunEntryRootPlanarSpeedCap = 1.6f;
    private const float GroundedStunEntryRootPlanarVelocityScale = 0.18f;
    private const float GroundedStunEntryRootPlanarSpeedCap = 0.55f;
    private const float StunEntryRootAngularVelocityScale = 0.22f;
    private const float GroundedStunEntryRootAngularVelocityScale = 0.08f;
    private const float StunEntryMusclePlanarVelocityScale = 0.35f;
    private const float StunEntryMusclePlanarSpeedCap = 1.15f;
    private const float GroundedStunEntryMusclePlanarVelocityScale = 0.20f;
    private const float GroundedStunEntryMusclePlanarSpeedCap = 0.50f;
    private const float StunEntryMuscleAngularVelocityScale = 0.25f;
    private const float GroundedStunEntryMuscleAngularVelocityScale = 0.10f;
    private const float StunCollapseDuration = 0.25f;
    private const float StunCollapseEarlyDuration = 0.12f;
    private const float SettledStunnedEntryDuration = 0.10f;
    private const float SettledStunnedRootPlanarThreshold = 0.42f;
    private const float SettledStunnedRootAngularThreshold = 1.35f;
    private const float SettledStunnedMusclePlanarThreshold = 0.36f;
    private const float SettledStunnedTimerDecayRate = 2.0f;
    private const float StunCollapseEntryMainSpringScale = 0.08f;
    private const float StunCollapseEntryBoneSpringLerp = 0.06f;
    private const float StunnedGroundedMainSpringScale = 0.10f;
    private const float StunnedCarriedMainSpringScale = 0.35f;
    private const float StunnedRootPlanarSpeedCap = 0.78f;
    private const float StunnedMusclePlanarSpeedCap = 0.66f;
    private const float StunnedRootAngularSpeedCap = 1.10f;
    private const float StunnedMuscleAngularSpeedCap = 1.35f;
    private const float CollapseRootPlanarSpeedCap = 1.05f;
    private const float CollapseMusclePlanarSpeedCap = 0.82f;
    private const float CollapseRootAngularSpeedCap = 1.55f;
    private const float CollapseMuscleAngularSpeedCap = 1.85f;
    private const float CollapseEarlyRootPlanarSpeedCap = 0.60f;
    private const float CollapseEarlyMusclePlanarSpeedCap = 0.48f;
    private const float CollapseEarlyRootAngularSpeedCap = 0.70f;
    private const float CollapseEarlyMuscleAngularSpeedCap = 0.90f;
    private const float CollapseEarlyGroundedPlanarDrag = 5.5f;
    private const float CollapseGroundedPlanarDrag = 7.5f;
    private const float StunnedGroundedPlanarDrag = 9.5f;
    private const float SettledStunnedRootPlanarSpeedCap = 0.18f;
    private const float SettledStunnedMusclePlanarSpeedCap = 0.16f;
    private const float SettledStunnedRootAngularSpeedCap = 0.32f;
    private const float SettledStunnedMuscleAngularSpeedCap = 0.42f;
    private const float SettledStunnedGroundedPlanarDrag = 22.0f;
    private const float StunnedGroundContactMemory = 0.12f;
    private const float DraggedStunnedRootPlanarSpeedCap = 0.78f;
    private const float DraggedStunnedMusclePlanarSpeedCap = 0.62f;
    private const float DraggedStunnedRootAngularSpeedCap = 2.4f;
    private const float DraggedStunnedMuscleAngularSpeedCap = 2.8f;
    private const float DraggedStunnedGroundedPlanarDrag = 14.0f;
    private const float CollapseEarlyAngularDampingRate = 14f;
    private const float CollapseAngularDampingRate = 10f;
    private const float StunnedAngularDampingRate = 12f;
    private const float SettledStunnedAngularDampingRate = 18f;
    private const float DraggedStunnedAngularDampingRate = 9f;
    private const float CarriedStunnedAngularDampingRate = 6f;
    // BeingCarriedStunned: 운반 중 피해자는 위로 끌려야 하므로 클램프 완화
    private const float CarriedStunnedRootPlanarSpeedCap = 2.50f;
    private const float CarriedStunnedMusclePlanarSpeedCap = 2.00f;
    private const float CarriedStunnedRootAngularSpeedCap = 3.8f;
    private const float CarriedStunnedMuscleAngularSpeedCap = 4.0f;
    private const float CarriedStunnedMaxUpwardSpeed = 3.0f;
    private const float StunRootUpwardSyncStep = 0.08f;
    private const float DownedRootStrongCorrectionDistance = 0.75f;
    private const float DownedRootEmergencySnapDistance = 1.25f;
    private const float DownedRootStrongVerticalGap = 0.55f;
    private const float DownedRootEmergencyVerticalGap = 1.10f;
    private const float DownedRootPlanarFollowSpeed = 12.0f;
    private const float DownedRootVerticalFollowSpeed = 16.0f;
    private const float GroundedPlainStunNoCollapsePlanarThreshold = 0.85f;
    private const float GroundedPlainStunNoCollapseVerticalThreshold = 0.40f;
    private const float GroundedPlainStunNoCollapseAngularThreshold = 0.55f;
    private const float GroundedPlainStunNoCollapsePelvisVerticalThreshold = 0.45f;
    private const float CarriedRootTraceGapThreshold = 0.3f;
    private const float HitInstabilityBoostMin = 0.08f;
    private const float HitInstabilityBoostMax = 0.22f;
    private const float HitInstabilityBoostDecay = 1.5f;
    private const float HitReactionMoveSpeedScale = 0.82f;
    private const float HitReactionBrakeScale = 0.78f;
    private const float HitReactionGroundStickScale = 0.78f;
    private const float HostRemoteClientMoveSpeedCompensation = 1f;

    private void DoPhysicsStep(PlayerNetworkInput input, float dt)
    {
        if (config == null || rigidbody3D == null || mainJoint == null)
            return;

        // Single-button grab: left-hold enables grab attempts on both hands.
        // Each hand still decides attachment independently from its own reach/target distance.
        var unifiedGrabHold = input.LeftGrabHold || input.RightGrabHold;
        _isLeftGrabActive = unifiedGrabHold;
        _isRightGrabActive = unifiedGrabHold;
        _isGrabActive = unifiedGrabHold;

        // TickHitStopRecovery(); // 히트스탑 제거
        TickHitRecoil(dt);
        TickHitFlinch(dt);
        TickHitInstabilityBoost(dt);
        TickVitalState(dt);
        UpdateStunDecay(dt);
        UpdateRecoveringWindow(dt);

        if (TryTickDeadState(dt))
            return;

        if (TryTickStunnedState(dt))
            return;

        SimulateLocomotion(input, dt);
        SynchronizeMotorPresentation();
        UpdateActiveRagdollJoints();
        ProcessInteractions(input);
        UpdatePhysicalPhaseState(dt);
        TickPunchHitDetectionWindow();
        TickKickHitDetectionWindow();
        TickHeadbuttHitDetectionWindow();
        TickAerialKickHitDetectionWindow(dt);
        TickAerialKickMomentum(dt);
        TickAerialKickSpringRestore(dt);
        SyncHeldItemNetworkState();
    }

    public void ApplyStunDamage(
        float stunDamage,
        float bodyPartMultiplier,
        float attackerVelocity,
        float impulseMagnitude,
        bool deferStunEntryDamping = false,
        NetworkPlayer instigator = null,
        int hitCountToStun = 0,
        float groggyVulnerabilityMultiplier = 1f,
        DownedHitPolicy downedHitPolicy = DownedHitPolicy.Ignore)
    {
        if (GetIsDeadState())
            return;

        var buffApplier = ResolveItemBuffApplier();
        if (buffApplier != null && buffApplier.IsSuperArmorActive)
            return;

        if (!_isActiveRagdoll)
        {
            ApplyDownedHitPenalty(stunDamage, impulseMagnitude, downedHitPolicy);
            return;
        }

        if (GetStunComboWindowRemaining() <= 0f)
            SetRecentStunHitCount(0);

        var requiredHitCount = Mathf.Max(1, hitCountToStun);
        var nextHitCount = Mathf.Max(1, GetRecentStunHitCount() + 1);
        SetRecentStunHitCount(nextHitCount);
        SetStunComboWindowRemaining(Mathf.Max(GetStunComboWindowRemaining(), ResolveConfiguredStunComboWindow()));

        var noStaggerActive = GetNoStaggerRemaining() > 0f;
        var rehitImmunityActive = GetStunHitImmunityRemaining() > 0f;
        var groggyActive = IsGroggyActive();
        var finalStunDamage = stunDamage * bodyPartMultiplier * ResolveStunStateMultiplier();
        if (groggyActive)
            finalStunDamage *= Mathf.Max(1f, groggyVulnerabilityMultiplier);
        if (rehitImmunityActive)
            finalStunDamage *= ResolveConfiguredRepeatStunDamageScale();
        finalStunDamage = ConsumeStunShield(finalStunDamage);

        if (finalStunDamage <= 0.01f)
        {
            // 데미지가 흡수되어도 히트 VFX는 표시해야 한다.
            // GetHit 대신 HitVFXOnly를 발생시켜 스태거 애니메이션 없이 VFX만 트리거한다.
            RaiseAnimationEvent(AnimationEventType.HitVFXOnly, 0);
            SetStunHitImmunityRemaining(Mathf.Max(GetStunHitImmunityRemaining(), ResolveConfiguredStunRehitImmunity()));
            SetNoStaggerRemaining(Mathf.Max(GetNoStaggerRemaining(), ResolveConfiguredNoStaggerWindow()));
            return;
        }

        var accumulated = AddStunDamage(finalStunDamage);
        if (!noStaggerActive)
        {
            ArmHitInstabilityBoost(Mathf.Max(impulseMagnitude, finalStunDamage * 0.6f));
            RaiseAnimationEvent(AnimationEventType.GetHit, H_GetHit);
        }
        else
        {
            // noStagger 상태에서도 히트 VFX는 표시해야 한다.
            // 스태거 애니메이션 없이 VFX만 발생시킨다.
            RaiseAnimationEvent(AnimationEventType.HitVFXOnly, 0);
        }

        SetStunHitImmunityRemaining(Mathf.Max(GetStunHitImmunityRemaining(), ResolveConfiguredStunRehitImmunity()));
        SetNoStaggerRemaining(Mathf.Max(GetNoStaggerRemaining(), ResolveConfiguredNoStaggerWindow()));

        var threshold = CombatSettings.Instance != null
            ? CombatSettings.Instance.knockoutThreshold
            : 30f;
        if (groggyActive)
            RefreshGroggyState(nextHitCount);
        else if (ShouldEnterGroggy(requiredHitCount, nextHitCount, accumulated, threshold))
            EnterGroggy(nextHitCount);

        if (accumulated >= threshold && HasReachedStunHitRequirement(requiredHitCount, nextHitCount))
        {
            // groggy 상태에서는 확률 판정: groggyToStunChance로 stun 전환 결정
            if (groggyActive && !RollGroggyToStunChance())
            {
                // 확률 실패: stun 대신 groggy 유지, 축적량을 threshold 직전으로 클램프
                SetAccumulatedStun(threshold * GroggyStunFailRetainRatio);
                RefreshGroggyState(nextHitCount);
                if (!noStaggerActive)
                    _hitRecoilTimer = HIT_RECOIL_DURATION;
            }
            else
            {
                var overflow = Mathf.Max(0f, accumulated - threshold);
                TriggerStun(
                    CalculateStunDuration(attackerVelocity, impulseMagnitude, overflow, threshold),
                    applyEntryDamping: !deferStunEntryDamping);

                if (instigator != null && instigator != this)
                    instigator.TriggerKnockoutConfirm();
            }
        }
        else if (!noStaggerActive)
            _hitRecoilTimer = HIT_RECOIL_DURATION;
    }

    public void OnPlayerBodyPartHit()
    {
        ApplyCombinedDamage(2f, 6f, "BodyPartHit");
    }

    private void UpdateStunDecay(float dt)
    {
        if (GetIsDeadState())
            return;

        var decayRate = CombatSettings.Instance != null
            ? Mathf.Max(0f, CombatSettings.Instance.stunAccumulateDecay)
            : 5f;
        var accumulated = GetAccumulatedStun() - decayRate * dt;
        SetAccumulatedStun(Mathf.Max(0f, accumulated));
    }

    private bool HasReachedStunHitRequirement(int requiredHitCount, int recentHitCount)
    {
        return requiredHitCount <= 1 || recentHitCount >= requiredHitCount;
    }

    private bool ShouldEnterGroggy(int requiredHitCount, int recentHitCount, float accumulated, float threshold)
    {
        if (IsGroggyActive())
            return false;

        if (requiredHitCount > 1 && recentHitCount >= requiredHitCount - 1)
            return true;

        if (threshold <= 0.01f)
            return false;

        return accumulated >= threshold * GroggyEntryThresholdRatio;
    }

    private void EnterGroggy(int recentHitCount)
    {
        var duration = ResolveConfiguredGroggyDuration();
        if (duration <= 0f)
            return;

        SetGroggyRemaining(Mathf.Max(GetGroggyRemaining(), duration));
        SetRecentStunHitCount(Mathf.Max(GetRecentStunHitCount(), recentHitCount));
        _localInstability = Mathf.Max(_localInstability, UnstableEnterThreshold);
    }

    private void RefreshGroggyState(int recentHitCount)
    {
        if (!IsGroggyActive())
            return;

        SetGroggyRemaining(Mathf.Max(GetGroggyRemaining(), ResolveConfiguredGroggyDuration()));
        SetRecentStunHitCount(Mathf.Max(GetRecentStunHitCount(), recentHitCount));
    }

    /// <summary>
    /// groggy 상태에서 threshold 도달 시 stun 전환 확률 판정.
    /// CombatSettings.groggyToStunChance (0~1) 기반. 1.0이면 항상 전환.
    /// </summary>
    private bool RollGroggyToStunChance()
    {
        var chance = CombatSettings.Instance != null
            ? Mathf.Clamp01(CombatSettings.Instance.groggyToStunChance)
            : 0.7f;

        if (chance >= 1f)
            return true;
        if (chance <= 0f)
            return false;

        return UnityEngine.Random.value <= chance;
    }

    private float ConsumeStunShield(float stunDamage)
    {
        if (stunDamage <= 0.01f)
            return 0f;

        var recoverDelay = ResolveConfiguredStunShieldRecoverDelay();
        if (recoverDelay > 0f)
            SetStunShieldRecoverDelayRemaining(Mathf.Max(GetStunShieldRecoverDelayRemaining(), recoverDelay));

        var shield = GetStunShield();
        if (shield <= 0.01f)
            return stunDamage;

        var absorbed = Mathf.Min(shield, stunDamage);
        SetStunShield(shield - absorbed);
        return stunDamage - absorbed;
    }

    private void RestoreRecoveryStunShield()
    {
        var refill = ResolveConfiguredStunShieldRecoveryRefill();
        if (refill <= 0f)
            return;

        SetStunShield(Mathf.Max(GetStunShield(), refill));
        SetStunShieldRecoverDelayRemaining(0f);
    }

    // 2-phase 회복: stabilization(스프링 점진 복원) + vulnerable(취약 창)
    private bool _isRecoverStabilizing;
    private float _recoverStabilizeTimer;
    private const float RECOVER_STABILIZE_DURATION = 0.4f;
    private const float GroundedCollapseRootMinYOffset = -0.12f;
    private float _recoverMinColliderY = float.NegativeInfinity;
    private bool _hasRecoverAnchorPose;
    private Vector3 _recoverAnchorPosition;
    private Quaternion _recoverAnchorRotation = Quaternion.identity;
    private bool _recoverAnchorCapturedWhileGrounded;
    private bool _hasPendingRecoveryStandUpHandoff;
    private Vector3 _pendingRecoveryStandUpPosition;
    private Quaternion _pendingRecoveryStandUpRotation = Quaternion.identity;
    private bool ShouldUseCollapseAnchor()
    {
        if (!_hasRecoverAnchorPose)
            return false;

        if (_beingGrabbedRefCount > 0 || IsDualGrabbingStunnedPlayer)
        {
            _hasRecoverAnchorPose = false;
            return false;
        }

        return !_isActiveRagdoll || _isRecovering || _isRecoverStabilizing;
    }

    private void CaptureCollapseAnchorPose(Vector3 position, Quaternion rotation)
    {
        TryResolveCollapseAnchorPose(position, rotation, out _recoverAnchorPosition, out _recoverAnchorRotation);
        _hasRecoverAnchorPose = true;
        _recoverAnchorCapturedWhileGrounded = _isGrounded;
    }

    private void RefreshCollapseAnchorPoseFromCurrentPose(bool beingCarried)
    {
        if (beingCarried)
        {
            _hasRecoverAnchorPose = false;
            _recoverAnchorCapturedWhileGrounded = false;
            return;
        }

        var referenceRotation = _targetRoot != null ? _targetRoot.rotation : transform.rotation;
        CaptureCollapseAnchorPose(transform.position, referenceRotation);
    }

    private void TryResolveCollapseAnchorPose(
        Vector3 fallbackPosition,
        Quaternion fallbackRotation,
        out Vector3 anchorPosition,
        out Quaternion anchorRotation)
    {
        if (!TryGetRecoverReferencePosition(out anchorPosition))
            anchorPosition = fallbackPosition;

        var planarFacing = Vector3.zero;
        if (TryResolveRecoveryFacingVector(out var facing))
            planarFacing = Vector3.ProjectOnPlane(facing, Vector3.up);

        if (planarFacing.sqrMagnitude <= 0.0001f && _targetRoot != null)
            planarFacing = Vector3.ProjectOnPlane(_targetRoot.forward, Vector3.up);
        if (planarFacing.sqrMagnitude <= 0.0001f)
            planarFacing = Vector3.ProjectOnPlane(fallbackRotation * Vector3.forward, Vector3.up);
        if (planarFacing.sqrMagnitude <= 0.0001f)
            planarFacing = Vector3.ProjectOnPlane(transform.forward, Vector3.up);

        if (planarFacing.sqrMagnitude > 0.0001f)
            anchorRotation = Quaternion.LookRotation(planarFacing.normalized, Vector3.up);
        else if (_hasRecoverAnchorPose)
            anchorRotation = _recoverAnchorRotation;
        else
            anchorRotation = Quaternion.Euler(0f, fallbackRotation.eulerAngles.y, 0f);
    }

    private void UpdateRecoveringWindow(float dt)
    {
        if (_isRecoverStabilizing)
        {
            TickRecoverStabilization(dt);
            return;
        }

        if (!_isRecovering)
            return;

        // recover 중에도 root를 pelvis에 동기화 유지
        MaintainRecoveringHorizontalAnchor();
        MaintainRecoveringUprightRotation();
        MaintainRecoveringAboveGround();
        SyncRootToPhysicsBody();
        TraceStunnedMotionSample("UpdateRecoveringWindow");

        _recoveringTimer -= dt;
        if (_recoveringTimer <= 0f)
        {
            _isRecovering = false;
            _recoverMinColliderY = float.NegativeInfinity;
            _hasRecoverAnchorPose = false;
            // 복구 완료 시 최종 root-to-pelvis 스냅
            SyncRootToPhysicsBody();
            SetLocalPhysicalPhase(PhysicalPhase.Stable, 0f, false);
            FlagPhysicsPresentationReset();
        }
    }

    private void InterruptRecoveryForInboundGrab(string source)
    {
        if (!_isRecovering && !_isRecoverStabilizing && !_hasPendingRecoveryStandUpHandoff)
            return;

        _isRecovering = false;
        _isRecoverStabilizing = false;
        _recoveringTimer = 0f;
        _recoverStabilizeTimer = 0f;
        _hasPendingRecoveryStandUpHandoff = false;
        _recoverMinColliderY = float.NegativeInfinity;
        _hasRecoverAnchorPose = false;
        _recoverAnchorCapturedWhileGrounded = false;
        SetRecoveryAnimationVariant(RecoveryAnimationVariant.None);
        SynchronizeStunPresentationPhase();
        TraceCarryDebugSample("InterruptRecoveryForInboundGrab", $"source={source}", true);
    }

    private void TickHitInstabilityBoost(float dt)
    {
        if (_hitInstabilityBoost <= 0f)
            return;

        _hitInstabilityBoost = Mathf.Max(0f, _hitInstabilityBoost - dt * HitInstabilityBoostDecay);
    }

    private void ArmHitInstabilityBoost(float impactMagnitude)
    {
        var normalizedImpact = NormalizePunchImpact(Mathf.Max(impactMagnitude, 0f));
        var boost = Mathf.Lerp(HitInstabilityBoostMin, HitInstabilityBoostMax, normalizedImpact);
        _hitInstabilityBoost = Mathf.Max(_hitInstabilityBoost, boost);
        _localInstability = Mathf.Max(_localInstability, Mathf.Min(1f, HIT_RECOIL_INSTABILITY_FLOOR + boost * 0.2f));
    }

    private bool ShouldSuppressRepeatedHitReaction()
    {
        return GetNoStaggerRemaining() > 0f;
    }

    private void ApplyCloseCombatHitReaction(Vector3 hitPoint, float impactMagnitude, bool suppressReaction)
    {
        if (suppressReaction)
            return;

        ArmHitInstabilityBoost(impactMagnitude);
        ArmHitFlinch(impactMagnitude);
        ArmDirectionalCombatFlinch(hitPoint, impactMagnitude);
    }

    private bool ShouldApplyHitMovementPenalty()
    {
        if (!_isActiveRagdoll || _isRecovering || _isRecoverStabilizing)
            return false;

        return _hitInstabilityBoost > 0.01f && (IsInHitRecoil || _localInstability >= UnstableExitThreshold * 0.8f);
    }

    private void TickPunchHitDetectionWindow()
    {
        if (_activePunchWindowEndTick < 0)
            return;

        if (Runner != null && Object != null && Object.IsValid && !HasStateAuthority)
        {
            ClearPunchHitDetectionWindow();
            return;
        }

        var currentTick = ResolveCurrentSimulationTick();
        if (currentTick > _activePunchWindowEndTick || !_isActiveRagdoll)
        {
            ClearPunchHitDetectionWindow();
            return;
        }

        var currentSamplePosition = ResolvePunchHitSamplePosition(_activePunchIsLeft);
        var previousSamplePosition = _activePunchHasPreviousSample
            ? _activePunchPreviousSamplePosition
            : currentSamplePosition;

        _activePunchPreviousSamplePosition = currentSamplePosition;
        _activePunchHasPreviousSample = true;

        if (!TryResolvePunchVictim(previousSamplePosition, currentSamplePosition, out var victimPlayer, out var hitPoint))
            return;

        ApplyPunchHit(victimPlayer, hitPoint);
        ClearPunchHitDetectionWindow();
    }

    private void TickKickHitDetectionWindow()
    {
        if (_activeKickWindowEndTick < 0)
            return;

        if (Runner != null && Object != null && Object.IsValid && !HasStateAuthority)
        {
            ClearKickHitDetectionWindow();
            return;
        }

        var currentTick = ResolveCurrentSimulationTick();
        if (currentTick > _activeKickWindowEndTick || !_isActiveRagdoll)
        {
            ClearKickHitDetectionWindow();
            return;
        }

        var currentSamplePosition = ResolveKickHitSamplePosition(_activeKickIsLeft);
        var previousSamplePosition = _activeKickHasPreviousSample
            ? _activeKickPreviousSamplePosition
            : currentSamplePosition;

        _activeKickPreviousSamplePosition = currentSamplePosition;
        _activeKickHasPreviousSample = true;

        if (!TryResolveKickVictim(previousSamplePosition, currentSamplePosition, out var victimPlayer, out var hitPoint))
            return;

        ApplyKickHit(victimPlayer, hitPoint);
        ClearKickHitDetectionWindow();
    }

    private void TickHeadbuttHitDetectionWindow()
    {
        if (_activeHeadbuttWindowEndTick < 0)
            return;

        if (Runner != null && Object != null && Object.IsValid && !HasStateAuthority)
        {
            ClearHeadbuttHitDetectionWindow();
            return;
        }

        if (ShouldCancelActiveHeadbuttWindow())
        {
            ClearHeadbuttHitDetectionWindow();
            return;
        }

        var currentTick = ResolveCurrentSimulationTick();
        if (currentTick > _activeHeadbuttWindowEndTick)
        {
            ClearHeadbuttHitDetectionWindow();
            return;
        }

        var currentSamplePosition = ResolveHeadbuttHitSamplePosition();
        var previousSamplePosition = _activeHeadbuttHasPreviousSample
            ? _activeHeadbuttPreviousSamplePosition
            : currentSamplePosition;

        _activeHeadbuttPreviousSamplePosition = currentSamplePosition;
        _activeHeadbuttHasPreviousSample = true;

        if (TryResolveHeadbuttVictim(previousSamplePosition, currentSamplePosition, out var victimPlayer, out var hitPoint))
        {
            ApplyHeadbuttHit(victimPlayer, hitPoint);
            ClearHeadbuttHitDetectionWindow();
            return;
        }

        if (TryResolveHeadbuttEnvironmentImpact(previousSamplePosition, currentSamplePosition, out var environmentHit, out var impactType))
        {
            ApplyHeadbuttEnvironmentImpact(environmentHit, impactType);
            ClearHeadbuttHitDetectionWindow();
        }
    }

    private bool ShouldCancelActiveHeadbuttWindow()
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

        var beingGrabbed = _beingGrabbedRefCount > 0 || IsGrabbedByOther || NetworkedIsBeingGrabbed;
        var dragged = _localIsDragged || (IsNetworkReady && !HasStateAuthority && NetworkedIsDragged);
        var canPerformHeadbuttActions = _isActiveRagdoll && !_isRecovering && !_isRecoverStabilizing && !GetIsDeadState();
        return !ShouldAllowHeadbuttDecision(
            IsAnyHandHoldingObject(),
            HasHeldRuntimeItem(),
            _isGrabActive,
            hasReachPending,
            hasAttachPending,
            canPerformHeadbuttActions,
            beingGrabbed,
            dragged,
            _localInstability,
            GetPhysicalPhase());
    }

    private void TickAerialKickHitDetectionWindow(float dt)
    {
        if (_activeAerialKickWindowEndTick < 0)
            return;

        if (Runner != null && Object != null && Object.IsValid && !HasStateAuthority)
        {
            ClearAerialKickHitDetectionWindow();
            BeginAerialKickSpringRestore("lost-authority");
            return;
        }

        if (!_isActiveRagdoll || GetIsDeadState())
        {
            ClearAerialKickHitDetectionWindow();
            BeginAerialKickSpringRestore("inactive-ragdoll");
            return;
        }

        var currentTick = ResolveCurrentSimulationTick();
        if (_activeAerialKickHasPreviousSample && _isGrounded)
        {
            _activeAerialKickGroundedGraceTimer -= dt;
            if (_activeAerialKickGroundedGraceTimer <= 0f)
            {
                ApplyAerialKickMissPenalty();
                ClearAerialKickHitDetectionWindow();
                BeginAerialKickSpringRestore("grounded-during-hit-window");
                return;
            }
        }
        else
        {
            _activeAerialKickGroundedGraceTimer = AerialKickGroundedGraceDuration;
        }

        if (currentTick > _activeAerialKickWindowEndTick)
        {
            // 공중에서 윈도우가 만료되면 착지 시점까지 미스 패널티를 지연한다.
            // 즉시 스턴을 걸면 짧은 스턴이 공중에서 끝나 Idle 포즈로 서있는 버그가 발생한다.
            if (!_isGrounded && !_activeAerialKickNearGround && _activeAerialKickHasLeftGround)
            {
                if (!_activeAerialKickHasHit)
                    _aerialKickMissPenaltyPending = true;
                ClearAerialKickHitDetectionWindow();
                return;
            }

            ApplyAerialKickMissPenalty();
            ClearAerialKickHitDetectionWindow();
            return;
        }

        var currentSamplePosition = ResolveAerialKickHitSamplePosition();
        var previousSamplePosition = _activeAerialKickHasPreviousSample
            ? _activeAerialKickPreviousSamplePosition
            : currentSamplePosition;

        _activeAerialKickPreviousSamplePosition = currentSamplePosition;
        _activeAerialKickHasPreviousSample = true;

        if (!TryResolveAerialKickVictim(previousSamplePosition, currentSamplePosition, out var victimPlayer, out var hitPoint))
            return;

        _activeAerialKickHasHit = true;
        ApplyAerialKickHit(victimPlayer, hitPoint);
        ClearAerialKickHitDetectionWindow();
    }

    private int ResolveCurrentSimulationTick()
    {
        return Runner != null ? Runner.Tick.Raw : Mathf.RoundToInt(Time.time / Time.fixedDeltaTime);
    }

    private void ClearPunchHitDetectionWindow()
    {
        _activePunchWindowEndTick = -1;
        _activePunchHasPreviousSample = false;
    }

    private void ClearKickHitDetectionWindow()
    {
        _activeKickWindowEndTick = -1;
        _activeKickHasPreviousSample = false;
    }

    private void ClearHeadbuttHitDetectionWindow()
    {
        _activeHeadbuttWindowEndTick = -1;
        _activeHeadbuttHasPreviousSample = false;
    }

    private void ClearAerialKickHitDetectionWindow()
    {
        _activeAerialKickWindowEndTick = -1;
        _activeAerialKickHasPreviousSample = false;
        _activeAerialKickHasHit = false;
        _activeAerialKickGroundedGraceTimer = 0f;
        if (!_isAerialKickMomentumActive)
        {
            _activeAerialKickTargetPlanarSpeed = 0f;
            _activeAerialKickBallisticFallActive = false;
            _activeAerialKickHasLeftGround = false;
            _activeAerialKickFlightForceReleaseTime = float.NegativeInfinity;
            _activeAerialKickStartedAt = float.NegativeInfinity;
            _activeAerialKickLandingConfirmTimer = 0f;
            _activeAerialKickRawGrounded = false;
            _activeAerialKickNearGround = false;
            _activeAerialKickLastGroundContactTime = float.NegativeInfinity;
            _nextAerialKickDiagnosticsSampleTime = float.NegativeInfinity;
            _activeAerialKickForwardDirection = Vector3.zero;
        }

        // 킥 종료 → spring 점진 복원 시작
    }

    private void BeginAerialKickSpringRestore(string reason)
    {
        if (!_isAerialKickMomentumActive || _aerialKickSpringRestoreTimer > 0f)
            return;

        // 공중에서 지연된 미스 패널티가 있으면 착지 시점에 적용한다.
        // ApplyAerialKickMissPenalty → TriggerStun이 _isAerialKickMomentumActive를
        // false로 설정하므로, 이후 spring restore는 불필요해진다.
        if (_aerialKickMissPenaltyPending)
        {
            _aerialKickMissPenaltyPending = false;
            LogAerialKickDiagnostic("DeferredMissPenalty", $"reason={reason}");
            ApplyAerialKickMissPenalty();
            return;
        }

        _aerialKickSpringRestoreStartLerp = _aerialKickCurrentSpringLerp;
        _activeAerialKickBallisticFallActive = false;
        _activeAerialKickTargetPlanarSpeed = 0f;
        _activeAerialKickFlightForceReleaseTime = float.NegativeInfinity;
        _aerialKickSpringRestoreTimer = AerialKickSpringRestoreDuration;
        LogAerialKickDiagnostic("BeginSpringRestore", reason);
    }

    private void EnterAerialKickBallisticFall(string reason)
    {
        if (!_isAerialKickMomentumActive || _activeAerialKickBallisticFallActive)
            return;

        _activeAerialKickBallisticFallActive = true;
        _activeAerialKickTargetPlanarSpeed = 0f;
        _activeAerialKickFlightForceReleaseTime = float.NegativeInfinity;
        SetAerialKickSpringLerp(AerialKickBallisticFallSpringLerp);
        LogAerialKickDiagnostic("BallisticFall", reason);
    }

    private static bool ShouldEnterAerialKickBallisticFall(
        bool hasLeftGround,
        bool isGrounded,
        float verticalVelocity,
        bool nearGround,
        bool hasRecentGroundContact)
    {
        if (!hasLeftGround || isGrounded)
            return false;

        return verticalVelocity <= AerialKickRisingVerticalVelocityThreshold ||
               nearGround ||
               hasRecentGroundContact;
    }

    private bool AreFeetClearForAerialKickStart()
    {
        if (_groundProbe == null)
            return !_isGrounded;

        var leftFoot = ResolveAerialKickGroundProbeFootTransform(true);
        var rightFoot = ResolveAerialKickGroundProbeFootTransform(false);
        if (leftFoot == null && rightFoot == null)
            return !_isGrounded;

        return !IsAerialKickFootGrounded(leftFoot, AerialKickStartFootProbeRadius, AerialKickStartFootProbeDistance) &&
               !IsAerialKickFootGrounded(rightFoot, AerialKickStartFootProbeRadius, AerialKickStartFootProbeDistance);
    }

    private bool HasAerialKickFootLandingSignal()
    {
        if (_groundProbe == null)
            return _isGrounded;

        var leftFoot = ResolveAerialKickGroundProbeFootTransform(true);
        var rightFoot = ResolveAerialKickGroundProbeFootTransform(false);
        if (leftFoot == null && rightFoot == null)
            return _isGrounded;

        return IsAerialKickFootGrounded(leftFoot, AerialKickLandingFootProbeRadius, AerialKickLandingFootProbeDistance) ||
               IsAerialKickFootGrounded(rightFoot, AerialKickLandingFootProbeRadius, AerialKickLandingFootProbeDistance);
    }

    private bool IsAerialKickFootGrounded(bool isLeft, float probeRadius, float probeDistance)
    {
        return IsAerialKickFootGrounded(ResolveAerialKickGroundProbeFootTransform(isLeft), probeRadius, probeDistance);
    }

    private bool IsAerialKickFootGrounded(Transform footTransform, float probeRadius, float probeDistance)
    {
        if (footTransform == null)
            return false;

        var probeOrigin = footTransform.position + Vector3.up * (probeRadius + AerialKickFootProbeOriginLift);
        return _groundProbe.IsGrounded(
            probeOrigin,
            transform,
            probeRadius,
            probeDistance + probeRadius + AerialKickFootProbeOriginLift);
    }

    private bool IsAerialKickGroundContactNearFoot(Vector3 contactPoint)
    {
        var hasFootReference = false;
        var maxHorizontalDistanceSqr = AerialKickGroundContactFootHorizontalSlack * AerialKickGroundContactFootHorizontalSlack;
        for (var i = 0; i < 2; i++)
        {
            var footTransform = ResolveAerialKickGroundProbeFootTransform(i == 0);
            if (footTransform == null)
                continue;

            hasFootReference = true;
            var horizontalOffset = contactPoint - footTransform.position;
            horizontalOffset.y = 0f;
            if (horizontalOffset.sqrMagnitude > maxHorizontalDistanceSqr)
                continue;

            if (contactPoint.y <= footTransform.position.y + AerialKickGroundContactFootHeightSlack)
                return true;
        }

        if (hasFootReference)
            return false;

        var heightCutoff = (rigidbody3D != null ? rigidbody3D.worldCenterOfMass.y : transform.position.y) + AerialKickGroundContactMaxHeightOffset;
        return contactPoint.y <= heightCutoff;
    }

    private AerialKickPresentationState ResolveLocalAerialKickPresentationState()
    {
        if (!_isAerialKickMomentumActive)
            return AerialKickPresentationState.None;

        if (_aerialKickSpringRestoreTimer > 0f)
            return AerialKickPresentationState.Restoring;

        if (_activeAerialKickBallisticFallActive)
            return AerialKickPresentationState.Fall;

        if (_activeAerialKickWindowEndTick < 0 &&
            ShouldEnterAerialKickBallisticFall(
                _activeAerialKickHasLeftGround,
                _isGrounded,
                rigidbody3D != null ? rigidbody3D.velocity.y : 0f,
                _activeAerialKickNearGround,
                HasRecentAerialKickGroundContact()))
        {
            return AerialKickPresentationState.Fall;
        }

        return AerialKickPresentationState.Launch;
    }

    private Vector3 ResolvePunchHitSamplePosition(bool isLeft)
    {
        var forward = ResolvePunchForward();
        var handTransform = ResolvePunchHandTransform(isLeft);
        if (handTransform != null)
            return handTransform.position + forward * PunchHitForwardOffset;

        return transform.position + Vector3.up * 0.6f + forward * PunchFallbackReach;
    }

    private Vector3 ResolveKickHitSamplePosition(bool isLeft)
    {
        var forward = ResolvePunchForward();
        var footTransform = ResolveKickFootTransform(isLeft);
        if (footTransform != null)
            return footTransform.position + forward * KickHitForwardOffset;

        return transform.position + Vector3.up * 0.28f + forward * KickFallbackReach;
    }

    private Vector3 ResolveHeadbuttHitSamplePosition()
    {
        var forward = ResolvePunchForward();
        var headTransform = ResolveHeadbuttHeadTransform();
        if (headTransform != null)
            return headTransform.position + forward * HeadbuttHitForwardOffset + Vector3.down * HeadbuttHitVerticalOffset;

        return transform.position + Vector3.up * HeadbuttFallbackHeight + forward * HeadbuttFallbackReach;
    }

    private Vector3 ResolveAerialKickHitSamplePosition()
    {
        var forward = ResolvePunchForward();
        var speed = rigidbody3D != null ? rigidbody3D.velocity.magnitude : _activeAerialKickAttackerSpeed;
        var normalizedSpeed = Mathf.Clamp01(speed / AerialKickSpeedForMaxBonus);
        var reach = Mathf.Lerp(AerialKickForwardReachMin, AerialKickForwardReachMax, normalizedSpeed);
        var kickFoot = ResolveKickFootTransform(false);
        if (kickFoot != null)
            return kickFoot.position + Vector3.up * 0.04f + forward * (reach * 0.55f);

        var root = _targetRoot != null ? _targetRoot : transform;
        var height = Mathf.Lerp(AerialKickHeightMin, AerialKickHeightMax, normalizedSpeed);
        return root.position + Vector3.up * height + forward * reach;
    }

    private Transform ResolvePunchHandTransform(bool isLeft)
    {
        if (_handGrabHandlers != null)
        {
            var side = isLeft ? HandGrabHandler.HandSide.Left : HandGrabHandler.HandSide.Right;
            for (var i = 0; i < _handGrabHandlers.Length; i++)
            {
                var handler = _handGrabHandlers[i];
                if (handler != null && handler.Side == side)
                    return handler.transform;
            }
        }

        if (animator != null && animator.isHuman)
        {
            var handBone = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            if (handBone != null)
                return handBone;

            return animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        }

        return _targetRoot;
    }

    private Transform ResolveKickFootTransform(bool isLeft)
    {
        if (animator != null && animator.isHuman)
        {
            var footBone = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
            if (footBone != null)
                return footBone;

            var lowerLegBone = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
            if (lowerLegBone != null)
                return lowerLegBone;
        }

        if (_puppetMaster != null && _puppetMaster.muscles != null)
        {
            var preferredName = isLeft ? "LeftFoot" : "RightFoot";
            var fallbackName = isLeft ? "LeftLowerLeg" : "RightLowerLeg";
            Transform fallback = null;
            for (var i = 0; i < _puppetMaster.muscles.Length; i++)
            {
                var muscleTransform = _puppetMaster.muscles[i].transform;
                if (muscleTransform == null)
                    continue;

                if (muscleTransform.name == preferredName)
                    return muscleTransform;

                if (fallback == null && muscleTransform.name == fallbackName)
                    fallback = muscleTransform;
            }

            if (fallback != null)
                return fallback;
        }

        return _targetRoot;
    }

    private Transform ResolveAerialKickGroundProbeFootTransform(bool isLeft)
    {
        if (_puppetMaster != null && _puppetMaster.muscles != null)
        {
            var preferredName = isLeft ? "LeftFoot" : "RightFoot";
            var fallbackName = isLeft ? "LeftLowerLeg" : "RightLowerLeg";
            Transform fallback = null;
            for (var i = 0; i < _puppetMaster.muscles.Length; i++)
            {
                var muscleTransform = _puppetMaster.muscles[i].transform;
                if (muscleTransform == null)
                    continue;

                if (muscleTransform.name == preferredName)
                    return muscleTransform;

                if (fallback == null && muscleTransform.name == fallbackName)
                    fallback = muscleTransform;
            }

            if (fallback != null)
                return fallback;
        }

        if (animator != null && animator.isHuman)
        {
            var footBone = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
            if (footBone != null)
                return footBone;

            var lowerLegBone = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
            if (lowerLegBone != null)
                return lowerLegBone;
        }

        return null;
    }

    private Transform ResolveHeadbuttHeadTransform()
    {
        if (_puppetMaster != null && _puppetMaster.muscles != null)
        {
            Transform fallback = null;
            for (var i = 0; i < _puppetMaster.muscles.Length; i++)
            {
                var muscleTransform = _puppetMaster.muscles[i].transform;
                if (muscleTransform == null)
                    continue;

                if (muscleTransform.name == "Head")
                    return muscleTransform;

                if (fallback == null &&
                    (muscleTransform.name == "Neck" ||
                     muscleTransform.name == "UpperChest" ||
                     muscleTransform.name == "Chest"))
                {
                    fallback = muscleTransform;
                }
            }

            if (fallback != null)
                return fallback;
        }

        if (animator != null && animator.isHuman)
        {
            var headBone = animator.GetBoneTransform(HumanBodyBones.Head);
            if (headBone != null)
                return headBone;

            var neckBone = animator.GetBoneTransform(HumanBodyBones.Neck);
            if (neckBone != null)
                return neckBone;

            return animator.GetBoneTransform(HumanBodyBones.UpperChest)
                ?? animator.GetBoneTransform(HumanBodyBones.Chest);
        }

        return _targetRoot;
    }

    private Vector3 ResolvePunchForward()
    {
        return _targetRoot != null ? _targetRoot.forward : transform.forward;
    }

    private bool TryResolvePunchVictim(Vector3 sweepStart, Vector3 sweepEnd, out NetworkPlayer victimPlayer, out Vector3 hitPoint)
    {
        return TryResolveCloseCombatVictim(
            sweepStart,
            sweepEnd,
            PunchHitRadius,
            _punchHitResults,
            true,
            out victimPlayer,
            out hitPoint);
    }

    private bool TryResolveKickVictim(Vector3 sweepStart, Vector3 sweepEnd, out NetworkPlayer victimPlayer, out Vector3 hitPoint)
    {
        return TryResolveCloseCombatVictim(
            sweepStart,
            sweepEnd,
            KickHitRadius,
            _kickHitResults,
            true,
            out victimPlayer,
            out hitPoint);
    }

    private bool TryResolveAerialKickVictim(Vector3 sweepStart, Vector3 sweepEnd, out NetworkPlayer victimPlayer, out Vector3 hitPoint)
    {
        return TryResolveCloseCombatVictim(
            sweepStart,
            sweepEnd,
            AerialKickHitRadius,
            _aerialKickHitResults,
            true,
            out victimPlayer,
            out hitPoint);
    }

    private bool TryResolveHeadbuttVictim(Vector3 sweepStart, Vector3 sweepEnd, out NetworkPlayer victimPlayer, out Vector3 hitPoint)
    {
        return TryResolveCloseCombatVictim(
            sweepStart,
            sweepEnd,
            HeadbuttHitRadius,
            _headbuttHitResults,
            true,
            out victimPlayer,
            out hitPoint);
    }

    private enum HeadbuttEnvironmentImpactType
    {
        None = 0,
        Wall = 1,
        Floor = 2
    }

    private bool TryResolveHeadbuttEnvironmentImpact(
        Vector3 sweepStart,
        Vector3 sweepEnd,
        out RaycastHit environmentHit,
        out HeadbuttEnvironmentImpactType impactType)
    {
        environmentHit = default;
        impactType = HeadbuttEnvironmentImpactType.None;

        var castOrigin = sweepStart;
        var castDirection = sweepEnd - sweepStart;
        var castDistance = castDirection.magnitude;
        if (castDistance > HeadbuttMinimumSweepDistance)
        {
            castDirection /= castDistance;
            castDistance += HeadbuttEnvironmentProbeDistance;
        }
        else
        {
            castOrigin = sweepEnd;
            castDirection = ResolvePunchForward();
            castDistance = HeadbuttEnvironmentProbeDistance;
        }

        if (castDirection.sqrMagnitude <= 0.0001f || castDistance <= 0.001f)
            return false;

        var hitCount = Physics.SphereCastNonAlloc(
            castOrigin,
            HeadbuttEnvironmentProbeRadius,
            castDirection.normalized,
            _headbuttEnvironmentHits,
            castDistance,
            ~0,
            QueryTriggerInteraction.Ignore);

        if (hitCount <= 0)
            return false;

        var bestDistance = float.MaxValue;
        for (var i = 0; i < hitCount; i++)
        {
            var hit = _headbuttEnvironmentHits[i];
            _headbuttEnvironmentHits[i] = default;

            var collider = hit.collider;
            if (collider == null)
                continue;

            if (collider.transform.root == transform)
                continue;

            if (collider.GetComponentInParent<NetworkPlayer>() != null)
                continue;

            var normal = hit.normal;
            if (normal.sqrMagnitude <= 0.0001f)
                continue;

            normal.Normalize();
            var impactAlignment = Vector3.Dot(castDirection.normalized, -normal);
            if (impactAlignment < HeadbuttEnvironmentImpactAlignmentThreshold)
                continue;

            var upDot = Vector3.Dot(normal, Vector3.up);
            var candidateImpactType = upDot >= HeadbuttFloorNormalThreshold
                ? HeadbuttEnvironmentImpactType.Floor
                : Mathf.Abs(upDot) <= HeadbuttWallNormalThreshold
                    ? HeadbuttEnvironmentImpactType.Wall
                    : HeadbuttEnvironmentImpactType.None;
            if (candidateImpactType == HeadbuttEnvironmentImpactType.None)
                continue;

            if (hit.distance >= bestDistance)
                continue;

            bestDistance = hit.distance;
            environmentHit = hit;
            impactType = candidateImpactType;
        }

        return impactType != HeadbuttEnvironmentImpactType.None;
    }

    private bool TryResolveCloseCombatVictim(
        Vector3 sweepStart,
        Vector3 sweepEnd,
        float radius,
        Collider[] hitResults,
        bool allowDownedTargets,
        out NetworkPlayer victimPlayer,
        out Vector3 hitPoint)
    {
        victimPlayer = null;
        hitPoint = sweepEnd;

        var hitCount = Physics.OverlapCapsuleNonAlloc(
            sweepStart,
            sweepEnd,
            radius,
            hitResults,
            ~0,
            QueryTriggerInteraction.Ignore);

        if (hitCount <= 0)
            return false;

        var bestDistance = float.MaxValue;
        for (var i = 0; i < hitCount; i++)
        {
            var hit = hitResults[i];
            hitResults[i] = null;
            if (hit == null)
                continue;

            var candidate = hit.GetComponentInParent<NetworkPlayer>();
            if (!CanReceiveCloseCombatVictim(candidate, allowDownedTargets))
                continue;

            var candidatePoint = hit.ClosestPoint(sweepEnd);
            var candidateDistance = (candidatePoint - sweepEnd).sqrMagnitude;
            if (candidateDistance >= bestDistance)
                continue;

            bestDistance = candidateDistance;
            victimPlayer = candidate;
            hitPoint = candidatePoint;
        }

        return victimPlayer != null;
    }

    private bool CanReceiveCloseCombatVictim(NetworkPlayer candidate, bool allowDownedTargets)
    {
        if (candidate == null || candidate == this || candidate.GetIsDeadState())
            return false;

        if (candidate.IsActiveRagdoll)
            return true;

        if (!allowDownedTargets)
            return false;

        return IsDownedCloseCombatPhase(candidate.GetPhysicalPhase());
    }

    private static bool IsDownedCloseCombatPhase(PhysicalPhase phase)
    {
        return phase == PhysicalPhase.Stunned ||
               phase == PhysicalPhase.StunnedCollapse ||
               phase == PhysicalPhase.SettledStunned;
    }

    private static bool IsBeingCarriedWhileStunned(NetworkPlayer victim)
    {
        return victim != null && (victim._beingGrabbedRefCount > 0 || victim.IsDualGrabbingStunnedPlayer);
    }

    private static bool ShouldSuppressDownedRepeatHitLaunch(NetworkPlayer victim, bool wasAlreadyStunnedBeforeHit, PhysicalPhase phaseAfterHit)
    {
        return victim != null &&
               wasAlreadyStunnedBeforeHit &&
               !victim._isActiveRagdoll &&
               IsDownedCloseCombatPhase(phaseAfterHit) &&
               !IsBeingCarriedWhileStunned(victim);
    }

    private void ApplyPunchHit(NetworkPlayer victimPlayer, Vector3 hitPoint)
    {
        if (victimPlayer == null)
            return;

        var suppressRepeatReaction = victimPlayer.ShouldSuppressRepeatedHitReaction();
        var wasAlreadyStunnedBeforeHit = !victimPlayer.IsActiveRagdoll;
        var forward = ResolvePunchForward();
        float lateralRatio;
        float heightRatio;
        var knockbackDir = BuildPunchKnockbackDirection(victimPlayer, forward, hitPoint, out lateralRatio, out heightRatio);
        var speedBonus = 1f + Mathf.Clamp01(_activePunchAttackerSpeed / 8f) * 0.5f;
        var finalKnockback = _activePunchKnockbackForce * speedBonus;

        victimPlayer.ApplyCombinedDamage(
            _activePunchHealthDamage,
            _activePunchStunDamage,
            "Punch",
            _activePunchAttackerSpeed,
            _activePunchKnockbackForce,
            1.0f,
            deferStunEntryDamping: true,
            instigator: this,
            hitCountToStun: _activePunchHitCountToStun,
            groggyVulnerabilityMultiplier: _activePunchGroggyVulnerabilityMultiplier,
            downedHitPolicy: DownedHitPolicy.RecoveryPenalty);
        var phaseAfterHit = victimPlayer.GetPhysicalPhase();
        var isStunnedAfterHit = !victimPlayer._isActiveRagdoll;
        var enteredStunThisHit = !wasAlreadyStunnedBeforeHit && isStunnedAfterHit;
        var collapseVictim = isStunnedAfterHit && phaseAfterHit == PhysicalPhase.StunnedCollapse;
        var repeatDownedHit = ShouldSuppressDownedRepeatHitLaunch(victimPlayer, wasAlreadyStunnedBeforeHit, phaseAfterHit);
        var groundedStunEntry = enteredStunThisHit && victimPlayer._isGrounded && victimPlayer._beingGrabbedRefCount <= 0;
        var reactionKnockback = isStunnedAfterHit ? finalKnockback * StunLaunchKnockbackScale : finalKnockback;
        var linearKnockback = repeatDownedHit
            ? finalKnockback * DownedRepeatHitRootKnockbackScale
            : groundedStunEntry
                ? finalKnockback * GroundedStunEntryLinearKnockbackScale
                : enteredStunThisHit
                    ? finalKnockback * StunEntryLinearKnockbackScale
                    : reactionKnockback;
        var toppleKnockback = groundedStunEntry
            ? finalKnockback * GroundedStunEntryToppleKnockbackScale
            : enteredStunThisHit
                ? finalKnockback * StunEntryToppleKnockbackScale
            : reactionKnockback;
        var responseKnockback = repeatDownedHit ? reactionKnockback : toppleKnockback;
        victimPlayer.ApplyCloseCombatHitReaction(hitPoint, responseKnockback, suppressRepeatReaction);

        var victimRb = victimPlayer.rigidbody3D;
        var victimVelocityBeforeForce = victimRb != null && !victimRb.isKinematic
            ? victimRb.velocity
            : Vector3.zero;
        if (victimRb != null && !victimRb.isKinematic)
        {
            victimRb.AddForce(knockbackDir * linearKnockback, ForceMode.Impulse);
            if (!repeatDownedHit)
            {
                var rotationScale = collapseVictim
                    ? Mathf.Lerp(0.035f, 0.06f, heightRatio)
                    : isStunnedAfterHit
                        ? Mathf.Lerp(0.09f, 0.12f, heightRatio)
                        : Mathf.Lerp(0.24f, 0.32f, heightRatio);
                victimRb.AddForceAtPosition(
                    knockbackDir * toppleKnockback * rotationScale,
                    hitPoint,
                    ForceMode.Impulse);

            // 측면 타격일수록 yaw 토크 → 몸이 비틀려 돌아감
                if (lateralRatio > 0.25f)
                {
                    var yawSign = Mathf.Sign(Vector3.Cross(victimPlayer.transform.forward, knockbackDir).y);
                    var yawTorqueScale = collapseVictim
                        ? Mathf.Lerp(0.015f, 0.035f, lateralRatio)
                        : Mathf.Lerp(0.08f, 0.16f, lateralRatio);
                    var yawTorque = Vector3.up * yawSign * toppleKnockback * yawTorqueScale;
                    victimRb.AddTorque(yawTorque, ForceMode.Impulse);
                }
            }
        }
        victimPlayer.TraceStunForceEvent(
            "PunchRoot",
            victimRb,
            knockbackDir * linearKnockback,
            ForceMode.Impulse,
            victimVelocityBeforeForce,
            victimRb != null && !victimRb.isKinematic ? victimRb.velocity : victimVelocityBeforeForce,
            linearKnockback > 0.0001f,
            $"enteredStunThisHit={enteredStunThisHit} repeatDownedHit={repeatDownedHit} collapseVictim={collapseVictim} linear={linearKnockback:F2} topple={toppleKnockback:F2}");

        ApplyPunchFollowThrough(knockbackDir, finalKnockback);
        ApplyMuscleImpulseOnHit(victimPlayer, hitPoint, knockbackDir, responseKnockback, enteredStunThisHit, repeatDownedHit);
        if (enteredStunThisHit)
            victimPlayer.DampenStunEntryVelocities(groundedStunEntry);

        TriggerAttackCameraKick(forward, finalKnockback);
        victimPlayer.TriggerVictimCameraKick(knockbackDir, responseKnockback);
        // 히트스탑 제거 — 파티애니멀즈 스타일은 래그돌 과장 반응이 타격감 핵심, 속도 동결은 물리 흐름을 끊음
        // ApplyLocalHitStop(victimPlayer);
        SpawnHitImpactVFX(hitPoint, knockbackDir, responseKnockback);
    }

    private void ApplyKickHit(NetworkPlayer victimPlayer, Vector3 hitPoint)
    {
        if (victimPlayer == null)
            return;

        var suppressRepeatReaction = victimPlayer.ShouldSuppressRepeatedHitReaction();
        var wasAlreadyStunnedBeforeHit = !victimPlayer.IsActiveRagdoll;
        var forward = ResolvePunchForward();
        float lateralRatio;
        float heightRatio;
        var knockbackDir = BuildPunchKnockbackDirection(victimPlayer, forward, hitPoint, out lateralRatio, out heightRatio);
        var speedBonus = 1f + Mathf.Clamp01(_activeKickAttackerSpeed / 8f) * 0.55f;
        var finalKnockback = _activeKickKnockbackForce * speedBonus;

        victimPlayer.ApplyCombinedDamage(
            _activeKickHealthDamage,
            _activeKickStunDamage,
            "Kick",
            _activeKickAttackerSpeed,
            _activeKickKnockbackForce,
            1.0f,
            deferStunEntryDamping: true,
            instigator: this,
            hitCountToStun: _activeKickHitCountToStun,
            groggyVulnerabilityMultiplier: _activeKickGroggyVulnerabilityMultiplier,
            downedHitPolicy: DownedHitPolicy.RecoveryPenalty);
        var phaseAfterHit = victimPlayer.GetPhysicalPhase();
        var isStunnedAfterHit = !victimPlayer._isActiveRagdoll;
        var enteredStunThisHit = !wasAlreadyStunnedBeforeHit && isStunnedAfterHit;
        var collapseVictim = isStunnedAfterHit && phaseAfterHit == PhysicalPhase.StunnedCollapse;
        var repeatDownedHit = ShouldSuppressDownedRepeatHitLaunch(victimPlayer, wasAlreadyStunnedBeforeHit, phaseAfterHit);
        var groundedStunEntry = enteredStunThisHit && victimPlayer._isGrounded && victimPlayer._beingGrabbedRefCount <= 0;
        var reactionKnockback = isStunnedAfterHit ? finalKnockback * StunLaunchKnockbackScale : finalKnockback;
        var linearKnockback = repeatDownedHit
            ? finalKnockback * DownedRepeatHitRootKnockbackScale
            : groundedStunEntry
                ? finalKnockback * GroundedStunEntryLinearKnockbackScale
                : enteredStunThisHit
                    ? finalKnockback * StunEntryLinearKnockbackScale
                    : reactionKnockback;
        var toppleKnockback = groundedStunEntry
            ? finalKnockback * GroundedStunEntryToppleKnockbackScale
            : enteredStunThisHit
                ? finalKnockback * StunEntryToppleKnockbackScale
            : reactionKnockback;
        var responseKnockback = repeatDownedHit ? reactionKnockback : toppleKnockback;
        victimPlayer.ApplyCloseCombatHitReaction(hitPoint, responseKnockback, suppressRepeatReaction);

        var victimRb = victimPlayer.rigidbody3D;
        var victimVelocityBeforeForce = victimRb != null && !victimRb.isKinematic
            ? victimRb.velocity
            : Vector3.zero;
        if (victimRb != null && !victimRb.isKinematic)
        {
            victimRb.AddForce(knockbackDir * linearKnockback, ForceMode.Impulse);
            if (!repeatDownedHit)
            {
                var rotationScale = collapseVictim
                    ? Mathf.Lerp(0.04f, 0.07f, heightRatio)
                    : isStunnedAfterHit
                        ? Mathf.Lerp(0.10f, 0.14f, heightRatio)
                        : Mathf.Lerp(0.26f, 0.34f, heightRatio);
                victimRb.AddForceAtPosition(
                    knockbackDir * toppleKnockback * rotationScale,
                    hitPoint,
                    ForceMode.Impulse);

                if (lateralRatio > 0.20f)
                {
                    var yawSign = Mathf.Sign(Vector3.Cross(victimPlayer.transform.forward, knockbackDir).y);
                    var yawTorqueScale = collapseVictim
                        ? Mathf.Lerp(0.02f, 0.045f, lateralRatio)
                        : Mathf.Lerp(0.10f, 0.18f, lateralRatio);
                    victimRb.AddTorque(Vector3.up * yawSign * toppleKnockback * yawTorqueScale, ForceMode.Impulse);
                }
            }
        }

        victimPlayer.TraceStunForceEvent(
            "KickRoot",
            victimRb,
            knockbackDir * linearKnockback,
            ForceMode.Impulse,
            victimVelocityBeforeForce,
            victimRb != null && !victimRb.isKinematic ? victimRb.velocity : victimVelocityBeforeForce,
            linearKnockback > 0.0001f,
            $"enteredStunThisHit={enteredStunThisHit} repeatDownedHit={repeatDownedHit} collapseVictim={collapseVictim} linear={linearKnockback:F2} topple={toppleKnockback:F2}");

        ApplyPunchFollowThrough(knockbackDir, finalKnockback);
        ApplyMuscleImpulseOnHit(victimPlayer, hitPoint, knockbackDir, responseKnockback, enteredStunThisHit, repeatDownedHit);
        if (enteredStunThisHit)
            victimPlayer.DampenStunEntryVelocities(groundedStunEntry);

        TriggerAttackCameraKick(forward, finalKnockback);
        victimPlayer.TriggerVictimCameraKick(knockbackDir, responseKnockback);
        SpawnHitImpactVFX(hitPoint, knockbackDir, responseKnockback);
    }

    private void ApplyHeadbuttHit(NetworkPlayer victimPlayer, Vector3 hitPoint)
    {
        if (victimPlayer == null)
            return;

        var suppressRepeatReaction = victimPlayer.ShouldSuppressRepeatedHitReaction();
        var wasAlreadyStunnedBeforeHit = !victimPlayer.IsActiveRagdoll;
        var forward = ResolvePunchForward();
        float lateralRatio;
        float heightRatio;
        var knockbackDir = BuildPunchKnockbackDirection(victimPlayer, forward, hitPoint, out lateralRatio, out heightRatio);
        var speedBonus = 1f + Mathf.Clamp01(_activeHeadbuttAttackerSpeed / 6f) * 0.35f;
        var finalKnockback = _activeHeadbuttKnockbackForce * speedBonus;

        victimPlayer.ApplyCombinedDamage(
            _activeHeadbuttHealthDamage,
            _activeHeadbuttStunDamage,
            "Headbutt",
            _activeHeadbuttAttackerSpeed,
            _activeHeadbuttKnockbackForce,
            1.1f,
            deferStunEntryDamping: true,
            instigator: this,
            hitCountToStun: _activeHeadbuttHitCountToStun,
            groggyVulnerabilityMultiplier: _activeHeadbuttGroggyVulnerabilityMultiplier,
            downedHitPolicy: DownedHitPolicy.RecoveryPenalty);
        var phaseAfterHit = victimPlayer.GetPhysicalPhase();
        var isStunnedAfterHit = !victimPlayer._isActiveRagdoll;
        var enteredStunThisHit = !wasAlreadyStunnedBeforeHit && isStunnedAfterHit;
        var collapseVictim = isStunnedAfterHit && phaseAfterHit == PhysicalPhase.StunnedCollapse;
        var repeatDownedHit = ShouldSuppressDownedRepeatHitLaunch(victimPlayer, wasAlreadyStunnedBeforeHit, phaseAfterHit);
        var groundedStunEntry = enteredStunThisHit && victimPlayer._isGrounded && victimPlayer._beingGrabbedRefCount <= 0;
        var reactionKnockback = isStunnedAfterHit ? finalKnockback * StunLaunchKnockbackScale : finalKnockback;
        var linearKnockback = repeatDownedHit
            ? finalKnockback * DownedRepeatHitRootKnockbackScale
            : groundedStunEntry
                ? finalKnockback * GroundedStunEntryLinearKnockbackScale * 0.7f
                : enteredStunThisHit
                    ? finalKnockback * 0.78f
                    : reactionKnockback * 0.85f;
        var toppleKnockback = repeatDownedHit
            ? reactionKnockback
            : groundedStunEntry
                ? finalKnockback * GroundedStunEntryToppleKnockbackScale * 0.78f
                : enteredStunThisHit
                    ? finalKnockback * StunEntryToppleKnockbackScale * 0.92f
                    : reactionKnockback;
        var responseKnockback = repeatDownedHit ? reactionKnockback : toppleKnockback;
        victimPlayer.ApplyCloseCombatHitReaction(hitPoint, responseKnockback, suppressRepeatReaction);

        var victimRb = victimPlayer.rigidbody3D;
        var victimVelocityBeforeForce = victimRb != null && !victimRb.isKinematic
            ? victimRb.velocity
            : Vector3.zero;
        if (victimRb != null && !victimRb.isKinematic)
        {
            victimRb.AddForce(knockbackDir * linearKnockback, ForceMode.Impulse);
            if (!repeatDownedHit)
            {
                var rotationScale = collapseVictim
                    ? Mathf.Lerp(0.03f, 0.05f, heightRatio)
                    : isStunnedAfterHit
                        ? Mathf.Lerp(0.08f, 0.11f, heightRatio)
                        : Mathf.Lerp(0.18f, 0.25f, heightRatio);
                victimRb.AddForceAtPosition(
                    knockbackDir * toppleKnockback * rotationScale,
                    hitPoint,
                    ForceMode.Impulse);

                var yawSign = Mathf.Abs(lateralRatio) > 0.01f
                    ? Mathf.Sign(Vector3.Dot(victimPlayer.transform.right, knockbackDir))
                    : Mathf.Sign(Vector3.Cross(victimPlayer.transform.forward, knockbackDir).y);
                if (Mathf.Abs(yawSign) > 0.0001f)
                {
                    var yawTorqueScale = repeatDownedHit
                        ? Mathf.Lerp(0.04f, 0.07f, lateralRatio)
                        : collapseVictim
                            ? Mathf.Lerp(0.05f, 0.09f, lateralRatio)
                            : enteredStunThisHit
                                ? Mathf.Lerp(0.08f, 0.14f, lateralRatio)
                                : Mathf.Lerp(0.09f, 0.16f, lateralRatio);
                    victimRb.AddTorque(Vector3.up * yawSign * toppleKnockback * yawTorqueScale, ForceMode.Impulse);
                }
            }
        }

        victimPlayer.TraceStunForceEvent(
            "HeadbuttRoot",
            victimRb,
            knockbackDir * linearKnockback,
            ForceMode.Impulse,
            victimVelocityBeforeForce,
            victimRb != null && !victimRb.isKinematic ? victimRb.velocity : victimVelocityBeforeForce,
            linearKnockback > 0.0001f,
            $"enteredStunThisHit={enteredStunThisHit} repeatDownedHit={repeatDownedHit} collapseVictim={collapseVictim} linear={linearKnockback:F2} topple={toppleKnockback:F2}");

        ApplyPunchFollowThrough(knockbackDir, finalKnockback * 0.75f);
        ApplyMuscleImpulseOnHit(victimPlayer, hitPoint, knockbackDir, responseKnockback, enteredStunThisHit, repeatDownedHit);
        if (enteredStunThisHit)
            victimPlayer.DampenStunEntryVelocities(groundedStunEntry);

        TriggerAttackCameraKick(forward, finalKnockback * 0.85f);
        victimPlayer.TriggerVictimCameraKick(knockbackDir, responseKnockback);
        SpawnHitImpactVFX(hitPoint, knockbackDir, responseKnockback);
    }

    private void ApplyHeadbuttEnvironmentImpact(RaycastHit environmentHit, HeadbuttEnvironmentImpactType impactType)
    {
        var forward = ResolvePunchForward();
        var bounceDirection = Vector3.ProjectOnPlane(environmentHit.normal, Vector3.up);
        if (bounceDirection.sqrMagnitude <= 0.0001f)
            bounceDirection = -Vector3.ProjectOnPlane(forward, Vector3.up);
        if (bounceDirection.sqrMagnitude <= 0.0001f)
            bounceDirection = -transform.forward;
        bounceDirection.Normalize();

        var verticalBounce = impactType == HeadbuttEnvironmentImpactType.Floor ? 0.14f : 0.08f;
        var bounceImpulse = (bounceDirection + Vector3.up * verticalBounce) * HeadbuttSelfBounceImpulse;

        ApplyCloseCombatHitReaction(environmentHit.point, _activeHeadbuttKnockbackForce, false);
        TriggerAttackCameraKick(-bounceDirection, _activeHeadbuttKnockbackForce * 0.65f);

        var selfStunDuration = impactType == HeadbuttEnvironmentImpactType.Floor
            ? CombatSettings.Instance != null ? CombatSettings.Instance.headbuttSelfStunFloor : HeadbuttFallbackFloorSelfStun
            : CombatSettings.Instance != null ? CombatSettings.Instance.headbuttSelfStunWall : HeadbuttFallbackWallSelfStun;
        if (selfStunDuration <= 0.01f)
            return;

        var selfStunChance = Mathf.Clamp01(_activeHeadbuttSelfStunChance);
        if (selfStunChance <= 0f)
        {
            if (rigidbody3D != null && !rigidbody3D.isKinematic)
                rigidbody3D.AddForce(bounceImpulse, ForceMode.Impulse);
            return;
        }

        if (selfStunChance < 1f && UnityEngine.Random.value > selfStunChance)
        {
            if (rigidbody3D != null && !rigidbody3D.isKinematic)
                rigidbody3D.AddForce(bounceImpulse, ForceMode.Impulse);
            return;
        }

        var activeHeadbuttPresentation = GetComponent<ProceduralHeadbutt>();
        if (activeHeadbuttPresentation != null)
            activeHeadbuttPresentation.CancelHeadbutt();

        var groundedStunEntry = impactType == HeadbuttEnvironmentImpactType.Floor || _isGrounded;
        TriggerStun(selfStunDuration, applyEntryDamping: false);

        var velocityBeforeBounce = rigidbody3D != null && !rigidbody3D.isKinematic
            ? rigidbody3D.velocity
            : Vector3.zero;
        if (rigidbody3D != null && !rigidbody3D.isKinematic)
            rigidbody3D.AddForce(bounceImpulse, ForceMode.Impulse);

        TraceStunForceEvent(
            "HeadbuttSelfRoot",
            rigidbody3D,
            bounceImpulse,
            ForceMode.Impulse,
            velocityBeforeBounce,
            rigidbody3D != null && !rigidbody3D.isKinematic ? rigidbody3D.velocity : velocityBeforeBounce,
            rigidbody3D != null && !rigidbody3D.isKinematic,
            $"impactType={impactType} groundedEntry={(groundedStunEntry ? 1 : 0)}");

        ApplyMuscleImpulseOnHit(this, environmentHit.point, bounceDirection, _activeHeadbuttKnockbackForce, enteredStunThisHit: true, repeatDownedHit: false);
        DampenStunEntryVelocities(groundedStunEntry);
        SyncRootToPhysicsBody(forceImmediate: true);
    }

    private void ApplyAerialKickHit(NetworkPlayer victimPlayer, Vector3 hitPoint)
    {
        if (victimPlayer == null)
            return;

        var suppressRepeatReaction = victimPlayer.ShouldSuppressRepeatedHitReaction();
        var wasAlreadyStunnedBeforeHit = !victimPlayer.IsActiveRagdoll;
        var forward = ResolvePunchForward();
        ResolveAerialKickImpactStats(victimPlayer, out var finalHealthDamage, out var finalStunDamage, out var finalKnockback, out var attackerSpeed);

        float lateralRatio;
        float heightRatio;
        var knockbackDir = BuildPunchKnockbackDirection(victimPlayer, forward, hitPoint, out lateralRatio, out heightRatio);

        victimPlayer.ApplyCombinedDamage(
            finalHealthDamage,
            finalStunDamage,
            "AerialKick",
            attackerSpeed,
            finalKnockback,
            1.0f,
            deferStunEntryDamping: true,
            instigator: this,
            hitCountToStun: _activeAerialKickHitCountToStun,
            groggyVulnerabilityMultiplier: _activeAerialKickGroggyVulnerabilityMultiplier,
            downedHitPolicy: DownedHitPolicy.RecoveryPenalty);
        var phaseAfterHit = victimPlayer.GetPhysicalPhase();
        var isStunnedAfterHit = !victimPlayer._isActiveRagdoll;
        var enteredStunThisHit = !wasAlreadyStunnedBeforeHit && isStunnedAfterHit;
        var collapseVictim = isStunnedAfterHit && phaseAfterHit == PhysicalPhase.StunnedCollapse;
        var repeatDownedHit = ShouldSuppressDownedRepeatHitLaunch(victimPlayer, wasAlreadyStunnedBeforeHit, phaseAfterHit);
        var groundedStunEntry = enteredStunThisHit && victimPlayer._isGrounded && victimPlayer._beingGrabbedRefCount <= 0;
        var reactionKnockback = isStunnedAfterHit ? finalKnockback * StunLaunchKnockbackScale : finalKnockback;
        var linearKnockback = repeatDownedHit
            ? finalKnockback * DownedRepeatHitRootKnockbackScale
            : groundedStunEntry
                ? finalKnockback * GroundedStunEntryLinearKnockbackScale
                : enteredStunThisHit
                    ? finalKnockback * StunEntryLinearKnockbackScale
                    : reactionKnockback;
        var toppleKnockback = groundedStunEntry
            ? finalKnockback * GroundedStunEntryToppleKnockbackScale
            : enteredStunThisHit
                ? finalKnockback * StunEntryToppleKnockbackScale
            : reactionKnockback;
        var responseKnockback = repeatDownedHit ? reactionKnockback : toppleKnockback;
        victimPlayer.ApplyCloseCombatHitReaction(hitPoint, responseKnockback, suppressRepeatReaction);

        var victimRb = victimPlayer.rigidbody3D;
        var victimVelocityBeforeForce = victimRb != null && !victimRb.isKinematic
            ? victimRb.velocity
            : Vector3.zero;
        if (victimRb != null && !victimRb.isKinematic)
        {
            victimRb.AddForce(knockbackDir * linearKnockback, ForceMode.Impulse);
            if (!repeatDownedHit)
            {
                var rotationScale = collapseVictim
                    ? Mathf.Lerp(0.045f, 0.075f, heightRatio)
                    : isStunnedAfterHit
                        ? Mathf.Lerp(0.11f, 0.15f, heightRatio)
                        : Mathf.Lerp(0.30f, 0.38f, heightRatio);
                victimRb.AddForceAtPosition(
                    knockbackDir * toppleKnockback * rotationScale,
                    hitPoint,
                    ForceMode.Impulse);

                if (lateralRatio > 0.18f)
                {
                    var yawSign = Mathf.Sign(Vector3.Cross(victimPlayer.transform.forward, knockbackDir).y);
                    var yawTorqueScale = collapseVictim
                        ? Mathf.Lerp(0.025f, 0.05f, lateralRatio)
                        : Mathf.Lerp(0.12f, 0.22f, lateralRatio);
                    victimRb.AddTorque(Vector3.up * yawSign * toppleKnockback * yawTorqueScale, ForceMode.Impulse);
                }
            }
        }

        victimPlayer.TraceStunForceEvent(
            "AerialKickRoot",
            victimRb,
            knockbackDir * linearKnockback,
            ForceMode.Impulse,
            victimVelocityBeforeForce,
            victimRb != null && !victimRb.isKinematic ? victimRb.velocity : victimVelocityBeforeForce,
            linearKnockback > 0.0001f,
            $"airborneVictim={!victimPlayer._isGrounded} enteredStunThisHit={enteredStunThisHit} repeatDownedHit={repeatDownedHit} collapseVictim={collapseVictim} linear={linearKnockback:F2} topple={toppleKnockback:F2}");

        ApplyPunchFollowThrough(knockbackDir, finalKnockback * 1.12f);
        ApplyMuscleImpulseOnHit(victimPlayer, hitPoint, knockbackDir, responseKnockback, enteredStunThisHit, repeatDownedHit);
        if (enteredStunThisHit)
            victimPlayer.DampenStunEntryVelocities(groundedStunEntry);

        TriggerAttackCameraKick(forward, finalKnockback * 1.08f);
        victimPlayer.TriggerVictimCameraKick(knockbackDir, responseKnockback);
        SpawnHitImpactVFX(hitPoint, knockbackDir, responseKnockback);
    }

    private void ResolveAerialKickImpactStats(
        NetworkPlayer victimPlayer,
        out float healthDamage,
        out float stunDamage,
        out float knockbackForce,
        out float attackerSpeed)
    {
        var forward = ResolvePunchForward();
        var velocity = rigidbody3D != null ? rigidbody3D.velocity : Vector3.zero;
        var totalSpeed = Mathf.Max(_activeAerialKickStartSpeed, velocity.magnitude);
        var forwardSpeed = Mathf.Max(0f, Vector3.Dot(velocity, forward));
        attackerSpeed = Mathf.Max(totalSpeed * 0.7f, forwardSpeed);

        var normalizedMomentum = Mathf.Clamp01(attackerSpeed / AerialKickSpeedForMaxBonus);
        var damageScale = 1f + normalizedMomentum * _activeAerialKickVelocityDamageMultiplier;
        var stunScale = 1f + normalizedMomentum * (_activeAerialKickVelocityDamageMultiplier + 0.45f);
        var knockbackScale = 1f + normalizedMomentum * 0.9f;

        healthDamage = _activeAerialKickHealthDamage * damageScale;
        stunDamage = _activeAerialKickStunDamage * stunScale;
        knockbackForce = _activeAerialKickKnockbackForce * knockbackScale;

        if (victimPlayer != null && !victimPlayer._isGrounded)
        {
            stunDamage *= _activeAerialKickAirborneVulnerabilityMultiplier;
            healthDamage *= Mathf.Lerp(1f, _activeAerialKickAirborneVulnerabilityMultiplier, 0.35f);
            knockbackForce *= Mathf.Lerp(1f, _activeAerialKickAirborneVulnerabilityMultiplier, 0.28f);
        }
    }

    private void ApplyAerialKickMissPenalty()
    {
        if (_activeAerialKickHasHit || !_isActiveRagdoll || GetIsDeadState())
            return;

        if (_activeAerialKickSelfStunDuration <= 0.01f || !ShouldApplyAerialKickSelfStun())
        {
            LogAerialKickDiagnostic(
                "MissPenaltySkipped",
                $"selfStunDuration={_activeAerialKickSelfStunDuration:F2} chance={_activeAerialKickSelfStunChance:F2}");
            ArmHitInstabilityBoost(1.1f);
            _hitRecoilTimer = Mathf.Max(_hitRecoilTimer, HIT_RECOIL_DURATION * 0.75f);
            return;
        }

        LogAerialKickDiagnostic(
            "MissPenaltyApplied",
            $"selfStunDuration={_activeAerialKickSelfStunDuration:F2} chance={_activeAerialKickSelfStunChance:F2}");
        TraceRootGapAnomaly(
            "AerialKick.PreMissPenaltyStun",
            $"selfStunDuration={_activeAerialKickSelfStunDuration:F2} chance={_activeAerialKickSelfStunChance:F2} grounded={(_isGrounded ? 1 : 0)} rawGrounded={(_activeAerialKickRawGrounded ? 1 : 0)} nearGround={(_activeAerialKickNearGround ? 1 : 0)} leftGround={(_activeAerialKickHasLeftGround ? 1 : 0)}",
            force: true);
        var hasRecentGroundContact = Time.time - _activeAerialKickLastGroundContactTime <= AerialKickGroundContactMemory;
        var groundedPlop = ShouldUseGroundedAerialKickMissPlop(
            _isGrounded,
            _activeAerialKickRawGrounded,
            _activeAerialKickNearGround,
            hasRecentGroundContact);
        var missPenaltyDuration = ResolveAerialKickMissPenaltyDuration(_activeAerialKickSelfStunDuration, groundedPlop);
        LogAerialKickDiagnostic(
            "MissPenaltyResolved",
            $"resolvedDuration={missPenaltyDuration:F2} groundedPlop={(groundedPlop ? 1 : 0)} nearGround={(_activeAerialKickNearGround ? 1 : 0)} recentGround={(hasRecentGroundContact ? 1 : 0)}");
        TriggerStun(
            missPenaltyDuration,
            applyEntryDamping: false,
            suppressImplicitPlainStunDamping: true);
        DampenStunEntryVelocities(groundedPlopEntry: groundedPlop);
    }

    private bool ShouldApplyAerialKickSelfStun()
    {
        if (_activeAerialKickSelfStunChance <= 0f)
            return false;

        if (_activeAerialKickSelfStunChance >= 1f)
            return true;

        var seed = ResolveCurrentSimulationTick() * 12.9898f
            + transform.position.x * 78.233f
            + transform.position.z * 37.719f;
        var pseudoRandom = Mathf.Abs(Mathf.Sin(seed) * 43758.5453f);
        pseudoRandom -= Mathf.Floor(pseudoRandom);
        return pseudoRandom <= _activeAerialKickSelfStunChance;
    }

    private static bool ShouldUseGroundedAerialKickMissPlop(
        bool isGrounded,
        bool rawGrounded,
        bool nearGround,
        bool hasRecentGroundContact)
    {
        return isGrounded || rawGrounded || nearGround || hasRecentGroundContact;
    }

    private static float ResolveAerialKickMissPenaltyDuration(float configuredSelfStunDuration, bool groundedPlop)
    {
        var scaledDuration = Mathf.Max(0f, configuredSelfStunDuration) * (groundedPlop ? 0.65f : 0.50f);
        var minDuration = groundedPlop ? 0.22f : 0.16f;
        var maxDuration = groundedPlop ? 0.34f : 0.26f;
        return Mathf.Clamp(scaledDuration, minDuration, maxDuration);
    }

    private Vector3 BuildPunchKnockbackDirection(NetworkPlayer victimPlayer, Vector3 forward)
    {
        return BuildPunchKnockbackDirection(victimPlayer, forward, out _);
    }

    private Vector3 BuildPunchKnockbackDirection(NetworkPlayer victimPlayer, Vector3 forward, out float lateralRatio)
    {
        var planarForward = Vector3.ProjectOnPlane(forward, Vector3.up);
        if (planarForward.sqrMagnitude < 0.0001f)
            planarForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (planarForward.sqrMagnitude < 0.0001f)
            planarForward = Vector3.forward;

        var dirToVictim = Vector3.ProjectOnPlane(victimPlayer.transform.position - transform.position, Vector3.up);
        if (dirToVictim.sqrMagnitude < 0.0001f)
            dirToVictim = planarForward;

        // 피격자 정면 기준으로 측면 비율 계산 (0=정면/뒤, 1=완전 측면)
        var victimForward = Vector3.ProjectOnPlane(victimPlayer.transform.forward, Vector3.up);
        if (victimForward.sqrMagnitude < 0.0001f)
            victimForward = Vector3.forward;
        var dot = Vector3.Dot(victimForward.normalized, planarForward.normalized);
        lateralRatio = Mathf.Sqrt(1f - dot * dot); // sin(angle) — 측면일수록 1에 가까움

        var blendedPlanar = (dirToVictim.normalized * 0.55f + planarForward.normalized * 0.45f).normalized;

        // 공중이면 lift 증가, 측면이면 약간의 추가 lift
        var upwardBias = victimPlayer._isGrounded
            ? Mathf.Lerp(0.025f, 0.05f, lateralRatio)
            : 0.10f;
        var knockbackDir = (blendedPlanar + Vector3.up * upwardBias).normalized;
        knockbackDir.y = Mathf.Clamp(knockbackDir.y, -0.02f, 0.12f);
        return knockbackDir.normalized;
    }

    private Vector3 BuildPunchKnockbackDirection(
        NetworkPlayer victimPlayer,
        Vector3 forward,
        Vector3 hitPoint,
        out float lateralRatio,
        out float heightRatio)
    {
        var knockbackDir = BuildPunchKnockbackDirection(victimPlayer, forward, out lateralRatio);
        var localHitOffset = victimPlayer.ResolveImpactLocalOffset(hitPoint);
        lateralRatio = Mathf.Max(lateralRatio, Mathf.Clamp01(Mathf.Abs(localHitOffset.x) / 0.32f));
        heightRatio = Mathf.Clamp01((localHitOffset.y + 0.05f) / 0.70f);

        var victimRight = Vector3.ProjectOnPlane(victimPlayer.transform.right, Vector3.up);
        if (victimRight.sqrMagnitude < 0.0001f)
            victimRight = Vector3.Cross(Vector3.up, victimPlayer.transform.forward).normalized;

        var sideSign = Mathf.Abs(localHitOffset.x) > 0.01f
            ? Mathf.Sign(localHitOffset.x)
            : Mathf.Sign(Vector3.Cross(victimPlayer.transform.forward, knockbackDir).y);
        var sideBias = victimRight.normalized * sideSign * lateralRatio * 0.20f;
        knockbackDir = (knockbackDir + sideBias + Vector3.up * (heightRatio * 0.05f)).normalized;
        knockbackDir.y = Mathf.Clamp(knockbackDir.y, -0.02f, 0.12f);
        return knockbackDir.normalized;
    }

    private Vector3 ResolveImpactCenter()
    {
        if (rigidbody3D != null && !rigidbody3D.isKinematic)
            return rigidbody3D.worldCenterOfMass;

        if (_puppetMaster != null &&
            _puppetMaster.muscles != null &&
            _puppetMaster.muscles.Length > 0 &&
            _puppetMaster.muscles[0].joint != null)
        {
            return _puppetMaster.muscles[0].joint.transform.position;
        }

        return transform.position + Vector3.up * 0.9f;
    }

    private Vector3 ResolveImpactLocalOffset(Vector3 hitPoint)
    {
        var victimForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (victimForward.sqrMagnitude < 0.0001f)
            victimForward = Vector3.forward;

        var impactFrame = Quaternion.LookRotation(victimForward.normalized, Vector3.up);
        return Quaternion.Inverse(impactFrame) * (hitPoint - ResolveImpactCenter());
    }

    /// <summary>
    /// 회복 안정화 단계: 0.4초에 걸쳐 스프링을 점진적으로 복원하고
    /// 물리 뼈 각속도를 지속적으로 감쇠시켜 떨림을 방지.
    /// </summary>
    private void TickRecoverStabilization(float dt)
    {
        _recoverStabilizeTimer -= dt;
        var t = 1f - Mathf.Clamp01(_recoverStabilizeTimer / RECOVER_STABILIZE_DURATION);

        // mainJoint 스프링 점진 복원
        if (!ShouldDisablePhysicsAnimationSync && mainJoint != null)
        {
            var jd = mainJoint.slerpDrive;
            jd.positionSpring = Mathf.Lerp(0f, _startSlerpPositionSpring, t);
            mainJoint.slerpDrive = jd;
        }

        // 각 관절(SyncPhysicsObject)도 동일하게 점진 복원
        if (!ShouldDisablePhysicsAnimationSync)
        {
            for (var i = 0; i < syncPhysicsObjects.Length; i++)
            {
                if (syncPhysicsObjects[i] != null)
                    syncPhysicsObjects[i].SetSpringLerp(t);
            }
        }

        // 물리 뼈 각속도 지속 감쇠
        if (rigidbody3D != null && !rigidbody3D.isKinematic)
        {
            rigidbody3D.velocity *= 0.6f;
            rigidbody3D.angularVelocity *= 0.6f;
        }

        if (_puppetMaster != null && _puppetMaster.muscles != null)
        {
            foreach (var muscle in _puppetMaster.muscles)
            {
                if (muscle.joint == null) continue;
                var rb = muscle.joint.GetComponent<Rigidbody>();
                if (rb != null && !rb.isKinematic)
                {
                    rb.velocity *= 0.6f;
                    rb.angularVelocity *= 0.6f;
                }
            }
        }

        // recover 중에도 root를 pelvis에 동기화 — NetworkTransform이 stale 위치를 보내지 않도록
        // 주의: stabilization 동안에는 MaintainRecoveringAboveGround를 호출하지 않음.
        // 스프링 점진 복원과 전신 텔레포트가 동시에 일어나면 떨림의 원인이 됨.
        MaintainRecoveringHorizontalAnchor();
        MaintainRecoveringUprightRotation();
        SyncRootToPhysicsBody();

        if (_recoverStabilizeTimer <= 0f)
        {

            // 최종 스프링 값 확정 — mainJoint + 모든 관절
            if (!ShouldDisablePhysicsAnimationSync && mainJoint != null)
            {
                var jd = mainJoint.slerpDrive;
                jd.positionSpring = _startSlerpPositionSpring;
                mainJoint.slerpDrive = jd;
            }

            if (!ShouldDisablePhysicsAnimationSync)
            {
                for (var i = 0; i < syncPhysicsObjects.Length; i++)
                {
                    if (syncPhysicsObjects[i] != null)
                        syncPhysicsObjects[i].MakeActiveRagdoll();
                }
            }

            CompleteRecoveryStandUpHandoff();
            _isRecoverStabilizing = false;
            SynchronizeStunPresentationPhase();
        }
    }

    private bool TryTickStunnedState(float dt)
    {
        if (_isActiveRagdoll)
            return false;

        if (GetIsDeadState())
        {
            ClampStunnedMotion();
            SyncRootToPhysicsBody();
            return true;
        }

        TickStunnedGroundContactMemory(dt);
        var collapsePhase = TickStunCollapseTimer(dt);
        TickStunnedFloorSettleTimer(dt, collapsePhase);
        var stunnedPhase = ResolveCurrentStunnedPhase(collapsePhase);
        SetLocalPhysicalPhase(stunnedPhase, 1f, false);
        UpdateLocalCarryMode();
        TickCarryReleaseSettle(dt);
        ApplyStunCollapseSpringState(collapsePhase, stunnedPhase);
        if (_downedHitCooldownRemaining > 0f)
            _downedHitCooldownRemaining = Mathf.Max(0f, _downedHitCooldownRemaining - dt);

        // 잡혀서 운반 중이면 기절 타이머 정지 (운반 중 자동 회복 방지)
        bool pauseStunTimer = stunnedPhase == PhysicalPhase.BeingCarriedStunned;
        var timerScale = pauseStunTimer ? 0f : GetCurrentDownedRecoverScale();
        if (stunnedPhase == PhysicalPhase.DraggedStunned)
            timerScale *= 0.35f;
        var remaining = GetStunTimeRemaining() - timerScale * dt;
        SetStunTimeRemaining(Mathf.Max(0f, remaining));

        if (remaining <= 0f && !pauseStunTimer)
        {
            ForceRecover();
            if (_isActiveRagdoll)
                return true;
        }

        bool settledStunned = stunnedPhase == PhysicalPhase.SettledStunned;
        bool draggedStunned = stunnedPhase == PhysicalPhase.DraggedStunned;
        bool beingCarried = stunnedPhase == PhysicalPhase.BeingCarriedStunned;

        // 운반 중: 루트 바디 중력 비활성화 — grab joint와 PuppetMaster hips-root 조인트 충돌 방지
        // 중력이 남아 있으면 root가 매 물리 스텝마다 바닥으로 끌려가 body가 찌그러짐
        if (rigidbody3D != null && !rigidbody3D.isKinematic)
        {
            if (beingCarried && rigidbody3D.useGravity)
            {
                rigidbody3D.useGravity = false;
                rigidbody3D.velocity = Vector3.zero;
            }
            else if (!beingCarried && !rigidbody3D.useGravity)
            {
                rigidbody3D.useGravity = true;
            }
        }

        ClampStunnedMotion(collapsePhase, beingCarried, draggedStunned, settledStunned);
        if (collapsePhase)
            TraceStunCollapsePose("TryTickStunnedState");
        else
            TraceStunnedMotionSample("TryTickStunnedState");


        // 기절 중 물리 뼈(메인 리지드바디)가 잡기 조인트 등에 의해 끌려갈 수 있으므로
        // 루트 트랜스폼을 메인 리지드바디 위치에 맞춘다.
        // 이렇게 해야 NetworkTransform이 원격 클라이언트에 올바른 위치를 전달한다.
        if (beingCarried)
            SyncCarriedRootToPhysicsBody();
        else
            SyncRootToPhysicsBody();

        return true;
    }

    private bool TickStunCollapseTimer(float dt)
    {
        if (_beingGrabbedRefCount > 0)
        {
            _stunCollapseTimer = 0f;
            return false;
        }

        var collapsePhase = _stunCollapseTimer > 0f;
        if (collapsePhase)
            _stunCollapseTimer = Mathf.Max(0f, _stunCollapseTimer - dt);

        return collapsePhase;
    }

    private PhysicalPhase ResolveCurrentStunnedPhase(bool collapsePhase)
    {
        if (ShouldUseDraggedStunnedPhase())
            return PhysicalPhase.DraggedStunned;

        if (_beingGrabbedRefCount > 0)
            return PhysicalPhase.BeingCarriedStunned;

        if (ShouldUseSettledStunnedPhase(collapsePhase))
            return PhysicalPhase.SettledStunned;

        return collapsePhase
            ? PhysicalPhase.StunnedCollapse
            : PhysicalPhase.Stunned;
    }

    private bool ShouldUseDraggedStunnedPhase()
    {
        return _beingGrabbedRefCount == 1 && _isGrounded;
    }

    private bool ShouldUseSettledStunnedPhase(bool collapsePhase)
    {
        return !collapsePhase &&
               _beingGrabbedRefCount <= 0 &&
               HasRecentStunnedGroundContact() &&
               _stunnedFloorSettleTimer >= SettledStunnedEntryDuration;
    }

    private bool ShouldUseGroundedPlainStunNoCollapseEntry()
    {
        if (_beingGrabbedRefCount > 0 || !_isGrounded)
            return false;

        var rootVelocity = rigidbody3D != null && !rigidbody3D.isKinematic
            ? rigidbody3D.velocity
            : Vector3.zero;
        var rootAngularSpeed = rigidbody3D != null && !rigidbody3D.isKinematic
            ? rigidbody3D.angularVelocity.magnitude
            : 0f;
        ResolveStartupLaunchPelvisPosition(out var pelvisVelocity);

        var rootPlanarSpeed = new Vector2(rootVelocity.x, rootVelocity.z).magnitude;
        return rootPlanarSpeed <= GroundedPlainStunNoCollapsePlanarThreshold &&
               Mathf.Abs(rootVelocity.y) <= GroundedPlainStunNoCollapseVerticalThreshold &&
               rootAngularSpeed <= GroundedPlainStunNoCollapseAngularThreshold &&
               Mathf.Abs(pelvisVelocity.y) <= GroundedPlainStunNoCollapsePelvisVerticalThreshold;
    }

    private static SSAFYPlayTime.Character.BodyPartPhysicsProfile.CharacterPhysicsState ResolveImmediateStunBodyState(PhysicalPhase phase)
    {
        return phase switch
        {
            PhysicalPhase.BeingCarriedStunned => SSAFYPlayTime.Character.BodyPartPhysicsProfile.CharacterPhysicsState.CarriedStunned,
            PhysicalPhase.DraggedStunned => SSAFYPlayTime.Character.BodyPartPhysicsProfile.CharacterPhysicsState.DraggedStunned,
            PhysicalPhase.SettledStunned => SSAFYPlayTime.Character.BodyPartPhysicsProfile.CharacterPhysicsState.SettledStunned,
            PhysicalPhase.Stunned => SSAFYPlayTime.Character.BodyPartPhysicsProfile.CharacterPhysicsState.Stunned,
            _ => SSAFYPlayTime.Character.BodyPartPhysicsProfile.CharacterPhysicsState.StunnedCollapse
        };
    }

    private void TickStunnedGroundContactMemory(float dt)
    {
        if (_isGrounded)
        {
            _stunnedGroundContactTimer = StunnedGroundContactMemory;
            return;
        }

        _stunnedGroundContactTimer = Mathf.Max(0f, _stunnedGroundContactTimer - Mathf.Max(0f, dt));
    }

    private bool HasRecentStunnedGroundContact()
    {
        return _isGrounded || _stunnedGroundContactTimer > 0f;
    }

    private void TickStunnedFloorSettleTimer(float dt, bool collapsePhase)
    {
        if (collapsePhase ||
            _beingGrabbedRefCount > 0 ||
            !HasRecentStunnedGroundContact() ||
            _isRecovering ||
            _isRecoverStabilizing)
        {
            _stunnedFloorSettleTimer = 0f;
            return;
        }

        var safeDt = Mathf.Max(0f, dt);
        if (safeDt <= 0f)
            return;

        if (HasLowStunnedFloorMotion())
        {
            _stunnedFloorSettleTimer = Mathf.Min(
                SettledStunnedEntryDuration,
                _stunnedFloorSettleTimer + safeDt);
        }
        else
        {
            _stunnedFloorSettleTimer = Mathf.Max(
                0f,
                _stunnedFloorSettleTimer - safeDt * SettledStunnedTimerDecayRate);
        }
    }

    private bool HasLowStunnedFloorMotion()
    {
        var rootPlanarSpeed = 0f;
        var rootAngularSpeed = 0f;
        if (rigidbody3D != null && !rigidbody3D.isKinematic)
        {
            rootPlanarSpeed = new Vector3(rigidbody3D.velocity.x, 0f, rigidbody3D.velocity.z).magnitude;
            rootAngularSpeed = rigidbody3D.angularVelocity.magnitude;
        }

        if (rootPlanarSpeed > SettledStunnedRootPlanarThreshold ||
            rootAngularSpeed > SettledStunnedRootAngularThreshold)
        {
            return false;
        }

        if (_puppetMaster == null || _puppetMaster.muscles == null)
            return true;

        for (var i = 0; i < _puppetMaster.muscles.Length; i++)
        {
            var joint = _puppetMaster.muscles[i].joint;
            if (joint == null)
                continue;

            var rb = joint.GetComponent<Rigidbody>();
            if (rb == null || rb.isKinematic)
                continue;

            var planarSpeed = new Vector3(rb.velocity.x, 0f, rb.velocity.z).magnitude;
            if (planarSpeed > SettledStunnedMusclePlanarThreshold)
                return false;
        }

        return true;
    }

    private bool IsEarlyCollapsePhaseActive()
    {
        return GetPhysicalPhase() == PhysicalPhase.StunnedCollapse &&
               _stunCollapseTimer > Mathf.Max(0f, StunCollapseDuration - StunCollapseEarlyDuration);
    }

    private void ApplyStunCollapseSpringState(bool collapsePhase, PhysicalPhase stunnedPhase = PhysicalPhase.Stunned)
    {
        if (ShouldDisablePhysicsAnimationSync)
            return;

        var beingCarried = stunnedPhase == PhysicalPhase.BeingCarriedStunned;
        var draggedStunned = stunnedPhase == PhysicalPhase.DraggedStunned;

        if (mainJoint != null)
        {
            var jd = mainJoint.slerpDrive;
            if (collapsePhase)
                jd.positionSpring = Mathf.Max(1f, _startSlerpPositionSpring * StunCollapseEntryMainSpringScale);
            else if (beingCarried)
                jd.positionSpring = Mathf.Max(1f, _startSlerpPositionSpring * StunnedCarriedMainSpringScale);
            else
                jd.positionSpring = Mathf.Max(1f, _startSlerpPositionSpring * StunnedGroundedMainSpringScale);
            mainJoint.slerpDrive = jd;
        }

        for (var i = 0; i < syncPhysicsObjects.Length; i++)
        {
            if (syncPhysicsObjects[i] == null)
                continue;

            if (collapsePhase)
                syncPhysicsObjects[i].SetSpringLerp(StunCollapseEntryBoneSpringLerp);
            else if (beingCarried)
                syncPhysicsObjects[i].MakeStunnedCarried();
            else
                syncPhysicsObjects[i].MakeStunnedGrounded();
        }
    }

    /// <summary>
    /// 기절/레그돌 상태에서 실제 물리 뼈(pelvis) 위치를 루트 트랜스폼에 반영.
    /// 잡기 조인트로 끌려가는 건 PuppetMaster muscle 뼈이므로,
    /// rigidbody3D(루트)가 아닌 muscles[0](pelvis/hips)를 기준으로 해야 한다.
    ///
    /// 즉시 스냅 대신 Lerp를 사용하여 카메라 앵커에 급격한 점프가 전달되지 않도록 한다.
    /// 텔레포트 수준(5m+)이면 즉시 스냅.
    /// </summary>
    private bool TryResolveRootSyncTargetPosition(out Vector3 targetPos)
    {
        if (_puppetMaster != null && _puppetMaster.muscles != null && _puppetMaster.muscles.Length > 0)
        {
            var pelvisMuscle = _puppetMaster.muscles[0];
            if (pelvisMuscle.joint != null)
            {
                targetPos = pelvisMuscle.joint.transform.position;
                return true;
            }
        }

        if (rigidbody3D != null && !rigidbody3D.isKinematic)
        {
            targetPos = rigidbody3D.position;
            return true;
        }

        targetPos = default;
        return false;
    }

    private static string FormatCarryDebugVector(Vector3 value)
    {
        return $"({value.x:F2},{value.y:F2},{value.z:F2})";
    }

    private static float ResolveCarryEmergencySnapDistance(float baseSnapDistance)
    {
        return Mathf.Max(baseSnapDistance * 1.75f, baseSnapDistance + 0.75f);
    }

    private static float ResolveCarryEmergencyVerticalGap(float baseVerticalGap)
    {
        return Mathf.Max(baseVerticalGap * 1.60f, baseVerticalGap + 0.25f);
    }

    private static float ResolveCarryCorrectionFollowSpeed(float baseSpeed, bool largeCorrection)
    {
        return baseSpeed * (largeCorrection ? 2.35f : 1f);
    }

    private void SyncCarriedRootToPhysicsBody()
    {
        // CarrySolveFrame: carry anchor 기준으로 root follow
        if (!TryResolveCarryAnchorTargetPosition(out var targetPos))
        {
            // 폴백: 기존 pelvis 기반
            if (!TryResolveRootSyncTargetPosition(out targetPos))
                return;
        }

        var previousRootPos = transform.position;
        var delta = targetPos - previousRootPos;
        if (delta.sqrMagnitude > 0.25f || targetPos.y - previousRootPos.y > 0.08f)
        {
            TraceStartupLaunchDiagnostics(
                "SyncCarriedRootToPhysicsBody",
                targetPos,
                force: true,
                note: $"mode={_localCarryMode} deltaY={targetPos.y - previousRootPos.y:F2}");
        }

        if (delta.sqrMagnitude < 0.0004f)
            return;

        var dt = Runner != null ? Runner.DeltaTime : Time.fixedDeltaTime;
        var settings = ResolveCarryModeSettings();
        var verticalGap = targetPos.y - previousRootPos.y;
        var requiresStrongCorrection = delta.sqrMagnitude > settings.rootSnapDistance * settings.rootSnapDistance ||
                                       verticalGap > settings.rootSnapVerticalGap;
        var emergencySnapDistance = ResolveCarryEmergencySnapDistance(settings.rootSnapDistance);
        var emergencyVerticalGap = ResolveCarryEmergencyVerticalGap(settings.rootSnapVerticalGap);
        var shouldEmergencySnap = delta.sqrMagnitude > emergencySnapDistance * emergencySnapDistance ||
                                  verticalGap > emergencyVerticalGap;

        Vector3 newRootPos;
        if (shouldEmergencySnap)
        {
            newRootPos = targetPos;
        }
        else
        {
            var planarCurrent = new Vector3(previousRootPos.x, 0f, previousRootPos.z);
            var planarTarget = new Vector3(targetPos.x, 0f, targetPos.z);
            var planarNext = Vector3.MoveTowards(
                planarCurrent,
                planarTarget,
                ResolveCarryCorrectionFollowSpeed(settings.rootPlanarFollowSpeed, requiresStrongCorrection) * dt);
            var yNext = Mathf.MoveTowards(
                previousRootPos.y,
                targetPos.y,
                ResolveCarryCorrectionFollowSpeed(settings.rootVerticalFollowSpeed, requiresStrongCorrection) * dt);
            newRootPos = new Vector3(planarNext.x, yNext, planarNext.z);
        }

        // 운반 중에는 항상 velocity 리셋 — 잔여 중력/충돌 속도가 root를 끌어내리는 것 방지
        ApplyCarryRootPosition(newRootPos, resetVelocity: shouldEmergencySnap);

        // carry anchor 캐시 갱신 (네트워크 동기화 + settle 용)
        _lastCarryAnchorPosition = targetPos;

        var remainingGap = Vector3.Distance(newRootPos, targetPos);
        if (remainingGap > CarriedRootTraceGapThreshold)
        {
            TraceCarryDebugSample(
                "CarryAnchorSolve",
                $"mode={_localCarryMode} prevRoot={FormatCarryDebugVector(previousRootPos)} target={FormatCarryDebugVector(targetPos)} " +
                $"newRoot={FormatCarryDebugVector(newRootPos)} delta={FormatCarryDebugVector(delta)} " +
                $"verticalGap={verticalGap:F2} remainingGap={remainingGap:F2} correction={requiresStrongCorrection} emergencySnap={shouldEmergencySnap}",
                remainingGap > CarryRootDebugGapThreshold);
        }
    }

    /// <summary>
    /// CarryRig의 victim anchor를 기준으로 carry target 위치를 해석.
    /// victim 쪽: hips-chest 가중 평균 anchor
    /// </summary>
    private int CountCarryHandsTargetingSelf(NetworkPlayer candidateHolder, NetworkId selfId)
    {
        if (candidateHolder == null)
            return 0;

        var count = 0;
        if (candidateHolder.TryGetNetworkHeldAnchorData(HandGrabHandler.HandSide.Left, out var leftTargetId, out _, out _) &&
            leftTargetId == selfId)
        {
            count++;
        }

        if (candidateHolder.TryGetNetworkHeldAnchorData(HandGrabHandler.HandSide.Right, out var rightTargetId, out _, out _) &&
            rightTargetId == selfId)
        {
            count++;
        }

        return count;
    }

    private bool TryResolveCarrierOwnedVictimRootTarget(out Vector3 targetPos, out Vector3 targetFwd)
    {
        targetPos = default;
        targetFwd = transform.forward;

        if (_localCarryMode != SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.CarriedVictim ||
            !IsNetworkReady ||
            !Object.IsValid)
        {
            return false;
        }

        var selfId = Object.Id;
        NetworkPlayer bestHolder = null;
        var bestCarryMode = SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.None;
        var bestHoldVariant = CharacterGrabController.HoldVariant.None;
        var bestHandCount = 0;
        var bestDistanceSqr = float.PositiveInfinity;

        foreach (var candidate in RegisteredPlayers)
        {
            if (candidate == null || candidate == this || !candidate.IsNetworkReady || !candidate.Object.IsValid)
                continue;

            var candidateCarryMode = candidate.GetLocalCarryMode();
            if (candidateCarryMode != SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.StunnedSingleCarry &&
                candidateCarryMode != SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.StunnedDualCarry)
            {
                continue;
            }

            var handCount = CountCarryHandsTargetingSelf(candidate, selfId);
            if (handCount <= 0)
                continue;

            var candidateDistanceSqr = (candidate.transform.position - transform.position).sqrMagnitude;
            if (handCount < bestHandCount ||
                (handCount == bestHandCount && candidateDistanceSqr >= bestDistanceSqr))
            {
                continue;
            }

            var candidateHoldVariant = candidate.ResolveCurrentOrReplicatedHoldVariant();
            if (candidateHoldVariant != CharacterGrabController.HoldVariant.FrontCarry &&
                candidateHoldVariant != CharacterGrabController.HoldVariant.OverheadCarry &&
                candidateHoldVariant != CharacterGrabController.HoldVariant.DualCarry)
            {
                candidateHoldVariant = CharacterGrabController.ResolveCarryHoldVariant(candidateCarryMode);
            }

            bestHolder = candidate;
            bestCarryMode = candidateCarryMode;
            bestHoldVariant = candidateHoldVariant;
            bestHandCount = handCount;
            bestDistanceSqr = candidateDistanceSqr;
        }

        if (bestHolder == null)
            return false;

        var holderCarryRig = bestHolder.GetCarryRig();
        return holderCarryRig != null &&
               holderCarryRig.TryGetCarriedVictimRootTargetWorld(bestCarryMode, bestHoldVariant, out targetPos, out targetFwd);
    }

    private bool TryResolveCarryAnchorTargetPosition(out Vector3 targetPos)
    {
        if (_localCarryMode == SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.CarriedVictim &&
            TryResolveCarrierOwnedVictimRootTarget(out targetPos, out var carrierTargetForward))
        {
            _lastCarryAnchorForward = carrierTargetForward;
            return true;
        }

        if (TryResolveCarryAnchorBasePosition(out var anchorPos, out var anchorFwd))
        {
            _lastCarryAnchorForward = anchorFwd;
            targetPos = anchorPos;

            if (_localCarryMode == SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.CarriedVictim &&
                _hasCarriedVictimRootOffset)
            {
                targetPos += _carriedVictimRootOffset;
            }

            return true;
        }

        targetPos = default;
        return false;
    }

    private bool TryResolveCarryAnchorBasePosition(out Vector3 anchorPos, out Vector3 anchorFwd)
    {
        if (carryRig != null)
        {
            carryRig.UpdateVictimAnchor();
            if (carryRig.TryGetVictimSupportFrameWorld(out anchorPos, out anchorFwd))
                return true;
        }

        if (TryResolveRootSyncTargetPosition(out anchorPos))
        {
            anchorFwd = transform.forward;
            return true;
        }

        anchorPos = default;
        anchorFwd = transform.forward;
        return false;
    }

    private void CaptureCarriedVictimRootOffset()
    {
        if (TryResolveCarrierOwnedVictimRootTarget(out _, out _))
        {
            _hasCarriedVictimRootOffset = false;
            _carriedVictimRootOffset = Vector3.zero;
            return;
        }

        if (!TryResolveCarryAnchorBasePosition(out var anchorPos, out _))
        {
            _hasCarriedVictimRootOffset = false;
            _carriedVictimRootOffset = Vector3.zero;
            return;
        }

        _carriedVictimRootOffset = Vector3.ClampMagnitude(transform.position - anchorPos, 1.0f);
        _hasCarriedVictimRootOffset = true;
    }

    /// <summary>
    /// 현재 carry mode에 맞는 물리 설정을 반환.
    /// CarryPhysicsProfile이 없으면 하드코드 폴백.
    /// </summary>
    private SSAFYPlayTime.Character.CarryPhysicsProfile.CarryModeSettings ResolveCarryModeSettings()
    {
        return ResolveCarryModeSettings(_localCarryMode);
    }

    private SSAFYPlayTime.Character.CarryPhysicsProfile.CarryModeSettings ResolveCarryModeSettings(
        SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode mode)
    {
        if (carryPhysicsProfile != null)
            return carryPhysicsProfile.GetSettings(mode);

        // 폴백: 기존 하드코드 값
        return SSAFYPlayTime.Character.CarryPhysicsProfile.GetDefaultSettings(mode);
    }

    private static bool ShouldStartCarryReleaseSettle(
        SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode previousMode,
        SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode newMode,
        bool suppressNextCarryReleaseSettle)
    {
        return previousMode != SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.None &&
               newMode == SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.None &&
               !suppressNextCarryReleaseSettle;
    }

    internal void SuppressNextCarryReleaseSettle()
    {
        _suppressNextCarryReleaseSettle = true;
        _carryReleaseSettleRemaining = 0f;
        _lastCarryAnchorPosition = Vector3.zero;
        _lastCarryAnchorForward = Vector3.zero;
        _hasCarriedVictimRootOffset = false;
        _carriedVictimRootOffset = Vector3.zero;

        TraceCarryDebugSample(
            "SuppressCarryReleaseSettle",
            $"carry={_localCarryMode} phase={_localPhysicalPhase}",
            forceSample: true);
    }

    internal void ResetPhysicsMotionForHeldRelease()
    {
        if (rigidbody3D != null && !rigidbody3D.isKinematic)
        {
            rigidbody3D.velocity = Vector3.zero;
            rigidbody3D.angularVelocity = Vector3.zero;
            rigidbody3D.useGravity = true;
        }

        if (_puppetMaster == null || _puppetMaster.muscles == null)
            return;

        foreach (var muscle in _puppetMaster.muscles)
        {
            if (muscle.joint == null)
                continue;

            var rb = muscle.joint.GetComponent<Rigidbody>();
            if (rb == null || rb.isKinematic)
                continue;

            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    /// <summary>
    /// carry 모드를 phase 기반으로 갱신.
    /// phase 전환 시 settle 타이머도 관리.
    /// </summary>
    private void UpdateLocalCarryMode()
    {
        var phase = _localPhysicalPhase;
        var previousMode = _localCarryMode;

        SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode newMode;
        if (phase == PhysicalPhase.BeingCarriedStunned)
        {
            newMode = SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.CarriedVictim;
        }
        else if (phase == PhysicalPhase.CarryingStunned)
        {
            newMode = IsDualGrabbingStunnedPlayer
                ? SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.StunnedDualCarry
                : SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.StunnedSingleCarry;
        }
        else if (phase == PhysicalPhase.Holding)
        {
            newMode = SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.NormalGrab;
        }
        else
        {
            newMode = SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.None;
        }

        // carry → non-carry 전환 시 settle 시작
        if (ShouldStartCarryReleaseSettle(previousMode, newMode, _suppressNextCarryReleaseSettle))
        {
            var settings = carryPhysicsProfile != null
                ? carryPhysicsProfile.GetSettings(previousMode)
                : ResolveCarryModeSettings();
            _carryReleaseSettleRemaining = settings.carryReleaseSettleDuration;
        }
        else if (previousMode != SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.None &&
                 newMode == SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.None)
        {
            _carryReleaseSettleRemaining = 0f;
        }

        if (newMode == SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.None)
            _suppressNextCarryReleaseSettle = false;

        if (previousMode != SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.CarriedVictim &&
            newMode == SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.CarriedVictim)
        {
            CaptureCarriedVictimRootOffset();
        }
        else if (previousMode == SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.CarriedVictim &&
                 newMode != SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.CarriedVictim)
        {
            _hasCarriedVictimRootOffset = false;
            _carriedVictimRootOffset = Vector3.zero;
        }

        _localCarryMode = newMode;
        if (newMode != SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.None)
            _lastObservedCarryMode = newMode;

        if (previousMode != newMode)
        {
            TraceCarryDebugSample(
                "UpdateLocalCarryMode",
                $"carry={previousMode}->{newMode} phase={phase} hasVictimRootOffset={_hasCarriedVictimRootOffset}",
                forceSample: true);
            TraceStartupLaunchDiagnostics(
                "UpdateLocalCarryMode",
                force: true,
                note: $"carry={previousMode}->{newMode}");
        }
    }

    /// <summary>
    /// carry 종료 직후 settle 기간 중 root를 마지막 carry anchor 기준으로 유지.
    /// </summary>
    private void TickCarryReleaseSettle(float dt)
    {
        if (_carryReleaseSettleRemaining <= 0f)
            return;

        _carryReleaseSettleRemaining = Mathf.Max(0f, _carryReleaseSettleRemaining - dt);

        // settle 중에는 마지막 carry anchor를 기준으로 root를 유지
        if (_lastCarryAnchorPosition != Vector3.zero)
        {
            var currentRoot = transform.position;
            var toAnchor = _lastCarryAnchorPosition - currentRoot;
            if (toAnchor.sqrMagnitude > 0.01f)
            {
                var settleMode = _lastObservedCarryMode != SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.None
                    ? _lastObservedCarryMode
                    : SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.StunnedSingleCarry;
                var settings = ResolveCarryModeSettings(settleMode);
                var planarCurrent = new Vector3(currentRoot.x, 0f, currentRoot.z);
                var planarTarget = new Vector3(_lastCarryAnchorPosition.x, 0f, _lastCarryAnchorPosition.z);
                var planarNext = Vector3.MoveTowards(
                    planarCurrent,
                    planarTarget,
                    ResolveCarryCorrectionFollowSpeed(settings.rootPlanarFollowSpeed, largeCorrection: true) * dt);
                var yNext = Mathf.MoveTowards(
                    currentRoot.y,
                    _lastCarryAnchorPosition.y,
                    ResolveCarryCorrectionFollowSpeed(settings.rootVerticalFollowSpeed, largeCorrection: true) * dt);
                var settledRoot = new Vector3(planarNext.x, yNext, planarNext.z);
                ApplyCarryRootPosition(settledRoot, resetVelocity: true);

                TraceCarryDebugSample(
                    "CarryReleaseSettle",
                    $"remaining={_carryReleaseSettleRemaining:F2} root={FormatCarryDebugVector(currentRoot)} " +
                    $"anchor={FormatCarryDebugVector(_lastCarryAnchorPosition)} gap={toAnchor.magnitude:F2}");
            }
        }
    }

    private void ApplyRootPositionInternal(Vector3 nextRootPosition, bool resetVelocity, string writerSource)
    {
        var rootBefore = transform.position;
        var bodyBefore = rigidbody3D != null ? rigidbody3D.position : rootBefore;
        var pelvisBefore = ResolveStartupLaunchPelvisPosition(out _);
        var visualRoot = GetPresentationRootTransform();
        var visualBefore = visualRoot != null ? visualRoot.position : rootBefore;

        if (rigidbody3D != null && !rigidbody3D.isKinematic)
        {
            rigidbody3D.position = nextRootPosition;
            if (resetVelocity)
            {
                rigidbody3D.velocity = Vector3.zero;
                rigidbody3D.angularVelocity = Vector3.zero;
            }
        }

        transform.position = nextRootPosition;

        var rootAfter = transform.position;
        var bodyAfter = rigidbody3D != null ? rigidbody3D.position : rootAfter;
        var pelvisAfter = ResolveStartupLaunchPelvisPosition(out _);
        var visualAfter = visualRoot != null ? visualRoot.position : rootAfter;
        TraceStunTransformWriter(
            writerSource,
            rootBefore,
            rootAfter,
            bodyBefore,
            bodyAfter,
            pelvisBefore,
            pelvisAfter,
            visualBefore,
            visualAfter,
            $"resetVel={(resetVelocity ? 1 : 0)} phase={GetPhysicalPhase()}",
            force: Mathf.Abs(rootAfter.y - rootBefore.y) > 0.25f);
    }

    private void ApplyCarryRootPosition(Vector3 nextRootPosition, bool resetVelocity)
    {
        ApplyRootPositionInternal(nextRootPosition, resetVelocity, "Writer.ApplyCarryRootPosition");
    }

    private void SyncRootToPhysicsBody(bool forceImmediate = false)
    {
        if (!TryResolveRootSyncTargetPosition(out var targetPos))
            return;

        var originalTargetPos = targetPos;
        if (ShouldUseCollapseAnchor())
        {
            targetPos.x = _recoverAnchorPosition.x;
            targetPos.z = _recoverAnchorPosition.z;
        }

        var clampToGroundedAnchorFloor =
            ShouldUseCollapseAnchor() &&
            _recoverAnchorCapturedWhileGrounded &&
            _isGrounded &&
            !_isActiveRagdoll &&
            !_isRecovering &&
            !_isRecoverStabilizing;
        if (clampToGroundedAnchorFloor)
        {
            var minRootY = _recoverAnchorPosition.y + GroundedCollapseRootMinYOffset;
            if (targetPos.y < minRootY)
                targetPos.y = minRootY;
        }

        var upwardSyncStep = StunRootUpwardSyncStep;
        if ((!_isActiveRagdoll || _isRecovering || _isRecoverStabilizing) &&
            targetPos.y > transform.position.y + upwardSyncStep)
        {
            targetPos.y = transform.position.y + upwardSyncStep;
        }

        var delta = targetPos - transform.position;
        var upwardTarget = targetPos.y - transform.position.y;
        if (upwardTarget > 0.08f || delta.sqrMagnitude > 0.25f)
        {
            TraceStartupLaunchDiagnostics(
                "SyncRootToPhysicsBody",
                targetPos,
                force: true,
                note: $"originalTargetY={originalTargetPos.y:F2} upwardTarget={upwardTarget:F2} collapseAnchor={ShouldUseCollapseAnchor()} groundedAnchorClamp={(clampToGroundedAnchorFloor ? 1 : 0)} anchorY={_recoverAnchorPosition.y:F2}");
        }

        if (delta.sqrMagnitude < 0.001f)
            return;

        var downedRootSync = !_isActiveRagdoll || _isRecovering || _isRecoverStabilizing;
        if (downedRootSync)
        {
            var verticalGap = Mathf.Abs(targetPos.y - transform.position.y);
            var requiresStrongCorrection =
                delta.sqrMagnitude > DownedRootStrongCorrectionDistance * DownedRootStrongCorrectionDistance ||
                verticalGap > DownedRootStrongVerticalGap;
            var shouldEmergencySnap = forceImmediate ||
                                      delta.sqrMagnitude > DownedRootEmergencySnapDistance * DownedRootEmergencySnapDistance ||
                                      verticalGap > DownedRootEmergencyVerticalGap;

            Vector3 nextRootPosition;
            if (shouldEmergencySnap)
            {
                nextRootPosition = targetPos;
            }
            else
            {
                var dt = Runner != null ? Runner.DeltaTime : Time.fixedDeltaTime;
                var planarCurrent = new Vector3(transform.position.x, 0f, transform.position.z);
                var planarTarget = new Vector3(targetPos.x, 0f, targetPos.z);
                var followMultiplier = requiresStrongCorrection ? 2.25f : 1f;
                var planarNext = Vector3.MoveTowards(
                    planarCurrent,
                    planarTarget,
                    DownedRootPlanarFollowSpeed * followMultiplier * dt);
                var yNext = Mathf.MoveTowards(
                    transform.position.y,
                    targetPos.y,
                    DownedRootVerticalFollowSpeed * followMultiplier * dt);
                nextRootPosition = new Vector3(planarNext.x, yNext, planarNext.z);
            }

            if ((forceImmediate || shouldEmergencySnap) && IsStunDiagnosticsRelevantPhase(GetPhysicalPhase()))
            {
                TraceStunDiagnosticSnapshot(
                    "StunRootSync",
                    $"force={(forceImmediate ? 1 : 0)} emergency={(shouldEmergencySnap ? 1 : 0)} " +
                    $"target=({targetPos.x:F2},{targetPos.y:F2},{targetPos.z:F2}) delta={delta.magnitude:F2} groundedAnchorClamp={(clampToGroundedAnchorFloor ? 1 : 0)} anchorY={_recoverAnchorPosition.y:F2} upwardStep={upwardSyncStep:F2}",
                    force: true);
            }

            ApplyCarryRootPosition(nextRootPosition, resetVelocity: shouldEmergencySnap);
            return;
        }

        // 텔레포트 방지: 5m+ 거리면 즉시 스냅
        if (delta.sqrMagnitude > 25f)
        {
            ApplyCarryRootPosition(targetPos, resetVelocity: true);
            return;
        }

        // 부드럽게 추적 — 카메라 앵커가 급격한 점프를 받지 않도록
        var smoothDt = Runner != null ? Runner.DeltaTime : Time.fixedDeltaTime;
        var nextSmoothRootPosition = Vector3.Lerp(transform.position, targetPos, 8f * smoothDt);
        ApplyCarryRootPosition(nextSmoothRootPosition, resetVelocity: false);
    }

    private void SimulateLocomotion(PlayerNetworkInput input, float dt)
    {
        // PuppetMaster BehaviourPuppet가 Puppet 상태가 아니면(넘어짐/일어남 중) 이동 무시
        if (!CanDriveLocomotion)
            return;

        var buffApplier = ResolveItemBuffApplier();
        var moveSpeedMultiplier = buffApplier != null ? buffApplier.CurrentMoveSpeedMultiplier : 1f;
        var gravityMultiplier = buffApplier != null ? buffApplier.CurrentGravityMultiplier : 1f;
        var jumpMultiplier = buffApplier != null ? buffApplier.CurrentJumpMultiplier : 1f;

        // 기절자 양손 운반 시 이동 속도 제한
        if (_localPhysicalPhase == PhysicalPhase.CarryingStunned)
            moveSpeedMultiplier *= 0.7f;

        // 스프린트 속도 배율 적용
        if (input.Sprint)
        {
            var sprintMultiplier = config != null ? config.sprintSpeedMultiplier : 1.8f;
            moveSpeedMultiplier *= sprintMultiplier;
        }

        moveSpeedMultiplier *= ResolveHostRemoteClientMoveSpeedCompensation();

        var wasGrounded = _isGrounded;
        var wasRawGrounded = _activeAerialKickRawGrounded;
        var wasNearGround = _activeAerialKickNearGround;
        var rawGrounded = _groundProbe.IsGrounded(
            rigidbody3D.position,
            transform,
            config.groundProbeRadius,
            config.groundProbeDistance);
        _isGrounded = ResolveEffectiveAerialKickGroundedState(rawGrounded, dt);

        // Coyote time: 착지 직후 떨어지면 짧은 유예 시간 동안 점프 허용
        if (_isGrounded)
        {
            _coyoteTimeRemaining = COYOTE_TIME;
            _lastGroundedTime = Time.time;
            // 안정적으로 서 있을 때 안전 위치 기억 — recover 시 활용
            if (_isActiveRagdoll && !_isRecovering)
                RememberSafeTransform(transform.position, transform.rotation);
        }
        else
        {
            _coyoteTimeRemaining -= dt;
            var extraAerialKickFallAcceleration = 0f;
            if (_isAerialKickMomentumActive &&
                _activeAerialKickHasLeftGround &&
                Time.time >= _activeAerialKickStartedAt + AerialKickExtraFallAccelerationDelay)
            {
                extraAerialKickFallAcceleration = AerialKickExtraFallAcceleration;
            }

            rigidbody3D.AddForce(
                Vector3.down * ((config.extraGravity * Mathf.Max(0.05f, gravityMultiplier)) + extraAerialKickFallAcceleration),
                ForceMode.Acceleration);
        }

        if (_isAerialKickMomentumActive &&
            (wasGrounded != _isGrounded || wasRawGrounded != _activeAerialKickRawGrounded || wasNearGround != _activeAerialKickNearGround))
        {
            LogAerialKickDiagnostic(
                "GroundState",
                $"raw={rawGrounded} effective={_isGrounded} near={_activeAerialKickNearGround} contact={HasRecentAerialKickGroundContact()} confirm={_activeAerialKickLandingConfirmTimer:F2}");
        }

        var moveInput = new Vector3(input.Move.x, 0f, input.Move.y);
        var cameraRelativeMove = Quaternion.Euler(0f, input.CameraYaw, 0f) * moveInput;
        var inputMagnitude = Mathf.Clamp01(moveInput.magnitude);
        var moveDirection = cameraRelativeMove.sqrMagnitude > 0.0001f
            ? cameraRelativeMove.normalized
            : Vector3.zero;
        var alignedVelocity = moveDirection == Vector3.zero
            ? 0f
            : Vector3.Dot(moveDirection, rigidbody3D.velocity);

        RotateTowardInput(cameraRelativeMove, inputMagnitude, dt);
        ApplyMovementForce(moveDirection, inputMagnitude, moveSpeedMultiplier, input.Sprint, dt);
        ApplyJumpIfPossible(input.Jump, jumpMultiplier);
        _stateMachine.Tick(_isGrounded, inputMagnitude, dt, config);

        var planarVelocity = rigidbody3D.velocity;
        planarVelocity.y = 0f;
        var normalizedMoveSpeed = planarVelocity.magnitude * 0.4f;
        var locomotionState = ResolveLocomotionState(normalizedMoveSpeed, input.Sprint);
        SetMotorPresentationState(normalizedMoveSpeed, (int)_stateMachine.CurrentState, locomotionState);

        // 원격 클라이언트 애니메이션용 스프린트 상태 동기화
        if (Runner != null && Object != null && Object.IsValid)
            NetworkedIsSprinting = input.Sprint;
    }

    private float ResolveHostRemoteClientMoveSpeedCompensation()
    {
        if (Runner == null || !Runner.IsServer || Object == null || !Object.IsValid)
            return 1f;

        if (!Object.InputAuthority.IsRealPlayer)
            return 1f;

        if (Runner.LocalPlayer.IsRealPlayer && Object.InputAuthority == Runner.LocalPlayer)
            return 1f;

        return HostRemoteClientMoveSpeedCompensation;
    }

    private void RotateTowardInput(Vector3 moveDirection, float inputMagnitude, float dt)
    {
        if (TryResolveGrabFacingRotation(moveDirection, inputMagnitude, out var desiredRotation, out var rotateSpeed))
        {
            ApplyDesiredFacingRotation(desiredRotation, rotateSpeed, dt);
            return;
        }

        if (inputMagnitude <= 0.001f || moveDirection.sqrMagnitude <= 0.0001f)
            return;

        if (_targetRoot != null)
        {
            // PuppetMaster 모드: targetRoot(애니메이션 스켈레톤)를 직접 회전.
            // PuppetMaster가 이 타겟 포즈를 따라가므로 joint를 직접 건드리지 않는다.
            var desired = Quaternion.LookRotation(moveDirection.normalized, Vector3.up);
            ApplyDesiredFacingRotation(desired, config.rotateSpeedDeg, dt);
        }
        else
        {
            // PuppetMaster 없는 커스텀 래그돌: 기존 ConfigurableJoint 방식
            var visualDirection = new Vector3(moveDirection.x, 0f, moveDirection.z);
            var desired = Quaternion.LookRotation(visualDirection.normalized, transform.up);
            ApplyDesiredFacingRotation(desired, config.rotateSpeedDeg, dt);
        }
    }

    private bool TryResolveGrabFacingRotation(Vector3 moveDirection, float inputMagnitude, out Quaternion desiredRotation, out float rotateSpeed)
    {
        desiredRotation = Quaternion.identity;
        rotateSpeed = config != null ? config.rotateSpeedDeg : 360f;

        if (!IsAnyHandHoldingObject() || !TryGetCarryOrHeldReferenceWorldPosition(out var grabAnchorWorld))
            return false;

        var pivotPosition = ResolveGrabFacingPivotPosition();
        var anchorPlanar = grabAnchorWorld - pivotPosition;
        anchorPlanar.y = 0f;
        if (anchorPlanar.sqrMagnitude <= 0.0001f)
            return false;

        var currentYaw = ResolveGrabFacingCurrentYaw();
        var anchorYaw = Quaternion.LookRotation(anchorPlanar.normalized, Vector3.up).eulerAngles.y;
        var desiredYaw = currentYaw;
        var hasMoveInput = inputMagnitude > 0.001f && moveDirection.sqrMagnitude > 0.0001f;
        if (hasMoveInput)
        {
            var planarMove = moveDirection;
            planarMove.y = 0f;
            if (planarMove.sqrMagnitude <= 0.0001f)
            {
                hasMoveInput = false;
            }
            else
            {
                desiredYaw = Quaternion.LookRotation(planarMove.normalized, Vector3.up).eulerAngles.y;
            }
        }

        var softLimit = config != null && config.grabYawSoftLimitDeg > 0f ? config.grabYawSoftLimitDeg : 60f;
        var hardLimit = config != null && config.grabYawHardLimitDeg > 0f
            ? Mathf.Max(softLimit, config.grabYawHardLimitDeg)
            : 75f;
        var currentDelta = Mathf.DeltaAngle(anchorYaw, currentYaw);
        var desiredDelta = Mathf.DeltaAngle(anchorYaw, desiredYaw);
        var clampedDelta = Mathf.Clamp(desiredDelta, -hardLimit, hardLimit);

        if (!hasMoveInput && Mathf.Abs(currentDelta) <= hardLimit)
            return false;

        desiredRotation = Quaternion.Euler(0f, anchorYaw + clampedDelta, 0f);
        if (Mathf.Abs(desiredDelta) > softLimit || Mathf.Abs(currentDelta) > softLimit)
        {
            var turnScale = config != null && config.grabTurnSpeedScale > 0f ? config.grabTurnSpeedScale : 0.45f;
            rotateSpeed *= turnScale;
        }

        TraceMoveHoldFacing(
            "TryResolveGrabFacingRotation",
            currentYaw,
            anchorYaw,
            desiredYaw,
            currentDelta,
            desiredDelta,
            clampedDelta,
            softLimit,
            hardLimit,
            rotateSpeed,
            hasMoveInput);

        return true;
    }

    private void ApplyDesiredFacingRotation(Quaternion desiredRotation, float rotateSpeed, float dt)
    {
        if (_targetRoot != null)
        {
            _targetRoot.rotation = Quaternion.RotateTowards(
                _targetRoot.rotation,
                desiredRotation,
                dt * rotateSpeed);
            SetPresentationVisualYaw(_targetRoot.rotation.eulerAngles.y);
            return;
        }

        mainJoint.targetRotation = Quaternion.RotateTowards(
            mainJoint.targetRotation,
            desiredRotation,
            dt * rotateSpeed);
        SetPresentationVisualYaw(desiredRotation.eulerAngles.y);
    }

    private float ResolveGrabFacingCurrentYaw()
    {
        if (_targetRoot != null)
            return _targetRoot.rotation.eulerAngles.y;

        return transform.eulerAngles.y;
    }

    private Vector3 ResolveGrabFacingPivotPosition()
    {
        if (_puppetMaster != null && _puppetMaster.muscles != null && _puppetMaster.muscles.Length > 0)
        {
            var pivot = _puppetMaster.muscles[0].joint;
            if (pivot != null)
                return pivot.transform.position;
        }

        if (_targetRoot != null)
            return _targetRoot.position;

        return transform.position;
    }

    private void ApplyMovementForce(
        Vector3 moveDirection,
        float inputMagnitude,
        float moveSpeedMultiplier,
        bool sprintPressed,
        float dt)
    {
        var planarVelocity = rigidbody3D.velocity;
        planarVelocity.y = 0f;
        var recoilActive = IsInHitRecoil;
        var unstableHitPenalty = ShouldApplyHitMovementPenalty();

        if (_isGrounded && inputMagnitude <= 0.001f)
        {
            ApplyPlanarBrake(planarVelocity, dt, recoilActive, unstableHitPenalty);
            return;
        }

        if (moveDirection == Vector3.zero)
            return;

        if (unstableHitPenalty)
            moveSpeedMultiplier *= HitReactionMoveSpeedScale;

        var targetSpeed = config.maxSpeed * Mathf.Max(0.05f, moveSpeedMultiplier) * inputMagnitude;
        var targetVelocity = moveDirection * targetSpeed;
        var acceleration = _isGrounded ? config.acceleration : config.airAcceleration;
        if (recoilActive)
            acceleration *= HIT_RECOIL_ACCEL_SCALE;
        var maxVelocityChange = Mathf.Max(0f, acceleration) * dt;
        var velocityDelta = Vector3.ClampMagnitude(targetVelocity - planarVelocity, maxVelocityChange);

        TraceMoveHoldForce(
            "ApplyMovementForce",
            moveDirection,
            inputMagnitude,
            moveSpeedMultiplier,
            planarVelocity,
            targetVelocity,
            velocityDelta,
            acceleration,
            sprintPressed,
            recoilActive,
            unstableHitPenalty);

        if (dt > 0f)
        {
            rigidbody3D.AddForce(velocityDelta / dt, ForceMode.Acceleration);

            if (_isGrounded && config.groundStickForce > 0f)
            {
                var stickScale = recoilActive ? HIT_RECOIL_GROUND_STICK_SCALE : 1f;
                if (unstableHitPenalty)
                    stickScale *= HitReactionGroundStickScale;
                rigidbody3D.AddForce(
                    Vector3.down * config.groundStickForce * stickScale,
                    ForceMode.Acceleration);
            }
        }
    }

    private void ApplyPlanarBrake(
        Vector3 planarVelocity,
        float dt,
        bool recoilActive = false,
        bool unstableHitPenalty = false)
    {
        if (dt <= 0f)
            return;

        if (planarVelocity.sqrMagnitude <= config.stopSpeedEpsilon * config.stopSpeedEpsilon)
        {
            if (!recoilActive)
                rigidbody3D.velocity = new Vector3(0f, rigidbody3D.velocity.y, 0f);
            return;
        }

        var brakeAccel = Mathf.Max(0f, config.brakingAcceleration);
        if (recoilActive)
            brakeAccel *= HIT_RECOIL_BRAKE_SCALE;
        if (unstableHitPenalty)
            brakeAccel *= HitReactionBrakeScale;
        var brakeSpeed = brakeAccel * dt;
        var newPlanarSpeed = Mathf.Max(0f, planarVelocity.magnitude - brakeSpeed);
        var newPlanarVelocity = planarVelocity.normalized * newPlanarSpeed;
        rigidbody3D.velocity = new Vector3(newPlanarVelocity.x, rigidbody3D.velocity.y, newPlanarVelocity.z);
    }

    private void ApplyJumpIfPossible(bool jumpPressed, float jumpMultiplier)
    {
        // Jump buffer: 공중에서 점프 입력 → 착지 직후 자동 점프
        if (jumpPressed)
            _jumpBufferRemaining = JUMP_BUFFER_TIME;
        else
            _jumpBufferRemaining -= Runner != null ? Runner.DeltaTime : Time.fixedDeltaTime;

        // Coyote time과 jump buffer 양쪽 모두 유효해야 점프
        bool canJump = _coyoteTimeRemaining > 0f && _jumpBufferRemaining > 0f;
        if (!canJump)
            return;

        rigidbody3D.AddForce(
            Vector3.up * config.jumpImpulse * Mathf.Max(0.05f, jumpMultiplier),
            ForceMode.Impulse);
        _stateMachine.SetJump();

        // 소비: 더블점프 방지
        _coyoteTimeRemaining = 0f;
        _jumpBufferRemaining = 0f;
    }

    private void SynchronizeMotorPresentation()
    {
        if (Runner != null && Object != null && Object.IsValid)
        {
            NetworkedMoveSpeed = _localMoveSpeed;
            NetworkedMotorState = _localMotorState;
            NetworkedLocomotionState = (byte)_localPresentationLocomotionState;
        }
    }

    private void SetMotorPresentationState(float moveSpeed, int motorState, PresentationLocomotionState locomotionState)
    {
        _localMoveSpeed = moveSpeed;
        _localMotorState = motorState;
        _localPresentationLocomotionState = locomotionState;

        if (Runner != null && Object != null && Object.IsValid)
        {
            NetworkedMoveSpeed = moveSpeed;
            NetworkedMotorState = motorState;
            NetworkedLocomotionState = (byte)locomotionState;
        }
    }

    private void UpdateActiveRagdollJoints()
    {
        if (!_isActiveRagdoll || ShouldDisablePhysicsAnimationSync)
            return;

        for (var i = 0; i < syncPhysicsObjects.Length; i++)
            syncPhysicsObjects[i].UpdateJointFromAnimation();
    }

    private void UpdatePhysicalPhaseState(float dt)
    {
        if (!_isActiveRagdoll)
        {
            SetLocalPhysicalPhase(
                ResolveCurrentStunnedPhase(_stunCollapseTimer > 0f),
                1f,
                false);
            UpdateLocalCarryMode();
            TickCarryReleaseSettle(dt);
            return;
        }

        var anyHolding = IsAnyHandHoldingObject();
        var beingGrabbed = _beingGrabbedRefCount > 0;

        UpdateInstabilityScore(dt, anyHolding, beingGrabbed);

        var dragged = ResolveDraggedState(beingGrabbed);
        var phase = ResolveAuthorityPhysicalPhaseCore(
            _localPhysicalPhase,
            _localInstability,
            _isRecovering,
            _isRecoverStabilizing,
            anyHolding,
            IsAnyHandHoldingStunnedPlayer,
            _isGrabActive,
            _itemRuntimeHost != null && _itemRuntimeHost.IsHeldItemEquipment,
            IsGroggyActive(),
            beingGrabbed,
            dragged);
        SetLocalPhysicalPhase(phase, _localInstability, dragged);
        UpdateLocalCarryMode();
        TickCarryReleaseSettle(dt);
    }

    private void RefreshPhysicalPhaseAfterGrabHandlers()
    {
        if (!_isActiveRagdoll)
        {
            SetLocalPhysicalPhase(
                ResolveCurrentStunnedPhase(_stunCollapseTimer > 0f),
                1f,
                false);
            UpdateLocalCarryMode();
            return;
        }

        var anyHolding = IsAnyHandHoldingObject();
        var beingGrabbed = _beingGrabbedRefCount > 0;
        var dragged = ResolveDraggedState(beingGrabbed);
        var phase = ResolveAuthorityPhysicalPhaseCore(
            _localPhysicalPhase,
            _localInstability,
            _isRecovering,
            _isRecoverStabilizing,
            anyHolding,
            IsAnyHandHoldingStunnedPlayer,
            _isGrabActive,
            _itemRuntimeHost != null && _itemRuntimeHost.IsHeldItemEquipment,
            IsGroggyActive(),
            beingGrabbed,
            dragged);
        SetLocalPhysicalPhase(phase, _localInstability, dragged);
        UpdateLocalCarryMode();
    }

    private void UpdateInstabilityScore(float dt, bool anyHolding, bool beingGrabbed)
    {
        if (rigidbody3D == null)
        {
            _localInstability = 0f;
            return;
        }

        var bodyUp = rigidbody3D.transform.up;
        var tilt = 1f - Mathf.Clamp01(Vector3.Dot(bodyUp, Vector3.up));
        var planarSpeed = new Vector3(rigidbody3D.velocity.x, 0f, rigidbody3D.velocity.z).magnitude;
        var lateralAngularSpeed = new Vector2(rigidbody3D.angularVelocity.x, rigidbody3D.angularVelocity.z).magnitude;

        var targetInstability = 0f;
        targetInstability += Mathf.Clamp01(tilt / 0.45f) * 0.55f;
        targetInstability += Mathf.Clamp01(lateralAngularSpeed / 6f) * 0.35f;

        if (!_isGrounded)
            targetInstability += 0.12f;
        if (beingGrabbed)
            targetInstability += 0.22f;
        if (_isGrabActive && !anyHolding)
            targetInstability += 0.06f;

        var maxSpeed = config != null ? Mathf.Max(1f, config.maxSpeed) : 3f;
        if (planarSpeed > maxSpeed * 0.9f)
            targetInstability += 0.08f;
        targetInstability += _hitInstabilityBoost;

        // 피격 직후 불안정 바닥값 강제 적용
        if (IsInHitRecoil)
            targetInstability = Mathf.Max(targetInstability, HIT_RECOIL_INSTABILITY_FLOOR);

        var safeDt = Mathf.Max(dt, 0.0001f);
        var changeSpeed = _localInstability < targetInstability ? InstabilityRiseSpeed : InstabilityFallSpeed;
        _localInstability = Mathf.MoveTowards(_localInstability, Mathf.Clamp01(targetInstability), changeSpeed * safeDt);
    }

    private bool ResolveDraggedState(bool beingGrabbed)
    {
        if (!beingGrabbed || rigidbody3D == null)
            return false;

        var planarSpeed = new Vector3(rigidbody3D.velocity.x, 0f, rigidbody3D.velocity.z).magnitude;
        var lateralAngularSpeed = new Vector2(rigidbody3D.angularVelocity.x, rigidbody3D.angularVelocity.z).magnitude;

        return planarSpeed >= DragPlanarSpeedThreshold ||
               lateralAngularSpeed >= DragAngularSpeedThreshold ||
               !_isGrounded;
    }

    private PhysicalPhase ResolveAuthorityPhysicalPhase(bool anyHolding, bool beingGrabbed, bool dragged)
    {
        return ResolveAuthorityPhysicalPhaseCore(
            _localPhysicalPhase,
            _localInstability,
            _isRecovering,
            _isRecoverStabilizing,
            anyHolding,
            IsAnyHandHoldingStunnedPlayer,
            _isGrabActive,
            _itemRuntimeHost != null && _itemRuntimeHost.IsHeldItemEquipment,
            IsGroggyActive(),
            beingGrabbed,
            dragged);
#if false
        if (beingGrabbed)
            return dragged ? PhysicalPhase.Dragged : PhysicalPhase.BeingGrabbed;

        if (_isRecovering || _isRecoverStabilizing)
            return PhysicalPhase.Recovering;

        if (anyHolding)
        {
            if (IsAnyHandHoldingStunnedPlayer)
                return PhysicalPhase.CarryingStunned;
            return PhysicalPhase.Holding;
        }

        if (_isGrabActive)
            return PhysicalPhase.GrabIntent;

        if (_itemRuntimeHost != null && _itemRuntimeHost.IsHeldItemEquipment)
            return PhysicalPhase.WeaponEquipped;

        var instabilityThreshold = _localPhysicalPhase == PhysicalPhase.Unstable
            ? UnstableExitThreshold
            : UnstableEnterThreshold;
        if (_localInstability >= instabilityThreshold || IsGroggyActive())
            return PhysicalPhase.Unstable;

        if (anyHolding)
        {
            // 기절자 잡기(한손/양손 모두) → CarryingStunned (코어 안정화 + 캐리 포즈)
            if (IsAnyHandHoldingStunnedPlayer)
                return PhysicalPhase.CarryingStunned;
            return PhysicalPhase.Holding;
        }

        if (_isGrabActive)
            return PhysicalPhase.GrabIntent;

        // 장비 아이템(수박칼, 화염방사기 등) 장착 중
        if (_itemRuntimeHost != null && _itemRuntimeHost.IsHeldItemEquipment)
            return PhysicalPhase.WeaponEquipped;

        if (IsGroggyActive())
            return PhysicalPhase.Unstable;

        return PhysicalPhase.Stable;
#endif
    }

    private static PhysicalPhase ResolveAuthorityPhysicalPhaseCore(
        PhysicalPhase currentPhase,
        float instability,
        bool isRecovering,
        bool isRecoverStabilizing,
        bool anyHolding,
        bool isHoldingStunnedPlayer,
        bool isGrabActive,
        bool hasHeldEquipment,
        bool isGroggy,
        bool beingGrabbed,
        bool dragged)
    {
        if (beingGrabbed)
            return dragged ? PhysicalPhase.Dragged : PhysicalPhase.BeingGrabbed;

        if (isRecovering || isRecoverStabilizing)
            return PhysicalPhase.Recovering;

        if (anyHolding)
            return isHoldingStunnedPlayer ? PhysicalPhase.CarryingStunned : PhysicalPhase.Holding;

        if (isGrabActive)
            return PhysicalPhase.GrabIntent;

        if (hasHeldEquipment)
            return PhysicalPhase.WeaponEquipped;

        var instabilityThreshold = currentPhase == PhysicalPhase.Unstable
            ? UnstableExitThreshold
            : UnstableEnterThreshold;
        if (instability >= instabilityThreshold || isGroggy)
            return PhysicalPhase.Unstable;

        return PhysicalPhase.Stable;
    }

    private void SetLocalPhysicalPhase(PhysicalPhase phase, float instability, bool dragged)
    {
        var previousPhase = _localPhysicalPhase;
        _localPhysicalPhase = phase;
        _localInstability = Mathf.Clamp01(instability);
        _localIsDragged = dragged;

        if (previousPhase != phase)
        {
            if (IsStunDiagnosticsRelevantPhase(phase))
            {
                ArmStunDiagnosticsWindow(
                    "SetLocalPhysicalPhase",
                    $"phase={previousPhase}->{phase} instability={instability:F2} dragged={dragged}");
            }

            TraceStartupLaunchDiagnostics(
                "SetLocalPhysicalPhase",
                force: true,
                note: $"phase={previousPhase}->{phase} instability={instability:F2} dragged={dragged}");

            TraceStunDiagnosticSnapshot(
                "SetLocalPhysicalPhase",
                $"phase={previousPhase}->{phase} instability={instability:F2} dragged={dragged}",
                force: true);
        }
    }

    private float ResolveStunStateMultiplier()
    {
        var multiplier = ResolveLowHealthStunMultiplier();
        if (IsGroggyActive())
            multiplier *= ResolveConfiguredGroggyMultiplier();

        if (_isRecovering)
            return multiplier * (CombatSettings.Instance != null ? CombatSettings.Instance.recoveringMultiplier : 2.0f);
        if (!_isGrounded)
            return multiplier * (CombatSettings.Instance != null ? CombatSettings.Instance.airborneMultiplier : 1.5f);

        return multiplier;
    }

    private float ResolveConfiguredDownedRecoverScaleStart()
    {
        return CombatSettings.Instance != null
            ? Mathf.Max(0.05f, CombatSettings.Instance.downedRecoverScaleStart)
            : 1f;
    }

    private float ResolveConfiguredDownedRecoverScaleMin()
    {
        var start = ResolveConfiguredDownedRecoverScaleStart();
        var configuredMin = CombatSettings.Instance != null
            ? CombatSettings.Instance.downedRecoverScaleMin
            : 0.35f;
        return Mathf.Clamp(configuredMin, 0.05f, start);
    }

    private float ResolveConfiguredDownedRecoverScaleHitPenalty()
    {
        return CombatSettings.Instance != null
            ? Mathf.Max(0f, CombatSettings.Instance.downedRecoverScaleHitPenalty)
            : 0.18f;
    }

    private float ResolveConfiguredDownedHitPenaltyCooldown()
    {
        return CombatSettings.Instance != null
            ? Mathf.Max(0f, CombatSettings.Instance.downedHitPenaltyCooldown)
            : 0.22f;
    }

    private void ResetDownedHitRecoveryState()
    {
        _downedRecoverScale = ResolveConfiguredDownedRecoverScaleStart();
        _downedHitCooldownRemaining = 0f;
    }

    private float GetCurrentDownedRecoverScale()
    {
        var start = ResolveConfiguredDownedRecoverScaleStart();
        if (_downedRecoverScale <= 0f)
            return start;

        return Mathf.Clamp(_downedRecoverScale, ResolveConfiguredDownedRecoverScaleMin(), start);
    }

    private void ApplyDownedHitPenalty(float stunDamage, float impulseMagnitude, DownedHitPolicy downedHitPolicy)
    {
        if (downedHitPolicy != DownedHitPolicy.RecoveryPenalty)
            return;
        if (_beingGrabbedRefCount > 0)
        {
            ArmStunForceDiagnostics(
                "ApplyDownedHitPenalty-Skipped",
                $"beingGrabbedRefCount={_beingGrabbedRefCount} stun={stunDamage:F2} impulse={impulseMagnitude:F2}");
            return;
        }
        if (_downedHitCooldownRemaining > 0f)
            return;

        var penalty = ResolveConfiguredDownedRecoverScaleHitPenalty();
        if (penalty <= 0f)
            return;

        var previousScale = GetCurrentDownedRecoverScale();
        var nextScale = Mathf.Max(ResolveConfiguredDownedRecoverScaleMin(), previousScale - penalty);
        _downedRecoverScale = nextScale;
        _downedHitCooldownRemaining = ResolveConfiguredDownedHitPenaltyCooldown();
        ArmHitInstabilityBoost(Mathf.Max(impulseMagnitude * 0.35f, stunDamage * 0.2f));
        ArmStunForceDiagnostics(
            "ApplyDownedHitPenalty",
            $"scale={previousScale:F2}->{nextScale:F2} stun={stunDamage:F2} impulse={impulseMagnitude:F2}");
    }

    private float AddStunDamage(float stunDamage)
    {
        var accumulated = GetAccumulatedStun() + stunDamage;
        SetAccumulatedStun(accumulated);
        return accumulated;
    }

    private float GetAccumulatedStun()
    {
        if (Runner != null && Object != null && Object.IsValid)
            return AccumulatedStunDamage;

        return _localAccumulatedStun;
    }

    private void SetAccumulatedStun(float value)
    {
        if (Runner != null && Object != null && Object.IsValid)
            AccumulatedStunDamage = value;
        else
            _localAccumulatedStun = value;
    }

    private float GetStunTimeRemaining()
    {
        if (Runner != null && Object != null && Object.IsValid)
            return StunTimeRemaining;

        return _localStunTimeRemaining;
    }

    private void SetStunTimeRemaining(float value)
    {
        if (Runner != null && Object != null && Object.IsValid)
            StunTimeRemaining = value;
        else
            _localStunTimeRemaining = value;
    }

    private float CalculateStunDuration(float attackerVelocity, float impulseMagnitude, float overflowDamage, float threshold)
    {
        var stunMin = CombatSettings.Instance != null ? CombatSettings.Instance.stunMinDuration : 1.5f;
        var stunMax = CombatSettings.Instance != null ? CombatSettings.Instance.stunMaxDuration : 8.0f;
        var velocityBonus = CombatSettings.Instance != null ? CombatSettings.Instance.stunVelocityBonus : 0.15f;
        var weightBonus = CombatSettings.Instance != null ? CombatSettings.Instance.stunWeightBonus : 0.02f;
        var safeThreshold = Mathf.Max(0.01f, threshold);
        var overflowRatio = Mathf.Clamp01(overflowDamage / safeThreshold);
        var baseDuration = Mathf.Lerp(stunMin, stunMax * 0.7f, overflowRatio);
        var duration = baseDuration
                       + attackerVelocity * velocityBonus
                       + impulseMagnitude * weightBonus;

        return Mathf.Clamp(duration, stunMin, stunMax);
    }

    private static bool ShouldApplyPlainStunEntryDamping(
        bool applyEntryDamping,
        bool plainStunEntry,
        bool suppressImplicitPlainStunDamping)
    {
        return applyEntryDamping || (plainStunEntry && !suppressImplicitPlainStunDamping);
    }

    private void TriggerStun(
        float duration,
        bool applyEntryDamping = true,
        bool suppressImplicitPlainStunDamping = false)
    {
        if (Runner != null && Object != null && Object.IsValid && !HasStateAuthority)
            return;

        TraceRootGapAnomaly(
            "TriggerStun.PreGap",
            $"duration={duration:F2} applyEntryDamping={(applyEntryDamping ? 1 : 0)} grounded={(_isGrounded ? 1 : 0)} rawGrounded={(_activeAerialKickRawGrounded ? 1 : 0)} nearGround={(_activeAerialKickNearGround ? 1 : 0)} leftGround={(_activeAerialKickHasLeftGround ? 1 : 0)} activeRagdoll={(_isActiveRagdoll ? 1 : 0)}",
            force: true);

        _localMoveSpeed = 0f;
        _localPresentationLocomotionState = PresentationLocomotionState.Idle;
        if (Runner != null && Object != null && Object.IsValid)
        {
            NetworkedMoveSpeed = 0f;
            NetworkedLocomotionState = (byte)PresentationLocomotionState.Idle;
            NetworkedIsSprinting = false;
        }

        if (!ShouldDisablePhysicsAnimationSync && mainJoint != null)
        {
            var jd = mainJoint.slerpDrive;
            jd.positionSpring = 0;
            mainJoint.slerpDrive = jd;
        }

        if (!ShouldDisablePhysicsAnimationSync)
        {
            for (int i = 0; i < syncPhysicsObjects.Length; i++)
                syncPhysicsObjects[i].MakeRagdoll();
        }

        _isActiveRagdoll = false;
        RestorePuppetMasterMappingAfterAerialKick();
        _isAerialKickMomentumActive = false;
        _aerialKickMissPenaltyPending = false;
        _aerialKickSpringRestoreTimer = 0f;
        SetStunShield(0f);
        SetStunShieldRecoverDelayRemaining(0f);
        SetGroggyRemaining(0f);
        SetStunComboWindowRemaining(0f);
        SetRecentStunHitCount(0);
        SetStunHitImmunityRemaining(0f);
        SetNoStaggerRemaining(0f);
        ClearPunchHitDetectionWindow();
        ClearKickHitDetectionWindow();
        ClearHeadbuttHitDetectionWindow();
        ClearAerialKickHitDetectionWindow();
        var activeHeadbuttPresentation = GetComponent<ProceduralHeadbutt>();
        if (activeHeadbuttPresentation != null)
            activeHeadbuttPresentation.CancelHeadbutt();
        _isLeftGrabActive = false;
        _isRightGrabActive = false;
        _isGrabActive = false;

        // 기절 시 장비 아이템 드롭
        _itemRuntimeHost?.NotifyStunned();
        SetStunTimeRemaining(duration);
        SetAccumulatedStun(0f);
        ResetDownedHitRecoveryState();
        _stunnedFloorSettleTimer = 0f;
        _stunnedGroundContactTimer = _isGrounded ? StunnedGroundContactMemory : 0f;
        var plainStunEntry = _beingGrabbedRefCount <= 0;
        var suppressCollapseForGroundedPlainStun = plainStunEntry && ShouldUseGroundedPlainStunNoCollapseEntry();
        _stunCollapseTimer = _beingGrabbedRefCount > 0 || suppressCollapseForGroundedPlainStun
            ? 0f
            : Mathf.Min(duration, StunCollapseDuration);
        var initialStunnedPhase = ResolveCurrentStunnedPhase(_stunCollapseTimer > 0f);
        ApplyStunCollapseSpringState(_stunCollapseTimer > 0f, initialStunnedPhase);
        CaptureCollapseAnchorPose(transform.position, _targetRoot != null ? _targetRoot.rotation : transform.rotation);
        var shouldDampenEntry = ShouldApplyPlainStunEntryDamping(
            applyEntryDamping,
            plainStunEntry,
            suppressImplicitPlainStunDamping);

        ArmStunDiagnosticsWindow(
            "TriggerStun",
            $"duration={duration:F2} applyEntryDamping={(applyEntryDamping ? 1 : 0)} suppressImplicit={(suppressImplicitPlainStunDamping ? 1 : 0)} effectiveDamping={(shouldDampenEntry ? 1 : 0)} groundedPlainNoCollapse={(suppressCollapseForGroundedPlainStun ? 1 : 0)} initialPhase={initialStunnedPhase}");
        ArmStunForceDiagnostics("TriggerStun", $"duration={duration:F2}");
        TraceStunCollapsePose("TriggerStun-Entry", true);
        if (shouldDampenEntry)
            DampenStunEntryVelocities(groundedPlopEntry: plainStunEntry && _isGrounded);
        TraceStunCollapsePose("TriggerStun-Damped", true);
        SetLocalPhysicalPhase(ResolveCurrentStunnedPhase(_stunCollapseTimer > 0f), 1f, false);
        _bodyPartPhysicsManager?.SetStateImmediate(ResolveImmediateStunBodyState(initialStunnedPhase));
        SyncRootToPhysicsBody(forceImmediate: true);
        FlagPhysicsPresentationReset();
        RaiseAnimationEvent(AnimationEventType.StunFall, H_StunFall);
        SynchronizeStunPresentationPhase();

        // 기절 비주얼: 애니메이션 비주얼 숨기고 물리 타겟 스켈레톤(래그돌)을 보여줌
        SetStunVisualMode(true);

        // 호스트: 회복 래치가 켜져 있으면 해제하고 mappingWeight 복원 (래그돌 포즈가 보여야 하므로)
        DeactivateAuthorityAnimatorVisualLatch();

        TraceStunDiagnosticSnapshot(
            "TriggerStun-Configured",
            $"duration={duration:F2} initialPhase={initialStunnedPhase} collapseTimer={_stunCollapseTimer:F2}",
            force: true);

        // 로컬 플레이어 기절 시 슬로우모션 연출
        TriggerStunSlowMotion();

    }

    private void DampenStunEntryVelocities(bool groundedPlopEntry = false)
    {
        var rootPlanarVelocityScale = groundedPlopEntry
            ? GroundedStunEntryRootPlanarVelocityScale
            : StunEntryRootPlanarVelocityScale;
        var rootPlanarSpeedCap = groundedPlopEntry
            ? GroundedStunEntryRootPlanarSpeedCap
            : StunEntryRootPlanarSpeedCap;
        var rootAngularVelocityScale = groundedPlopEntry
            ? GroundedStunEntryRootAngularVelocityScale
            : StunEntryRootAngularVelocityScale;
        var musclePlanarVelocityScale = groundedPlopEntry
            ? GroundedStunEntryMusclePlanarVelocityScale
            : StunEntryMusclePlanarVelocityScale;
        var musclePlanarSpeedCap = groundedPlopEntry
            ? GroundedStunEntryMusclePlanarSpeedCap
            : StunEntryMusclePlanarSpeedCap;
        var muscleAngularVelocityScale = groundedPlopEntry
            ? GroundedStunEntryMuscleAngularVelocityScale
            : StunEntryMuscleAngularVelocityScale;
        var rootVelocityBefore = rigidbody3D != null && !rigidbody3D.isKinematic
            ? rigidbody3D.velocity
            : Vector3.zero;
        var rootAngularBefore = rigidbody3D != null && !rigidbody3D.isKinematic
            ? rigidbody3D.angularVelocity
            : Vector3.zero;
        float maxMusclePlanarBefore = 0f;
        float maxMusclePlanarAfter = 0f;

        if (rigidbody3D != null && !rigidbody3D.isKinematic)
        {
            rigidbody3D.velocity = DampenStunEntryVelocity(
                rigidbody3D.velocity,
                rootPlanarVelocityScale,
                rootPlanarSpeedCap);
            rigidbody3D.angularVelocity *= rootAngularVelocityScale;
        }

        if (_puppetMaster == null || _puppetMaster.muscles == null)
            return;

        foreach (var muscle in _puppetMaster.muscles)
        {
            if (muscle.joint == null)
                continue;

            var rb = muscle.joint.GetComponent<Rigidbody>();
            if (rb == null || rb.isKinematic)
                continue;

            var planarBefore = new Vector3(rb.velocity.x, 0f, rb.velocity.z).magnitude;
            if (planarBefore > maxMusclePlanarBefore)
                maxMusclePlanarBefore = planarBefore;

            rb.velocity = DampenStunEntryVelocity(
                rb.velocity,
                musclePlanarVelocityScale,
                musclePlanarSpeedCap);
            rb.angularVelocity *= muscleAngularVelocityScale;

            var planarAfter = new Vector3(rb.velocity.x, 0f, rb.velocity.z).magnitude;
            if (planarAfter > maxMusclePlanarAfter)
                maxMusclePlanarAfter = planarAfter;
        }

        var rootVelocityAfter = rigidbody3D != null && !rigidbody3D.isKinematic
            ? rigidbody3D.velocity
            : Vector3.zero;
        var rootAngularAfter = rigidbody3D != null && !rigidbody3D.isKinematic
            ? rigidbody3D.angularVelocity
            : Vector3.zero;
        TraceStunVelocityClamp(
            "DampenStunEntryVelocities",
            rootVelocityBefore,
            rootVelocityAfter,
            rootAngularBefore,
            rootAngularAfter,
            maxMusclePlanarBefore,
            maxMusclePlanarAfter);
    }

    private static Vector3 DampenStunEntryVelocity(Vector3 velocity, float planarScale, float planarSpeedCap)
    {
        var planarVelocity = new Vector3(velocity.x, 0f, velocity.z) * planarScale;
        planarVelocity = Vector3.ClampMagnitude(planarVelocity, planarSpeedCap);

        velocity.x = planarVelocity.x;
        velocity.z = planarVelocity.z;
        velocity.y = Mathf.Clamp(velocity.y * 0.5f, -2f, 1.2f);
        return velocity;
    }

    private static Vector3 ApplyAngularDamping(Vector3 angularVelocity, float dampingRate, float dt)
    {
        if (dampingRate <= 0f || dt <= 0f || angularVelocity.sqrMagnitude <= 0.000001f)
            return angularVelocity;

        var dampingFactor = Mathf.Exp(-dampingRate * dt);
        return angularVelocity * dampingFactor;
    }

    private void ClampStunnedMotion(
        bool collapsePhase = false,
        bool beingCarried = false,
        bool draggedStunned = false,
        bool settledStunned = false)
    {
        var earlyCollapsePhase = collapsePhase && IsEarlyCollapsePhaseActive();
        var dt = Runner != null ? Runner.DeltaTime : Time.fixedDeltaTime;

        float rootPlanarSpeedCap, musclePlanarSpeedCap, rootAngularSpeedCap, muscleAngularSpeedCap;
        if (beingCarried)
        {
            // 운반 중인 기절 피해자: 손을 따라 끌려가야 하므로 캡을 크게 완화
            rootPlanarSpeedCap = CarriedStunnedRootPlanarSpeedCap;
            musclePlanarSpeedCap = CarriedStunnedMusclePlanarSpeedCap;
            rootAngularSpeedCap = CarriedStunnedRootAngularSpeedCap;
            muscleAngularSpeedCap = CarriedStunnedMuscleAngularSpeedCap;
        }
        else if (draggedStunned)
        {
            rootPlanarSpeedCap = DraggedStunnedRootPlanarSpeedCap;
            musclePlanarSpeedCap = DraggedStunnedMusclePlanarSpeedCap;
            rootAngularSpeedCap = DraggedStunnedRootAngularSpeedCap;
            muscleAngularSpeedCap = DraggedStunnedMuscleAngularSpeedCap;
        }
        else if (settledStunned)
        {
            rootPlanarSpeedCap = SettledStunnedRootPlanarSpeedCap;
            musclePlanarSpeedCap = SettledStunnedMusclePlanarSpeedCap;
            rootAngularSpeedCap = SettledStunnedRootAngularSpeedCap;
            muscleAngularSpeedCap = SettledStunnedMuscleAngularSpeedCap;
        }
        else if (earlyCollapsePhase)
        {
            rootPlanarSpeedCap = CollapseEarlyRootPlanarSpeedCap;
            musclePlanarSpeedCap = CollapseEarlyMusclePlanarSpeedCap;
            rootAngularSpeedCap = CollapseEarlyRootAngularSpeedCap;
            muscleAngularSpeedCap = CollapseEarlyMuscleAngularSpeedCap;
        }
        else if (collapsePhase)
        {
            rootPlanarSpeedCap = CollapseRootPlanarSpeedCap;
            musclePlanarSpeedCap = CollapseMusclePlanarSpeedCap;
            rootAngularSpeedCap = CollapseRootAngularSpeedCap;
            muscleAngularSpeedCap = CollapseMuscleAngularSpeedCap;
        }
        else
        {
            rootPlanarSpeedCap = StunnedRootPlanarSpeedCap;
            musclePlanarSpeedCap = StunnedMusclePlanarSpeedCap;
            rootAngularSpeedCap = StunnedRootAngularSpeedCap;
            muscleAngularSpeedCap = StunnedMuscleAngularSpeedCap;
        }

        var angularDampingRate = beingCarried
            ? CarriedStunnedAngularDampingRate
            : draggedStunned
                ? DraggedStunnedAngularDampingRate
                : settledStunned
                    ? SettledStunnedAngularDampingRate
                    : earlyCollapsePhase
                        ? CollapseEarlyAngularDampingRate
                        : collapsePhase
                            ? CollapseAngularDampingRate
                            : StunnedAngularDampingRate;

        var groundedPlanarDrag = 0f;
        if (!beingCarried && HasRecentStunnedGroundContact())
        {
            groundedPlanarDrag = draggedStunned
                ? DraggedStunnedGroundedPlanarDrag
                : settledStunned
                ? SettledStunnedGroundedPlanarDrag
                : earlyCollapsePhase
                ? CollapseEarlyGroundedPlanarDrag
                : collapsePhase
                    ? CollapseGroundedPlanarDrag
                    : StunnedGroundedPlanarDrag;
        }
        var rootVelocityBefore = rigidbody3D != null && !rigidbody3D.isKinematic
            ? rigidbody3D.velocity
            : Vector3.zero;
        var rootAngularBefore = rigidbody3D != null && !rigidbody3D.isKinematic
            ? rigidbody3D.angularVelocity
            : Vector3.zero;
        var maxMusclePlanarBefore = 0f;
        var maxMusclePlanarAfter = 0f;

        if (rigidbody3D != null && !rigidbody3D.isKinematic)
        {
            rigidbody3D.velocity = ClampStunnedVelocity(rigidbody3D.velocity, rootPlanarSpeedCap, beingCarried);
            if (groundedPlanarDrag > 0f)
                rigidbody3D.velocity = ApplyGroundedStunnedPlanarDrag(rigidbody3D.velocity, groundedPlanarDrag, dt);
            rigidbody3D.angularVelocity = Vector3.ClampMagnitude(
                rigidbody3D.angularVelocity,
                rootAngularSpeedCap);
            rigidbody3D.angularVelocity = ApplyAngularDamping(rigidbody3D.angularVelocity, angularDampingRate, dt);
        }

        var traceLabel = beingCarried ? "ClampStunnedMotion-BeingCarried"
            : draggedStunned ? "ClampStunnedMotion-DraggedStunned"
            : settledStunned ? "ClampStunnedMotion-SettledStunned"
            : collapsePhase ? "ClampStunnedMotion-Collapse"
            : "ClampStunnedMotion-Stunned";

        if (_puppetMaster == null || _puppetMaster.muscles == null)
        {
            TraceStunVelocityClamp(
                traceLabel,
                rootVelocityBefore,
                rigidbody3D != null && !rigidbody3D.isKinematic ? rigidbody3D.velocity : Vector3.zero,
                rootAngularBefore,
                rigidbody3D != null && !rigidbody3D.isKinematic ? rigidbody3D.angularVelocity : Vector3.zero,
                maxMusclePlanarBefore,
                maxMusclePlanarAfter);

            if (beingCarried)
            {
                TraceBeingCarriedClampSample(
                    rootVelocityBefore,
                    rigidbody3D != null && !rigidbody3D.isKinematic ? rigidbody3D.velocity : Vector3.zero,
                    rootPlanarSpeedCap,
                    musclePlanarSpeedCap);
            }
            return;
        }

        foreach (var muscle in _puppetMaster.muscles)
        {
            if (muscle.joint == null)
                continue;

            var rb = muscle.joint.GetComponent<Rigidbody>();
            if (rb == null || rb.isKinematic)
                continue;

            var planarBefore = new Vector3(rb.velocity.x, 0f, rb.velocity.z).magnitude;
            if (planarBefore > maxMusclePlanarBefore)
                maxMusclePlanarBefore = planarBefore;

            rb.velocity = ClampStunnedVelocity(rb.velocity, musclePlanarSpeedCap, beingCarried);
            if (groundedPlanarDrag > 0f)
                rb.velocity = ApplyGroundedStunnedPlanarDrag(rb.velocity, groundedPlanarDrag, dt);
            rb.angularVelocity = Vector3.ClampMagnitude(
                rb.angularVelocity,
                muscleAngularSpeedCap);
            rb.angularVelocity = ApplyAngularDamping(rb.angularVelocity, angularDampingRate, dt);

            var planarAfter = new Vector3(rb.velocity.x, 0f, rb.velocity.z).magnitude;
            if (planarAfter > maxMusclePlanarAfter)
                maxMusclePlanarAfter = planarAfter;
        }

        TraceStunVelocityClamp(
            traceLabel,
            rootVelocityBefore,
            rigidbody3D != null && !rigidbody3D.isKinematic ? rigidbody3D.velocity : Vector3.zero,
            rootAngularBefore,
            rigidbody3D != null && !rigidbody3D.isKinematic ? rigidbody3D.angularVelocity : Vector3.zero,
            maxMusclePlanarBefore,
            maxMusclePlanarAfter);

        if (beingCarried)
        {
            TraceBeingCarriedClampSample(
                rootVelocityBefore,
                rigidbody3D != null && !rigidbody3D.isKinematic ? rigidbody3D.velocity : Vector3.zero,
                rootPlanarSpeedCap,
                musclePlanarSpeedCap);
        }
    }

    private void TraceBeingCarriedClampSample(
        Vector3 rootVelocityBefore,
        Vector3 rootVelocityAfter,
        float rootPlanarSpeedCap,
        float musclePlanarSpeedCap)
    {
        var pelvisY = transform.position.y;
        if (_puppetMaster != null &&
            _puppetMaster.muscles != null &&
            _puppetMaster.muscles.Length > 0 &&
            _puppetMaster.muscles[0].joint != null)
        {
            pelvisY = _puppetMaster.muscles[0].joint.transform.position.y;
        }

        var rootPlanarBefore = new Vector2(rootVelocityBefore.x, rootVelocityBefore.z).magnitude;
        var rootPlanarAfter = new Vector2(rootVelocityAfter.x, rootVelocityAfter.z).magnitude;
        var rootToPelvisGap = Mathf.Abs(transform.position.y - pelvisY);
        var carriedVictimSettings = ResolveCarryModeSettings(SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.CarriedVictim);
        if (rootToPelvisGap <= carriedVictimSettings.rootSnapVerticalGap)
            return;

        TraceCarryDebugSample(
            "CarryAuthorityClampGap",
            $"rootY={rootVelocityBefore.y:F2}->{rootVelocityAfter.y:F2} rootPlanar={rootPlanarBefore:F2}->{rootPlanarAfter:F2} " +
            $"caps=root:{rootPlanarSpeedCap:F2}/muscle:{musclePlanarSpeedCap:F2}/up:{CarriedStunnedMaxUpwardSpeed:F2} " +
            $"rootPosY={transform.position.y:F2} pelvisY={pelvisY:F2} rootPelvisGap={rootToPelvisGap:F2}",
            rootToPelvisGap > CarryRootDebugGapThreshold);
    }

    private static Vector3 ClampStunnedVelocity(Vector3 velocity, float planarSpeedCap, bool beingCarried = false)
    {
        var planarVelocity = new Vector3(velocity.x, 0f, velocity.z);
        planarVelocity = Vector3.ClampMagnitude(planarVelocity, planarSpeedCap);

        velocity.x = planarVelocity.x;
        velocity.z = planarVelocity.z;

        if (beingCarried)
        {
            // 운반 중: 위로 끌려가야 하므로 양의 Y를 허용하되 상한만 설정
            velocity.y = Mathf.Clamp(velocity.y, -2f, CarriedStunnedMaxUpwardSpeed);
        }
        else
        {
            velocity.y = Mathf.Min(velocity.y, 0f);
        }

        return velocity;
    }

    private static Vector3 ApplyGroundedStunnedPlanarDrag(Vector3 velocity, float dragPerSecond, float dt)
    {
        if (dragPerSecond <= 0f || dt <= 0f)
            return velocity;

        var planarVelocity = new Vector3(velocity.x, 0f, velocity.z);
        planarVelocity = Vector3.MoveTowards(planarVelocity, Vector3.zero, dragPerSecond * dt);
        velocity.x = planarVelocity.x;
        velocity.z = planarVelocity.z;
        return velocity;
    }

    private void EnsureRecoveryPoseReferences()
    {
        if (_recoveryPoseHips != null &&
            _recoveryPoseHead != null &&
            _recoveryPoseLeftArm != null &&
            _recoveryPoseRightArm != null)
        {
            return;
        }

        if (animator == null || !animator.isHuman)
            return;

        _recoveryPoseHips ??= animator.GetBoneTransform(HumanBodyBones.Hips);
        _recoveryPoseHead ??=
            animator.GetBoneTransform(HumanBodyBones.Head) ??
            animator.GetBoneTransform(HumanBodyBones.UpperChest) ??
            animator.GetBoneTransform(HumanBodyBones.Chest);
        _recoveryPoseLeftArm ??=
            animator.GetBoneTransform(HumanBodyBones.LeftUpperArm) ??
            animator.GetBoneTransform(HumanBodyBones.LeftShoulder);
        _recoveryPoseRightArm ??=
            animator.GetBoneTransform(HumanBodyBones.RightUpperArm) ??
            animator.GetBoneTransform(HumanBodyBones.RightShoulder);
    }

    private bool TryResolveRecoveryFacingVector(out Vector3 facing)
    {
        facing = transform.forward;
        EnsureRecoveryPoseReferences();

        if (_recoveryPoseHips == null ||
            _recoveryPoseHead == null ||
            _recoveryPoseLeftArm == null ||
            _recoveryPoseRightArm == null)
        {
            return false;
        }

        var bodyUp = _recoveryPoseHead.position - _recoveryPoseHips.position;
        var shoulderRight = _recoveryPoseRightArm.position - _recoveryPoseLeftArm.position;
        if (bodyUp.sqrMagnitude <= 0.0001f || shoulderRight.sqrMagnitude <= 0.0001f)
            return false;

        bodyUp.Normalize();
        shoulderRight.Normalize();

        var derivedForward = Vector3.Cross(shoulderRight, bodyUp);
        if (derivedForward.sqrMagnitude <= 0.0001f)
            return false;

        derivedForward.Normalize();
        if (!_recoveryPoseForwardSignResolved)
        {
            var referenceForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (referenceForward.sqrMagnitude <= 0.0001f && _targetRoot != null)
                referenceForward = Vector3.ProjectOnPlane(_targetRoot.forward, Vector3.up);

            if (referenceForward.sqrMagnitude > 0.0001f)
                _recoveryPoseForwardSign = Vector3.Dot(derivedForward, referenceForward.normalized) < 0f ? -1f : 1f;

            _recoveryPoseForwardSignResolved = true;
        }

        facing = derivedForward * _recoveryPoseForwardSign;
        return true;
    }

    private RecoveryAnimationVariant ResolveRecoveryAnimationVariant()
    {
        if (TryResolveRecoveryFacingVector(out var facing))
        {
            var facingUpDot = Vector3.Dot(facing.normalized, Vector3.up);
            if (facingUpDot >= 0.18f)
                return RecoveryAnimationVariant.Supine;

            if (facingUpDot <= -0.18f)
                return RecoveryAnimationVariant.Prone;
        }

        return RecoveryAnimationVariant.Supine;
    }

    private void ForceRecover()
    {
        if (Runner != null && Object != null && Object.IsValid && !HasStateAuthority)
            return;

        ForceReleaseInboundGrabRelations("ForceRecover");

        if (!IsBeingCarriedWhileStunned(this))
            CaptureCollapseAnchorPose(transform.position, _targetRoot != null ? _targetRoot.rotation : transform.rotation);

        Vector3 recoveryPosition;
        Quaternion recoveryRotation;
        if (ShouldUseCollapseAnchor())
        {
            recoveryPosition = _recoverAnchorPosition;
            recoveryRotation = _recoverAnchorRotation;
        }
        else if (!TryResolveRecoveryTransform(out recoveryPosition, out recoveryRotation))
        {
            recoveryPosition = transform.position;
            recoveryRotation = transform.rotation;
        }

        var recoveryAnimationVariant = ResolveRecoveryAnimationVariant();
        SetRecoveryAnimationVariant(recoveryAnimationVariant);
        _pendingRecoveryStandUpPosition = recoveryPosition;
        _pendingRecoveryStandUpRotation = recoveryRotation;
        _hasPendingRecoveryStandUpHandoff = true;

        _localMoveSpeed = 0f;
        _localPresentationLocomotionState = PresentationLocomotionState.Idle;
        if (Runner != null && Object != null && Object.IsValid)
        {
            NetworkedMoveSpeed = 0f;
            NetworkedLocomotionState = (byte)PresentationLocomotionState.Idle;
            NetworkedIsSprinting = false;
        }

        // ── 1) 물리 안정화: 잔여 속도 제거 ──
        DampenAllPhysicsBoneVelocities();

        // ── 2) 기립 정렬: 캐릭터를 월드 업 방향으로 세움 ──

        // ── 2.5) 안전 위치 텔레포트: 바닥 침투 방지 ──

        // ── 3) 스프링을 0으로 시작 → stabilization 단계에서 점진적으로 복원 ──
        if (!ShouldDisablePhysicsAnimationSync && mainJoint != null)
        {
            var jd = mainJoint.slerpDrive;
            jd.positionSpring = 0f;
            mainJoint.slerpDrive = jd;
        }

        // 각 관절도 스프링 0으로 시작 — TickRecoverStabilization에서 점진 복원
        if (!ShouldDisablePhysicsAnimationSync)
        {
            for (int i = 0; i < syncPhysicsObjects.Length; i++)
                syncPhysicsObjects[i].SetSpringLerp(0f);
        }

        _isActiveRagdoll = true;
        RestorePuppetMasterMappingAfterAerialKick();
        _isAerialKickMomentumActive = false;
        _aerialKickMissPenaltyPending = false;
        _aerialKickSpringRestoreTimer = 0f;
        RestoreRecoveryStunShield();
        SetGroggyRemaining(0f);
        SetStunComboWindowRemaining(0f);
        SetRecentStunHitCount(0);
        SetStunHitImmunityRemaining(Mathf.Max(GetStunHitImmunityRemaining(), ResolveConfiguredStunRehitImmunity()));
        SetNoStaggerRemaining(Mathf.Max(GetNoStaggerRemaining(), ResolveConfiguredNoStaggerWindow()));
        ClearPunchHitDetectionWindow();
        ClearKickHitDetectionWindow();
        ClearHeadbuttHitDetectionWindow();
        ClearAerialKickHitDetectionWindow();
        _isLeftGrabActive = false;
        _isRightGrabActive = false;
        _isGrabActive = false;

        // 2-phase: stabilization → recovering
        _isRecoverStabilizing = true;
        _recoverStabilizeTimer = RECOVER_STABILIZE_DURATION;
        _isRecovering = true;
        _recoveringTimer = RECOVERING_DURATION;
        _stunCollapseTimer = 0f;
        _stunnedFloorSettleTimer = 0f;
        _stunnedGroundContactTimer = 0f;

        SetStunTimeRemaining(0f);
        SetAccumulatedStun(0f);
        ResetDownedHitRecoveryState();

        // 회복 시 잡힌 상태가 유지 중이면 grab spring 적용
        if (IsGrabbedByOther)
            ApplyGrabbedJointState(true);

        SetLocalPhysicalPhase(PhysicalPhase.Recovering, Mathf.Max(_localInstability, 0.45f), false);
        SyncRootToPhysicsBody(forceImmediate: true);
        ArmStunDiagnosticsWindow("ForceRecover", $"variant={recoveryAnimationVariant} grabbedByOther={(IsGrabbedByOther ? 1 : 0)}");
        ArmStunForceDiagnostics("ForceRecover");
        SynchronizeStunPresentationPhase();
        TraceStunDiagnosticSnapshot(
            "ForceRecover-Configured",
            $"variant={recoveryAnimationVariant} stabilizeTimer={_recoverStabilizeTimer:F2} recoveringTimer={_recoveringTimer:F2}",
            force: true);

        // ── 4) 기립 보조: 약한 위쪽 충격량으로 일어나는 느낌 ──
        // 회복 비주얼: 물리 타겟 스켈레톤 숨기고 애니메이션 비주얼 복원

        // 호스트: PuppetMaster.Map()이 target skeleton을 덮어쓰지 않도록 즉시 래치 활성화.
        // LateUpdate의 SynchronizePhysicsPresentationState() 전환 감지를 기다리지 않고
        // ForceRecover 시점에 바로 mappingWeight=0을 적용한다.

    }

    // ─── 회복 안정화 헬퍼 ───

    /// <summary>
    /// 모든 물리 뼈의 잔여 속도/각속도를 대폭 감쇠.
    /// 기절 중 축적된 충돌/관성을 제거해서 spring 복원 시 떨림을 방지.
    /// </summary>
    private void CompleteRecoveryStandUpHandoff()
    {
        if (!_hasPendingRecoveryStandUpHandoff)
            return;

        _hasPendingRecoveryStandUpHandoff = false;

        AlignCharacterUpright(_pendingRecoveryStandUpRotation);
        TeleportToSafeStandUpPosition(_pendingRecoveryStandUpPosition, _pendingRecoveryStandUpRotation);
        DampenAllPhysicsBoneVelocities();
        SyncRootToPhysicsBody();
        QueueRecoveryAnimationForVisuals();
        RaiseAnimationEvent(AnimationEventType.StunRecover, H_StunRecover);
        SetStunVisualMode(false);
        ActivateAuthorityAnimatorVisualLatch();
    }

    private void DampenAllPhysicsBoneVelocities()
    {
        // 메인 rigidbody
        if (rigidbody3D != null && !rigidbody3D.isKinematic)
        {
            rigidbody3D.velocity = Vector3.zero;
            rigidbody3D.angularVelocity = Vector3.zero;
        }

        // PuppetMaster 물리 뼈
        if (_puppetMaster != null && _puppetMaster.muscles != null)
        {
            foreach (var muscle in _puppetMaster.muscles)
            {
                if (muscle.joint == null) continue;
                var rb = muscle.joint.GetComponent<Rigidbody>();
                if (rb != null && !rb.isKinematic)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }
        }
    }

    /// <summary>
    /// 캐릭터 루트(메인 rigidbody)를 월드 업 방향으로 정렬.
    /// 기절 중 옆으로 눕거나 뒤집힌 상태에서 회복 시 서있는 자세로 복원.
    /// yaw(수평 회전)는 유지하고 roll/pitch만 제거.
    /// </summary>
    private void AlignCharacterUpright(Quaternion referenceRotation)
    {
        if (rigidbody3D == null) return;

        var uprightRotation = Quaternion.Euler(0f, referenceRotation.eulerAngles.y, 0f);
        rigidbody3D.rotation = uprightRotation;
        rigidbody3D.angularVelocity = Vector3.zero;
        transform.rotation = uprightRotation;

        // PuppetMaster targetRoot도 정렬
        if (_targetRoot != null)
            _targetRoot.rotation = uprightRotation;

        SetPresentationVisualYaw(uprightRotation.eulerAngles.y);
    }

    /// <summary>
    /// 회복 시 pelvis 기준 지면 raycast로 안전한 기립 위치를 계산하여 텔레포트.
    /// 바닥에 반쯤 박힌 상태에서 recover가 시작되는 문제를 방지.
    /// </summary>
    private void TeleportToSafeStandUpPosition(Vector3 recoveryPosition, Quaternion recoveryRotation)
    {
        // pelvis(muscles[0]) 위치를 기준점으로 사용
        Vector3 basePos;
        if (_puppetMaster != null && _puppetMaster.muscles != null && _puppetMaster.muscles.Length > 0
            && _puppetMaster.muscles[0].joint != null)
        {
            basePos = _puppetMaster.muscles[0].joint.transform.position;
        }
        else if (rigidbody3D != null)
        {
            basePos = rigidbody3D.position;
        }
        else
        {
            return;
        }

        // 지면 감지: pelvis에서 아래로 raycast (자기 자신 제외)
        const float rayOriginOffset = 1.5f;
        const float rayDistance = 5f;
        const float standUpHeightOffset = 0.15f;
        const float pelvisHeightAboveGround = 0.85f;
        const float minColliderClearance = 0.04f;

        var recoveryAnchor = recoveryPosition;
        var rayOrigin = new Vector3(
            recoveryAnchor.x,
            Mathf.Max(basePos.y, recoveryAnchor.y) + rayOriginOffset,
            recoveryAnchor.z);
        Vector3 safePos;

        var hits = Physics.RaycastAll(rayOrigin, Vector3.down, rayDistance);
        var foundGround = false;
        var closestDist = float.MaxValue;
        var groundPoint = Vector3.zero;
        foreach (var h in hits)
        {
            // 자기 자신의 물리 뼈/콜라이더 제외
            if (h.transform.root == transform.root)
                continue;
            if (h.distance < closestDist)
            {
                closestDist = h.distance;
                groundPoint = h.point;
                foundGround = true;
            }
        }

        if (foundGround)
        {
            safePos = new Vector3(recoveryAnchor.x, groundPoint.y + standUpHeightOffset, recoveryAnchor.z);
        }
        else
        {
            // raycast 실패 시 현재 pelvis 위치에 약간의 y 보정만 적용
            safePos = new Vector3(
                recoveryAnchor.x,
                Mathf.Max(basePos.y, recoveryAnchor.y) + standUpHeightOffset,
                recoveryAnchor.z);
        }

        // pelvis는 엉덩이 높이에 배치, root는 pelvis를 따라가므로 같은 위치로
        var pelvisPos = new Vector3(safePos.x, safePos.y + pelvisHeightAboveGround, safePos.z);
        var lowestColliderY = basePos.y - pelvisHeightAboveGround;
        if (!TryGetCharacterLowestColliderY(out lowestColliderY))
            lowestColliderY = basePos.y - pelvisHeightAboveGround;

        var desiredLowestColliderY = foundGround
            ? groundPoint.y + minColliderClearance
            : safePos.y;
        var shiftY = Mathf.Max(pelvisPos.y - basePos.y, desiredLowestColliderY - lowestColliderY);
        var delta = new Vector3(safePos.x - basePos.x, shiftY, safePos.z - basePos.z);

        // root/rigidbody/targetRoot → pelvis 높이로 재배치 (SyncRootToPhysicsBody가 root=pelvis 기준)
        TranslateRecoveringPhysicsBodies(delta, true);
        _recoverAnchorPosition = safePos;
        _recoverAnchorRotation = Quaternion.Euler(0f, recoveryRotation.eulerAngles.y, 0f);
        _hasRecoverAnchorPose = true;
        _recoverMinColliderY = desiredLowestColliderY;

        // pelvis 물리 뼈도 같은 엉덩이 높이로
        ArmStunForceDiagnostics(
            "TeleportToSafeStandUpPosition",
            $"delta={FormatStunForceDiagnosticsVector(delta)} anchor={FormatStunForceDiagnosticsVector(_recoverAnchorPosition)} yaw={_recoverAnchorRotation.eulerAngles.y:F1} minColliderY={_recoverMinColliderY:F2}");
    }

    // ─── 맨손 펀치 히트 판정 ───

    // CSV PUNCH 수치 폴백 (CombatSettings에서 로드 실패 시)
    /// <summary>
    /// 양방향 지면 보정: 하체 콜라이더가 기준선 아래면 올리고, 너무 위면 내린다.
    /// 한 프레임당 최대 보정량을 제한하여 스프링 복원과의 충돌을 방지.
    /// </summary>
    private void MaintainRecoveringAboveGround()
    {
        if (!_isRecovering && !_isRecoverStabilizing)
            return;

        if (!float.IsFinite(_recoverMinColliderY))
            return;

        if (!TryGetCharacterLowestColliderY(out var lowestColliderY))
            return;

        var correction = _recoverMinColliderY - lowestColliderY;

        // 데드존: 아주 작은 오차는 무시
        if (Mathf.Abs(correction) <= 0.005f)
            return;

        // 프레임당 최대 보정량 제한 — 급격한 텔레포트 방지
        const float maxCorrectionPerFrame = 0.15f;
        correction = Mathf.Clamp(correction, -maxCorrectionPerFrame, maxCorrectionPerFrame);

        // 위로 올릴 때만 velocity reset, 내릴 때는 자연스럽게
        var resetVel = correction > 0f;
        TranslateRecoveringPhysicsBodies(new Vector3(0f, correction, 0f), resetVel);
    }

    private void MaintainRecoveringHorizontalAnchor()
    {
        if (!ShouldUseCollapseAnchor())
            return;

        if (!TryGetRecoverReferencePosition(out var referencePosition))
            return;

        var correction = new Vector3(
            _recoverAnchorPosition.x - referencePosition.x,
            0f,
            _recoverAnchorPosition.z - referencePosition.z);

        if (correction.sqrMagnitude <= 0.0004f)
            return;

        const float maxCorrectionPerFrame = 0.08f;
        correction = Vector3.ClampMagnitude(correction, maxCorrectionPerFrame);
        TranslateRecoveringPhysicsBodies(correction, true);
    }

    private void MaintainRecoveringUprightRotation()
    {
        if (!ShouldUseCollapseAnchor())
            return;

        if (rigidbody3D != null && !rigidbody3D.isKinematic)
        {
            rigidbody3D.rotation = _recoverAnchorRotation;
            rigidbody3D.angularVelocity = Vector3.zero;
        }

        transform.rotation = _recoverAnchorRotation;
        if (_targetRoot != null)
            _targetRoot.rotation = _recoverAnchorRotation;

        SetPresentationVisualYaw(_recoverAnchorRotation.eulerAngles.y);
    }

    // 지면 보정 기준으로 사용할 muscle 인덱스 (Hips, 양 다리)
    // PuppetMaster 기본 매핑: 0=Hips, 1=Spine~, 하체는 보통 뒤쪽 인덱스.
    // 정확한 인덱스 대신 muscle 이름에 Leg/Foot/Hips가 포함된 것만 사용.
    private static readonly string[] _lowerBodyBoneNames = { "Hips", "UpperLeg", "LowerLeg", "Foot", "Toes" };

    private bool TryGetCharacterLowestColliderY(out float lowestY)
    {
        lowestY = float.PositiveInfinity;

        // root rigidbody 자체의 collider
        if (rigidbody3D != null)
        {
            var rootCol = rigidbody3D.GetComponent<Collider>();
            if (rootCol != null && rootCol.enabled && !rootCol.isTrigger)
            {
                var minY = rootCol.bounds.min.y;
                if (float.IsFinite(minY) && minY < lowestY)
                    lowestY = minY;
            }
        }

        // PuppetMaster muscle 중 하체(Hips/Leg/Foot)만 탐색
        if (_puppetMaster != null && _puppetMaster.muscles != null)
        {
            foreach (var muscle in _puppetMaster.muscles)
            {
                if (muscle.joint == null) continue;
                var boneName = muscle.joint.name;
                var isLowerBody = false;
                for (var i = 0; i < _lowerBodyBoneNames.Length; i++)
                {
                    if (boneName.IndexOf(_lowerBodyBoneNames[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        isLowerBody = true;
                        break;
                    }
                }
                if (!isLowerBody) continue;

                var col = muscle.joint.GetComponent<Collider>();
                if (col == null || !col.enabled || col.isTrigger) continue;
                var minY = col.bounds.min.y;
                if (float.IsFinite(minY) && minY < lowestY)
                    lowestY = minY;
            }
        }

        return !float.IsPositiveInfinity(lowestY);
    }

    private bool TryGetRecoverReferencePosition(out Vector3 referencePosition)
    {
        if (_puppetMaster != null && _puppetMaster.muscles != null && _puppetMaster.muscles.Length > 0)
        {
            var pelvisMuscle = _puppetMaster.muscles[0];
            if (pelvisMuscle.joint != null)
            {
                referencePosition = pelvisMuscle.joint.transform.position;
                return true;
            }
        }

        if (rigidbody3D != null)
        {
            referencePosition = rigidbody3D.position;
            return true;
        }

        referencePosition = Vector3.zero;
        return false;
    }

    private void TranslateRecoveringPhysicsBodies(Vector3 delta, bool resetVelocities)
    {
        if (delta.sqrMagnitude <= 0.000001f)
            return;

        var rootBefore = transform.position;
        var bodyBefore = rigidbody3D != null ? rigidbody3D.position : rootBefore;
        var pelvisBefore = ResolveStartupLaunchPelvisPosition(out _);
        var visualRoot = GetPresentationRootTransform();
        var visualBefore = visualRoot != null ? visualRoot.position : rootBefore;
        var nextRootPosition = transform.position + delta;
        if (rigidbody3D != null && !rigidbody3D.isKinematic)
        {
            rigidbody3D.position = nextRootPosition;
            if (resetVelocities)
            {
                rigidbody3D.velocity = Vector3.zero;
                rigidbody3D.angularVelocity = Vector3.zero;
            }
        }

        transform.position = nextRootPosition;
        var rootAfter = transform.position;
        var bodyAfter = rigidbody3D != null ? rigidbody3D.position : rootAfter;
        var pelvisAfter = ResolveStartupLaunchPelvisPosition(out _);
        var visualAfter = visualRoot != null ? visualRoot.position : rootAfter;
        TraceStunTransformWriter(
            "Writer.TranslateRecoveringPhysicsBodies",
            rootBefore,
            rootAfter,
            bodyBefore,
            bodyAfter,
            pelvisBefore,
            pelvisAfter,
            visualBefore,
            visualAfter,
            $"resetVel={(resetVelocities ? 1 : 0)} delta=({delta.x:F2},{delta.y:F2},{delta.z:F2})",
            force: Mathf.Abs(delta.y) > 0.15f);

        if (_puppetMaster == null || _puppetMaster.muscles == null)
            return;

        foreach (var muscle in _puppetMaster.muscles)
        {
            if (muscle.joint == null)
                continue;

            var rb = muscle.joint.GetComponent<Rigidbody>();
            if (rb == null || rb.isKinematic || rb == rigidbody3D)
                continue;

            rb.position += delta;
            if (resetVelocities)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
    }

    private const string PunchCombatStatId = "PUNCH";
    private const float FallbackPunchHealthDamage = 3f;
    private const float FallbackPunchStunDamage = 12f;
    private const float FallbackPunchKnockbackForce = 10f;
    private const int FallbackPunchHitCountToStun = 3;
    private const float PunchHitRadius = 0.38f;
    private const float PunchHitForwardOffset = 0.18f;
    private const float PunchActiveWindowSeconds = 0.10f;
    private const float PunchFallbackReach = 0.8f;
    private const float PunchCooldownScale = 1.3157895f;
    private const int PunchHitBufferSize = 16;
    private int _punchCooldownUntilTick;
    private int _activePunchWindowEndTick = -1;
    private bool _activePunchIsLeft;
    private bool _activePunchHasPreviousSample;
    private float _activePunchHealthDamage;
    private float _activePunchStunDamage;
    private float _activePunchKnockbackForce;
    private int _activePunchHitCountToStun;
    private float _activePunchGroggyVulnerabilityMultiplier;
    private float _activePunchAttackerSpeed;
    private Vector3 _activePunchPreviousSamplePosition;
    private readonly Collider[] _punchHitResults = new Collider[PunchHitBufferSize];

    private const string KickCombatStatId = "KICK";
    private const float FallbackKickHealthDamage = 4f;
    private const float FallbackKickStunDamage = 14f;
    private const float FallbackKickKnockbackForce = 12f;
    private const int FallbackKickHitCountToStun = 2;
    private const float KickHitRadius = 0.42f;
    private const float KickHitForwardOffset = 0.14f;
    private const float KickActiveWindowSeconds = 0.12f;
    private const float KickFallbackReach = 0.95f;
    private const int KickHitBufferSize = 16;
    private int _kickCooldownUntilTick;
    private int _activeKickWindowEndTick = -1;
    private bool _activeKickIsLeft;
    private bool _activeKickHasPreviousSample;
    private float _activeKickHealthDamage;
    private float _activeKickStunDamage;
    private float _activeKickKnockbackForce;
    private int _activeKickHitCountToStun;
    private float _activeKickGroggyVulnerabilityMultiplier;
    private float _activeKickAttackerSpeed;
    private Vector3 _activeKickPreviousSamplePosition;
    private readonly Collider[] _kickHitResults = new Collider[KickHitBufferSize];

    private const string HeadbuttCombatStatId = "HEADBUTT";
    private const float FallbackHeadbuttHealthDamage = 5f;
    private const float FallbackHeadbuttStunDamage = 20f;
    private const float FallbackHeadbuttKnockbackForce = 6f;
    private const int FallbackHeadbuttHitCountToStun = 2;
    private const float FallbackHeadbuttSelfStunChance = 0.15f;
    private const float HeadbuttHitRadius = 0.34f;
    private const float HeadbuttHitForwardOffset = 0.12f;
    private const float HeadbuttHitVerticalOffset = 0.03f;
    private const float HeadbuttActiveWindowSeconds = 0.14f;
    private const float HeadbuttFallbackReach = 0.7f;
    private const float HeadbuttFallbackHeight = 0.92f;
    private const float HeadbuttEnvironmentProbeRadius = 0.18f;
    private const float HeadbuttEnvironmentProbeDistance = 0.22f;
    private const float HeadbuttMinimumSweepDistance = 0.015f;
    private const float HeadbuttEnvironmentImpactAlignmentThreshold = 0.2f;
    private const float HeadbuttWallNormalThreshold = 0.25f;
    private const float HeadbuttFloorNormalThreshold = 0.65f;
    private const float HeadbuttSelfBounceImpulse = 1.85f;
    private const float HeadbuttFallbackWallSelfStun = 2.5f;
    private const float HeadbuttFallbackFloorSelfStun = 1.5f;
    private const int HeadbuttHitBufferSize = 16;
    private const int HeadbuttEnvironmentHitBufferSize = 8;
    private int _headbuttCooldownUntilTick;
    private int _activeHeadbuttWindowEndTick = -1;
    private bool _activeHeadbuttHasPreviousSample;
    private float _activeHeadbuttHealthDamage;
    private float _activeHeadbuttStunDamage;
    private float _activeHeadbuttKnockbackForce;
    private int _activeHeadbuttHitCountToStun;
    private float _activeHeadbuttGroggyVulnerabilityMultiplier;
    private float _activeHeadbuttSelfStunChance;
    private float _activeHeadbuttAttackerSpeed;
    private Vector3 _activeHeadbuttPreviousSamplePosition;
    private readonly Collider[] _headbuttHitResults = new Collider[HeadbuttHitBufferSize];
    private readonly RaycastHit[] _headbuttEnvironmentHits = new RaycastHit[HeadbuttEnvironmentHitBufferSize];

    private const string AerialKickCombatStatId = "JET_KICK";
    private const float FallbackAerialKickHealthDamage = 15f;
    private const float FallbackAerialKickStunDamage = 50f;
    private const float FallbackAerialKickKnockbackForce = 18f;
    private const float FallbackAerialKickSelfStunDuration = 0.4f;
    private const float FallbackAerialKickVelocityDamageMultiplier = 1.25f;
    private const float FallbackAerialKickAirborneVulnerabilityMultiplier = 1.5f;
    private const int FallbackAerialKickHitCountToStun = 1;
    private const float AerialKickHitRadius = 0.56f;
    private const float AerialKickActiveWindowSeconds = 0.42f;
    private const float AerialKickFallbackCooldown = 1.25f;
    private const float AerialKickForwardReachMin = 0.72f;
    private const float AerialKickForwardReachMax = 1.35f;
    private const float AerialKickHeightMin = 0.32f;
    private const float AerialKickHeightMax = 0.62f;
    private const float AerialKickSpeedForMaxBonus = 10.5f;
    private const float AerialKickForwardBoostSpeed = 8f;
    private const float AerialKickUpwardBoost = 0.18f;
    private const float AerialKickVelocityPreserveScale = 1.0f;
    private const float AerialKickMinimumAirborneTriggerTime = 0.10f;
    private const float AerialKickGroundedGraceDuration = 0.10f;
    private const float AerialKickMomentumAirborneSpeedScale = 1.06f;
    private const float AerialKickMomentumGroundedSpeedScale = 0.90f;
    private const float AerialKickMomentumMinPlanarSpeed = 7.4f;
    private const float AerialKickMomentumPlanarAcceleration = 40f;
    private const float AerialKickFlightMaxDuration = 1.00f;
    private const float AerialKickFlightTimeoutExtensionDuration = 0.20f;
    private const float AerialKickSpringLerpDuringKick = 0.36f;
    private const float AerialKickBallisticFallSpringLerp = 0.18f;
    private const float AerialKickSpringRestoreDuration = 0.18f;
    private const float AerialKickRisingVerticalVelocityThreshold = 0.05f;
    private const float AerialKickLandingProbeRadius = 0.06f;
    private const float AerialKickLandingProbeDistance = 0.18f;
    private const float AerialKickStartFootProbeRadius = 0.05f;
    private const float AerialKickStartFootProbeDistance = 0.10f;
    private const float AerialKickLandingFootProbeRadius = 0.06f;
    private const float AerialKickLandingFootProbeDistance = 0.14f;
    private const float AerialKickFootProbeOriginLift = 0.04f;
    private const float AerialKickLandingConfirmDuration = 0.08f;
    private const float AerialKickLandingMinAirTime = 0.14f;
    private const float AerialKickLandingMaxVerticalSpeed = 0.35f;
    private const float AerialKickExtraFallAcceleration = 12f;
    private const float AerialKickExtraFallAccelerationDelay = 0.10f;
    private const float AerialKickGroundContactNormalThreshold = 0.45f;
    private const float AerialKickGroundContactMaxHeightOffset = 0.38f;
    private const float AerialKickGroundContactFootHorizontalSlack = 0.22f;
    private const float AerialKickGroundContactFootHeightSlack = 0.16f;
    private const float AerialKickGroundContactMemory = 0.12f;
    private Vector3 _activeAerialKickForwardDirection;
    private bool _isAerialKickMomentumActive;
    private bool _activeAerialKickBallisticFallActive;
    private float _aerialKickSpringRestoreTimer;
    private float _aerialKickCurrentSpringLerp = 1f;
    private float _aerialKickSpringRestoreStartLerp = 1f;
    private bool _aerialKickMappingSuppressed;
    private float _aerialKickSavedMappingWeight;
    private const int AerialKickHitBufferSize = 16;
    private int _aerialKickCooldownUntilTick;
    private int _activeAerialKickWindowEndTick = -1;
    private bool _activeAerialKickHasPreviousSample;
    private bool _activeAerialKickHasHit;
    private float _activeAerialKickHealthDamage;
    private float _activeAerialKickStunDamage;
    private float _activeAerialKickKnockbackForce;
    private int _activeAerialKickHitCountToStun;
    private float _activeAerialKickGroggyVulnerabilityMultiplier;
    private float _activeAerialKickSelfStunDuration;
    private float _activeAerialKickSelfStunChance;
    private float _activeAerialKickVelocityDamageMultiplier;
    private float _activeAerialKickAirborneVulnerabilityMultiplier;
    private float _activeAerialKickAttackerSpeed;
    private float _activeAerialKickStartSpeed;
    private float _activeAerialKickTargetPlanarSpeed;
    private float _activeAerialKickGroundedGraceTimer;
    private bool _activeAerialKickHasLeftGround;
    private float _activeAerialKickFlightForceReleaseTime = float.NegativeInfinity;
    private float _activeAerialKickStartedAt = float.NegativeInfinity;
    private float _activeAerialKickLandingConfirmTimer;
    private bool _activeAerialKickRawGrounded;
    private bool _activeAerialKickNearGround;
    private float _activeAerialKickLastGroundContactTime = float.NegativeInfinity;
    private float _nextAerialKickDiagnosticsSampleTime = float.NegativeInfinity;
    private Vector3 _activeAerialKickPreviousSamplePosition;
    private readonly Collider[] _aerialKickHitResults = new Collider[AerialKickHitBufferSize];
    private bool _aerialKickMissPenaltyPending;
    private float _lastGroundedTime = float.NegativeInfinity;

    // ─── 히트스탑 상태 ───
    private float _hitStopEndTime;
    private Vector3 _hitStopSavedVelocity;
    private Vector3 _hitStopSavedAngularVelocity;
    private const float HIT_STOP_DURATION = 0.05f;
    private const float HIT_STOP_VELOCITY_SCALE = 0.05f;

    // ─── 피격 후 짧은 불안정(hit recoil) ───
    private float _hitRecoilTimer;
    private const float HIT_RECOIL_DURATION = 0.15f;
    private const float HIT_RECOIL_INSTABILITY_FLOOR = 0.42f;
    private const float HIT_RECOIL_ACCEL_SCALE = 0.55f;
    private const float HIT_RECOIL_BRAKE_SCALE = 0.55f;
    private const float HIT_RECOIL_GROUND_STICK_SCALE = 0.50f;

    private bool IsInHitRecoil => _hitRecoilTimer > 0f;
    private float _hitInstabilityBoost;

    // ─── Hit Flinch: 피격 시 pinWeight 일시 드롭 → 래그돌 흔들림 ───
    private float _hitFlinchTimer;
    private float _hitFlinchDuration;
    private float _hitFlinchSavedPinWeight;
    private float _hitFlinchDroppedPinWeight;
    private bool _hitFlinchActive;
    private const float HIT_FLINCH_DURATION_MIN = 0.12f;
    private const float HIT_FLINCH_DURATION_MAX = 0.22f;
    private const float HIT_FLINCH_PIN_DROP_LIGHT = 0.55f;   // 약타: pinWeight를 55%까지만 떨어뜨림
    private const float HIT_FLINCH_PIN_DROP_HEAVY = 0.20f;   // 강타: pinWeight를 20%까지 떨어뜨림
    private const float HIT_FLINCH_SPRING_SCALE = 0.35f;     // mainJoint 스프링도 이 비율로 약화

    private void TickHitRecoil(float dt)
    {
        if (_hitRecoilTimer > 0f)
            _hitRecoilTimer = Mathf.Max(0f, _hitRecoilTimer - dt);
    }

    private void TickHitFlinch(float dt)
    {
        if (!_hitFlinchActive)
            return;

        // 스턴 진입 시 flinch 즉시 종료 (스턴이 pinWeight를 직접 제어)
        if (!_isActiveRagdoll)
        {
            EndHitFlinch();
            return;
        }

        _hitFlinchTimer -= dt;
        if (_hitFlinchTimer <= 0f)
        {
            EndHitFlinch();
            return;
        }

        // 복원 진행도: 0(피격 직후) → 1(완전 복원)
        var recovery = 1f - Mathf.Clamp01(_hitFlinchTimer / _hitFlinchDuration);
        // ease-in: 초반에 천천히 복원 → 후반에 빠르게 복원 (흔들림 유지감)
        var easedRecovery = recovery * recovery;

        if (_puppetMaster != null)
        {
            var targetPin = Mathf.Lerp(_hitFlinchDroppedPinWeight, _hitFlinchSavedPinWeight, easedRecovery);
            _puppetMaster.pinWeight = targetPin;
        }

        // mainJoint 스프링도 연동 약화
        if (!ShouldDisablePhysicsAnimationSync && mainJoint != null)
        {
            var springScale = Mathf.Lerp(HIT_FLINCH_SPRING_SCALE, 1f, easedRecovery);
            var jd = mainJoint.slerpDrive;
            jd.positionSpring = _startSlerpPositionSpring * springScale;
            mainJoint.slerpDrive = jd;
        }
    }

    private void ArmHitFlinch(float impactMagnitude)
    {
        if (!_isActiveRagdoll || _puppetMaster == null)
            return;

        var normalizedImpact = NormalizePunchImpact(Mathf.Max(impactMagnitude, 0f));
        var dropTarget = Mathf.Lerp(HIT_FLINCH_PIN_DROP_LIGHT, HIT_FLINCH_PIN_DROP_HEAVY, normalizedImpact);
        var duration = Mathf.Lerp(HIT_FLINCH_DURATION_MIN, HIT_FLINCH_DURATION_MAX, normalizedImpact);

        // 이미 flinch 중이면 더 강한 드롭만 적용
        if (_hitFlinchActive)
        {
            if (dropTarget < _hitFlinchDroppedPinWeight)
            {
                _hitFlinchDroppedPinWeight = dropTarget;
                _hitFlinchTimer = duration;
                _hitFlinchDuration = duration;
                _puppetMaster.pinWeight = dropTarget;
            }
            return;
        }

        _hitFlinchSavedPinWeight = _puppetMaster.pinWeight;
        _hitFlinchDroppedPinWeight = _hitFlinchSavedPinWeight * dropTarget;
        _hitFlinchDuration = duration;
        _hitFlinchTimer = duration;
        _hitFlinchActive = true;

        // 즉시 pinWeight 드롭
        _puppetMaster.pinWeight = _hitFlinchDroppedPinWeight;
    }

    internal void ArmDirectionalCombatFlinch(Vector3 hitPoint, float impactMagnitude)
    {
        if (_bodyPartPhysicsManager == null || !_isActiveRagdoll)
            return;

        var localOffset = ResolveImpactLocalOffset(hitPoint);
        var duration = Mathf.Lerp(0.08f, 0.15f, NormalizePunchImpact(impactMagnitude));
        _bodyPartPhysicsManager.ArmCombatFlinch(localOffset, impactMagnitude, duration);
    }

    private void EndHitFlinch()
    {
        if (!_hitFlinchActive)
            return;

        _hitFlinchActive = false;
        _hitFlinchTimer = 0f;

        // pinWeight 복원
        if (_puppetMaster != null && _isActiveRagdoll)
            _puppetMaster.pinWeight = _hitFlinchSavedPinWeight;

        // mainJoint 스프링 복원
        if (!ShouldDisablePhysicsAnimationSync && mainJoint != null)
        {
            var jd = mainJoint.slerpDrive;
            jd.positionSpring = _startSlerpPositionSpring;
            mainJoint.slerpDrive = jd;
        }
    }

    // ─── 기절 슬로우모션 (로컬 전용) ───
    private bool _stunSlowMotionActive;
    private float _stunSlowMotionHoldEnd;
    private float _stunSlowMotionRampEnd;
    private float _localSlowMotionHoldEnd;   // unscaledTime 기준
    private float _localSlowMotionRampEnd;   // unscaledTime 기준
    private bool _knockoutConfirmSlowMotionActive;
    private float _knockoutConfirmSlowMotionHoldEnd;
    private float _knockoutConfirmSlowMotionRampEnd;
    private const float SLOWMO_BASE_FIXED_DELTA_TIME = 0.02f;
    private const float STUN_SLOWMO_SCALE = 0.15f;        // 85% 감속
    private const float STUN_SLOWMO_HOLD_DURATION = 0.25f; // 최저 유지 시간 (realtime)
    private const float STUN_SLOWMO_RAMP_DURATION = 0.35f; // 복원 램프 시간 (realtime)
    private const float KNOCKOUT_CONFIRM_SLOWMO_SCALE = 0.58f;
    private const float KNOCKOUT_CONFIRM_SLOWMO_HOLD_DURATION = 0.06f;
    private const float KNOCKOUT_CONFIRM_SLOWMO_RAMP_DURATION = 0.12f;

    internal float GetConfiguredPunchCooldown()
    {
        var stat = CombatSettings.Instance?.GetAttackStat(PunchCombatStatId);
        if (stat.HasValue)
            return Mathf.Max(stat.Value.CooldownSec * PunchCooldownScale, PunchActiveWindowSeconds);

        return 0.50f;
    }

    internal float GetConfiguredKickCooldown()
    {
        var stat = CombatSettings.Instance?.GetAttackStat(KickCombatStatId);
        if (stat.HasValue)
            return Mathf.Max(stat.Value.CooldownSec, KickActiveWindowSeconds);

        return 0.45f;
    }

    internal float GetConfiguredHeadbuttCooldown()
    {
        var stat = CombatSettings.Instance?.GetAttackStat(HeadbuttCombatStatId);
        if (stat.HasValue)
            return Mathf.Max(stat.Value.CooldownSec, HeadbuttActiveWindowSeconds);

        return 0.60f;
    }

    internal float GetConfiguredAerialKickCooldown()
    {
        var stat = CombatSettings.Instance?.GetAttackStat(AerialKickCombatStatId);
        if (stat.HasValue)
            return Mathf.Max(stat.Value.CooldownSec, AerialKickActiveWindowSeconds);

        return AerialKickFallbackCooldown;
    }

    internal bool TryBeginPunchHitDetection(bool isLeft)
    {
        if (Runner != null && Object != null && Object.IsValid && !HasStateAuthority)
            return false;

        var stat = CombatSettings.Instance?.GetAttackStat(PunchCombatStatId);
        var cooldown = GetConfiguredPunchCooldown();
        var currentTick = ResolveCurrentSimulationTick();
        var tickRate = Runner != null ? (int)Runner.TickRate : Mathf.Max(1, Mathf.RoundToInt(1f / Time.fixedDeltaTime));
        var cooldownTicks = Mathf.Max(1, Mathf.RoundToInt(cooldown * tickRate));
        if (currentTick < _punchCooldownUntilTick)
            return false;

        _punchCooldownUntilTick = currentTick + cooldownTicks;
        _activePunchIsLeft = isLeft;
        _activePunchHasPreviousSample = false;
        _activePunchHealthDamage = stat.HasValue ? stat.Value.BaseDamage : FallbackPunchHealthDamage;
        _activePunchStunDamage = stat.HasValue ? stat.Value.StunDamage : FallbackPunchStunDamage;
        _activePunchKnockbackForce = stat.HasValue ? stat.Value.KnockbackForce : FallbackPunchKnockbackForce;
        _activePunchHitCountToStun = stat.HasValue ? Mathf.Max(1, stat.Value.HitCountToStun) : FallbackPunchHitCountToStun;
        _activePunchGroggyVulnerabilityMultiplier = stat.HasValue
            ? Mathf.Max(1f, stat.Value.GroggyVulnerabilityMultiplier)
            : 1f;
        _activePunchAttackerSpeed = rigidbody3D != null ? rigidbody3D.velocity.magnitude : 0f;
        _activePunchWindowEndTick = currentTick + Mathf.Max(1, Mathf.RoundToInt(PunchActiveWindowSeconds * tickRate));
        return true;
    }

    internal bool TryBeginKickHitDetection(bool isLeft)
    {
        if (Runner != null && Object != null && Object.IsValid && !HasStateAuthority)
            return false;

        var stat = CombatSettings.Instance?.GetAttackStat(KickCombatStatId);
        var cooldown = GetConfiguredKickCooldown();
        var currentTick = ResolveCurrentSimulationTick();
        var tickRate = Runner != null ? (int)Runner.TickRate : Mathf.Max(1, Mathf.RoundToInt(1f / Time.fixedDeltaTime));
        var cooldownTicks = Mathf.Max(1, Mathf.RoundToInt(cooldown * tickRate));
        if (currentTick < _kickCooldownUntilTick)
            return false;

        _kickCooldownUntilTick = currentTick + cooldownTicks;
        _activeKickIsLeft = isLeft;
        _activeKickHasPreviousSample = false;
        _activeKickHealthDamage = stat.HasValue ? stat.Value.BaseDamage : FallbackKickHealthDamage;
        _activeKickStunDamage = stat.HasValue ? stat.Value.StunDamage : FallbackKickStunDamage;
        _activeKickKnockbackForce = stat.HasValue ? stat.Value.KnockbackForce : FallbackKickKnockbackForce;
        _activeKickHitCountToStun = stat.HasValue ? Mathf.Max(1, stat.Value.HitCountToStun) : FallbackKickHitCountToStun;
        _activeKickGroggyVulnerabilityMultiplier = stat.HasValue
            ? Mathf.Max(1f, stat.Value.GroggyVulnerabilityMultiplier)
            : 1f;
        _activeKickAttackerSpeed = rigidbody3D != null ? rigidbody3D.velocity.magnitude : 0f;
        _activeKickWindowEndTick = currentTick + Mathf.Max(1, Mathf.RoundToInt(KickActiveWindowSeconds * tickRate));
        return true;
    }

    internal bool TryBeginHeadbuttHitDetection()
    {
        if (Runner != null && Object != null && Object.IsValid && !HasStateAuthority)
            return false;

        if (!_isActiveRagdoll || _isRecovering || _isRecoverStabilizing || GetIsDeadState())
            return false;

        var stat = CombatSettings.Instance?.GetAttackStat(HeadbuttCombatStatId);
        var cooldown = GetConfiguredHeadbuttCooldown();
        var currentTick = ResolveCurrentSimulationTick();
        var tickRate = Runner != null ? (int)Runner.TickRate : Mathf.Max(1, Mathf.RoundToInt(1f / Time.fixedDeltaTime));
        var cooldownTicks = Mathf.Max(1, Mathf.RoundToInt(cooldown * tickRate));
        if (currentTick < _headbuttCooldownUntilTick || _activeHeadbuttWindowEndTick >= 0)
            return false;

        _headbuttCooldownUntilTick = currentTick + cooldownTicks;
        _activeHeadbuttHasPreviousSample = false;
        _activeHeadbuttHealthDamage = stat.HasValue ? stat.Value.BaseDamage : FallbackHeadbuttHealthDamage;
        _activeHeadbuttStunDamage = stat.HasValue ? stat.Value.StunDamage : FallbackHeadbuttStunDamage;
        _activeHeadbuttKnockbackForce = stat.HasValue ? stat.Value.KnockbackForce : FallbackHeadbuttKnockbackForce;
        _activeHeadbuttHitCountToStun = stat.HasValue ? Mathf.Max(1, stat.Value.HitCountToStun) : FallbackHeadbuttHitCountToStun;
        _activeHeadbuttGroggyVulnerabilityMultiplier = stat.HasValue
            ? Mathf.Max(1f, stat.Value.GroggyVulnerabilityMultiplier)
            : 1f;
        _activeHeadbuttSelfStunChance = stat.HasValue
            ? Mathf.Clamp01(stat.Value.SelfStunChance)
            : FallbackHeadbuttSelfStunChance;
        _activeHeadbuttAttackerSpeed = rigidbody3D != null ? rigidbody3D.velocity.magnitude : 0f;
        _activeHeadbuttWindowEndTick = currentTick + Mathf.Max(1, Mathf.RoundToInt(HeadbuttActiveWindowSeconds * tickRate));
        return true;
    }

    internal bool TryBeginAerialKickHitDetection()
    {
        if (Runner != null && Object != null && Object.IsValid && !HasStateAuthority)
            return false;

        if (!_isActiveRagdoll || GetIsDeadState())
            return false;

        if (!ShouldAllowAerialKickAirborneStart(
                _isGrounded,
                Time.time - _lastGroundedTime,
                _coyoteTimeRemaining,
                AreFeetClearForAerialKickStart(),
                _isAerialKickMomentumActive || _activeAerialKickBallisticFallActive || _aerialKickSpringRestoreTimer > 0f))
        {
            return false;
        }

        var stat = CombatSettings.Instance?.GetAttackStat(AerialKickCombatStatId);
        var cooldown = GetConfiguredAerialKickCooldown();
        var currentTick = ResolveCurrentSimulationTick();
        var tickRate = Runner != null ? (int)Runner.TickRate : Mathf.Max(1, Mathf.RoundToInt(1f / Time.fixedDeltaTime));
        var cooldownTicks = Mathf.Max(1, Mathf.RoundToInt(cooldown * tickRate));
        if (currentTick < _aerialKickCooldownUntilTick || _activeAerialKickWindowEndTick >= 0)
            return false;

        _aerialKickCooldownUntilTick = currentTick + cooldownTicks;
        _activeAerialKickHasPreviousSample = false;
        _activeAerialKickHasHit = false;
        _activeAerialKickHealthDamage = stat.HasValue ? stat.Value.BaseDamage : FallbackAerialKickHealthDamage;
        _activeAerialKickStunDamage = stat.HasValue ? stat.Value.StunDamage : FallbackAerialKickStunDamage;
        _activeAerialKickKnockbackForce = stat.HasValue ? stat.Value.KnockbackForce : FallbackAerialKickKnockbackForce;
        _activeAerialKickHitCountToStun = stat.HasValue ? Mathf.Max(1, stat.Value.HitCountToStun) : FallbackAerialKickHitCountToStun;
        _activeAerialKickGroggyVulnerabilityMultiplier = stat.HasValue
            ? Mathf.Max(1f, stat.Value.GroggyVulnerabilityMultiplier)
            : 1f;
        _activeAerialKickSelfStunDuration = stat.HasValue ? stat.Value.SelfStunDuration : FallbackAerialKickSelfStunDuration;
        _activeAerialKickSelfStunChance = stat.HasValue ? Mathf.Clamp01(stat.Value.SelfStunChance) : 1f;
        _activeAerialKickVelocityDamageMultiplier = stat.HasValue
            ? Mathf.Max(0f, stat.Value.VelocityDamageMultiplier)
            : FallbackAerialKickVelocityDamageMultiplier;
        _activeAerialKickAirborneVulnerabilityMultiplier = stat.HasValue
            ? Mathf.Max(1f, stat.Value.AirborneVulnerabilityMultiplier)
            : FallbackAerialKickAirborneVulnerabilityMultiplier;
        _activeAerialKickAttackerSpeed = rigidbody3D != null ? rigidbody3D.velocity.magnitude : 0f;
        _activeAerialKickStartSpeed = _activeAerialKickAttackerSpeed;
        _activeAerialKickWindowEndTick = currentTick + Mathf.Max(1, Mathf.RoundToInt(AerialKickActiveWindowSeconds * tickRate));
        _activeAerialKickGroundedGraceTimer = AerialKickGroundedGraceDuration;
        _activeAerialKickLandingConfirmTimer = 0f;
        _activeAerialKickRawGrounded = false;
        _activeAerialKickNearGround = false;
        _activeAerialKickLastGroundContactTime = float.NegativeInfinity;
        _nextAerialKickDiagnosticsSampleTime = Time.time;

        ApplyAerialKickBurst();
        LogAerialKickDiagnostic("Start", $"windowEndTick={_activeAerialKickWindowEndTick} startSpeed={_activeAerialKickStartSpeed:F2}");
        return true;
    }

    private void ApplyAerialKickBurst()
    {
        if (rigidbody3D == null || rigidbody3D.isKinematic)
            return;

        var planarForward = Vector3.ProjectOnPlane(ResolvePunchForward(), Vector3.up);
        if (planarForward.sqrMagnitude < 0.0001f)
            planarForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (planarForward.sqrMagnitude < 0.0001f)
            planarForward = Vector3.forward;

        planarForward.Normalize();
        _activeAerialKickForwardDirection = planarForward;

        // 기존 velocity 보존 + 킥 방향 부스트 합산
        var currentVelocity = rigidbody3D.velocity;
        var upwardBoost = currentVelocity.y > AerialKickRisingVerticalVelocityThreshold
            ? 0f
            : AerialKickUpwardBoost;
        var kickVelocity = planarForward * AerialKickForwardBoostSpeed
                         + Vector3.up * upwardBoost;
        var finalVelocity = currentVelocity * AerialKickVelocityPreserveScale + kickVelocity;
        _activeAerialKickTargetPlanarSpeed = Mathf.Max(
            AerialKickMomentumMinPlanarSpeed,
            Vector3.ProjectOnPlane(finalVelocity, Vector3.up).magnitude);

        // root rigidbody에 velocity 직접 설정
        rigidbody3D.velocity = finalVelocity;

        // PuppetMaster muscle 전체에 동일 velocity 주입
        if (_puppetMaster != null && _puppetMaster.muscles != null)
        {
            foreach (var muscle in _puppetMaster.muscles)
            {
                if (muscle.joint == null) continue;
                var rb = muscle.joint.GetComponent<Rigidbody>();
                if (rb != null && !rb.isKinematic)
                    rb.velocity = finalVelocity;
            }
        }

        // 관절 spring 약화 — muscle이 momentum에 저항하지 않도록
        WeakenJointSpringsForAerialKick();
        // PuppetMaster Map()이 target skeleton 위치를 덮어쓰지 않도록 매핑 억제
        SuppressPuppetMasterMappingForAerialKick();
        _isAerialKickMomentumActive = true;
        _activeAerialKickBallisticFallActive = false;
        _aerialKickSpringRestoreTimer = 0f;
        _aerialKickSpringRestoreStartLerp = _aerialKickCurrentSpringLerp;
        _activeAerialKickHasLeftGround = !_isGrounded;
        _activeAerialKickStartedAt = Time.time;
        _activeAerialKickFlightForceReleaseTime = Time.time + AerialKickFlightMaxDuration;
        _activeAerialKickLandingConfirmTimer = 0f;
    }

    private void TickAerialKickMomentum(float dt)
    {
        if (!_isAerialKickMomentumActive || _aerialKickSpringRestoreTimer > 0f)
            return;

        if (rigidbody3D == null || rigidbody3D.isKinematic)
            return;

        if (!_activeAerialKickHasLeftGround && !_isGrounded)
            _activeAerialKickHasLeftGround = true;

        var currentVelocity = rigidbody3D.velocity;
        if (_activeAerialKickWindowEndTick < 0)
        {
            var hasLandedAfterLaunch = _activeAerialKickHasLeftGround && _isGrounded;
            if (hasLandedAfterLaunch)
            {
                BeginAerialKickSpringRestore("landing-confirmed");
                return;
            }

            // 지면을 떠나지 않은 채 히트 윈도우가 종료된 경우
            // (지상 근처에서 킥 → 즉시 적중 등)
            // hasLandedAfterLaunch가 항상 false여서 스프링 복원이 불가능하므로 즉시 복원한다.
            if (!_activeAerialKickHasLeftGround && _isGrounded)
            {
                BeginAerialKickSpringRestore("grounded-no-launch");
                return;
            }

            if (_activeAerialKickBallisticFallActive)
                return;

            if (ShouldEnterAerialKickBallisticFall(
                    _activeAerialKickHasLeftGround,
                    _isGrounded,
                    currentVelocity.y,
                    _activeAerialKickNearGround,
                    HasRecentAerialKickGroundContact()))
            {
                EnterAerialKickBallisticFall("descent-started");
                return;
            }

            if (Time.time >= _activeAerialKickFlightForceReleaseTime)
            {
                EnterAerialKickBallisticFall("flight-timeout");
                return;
            }
        }

        var forward = _activeAerialKickForwardDirection;
        if (forward.sqrMagnitude < 0.0001f)
            return;

        forward.Normalize();

        var planarVelocity = Vector3.ProjectOnPlane(currentVelocity, Vector3.up);
        var sustainScale = _isGrounded
            ? AerialKickMomentumGroundedSpeedScale
            : AerialKickMomentumAirborneSpeedScale;
        var minimumPlanarSpeed = _isGrounded
            ? AerialKickMomentumMinPlanarSpeed * 0.85f
            : AerialKickMomentumMinPlanarSpeed;
        var targetPlanarSpeed = Mathf.Max(minimumPlanarSpeed, _activeAerialKickTargetPlanarSpeed * sustainScale);
        var desiredPlanarVelocity = forward * targetPlanarSpeed;
        var nextPlanarVelocity = Vector3.MoveTowards(
            planarVelocity,
            desiredPlanarVelocity,
            AerialKickMomentumPlanarAcceleration * dt);

        rigidbody3D.velocity = new Vector3(nextPlanarVelocity.x, currentVelocity.y, nextPlanarVelocity.z);

        if (ShouldLogAerialKickDiagnostics() && Time.time >= _nextAerialKickDiagnosticsSampleTime)
        {
            _nextAerialKickDiagnosticsSampleTime = Time.time + GetAerialKickDiagnosticsSampleInterval();
            LogAerialKickDiagnostic(
                "Momentum",
                $"planar={nextPlanarVelocity.magnitude:F2} target={targetPlanarSpeed:F2} vy={currentVelocity.y:F2} windowActive={_activeAerialKickWindowEndTick >= 0}");
        }
    }

    private void WeakenJointSpringsForAerialKick()
    {
        SetAerialKickSpringLerp(AerialKickSpringLerpDuringKick);
    }

    private void RestoreJointSpringsAfterAerialKick()
    {
        SetAerialKickSpringLerp(1f);

        for (int i = 0; i < syncPhysicsObjects.Length; i++)
            syncPhysicsObjects[i].MakeActiveRagdoll();

        RestorePuppetMasterMappingAfterAerialKick();
        _isAerialKickMomentumActive = false;
        _aerialKickMissPenaltyPending = false;
        _activeAerialKickBallisticFallActive = false;
        _aerialKickSpringRestoreTimer = 0f;
        _activeAerialKickTargetPlanarSpeed = 0f;
        _activeAerialKickHasLeftGround = false;
        _activeAerialKickFlightForceReleaseTime = float.NegativeInfinity;
        _activeAerialKickStartedAt = float.NegativeInfinity;
        _activeAerialKickLandingConfirmTimer = 0f;
        _activeAerialKickRawGrounded = false;
        _activeAerialKickNearGround = false;
        _activeAerialKickLastGroundContactTime = float.NegativeInfinity;
        _nextAerialKickDiagnosticsSampleTime = float.NegativeInfinity;
        _activeAerialKickForwardDirection = Vector3.zero;
        _aerialKickSpringRestoreStartLerp = 1f;
    }

    private void TickAerialKickSpringRestore(float dt)
    {
        if (!_isAerialKickMomentumActive || _aerialKickSpringRestoreTimer <= 0f)
            return;

        _aerialKickSpringRestoreTimer -= dt;
        if (_aerialKickSpringRestoreTimer <= 0f)
        {
            RestoreJointSpringsAfterAerialKick();
            return;
        }

        var t = 1f - (_aerialKickSpringRestoreTimer / AerialKickSpringRestoreDuration);
        var springLerp = Mathf.Lerp(_aerialKickSpringRestoreStartLerp, 1f, t);
        SetAerialKickSpringLerp(springLerp);
    }

    private void SetAerialKickSpringLerp(float springLerp)
    {
        springLerp = Mathf.Clamp01(springLerp);
        _aerialKickCurrentSpringLerp = springLerp;

        if (mainJoint != null)
        {
            var jd = mainJoint.slerpDrive;
            jd.positionSpring = _startSlerpPositionSpring * springLerp;
            mainJoint.slerpDrive = jd;
        }

        for (int i = 0; i < syncPhysicsObjects.Length; i++)
            syncPhysicsObjects[i].SetSpringLerp(springLerp);
    }

    private void SuppressPuppetMasterMappingForAerialKick()
    {
        if (_aerialKickMappingSuppressed || _puppetMaster == null)
            return;

        // 기존 _forceAnimatorVisualLatch가 활성이면 이미 mappingWeight=0이므로 건드리지 않음
        if (_forceAnimatorVisualLatch)
            return;

        _aerialKickSavedMappingWeight = _puppetMaster.mappingWeight;
        _puppetMaster.mappingWeight = 0f;
        _aerialKickMappingSuppressed = true;
    }

    private void RestorePuppetMasterMappingAfterAerialKick()
    {
        if (!_aerialKickMappingSuppressed || _puppetMaster == null)
            return;

        // 다른 시스템이 이미 mappingWeight를 제어 중이면 건드리지 않음
        if (!_forceAnimatorVisualLatch)
            _puppetMaster.mappingWeight = _aerialKickSavedMappingWeight;

        _aerialKickMappingSuppressed = false;
    }

    private bool ResolveEffectiveAerialKickGroundedState(bool rawGrounded, float dt)
    {
        _activeAerialKickRawGrounded = rawGrounded;

        if (!_isAerialKickMomentumActive || !_activeAerialKickHasLeftGround)
        {
            _activeAerialKickLandingConfirmTimer = 0f;
            _activeAerialKickNearGround = rawGrounded;
            return rawGrounded;
        }

        if (Time.time < _activeAerialKickStartedAt + AerialKickLandingMinAirTime)
        {
            _activeAerialKickLandingConfirmTimer = 0f;
            _activeAerialKickNearGround = false;
            return false;
        }

        var footLandingSignal = HasAerialKickFootLandingSignal();
        _activeAerialKickNearGround = IsNearGroundForAerialKickLanding(footLandingSignal);
        var hasRecentGroundContact = HasRecentAerialKickGroundContact();
        var hasLandingSignal = _activeAerialKickNearGround || hasRecentGroundContact;
        var isDescendingEnough = rigidbody3D == null ||
                                 rigidbody3D.velocity.y <= AerialKickLandingMaxVerticalSpeed ||
                                 hasRecentGroundContact;
        if (hasLandingSignal && isDescendingEnough)
        {
            if (_activeAerialKickLandingConfirmTimer <= 0f)
            {
                LogAerialKickDiagnostic(
                    "LandingSignalStart",
                    $"rawGrounded={rawGrounded} foot={footLandingSignal} nearGround={_activeAerialKickNearGround} recentContact={hasRecentGroundContact} vy={(rigidbody3D != null ? rigidbody3D.velocity.y : 0f):F2}");
            }

            _activeAerialKickLandingConfirmTimer += Mathf.Max(0f, dt);
            if (_activeAerialKickLandingConfirmTimer >= AerialKickLandingConfirmDuration)
            {
                LogAerialKickDiagnostic(
                    "LandingConfirmed",
                    $"rawGrounded={rawGrounded} foot={footLandingSignal} nearGround={_activeAerialKickNearGround} recentContact={hasRecentGroundContact} vy={(rigidbody3D != null ? rigidbody3D.velocity.y : 0f):F2}");
                return true;
            }
        }
        else
        {
            if (_activeAerialKickLandingConfirmTimer > 0f)
            {
                LogAerialKickDiagnostic(
                    "LandingSignalReset",
                    $"rawGrounded={rawGrounded} foot={footLandingSignal} nearGround={_activeAerialKickNearGround} recentContact={hasRecentGroundContact} vy={(rigidbody3D != null ? rigidbody3D.velocity.y : 0f):F2}");
            }

            _activeAerialKickLandingConfirmTimer = 0f;
        }

        return false;
    }

    private bool HasRecentAerialKickGroundContact()
    {
        return Time.time - _activeAerialKickLastGroundContactTime <= AerialKickGroundContactMemory;
    }

    private bool IsNearGroundForAerialKickLanding(bool footLandingSignal)
    {
        if (_groundProbe == null)
            return _isGrounded;

        if (footLandingSignal)
            return true;

        var probeOrigin = rigidbody3D != null ? rigidbody3D.position : transform.position;
        return _groundProbe.IsGrounded(
            probeOrigin,
            transform,
            AerialKickLandingProbeRadius,
            AerialKickLandingProbeDistance) && HasRecentAerialKickGroundContact();
    }

    private void OnCollisionEnter(Collision collision)
    {
        TryObserveAerialKickGroundContact(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        TryObserveAerialKickGroundContact(collision);
    }

    private void TryObserveAerialKickGroundContact(Collision collision)
    {
        if (!_isAerialKickMomentumActive ||
            collision == null ||
            Time.time < _activeAerialKickStartedAt + AerialKickLandingMinAirTime)
        {
            return;
        }

        var selfRoot = transform.root;
        for (int i = 0; i < collision.contactCount; i++)
        {
            var contact = collision.GetContact(i);
            if (contact.otherCollider != null && contact.otherCollider.transform.root == selfRoot)
                continue;

            if (contact.normal.y < AerialKickGroundContactNormalThreshold)
                continue;

            if (!IsAerialKickGroundContactNearFoot(contact.point))
                continue;

            _activeAerialKickLastGroundContactTime = Time.time;
            LogAerialKickDiagnostic(
                "GroundContact",
                $"normalY={contact.normal.y:F2} pointY={contact.point.y:F2}");
            return;
        }
    }

    private void LogAerialKickDiagnostic(string source, string note)
    {
        if (!ShouldLogAerialKickDiagnostics())
            return;

        var velocity = rigidbody3D != null ? rigidbody3D.velocity : Vector3.zero;
        var planarVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up).magnitude;
        var pelvisPosition = ResolveStartupLaunchPelvisPosition(out var pelvisVelocity);
        var presentationRoot = GetPresentationRootTransform();
        var rootY = transform.position.y;
        var pelvisY = pelvisPosition.y;
        var visualY = presentationRoot != null ? presentationRoot.position.y : rootY;
        var netHipsY = IsNetworkReady ? NetworkedHipsPosition.y : 0f;
        var rootGap = ResolveStunDiagnosticsRootGap();
        var netPelvisGap = IsNetworkReady
            ? Mathf.Abs(NetworkedHipsPosition.y - pelvisPosition.y)
            : 0f;
        var netHipsSummary = IsNetworkReady ? netHipsY.ToString("F2") : "n/a";
        var netPelvisSummary = IsNetworkReady ? (netHipsY - pelvisY).ToString("F2") : "n/a";
        var anomalySummary = (rootGap >= RootGapAnomalyAlertThreshold || netPelvisGap >= NetPelvisGapAnomalyAlertThreshold)
            ? $" {BuildRecentTransformWriterSummary()}"
            : string.Empty;
        Debug.Log(
            $"[AerialKickSim] {name} {source} t={Time.time:F2} rawGrounded={_activeAerialKickRawGrounded} grounded={_isGrounded} nearGround={_activeAerialKickNearGround} leftGround={_activeAerialKickHasLeftGround} planar={planarVelocity:F2} vy={velocity.y:F2} pelvisVy={pelvisVelocity.y:F2} rootY={rootY:F2} pelvisY={pelvisY:F2} visualY={visualY:F2} netHipsY={netHipsSummary} dy(rootPelvis)={(rootY - pelvisY):F2} dy(netPelvis)={netPelvisSummary} rootGap={rootGap:F2} restore={_aerialKickSpringRestoreTimer:F2} note={note}{anomalySummary}",
            this);
    }

    internal void ExecutePunchHitDetection(bool isLeft)
    {
        // Legacy compatibility wrapper for animation events that may still point here.
        TryBeginPunchHitDetection(isLeft);
        return;
        /*
        // 호스트에서만 판정
        if (Runner != null && Object != null && Object.IsValid && !HasStateAuthority)
            return;

        // 쿨다운 체크 (Fusion tick 기반 — 재시뮬레이션에서도 결정적)

        // CSV에서 수치 읽기

        // 캐릭터 전방 OverlapSphere로 피격 대상 탐색

        // 공격자 속도 — 달리면서 때리면 넉백/기절 시간이 더 길어짐

            // 피격 처리 — 공격자 속도를 반영

            // 넉백 방향: 공격자 forward 가중 + 측면/공중 성격 분리

            // 속도 보너스: 달리면서 때리면 최대 1.5배


            // 피격 muscle 직접 임펄스 — 맞은 부위가 물리적으로 밀림

            // 히트스탑: 양쪽 rigidbody 일시 감속

            break; // 1번의 펀치에 1명만 타격
        }
    }

    /// <summary>
    /// 피격 시 PuppetMaster muscle 뼈에 직접 임펄스를 가해서
    /// 맞은 부위가 물리적으로 밀리는 파티애니멀즈 스타일 효과.
    /// 가장 가까운 muscle에 집중 임펄스, 나머지에 분산 임펄스.
    /// </summary>
        */
    }

    private void ApplyMuscleImpulseOnHit(
        NetworkPlayer victim,
        Vector3 hitPoint,
        Vector3 knockbackDir,
        float force,
        bool enteredStunThisHit,
        bool repeatDownedHit)
    {
        if (victim._puppetMaster == null || victim._puppetMaster.muscles == null)
            return;

        var muscles = victim._puppetMaster.muscles;
        var stunnedVictim = !victim._isActiveRagdoll;
        var collapseVictim = victim.GetPhysicalPhase() == PhysicalPhase.StunnedCollapse;
        var mitigateCollapseImpulse = enteredStunThisHit && collapseVictim;
        var redirectImpulseToCore = repeatDownedHit || mitigateCollapseImpulse;
        var impactBlend = NormalizePunchImpact(force);
        var localHitOffset = victim.ResolveImpactLocalOffset(hitPoint);
        var lateralRatio = Mathf.Clamp01(Mathf.Abs(localHitOffset.x) / 0.32f);
        var heightRatio = Mathf.Clamp01((localHitOffset.y + 0.05f) / 0.70f);
        var torqueBlend = Mathf.Clamp01(Mathf.Max(lateralRatio, heightRatio * 0.85f));
        var focusedImpulseScale = repeatDownedHit
            ? Mathf.Lerp(0.018f, 0.036f, impactBlend)
            : mitigateCollapseImpulse
            ? Mathf.Lerp(0.035f, 0.075f, impactBlend)
            : stunnedVictim
            ? Mathf.Lerp(collapseVictim ? 0.12f : 0.18f, collapseVictim ? 0.22f : 0.28f, impactBlend)
            : Mathf.Lerp(0.42f, 0.62f, impactBlend);
        var spreadImpulseScale = repeatDownedHit
            ? 0f
            : mitigateCollapseImpulse
            ? 0f
            : stunnedVictim
            ? Mathf.Lerp(collapseVictim ? 0.02f : 0.04f, collapseVictim ? 0.05f : 0.08f, impactBlend)
            : Mathf.Lerp(0.10f, 0.18f, impactBlend);
        var twistTorqueScale = repeatDownedHit
            ? 0f
            : mitigateCollapseImpulse
            ? 0f
            : stunnedVictim
            ? Mathf.Lerp(collapseVictim ? 0f : 0.03f, collapseVictim ? 0.03f : 0.10f, torqueBlend)
            : Mathf.Lerp(0.06f, 0.14f, torqueBlend);
        float closestDist = float.MaxValue;
        int closestIdx = -1;
        float closestCoreDist = float.MaxValue;
        int closestCoreIdx = -1;

        // 히트 포인트에서 가장 가까운 muscle 찾기
        for (int i = 0; i < muscles.Length; i++)
        {
            if (muscles[i].joint == null) continue;
            var rb = muscles[i].joint.GetComponent<Rigidbody>();
            if (rb == null || rb.isKinematic) continue;

            var dist = (rb.position - hitPoint).sqrMagnitude;
            if (dist < closestDist)
            {
                closestDist = dist;
                closestIdx = i;
            }

            if (IsStunImpulseCoreMuscle(muscles[i].joint.name) && dist < closestCoreDist)
            {
                closestCoreDist = dist;
                closestCoreIdx = i;
            }
        }

        if (closestIdx < 0) return;

        var originalClosestIdx = closestIdx;
        if (redirectImpulseToCore && closestCoreIdx >= 0)
            closestIdx = closestCoreIdx;

        var targetMuscleName = muscles[closestIdx].joint != null ? muscles[closestIdx].joint.name : "unknown";
        var originalTargetMuscleName = muscles[originalClosestIdx].joint != null ? muscles[originalClosestIdx].joint.name : "unknown";
        victim.TraceStunImpulseSummary(
            "ApplyMuscleImpulseOnHit",
            force,
            focusedImpulseScale,
            spreadImpulseScale,
            twistTorqueScale,
            targetMuscleName,
            repeatDownedHit
                ? $"mitigated=repeat-downed redirected={(closestIdx != originalClosestIdx)} original={originalTargetMuscleName}"
                : mitigateCollapseImpulse
                ? $"mitigated=stun-entry-collapse redirected={(closestIdx != originalClosestIdx)} original={originalTargetMuscleName}"
                : null);

        var closestRb = muscles[closestIdx].joint.GetComponent<Rigidbody>();
        if (closestRb != null && !closestRb.isKinematic)
        {
            var victimRight = Vector3.ProjectOnPlane(victim.transform.right, Vector3.up);
            if (victimRight.sqrMagnitude < 0.0001f)
                victimRight = Vector3.right;
            victimRight.Normalize();

            var victimForward = Vector3.ProjectOnPlane(victim.transform.forward, Vector3.up);
            if (victimForward.sqrMagnitude < 0.0001f)
                victimForward = knockbackDir;
            victimForward.Normalize();

            var yawSign = Mathf.Abs(localHitOffset.x) > 0.02f
                ? Mathf.Sign(localHitOffset.x)
                : Mathf.Sign(Vector3.Cross(victimForward, knockbackDir).y);
            var yawTorque = repeatDownedHit || mitigateCollapseImpulse || collapseVictim
                ? Vector3.zero
                : Vector3.up * yawSign * force * Mathf.Lerp(0.015f, 0.075f, lateralRatio);
            var backwardLeanTorque = repeatDownedHit || mitigateCollapseImpulse
                ? Vector3.zero
                : -victimRight * force * Mathf.Lerp(0f, stunnedVictim ? 0.06f : 0.09f, heightRatio);

            closestRb.AddForceAtPosition(
                knockbackDir * force * focusedImpulseScale,
                hitPoint,
                ForceMode.Impulse);
            closestRb.AddTorque(
                Vector3.Cross(Vector3.up, knockbackDir) * force * twistTorqueScale +
                yawTorque +
                backwardLeanTorque,
                ForceMode.Impulse);
        }

        for (int i = 0; i < muscles.Length; i++)
        {
            if (i == closestIdx || muscles[i].joint == null) continue;
            var rb = muscles[i].joint.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
                rb.AddForce(knockbackDir * force * spreadImpulseScale, ForceMode.Impulse);
        }
    }

    private static bool IsStunImpulseCoreMuscle(string muscleName)
    {
        if (string.IsNullOrWhiteSpace(muscleName))
            return false;

        return muscleName.Contains("Hips") ||
               muscleName.Contains("Pelvis") ||
               muscleName.Contains("Waist") ||
               muscleName.Contains("Spine") ||
               muscleName.Contains("Chest") ||
               muscleName.Contains("Torso");
    }

    /// <summary>
    /// 로컬 히트스탑: 공격자 + 피격자의 rigidbody velocity를 일시 감속.
    /// Time.timeScale 대신 rigidbody 기반으로 처리하여 네트워크 영향 없음.
    /// FixedUpdate에서 HIT_STOP_DURATION 후 복원.
    /// </summary>
    private void ApplyLocalHitStop(NetworkPlayer victim)
    {
        // 공격자 히트스탑
        if (rigidbody3D != null && !rigidbody3D.isKinematic)
        {
            _hitStopSavedVelocity = rigidbody3D.velocity;
            _hitStopSavedAngularVelocity = rigidbody3D.angularVelocity;
            rigidbody3D.velocity *= HIT_STOP_VELOCITY_SCALE;
            rigidbody3D.angularVelocity *= HIT_STOP_VELOCITY_SCALE;
            _hitStopEndTime = Time.time + HIT_STOP_DURATION;
        }

        // 피격자 히트스탑
        if (victim == null || !victim._isActiveRagdoll)
            return;

        if (victim.rigidbody3D != null && !victim.rigidbody3D.isKinematic)
        {
            victim._hitStopSavedVelocity = victim.rigidbody3D.velocity;
            victim._hitStopSavedAngularVelocity = victim.rigidbody3D.angularVelocity;
            victim.rigidbody3D.velocity *= HIT_STOP_VELOCITY_SCALE;
            victim.rigidbody3D.angularVelocity *= HIT_STOP_VELOCITY_SCALE;
            victim._hitStopEndTime = Time.time + HIT_STOP_DURATION;
        }
    }

    /// <summary>
    /// 원격 클라이언트에서 GetHit 이벤트 수신 시 호출.
    /// 자기 자신의 rigidbody만 일시 감속하여 히트스탑 연출 재현.
    /// </summary>
    internal void ApplyReplicatedHitStop()
    {
        if (rigidbody3D == null || rigidbody3D.isKinematic) return;
        if (_hitStopEndTime > Time.time) return; // 이미 히트스탑 중

        _hitStopSavedVelocity = rigidbody3D.velocity;
        _hitStopSavedAngularVelocity = rigidbody3D.angularVelocity;
        rigidbody3D.velocity *= HIT_STOP_VELOCITY_SCALE;
        rigidbody3D.angularVelocity *= HIT_STOP_VELOCITY_SCALE;
        _hitStopEndTime = Time.time + HIT_STOP_DURATION;
    }

    /// <summary>
    /// FixedUpdate/DoPhysicsStep에서 호출 — 히트스탑 해제 시 velocity 복원.
    /// </summary>
    private void TickHitStopRecovery()
    {
        if (_hitStopEndTime <= 0f) return;
        if (Time.time < _hitStopEndTime) return;

        if (rigidbody3D != null && !rigidbody3D.isKinematic)
        {
            // 넉백 임펄스가 이미 적용된 후이므로 saved velocity를 더하지 않고
            // 현재 velocity가 HIT_STOP_VELOCITY_SCALE이면 원래 값으로 복원
            var current = rigidbody3D.velocity;
            if (current.sqrMagnitude < _hitStopSavedVelocity.sqrMagnitude)
                rigidbody3D.velocity = _hitStopSavedVelocity;
        }

        _hitStopEndTime = 0f;
    }

    // ─── 기절 슬로우모션 ───

    /// <summary>
    /// 로컬 플레이어가 기절할 때 호출. Time.timeScale을 순간적으로 낮춘다.
    /// Fusion의 FixedUpdateNetwork는 Runner.DeltaTime을 사용하므로 네트워크 시뮬레이션에 영향 없음.
    /// </summary>
    private void TriggerStunSlowMotion()
    {
        // 로컬 플레이어만 — HasInputAuthority 체크
        if (Runner != null && Object != null && Object.IsValid && !HasInputAuthority)
            return;

        if (_stunSlowMotionActive)
            return;

        _stunSlowMotionActive = true;
        Time.timeScale = STUN_SLOWMO_SCALE;
        Time.fixedDeltaTime = 0.02f * STUN_SLOWMO_SCALE; // physics step도 비례 축소

        float now = Time.unscaledTime;
        _stunSlowMotionHoldEnd = now + STUN_SLOWMO_HOLD_DURATION;
        _stunSlowMotionRampEnd = now + STUN_SLOWMO_HOLD_DURATION + STUN_SLOWMO_RAMP_DURATION;

    }

    /// <summary>
    /// Update (또는 LateUpdate)에서 매 프레임 호출.
    /// hold 구간 후 timeScale을 1.0까지 부드럽게 복원.
    /// </summary>
    private void TriggerKnockoutConfirmSlowMotion()
    {
        if (Runner != null && Object != null && Object.IsValid && !HasInputAuthority)
            return;

        if (_stunSlowMotionActive)
            return;

        _knockoutConfirmSlowMotionActive = true;
        Time.timeScale = KNOCKOUT_CONFIRM_SLOWMO_SCALE;
        Time.fixedDeltaTime = SLOWMO_BASE_FIXED_DELTA_TIME * KNOCKOUT_CONFIRM_SLOWMO_SCALE;

        float now = Time.unscaledTime;
        _knockoutConfirmSlowMotionHoldEnd = now + KNOCKOUT_CONFIRM_SLOWMO_HOLD_DURATION;
        _knockoutConfirmSlowMotionRampEnd = now + KNOCKOUT_CONFIRM_SLOWMO_HOLD_DURATION + KNOCKOUT_CONFIRM_SLOWMO_RAMP_DURATION;
    }

    private void TickKnockoutConfirmSlowMotion()
    {
        if (!_knockoutConfirmSlowMotionActive)
            return;

        float now = Time.unscaledTime;

        if (now < _knockoutConfirmSlowMotionHoldEnd)
            return;

        if (now >= _knockoutConfirmSlowMotionRampEnd)
        {
            Time.timeScale = 1f;
            Time.fixedDeltaTime = SLOWMO_BASE_FIXED_DELTA_TIME;
            _knockoutConfirmSlowMotionActive = false;
            return;
        }

        float t = (now - _knockoutConfirmSlowMotionHoldEnd) / KNOCKOUT_CONFIRM_SLOWMO_RAMP_DURATION;
        float scale = Mathf.Lerp(KNOCKOUT_CONFIRM_SLOWMO_SCALE, 1f, t);
        Time.timeScale = scale;
        Time.fixedDeltaTime = SLOWMO_BASE_FIXED_DELTA_TIME * scale;
    }

    internal void TickStunSlowMotion()
    {
        if (!_stunSlowMotionActive)
            return;

        float now = Time.unscaledTime;

        if (now < _stunSlowMotionHoldEnd)
            return; // hold 구간 — 낮은 timeScale 유지

        if (now >= _stunSlowMotionRampEnd)
        {
            // 복원 완료
            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
            _stunSlowMotionActive = false;
            return;
        }

        // ramp 구간 — STUN_SLOWMO_SCALE → 1.0 으로 선형 보간
        float t = (now - _stunSlowMotionHoldEnd) / STUN_SLOWMO_RAMP_DURATION;
        float scale = Mathf.Lerp(STUN_SLOWMO_SCALE, 1f, t);
        Time.timeScale = scale;
        Time.fixedDeltaTime = 0.02f * scale;
    }
}
