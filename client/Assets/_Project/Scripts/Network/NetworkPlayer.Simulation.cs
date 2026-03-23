using RootMotion.Dynamics;
using UnityEngine;

public sealed partial class NetworkPlayer
{
    private float _localAccumulatedStun;
    private float _localStunTimeRemaining;
    private float _stunCollapseTimer;

    // Coyote time + Jump buffer
    private float _coyoteTimeRemaining;
    private float _jumpBufferRemaining;
    private const float COYOTE_TIME = 0.1f;
    private const float JUMP_BUFFER_TIME = 0.1f;
    private const float InstabilityRiseSpeed = 3.5f;
    private const float InstabilityFallSpeed = 2.25f;
    private const float UnstableEnterThreshold = 0.48f;
    private const float UnstableExitThreshold = 0.26f;
    private const float DragPlanarSpeedThreshold = 1.75f;
    private const float DragAngularSpeedThreshold = 3.5f;
    private const float StunLaunchKnockbackScale = 0.45f;
    private const float StunEntryRootPlanarVelocityScale = 0.38f;
    private const float StunEntryRootPlanarSpeedCap = 2.2f;
    private const float StunEntryRootAngularVelocityScale = 0.22f;
    private const float StunEntryMusclePlanarVelocityScale = 0.35f;
    private const float StunEntryMusclePlanarSpeedCap = 1.6f;
    private const float StunEntryMuscleAngularVelocityScale = 0.25f;
    private const float StunCollapseDuration = 0.25f;
    private const float StunCollapseEarlyDuration = 0.12f;
    private const float StunCollapseEntryMainSpringScale = 0.08f;
    private const float StunCollapseEntryBoneSpringLerp = 0.06f;
    private const float StunnedRootPlanarSpeedCap = 1.10f;
    private const float StunnedMusclePlanarSpeedCap = 0.90f;
    private const float StunnedRootAngularSpeedCap = 2.9f;
    private const float StunnedMuscleAngularSpeedCap = 3.2f;
    private const float CollapseRootPlanarSpeedCap = 1.60f;
    private const float CollapseMusclePlanarSpeedCap = 1.25f;
    private const float CollapseRootAngularSpeedCap = 3.4f;
    private const float CollapseMuscleAngularSpeedCap = 3.8f;
    private const float CollapseEarlyRootPlanarSpeedCap = 0.95f;
    private const float CollapseEarlyMusclePlanarSpeedCap = 0.70f;
    private const float CollapseEarlyRootAngularSpeedCap = 1.45f;
    private const float CollapseEarlyMuscleAngularSpeedCap = 1.75f;
    // BeingCarriedStunned: 운반 중 피해자는 캐리어 이동 속도를 따라가야 하므로 클램프 대폭 완화.
    // maxSpeed(5) * sprintMultiplier(1.8) = 9 m/s + 여유분 → 12 m/s 이상으로 설정.
    // 기존 2.5f 상한은 빠른 캐리에서 joint가 속도를 못 따라가 victim 위치 괴리의 원인이었음.
    private const float CarriedStunnedRootPlanarSpeedCap = 12.0f;
    private const float CarriedStunnedMusclePlanarSpeedCap = 10.0f;
    private const float CarriedStunnedRootAngularSpeedCap = 6.0f;
    private const float CarriedStunnedMuscleAngularSpeedCap = 6.5f;
    private const float CarriedStunnedMaxUpwardSpeed = 8.0f;
    private const float StunRootUpwardSyncStep = 0.08f;
    // root → hips 추적 속도: 캐리어 스프린트 최대(9 m/s) 초과 보장.
    // 기존 7.5f는 스프린트(9 m/s) 상황에서 root가 매 틱 뒤처져 Fusion sync 값이 항상 지연됐음.
    private const float CarriedRootPlanarSyncSpeed = 18f;
    private const float CarriedRootVerticalSyncSpeed = 22f;
    private const float CarriedRootSnapDistance = 0.8f;
    private const float CarriedRootSnapVerticalGap = 0.45f;
    private const float CarriedRootTraceGapThreshold = 0.3f;
    private const float HitInstabilityBoostMin = 0.08f;
    private const float HitInstabilityBoostMax = 0.22f;
    private const float HitInstabilityBoostDecay = 1.5f;
    private const float HitReactionMoveSpeedScale = 0.70f;
    private const float HitReactionBrakeScale = 0.60f;
    private const float HitReactionGroundStickScale = 0.60f;
    private const float HostRemoteClientMoveSpeedCompensation = 1.12f;

    private void DoPhysicsStep(PlayerNetworkInput input, float dt)
    {
        if (config == null || rigidbody3D == null || mainJoint == null)
            return;

        var unifiedGrabHold = (bool)input.LeftGrabHold;
        _isLeftGrabActive = unifiedGrabHold;
        _isRightGrabActive = unifiedGrabHold;
        _isGrabActive = unifiedGrabHold;

        SimulateLocomotion(input, dt);
        SynchronizeMotorPresentation();
        UpdateActiveRagdollJoints();
        ProcessInteractions(input);
        // UpdatePhysicalPhaseState는 FixedUpdateNetwork()에서 UpdateGrabHandlers() 이후
        // 한 번만 호출한다. 여기서도 호출하면 TickCarryReleaseSettle() 등이 이중 실행되어
        // settle 타이머가 설정값의 절반 속도로 감소하는 버그 발생.
        TickPunchHitDetectionWindow();
        SyncHeldItemNetworkState();
        TraceStartupLaunchDiagnostics("DoPhysicsStep");
    }

    public void ApplyStunDamage(
        float stunDamage,
        float bodyPartMultiplier,
        float attackerVelocity,
        float impulseMagnitude,
        bool deferStunEntryDamping = false,
        NetworkPlayer instigator = null)
    {
        if (!_isActiveRagdoll || GetIsDeadState())
            return;

        var buffApplier = ResolveItemBuffApplier();
        if (buffApplier != null && buffApplier.IsSuperArmorActive)
            return;

        var finalStunDamage = stunDamage * bodyPartMultiplier * ResolveStunStateMultiplier();
        var accumulated = AddStunDamage(finalStunDamage);
        ArmHitInstabilityBoost(Mathf.Max(impulseMagnitude, finalStunDamage * 0.6f));

        RaiseAnimationEvent(AnimationEventType.GetHit, H_GetHit);

        var threshold = CombatSettings.Instance != null
            ? CombatSettings.Instance.knockoutThreshold
            : 30f;

        if (accumulated >= threshold)
        {
            var overflow = Mathf.Max(0f, accumulated - threshold);
            TriggerStun(
                CalculateStunDuration(attackerVelocity, impulseMagnitude, overflow, threshold),
                applyEntryDamping: !deferStunEntryDamping);

            if (instigator != null && instigator != this)
                instigator.TriggerKnockoutConfirm();
        }
        else
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

    // 2-phase 회복: stabilization(스프링 점진 복원) + vulnerable(취약 창)
    private bool _isRecoverStabilizing;
    private float _recoverStabilizeTimer;
    private const float RECOVER_STABILIZE_DURATION = 0.4f;
    private float _recoverMinColliderY = float.NegativeInfinity;
    private bool _hasRecoverAnchorPose;
    private Vector3 _recoverAnchorPosition;
    private bool _wasBeingCarriedLastStunnedTick;
    private float _savedCarryPinWeight = 1f;
    private Quaternion _recoverAnchorRotation = Quaternion.identity;
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
        _recoverAnchorPosition = position;
        _recoverAnchorRotation = Quaternion.Euler(0f, rotation.eulerAngles.y, 0f);
        _hasRecoverAnchorPose = true;
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

    private int ResolveCurrentSimulationTick()
    {
        return Runner != null ? Runner.Tick.Raw : Mathf.RoundToInt(Time.time / Time.fixedDeltaTime);
    }

    private void ClearPunchHitDetectionWindow()
    {
        _activePunchWindowEndTick = -1;
        _activePunchHasPreviousSample = false;
    }

    private Vector3 ResolvePunchHitSamplePosition(bool isLeft)
    {
        var forward = ResolvePunchForward();
        var handTransform = ResolvePunchHandTransform(isLeft);
        if (handTransform != null)
            return handTransform.position + forward * PunchHitForwardOffset;

        return transform.position + Vector3.up * 0.6f + forward * PunchFallbackReach;
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

    private Vector3 ResolvePunchForward()
    {
        return _targetRoot != null ? _targetRoot.forward : transform.forward;
    }

    private bool TryResolvePunchVictim(Vector3 sweepStart, Vector3 sweepEnd, out NetworkPlayer victimPlayer, out Vector3 hitPoint)
    {
        victimPlayer = null;
        hitPoint = sweepEnd;

        var hitCount = Physics.OverlapCapsuleNonAlloc(
            sweepStart,
            sweepEnd,
            PunchHitRadius,
            _punchHitResults,
            ~0,
            QueryTriggerInteraction.Ignore);

        if (hitCount <= 0)
            return false;

        var bestDistance = float.MaxValue;
        for (var i = 0; i < hitCount; i++)
        {
            var hit = _punchHitResults[i];
            _punchHitResults[i] = null;
            if (hit == null)
                continue;

            var candidate = hit.GetComponentInParent<NetworkPlayer>();
            if (candidate == null || candidate == this || !candidate.IsActiveRagdoll)
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

    private void ApplyPunchHit(NetworkPlayer victimPlayer, Vector3 hitPoint)
    {
        if (victimPlayer == null)
            return;

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
            instigator: this);
        var isStunnedByHit = !victimPlayer._isActiveRagdoll;
        var collapseVictim = isStunnedByHit && victimPlayer.GetPhysicalPhase() == PhysicalPhase.StunnedCollapse;
        var appliedKnockback = isStunnedByHit ? finalKnockback * StunLaunchKnockbackScale : finalKnockback;
        victimPlayer.ArmHitInstabilityBoost(appliedKnockback);
        victimPlayer.ArmHitFlinch(appliedKnockback);
        victimPlayer.ArmDirectionalCombatFlinch(hitPoint, appliedKnockback);

        var victimRb = victimPlayer.rigidbody3D;
        var victimVelocityBeforeForce = victimRb != null && !victimRb.isKinematic
            ? victimRb.velocity
            : Vector3.zero;
        if (victimRb != null && !victimRb.isKinematic)
        {
            victimRb.AddForce(knockbackDir * appliedKnockback, ForceMode.Impulse);
            var rotationScale = collapseVictim
                ? Mathf.Lerp(0.035f, 0.06f, heightRatio)
                : isStunnedByHit
                    ? Mathf.Lerp(0.09f, 0.12f, heightRatio)
                : Mathf.Lerp(0.24f, 0.32f, heightRatio);
            victimRb.AddForceAtPosition(
                knockbackDir * appliedKnockback * rotationScale,
                hitPoint,
                ForceMode.Impulse);

            // 측면 타격일수록 yaw 토크 → 몸이 비틀려 돌아감
            if (lateralRatio > 0.25f)
            {
                var yawSign = Mathf.Sign(Vector3.Cross(victimPlayer.transform.forward, knockbackDir).y);
                var yawTorqueScale = collapseVictim
                    ? Mathf.Lerp(0.015f, 0.035f, lateralRatio)
                    : Mathf.Lerp(0.08f, 0.16f, lateralRatio);
                var yawTorque = Vector3.up * yawSign * appliedKnockback * yawTorqueScale;
                victimRb.AddTorque(yawTorque, ForceMode.Impulse);
            }
        }
        victimPlayer.TraceStunForceEvent(
            "PunchRoot",
            victimRb,
            knockbackDir * appliedKnockback,
            ForceMode.Impulse,
            victimVelocityBeforeForce,
            victimRb != null && !victimRb.isKinematic ? victimRb.velocity : victimVelocityBeforeForce,
            appliedKnockback > 0.0001f,
            $"isStunnedByHit={isStunnedByHit} collapseVictim={collapseVictim}");

        ApplyPunchFollowThrough(knockbackDir, finalKnockback);
        ApplyMuscleImpulseOnHit(victimPlayer, hitPoint, knockbackDir, appliedKnockback);
        if (isStunnedByHit)
            victimPlayer.DampenStunEntryVelocities();

        TriggerAttackCameraKick(forward, finalKnockback);
        victimPlayer.TriggerVictimCameraKick(knockbackDir, appliedKnockback);
        // 히트스탑 제거 — 파티애니멀즈 스타일은 래그돌 과장 반응이 타격감 핵심, 속도 동결은 물리 흐름을 끊음
        // ApplyLocalHitStop(victimPlayer);
        SpawnHitImpactVFX(hitPoint, knockbackDir, appliedKnockback);
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

        var collapsePhase = TickStunCollapseTimer(dt);
        var stunnedPhase = ResolveCurrentStunnedPhase(collapsePhase);
        SetLocalPhysicalPhase(stunnedPhase, 1f, false);
        ApplyStunCollapseSpringState(collapsePhase);

        // FixedUpdateNetwork()는 HasStateAuthority에서만 실행되므로 항상 host.
        bool beingCarried = _beingGrabbedRefCount > 0;

        // _wasBeingCarriedLastStunnedTick은 이전 틱의 값을 먼저 읽은 뒤 갱신해야 한다.
        // 이 시점에서 읽으면 "지난 틱에 운반 중이었는가"를 정확히 판단할 수 있다.
        bool carryJustEnded = _wasBeingCarriedLastStunnedTick && !beingCarried;
        bool carryJustStarted = !_wasBeingCarriedLastStunnedTick && beingCarried;

        // 운반 중이면 ForceRecover()보다 먼저 처리해야 한다.
        // ForceRecover()가 _lastSafePosition을 읽을 때 carry 위치가 이미 저장돼 있어야
        // TeleportToSafeStandUpPosition이 올바른 위치(carry 위치)로 텔레포트한다.
        // 순서를 바꾸지 않으면 ForceRecover()가 기절 전 위치를 사용해 원위치로 돌아가는 버그 발생.
        if (beingCarried)
        {
            // carry 중 _hasRecoverAnchorPose 클리어: 종료 후 SyncRootToPhysicsBody()가
            // 기절 전 위치(_recoverAnchorPosition)로 X,Z를 덮어쓰는 것을 방지.
            _hasRecoverAnchorPose = false;

            // carry 진입 첫 틱: PuppetMaster pinWeight = 0으로 설정.
            // pinWeight > 0이면 target skeleton의 월드 위치(기절 진입 위치)로 근육을 끌어당기는
            // 스프링 힘이 carry joint와 줄다리기를 일으켜 캐릭터가 원래 위치로 돌아가는 버그 발생.
            if (carryJustStarted && _puppetMaster != null)
            {
                _savedCarryPinWeight = _puppetMaster.pinWeight;
                _puppetMaster.pinWeight = 0f;
            }

            SyncCarriedRootToPhysicsBody();

            // 운반 위치를 안전 위치로 저장 (ForceRecover 전에 반드시 수행).
            RememberSafeTransform(transform.position, transform.rotation);
        }
        else if (carryJustEnded && _puppetMaster != null)
        {
            // carry 종료 직후: pinWeight 복원.
            _puppetMaster.pinWeight = _savedCarryPinWeight;
        }

        _wasBeingCarriedLastStunnedTick = beingCarried;

        // 잡혀서 운반 중이면 기절 타이머 정지 (운반 중 자동 회복 방지)
        bool pauseStunTimer = beingCarried;
        var remaining = GetStunTimeRemaining() - (pauseStunTimer ? 0f : dt);
        SetStunTimeRemaining(remaining);

        if (remaining <= 0f && !pauseStunTimer)
        {
            ForceRecover();  // 이 시점에 _lastSafePosition = carry 위치 (위에서 업데이트됨)
            if (_isActiveRagdoll)
                return true;
        }

        ClampStunnedMotion(collapsePhase, beingCarried);
        if (collapsePhase)
            TraceStunCollapsePose("TryTickStunnedState");
        else
            TraceStunnedMotionSample("TryTickStunnedState");

        if (!beingCarried)
        {
            // carry 종료 직후 첫 틱: 즉시 hips 위치로 스냅 (lerp 없이).
            // carryJustEnded는 이전 틱 값으로 판단하므로 올바르게 첫 틱을 감지한다.
            if (carryJustEnded)
            {
                if (TryResolveRootSyncTargetPosition(out var snapPos))
                {
                    transform.position = snapPos;
                    RememberSafeTransform(transform.position, transform.rotation);
                }
            }
            else
            {
                SyncRootToPhysicsBody();
            }
        }

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
        if (_beingGrabbedRefCount > 0)
            return PhysicalPhase.BeingCarriedStunned;

        return collapsePhase
            ? PhysicalPhase.StunnedCollapse
            : PhysicalPhase.Stunned;
    }

    private bool IsEarlyCollapsePhaseActive()
    {
        return GetPhysicalPhase() == PhysicalPhase.StunnedCollapse &&
               _stunCollapseTimer > Mathf.Max(0f, StunCollapseDuration - StunCollapseEarlyDuration);
    }

    private void ApplyStunCollapseSpringState(bool collapsePhase)
    {
        if (ShouldDisablePhysicsAnimationSync)
            return;

        if (mainJoint != null)
        {
            var jd = mainJoint.slerpDrive;
            jd.positionSpring = collapsePhase
                ? Mathf.Max(1f, _startSlerpPositionSpring * StunCollapseEntryMainSpringScale)
                : 0f;
            mainJoint.slerpDrive = jd;
        }

        for (var i = 0; i < syncPhysicsObjects.Length; i++)
        {
            if (syncPhysicsObjects[i] == null)
                continue;

            if (collapsePhase)
                syncPhysicsObjects[i].SetSpringLerp(StunCollapseEntryBoneSpringLerp);
            else
                syncPhysicsObjects[i].MakeRagdoll();
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
        var shouldSnap = delta.sqrMagnitude > settings.rootSnapDistance * settings.rootSnapDistance ||
                         verticalGap > settings.rootSnapVerticalGap;

        Vector3 newRootPos;
        if (shouldSnap)
        {
            newRootPos = targetPos;
        }
        else
        {
            var planarCurrent = new Vector3(previousRootPos.x, 0f, previousRootPos.z);
            var planarTarget = new Vector3(targetPos.x, 0f, targetPos.z);
            var planarNext = Vector3.MoveTowards(planarCurrent, planarTarget, settings.rootPlanarFollowSpeed * dt);
            var yNext = Mathf.MoveTowards(previousRootPos.y, targetPos.y, settings.rootVerticalFollowSpeed * dt);
            newRootPos = new Vector3(planarNext.x, yNext, planarNext.z);
        }

        ApplyCarryRootPosition(newRootPos, shouldSnap);

        // carry anchor 캐시 갱신 (네트워크 동기화 + settle 용)
        _lastCarryAnchorPosition = targetPos;

        var remainingGap = Vector3.Distance(newRootPos, targetPos);
        if (remainingGap > CarriedRootTraceGapThreshold)
        {
            TraceCarryDebugSample(
                "CarryAnchorSolve",
                $"mode={_localCarryMode} prevRoot={FormatCarryDebugVector(previousRootPos)} target={FormatCarryDebugVector(targetPos)} " +
                $"newRoot={FormatCarryDebugVector(newRootPos)} delta={FormatCarryDebugVector(delta)} " +
                $"verticalGap={verticalGap:F2} remainingGap={remainingGap:F2} snap={shouldSnap}",
                remainingGap > CarryRootDebugGapThreshold);
        }
    }

    /// <summary>
    /// CarryRig의 victim anchor를 기준으로 carry target 위치를 해석.
    /// victim 쪽: hips-chest 가중 평균 anchor
    /// </summary>
    private bool TryResolveCarryAnchorTargetPosition(out Vector3 targetPos)
    {
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
            if (carryRig.TryGetVictimAnchorWorld(out anchorPos, out anchorFwd))
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
        if (carryPhysicsProfile != null)
            return carryPhysicsProfile.GetSettings(_localCarryMode);

        // 폴백: 기존 하드코드 값
        return new SSAFYPlayTime.Character.CarryPhysicsProfile.CarryModeSettings
        {
            rootPlanarFollowSpeed = CarriedRootPlanarSyncSpeed,
            rootVerticalFollowSpeed = CarriedRootVerticalSyncSpeed,
            rootSnapDistance = CarriedRootSnapDistance,
            rootSnapVerticalGap = CarriedRootSnapVerticalGap,
            proxyRootFollowSpeed = CarryProxyRootFollowSpeed,
            proxyRootSnapDistance = CarryProxyRootSnapDistance,
            carryReleaseSettleDuration = 0.15f,
            carrierTorsoReactionMultiplier = 1.15f,
            carrierTurnAssistMultiplier = 1.1f,
            victimCoreDriveSpringMultiplier = 0.9f,
            victimCoreDriveDamperMultiplier = 0.95f
        };
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
        if (previousMode != SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.None &&
            newMode == SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.None)
        {
            var settings = carryPhysicsProfile != null
                ? carryPhysicsProfile.GetSettings(previousMode)
                : ResolveCarryModeSettings();
            _carryReleaseSettleRemaining = settings.carryReleaseSettleDuration;
        }

        if (previousMode != SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.CarriedVictim &&
            newMode == SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.CarriedVictim)
        {
            // 새 carry 시작 시 이전 release settle 취소.
            // settle이 남아 있으면 _lastCarryAnchorPosition(이전 carry anchor)으로 root가
            // 당겨져 새 carry 위치와 충돌하는 버그 발생.
            _carryReleaseSettleRemaining = 0f;
            CaptureCarriedVictimRootOffset();
        }
        else if (previousMode == SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.CarriedVictim &&
                 newMode != SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.CarriedVictim)
        {
            _hasCarriedVictimRootOffset = false;
            _carriedVictimRootOffset = Vector3.zero;
        }

        _localCarryMode = newMode;

        if (previousMode != newMode)
        {
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
                var settleSpeed = 8f * dt;
                var settledRoot = Vector3.MoveTowards(currentRoot, _lastCarryAnchorPosition, settleSpeed);
                ApplyCarryRootPosition(settledRoot, true);

                TraceCarryDebugSample(
                    "CarryReleaseSettle",
                    $"remaining={_carryReleaseSettleRemaining:F2} root={FormatCarryDebugVector(currentRoot)} " +
                    $"anchor={FormatCarryDebugVector(_lastCarryAnchorPosition)} gap={toAnchor.magnitude:F2}");
            }
        }
    }

    private void ApplyCarryRootPosition(Vector3 nextRootPosition, bool resetVelocity)
    {
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
    }

    /// <summary>
    /// GetInput() 성공 여부와 무관하게 매 FixedUpdateNetwork 틱마다 실행.
    /// 기절/회복/피격 타이머가 패킷 손실의 영향을 받지 않도록 DoPhysicsStep에서 분리.
    ///
    /// 분리 이유: DoPhysicsStep은 GetInput() 성공 시에만 실행되므로
    /// 패킷 손실 틱에 기절 지속시간·회복 타이머·HP 무적 타이머가 멈추는 문제가 있었음.
    /// </summary>
    internal void TickAlwaysStates(float dt)
    {
        TickHitRecoil(dt);
        TickHitFlinch(dt);
        TickHitInstabilityBoost(dt);
        TickVitalState(dt);
        UpdateStunDecay(dt);
        UpdateRecoveringWindow(dt);
        TryTickStunnedState(dt);
    }

    /// <summary>
    /// 의식 있는 플레이어가 BeingGrabbed / Dragged 상태일 때
    /// 잡기 조인트가 hips 뼈만 끌고 가고 root는 locomotion이 구동하므로
    /// transform.position을 hips 위치로 즉시 스냅하여 NetworkTransform이
    /// 올바른 위치를 모든 클라이언트에 전달하도록 한다.
    ///
    /// GetInput() 결과와 무관하게 FixedUpdateNetwork()에서 항상 호출되어야 한다.
    /// </summary>
    private void SyncInteractionRootPosition()
    {
        if (_localPhysicalPhase != PhysicalPhase.BeingGrabbed &&
            _localPhysicalPhase != PhysicalPhase.Dragged)
            return;

        if (TryResolveRootSyncTargetPosition(out var syncPos))
            transform.position = syncPos;
    }

    private void SyncRootToPhysicsBody()
    {
        if (!TryResolveRootSyncTargetPosition(out var targetPos))
            return;

        var originalTargetPos = targetPos;
        if (ShouldUseCollapseAnchor())
        {
            targetPos.x = _recoverAnchorPosition.x;
            targetPos.z = _recoverAnchorPosition.z;
        }

        if ((!_isActiveRagdoll || _isRecovering || _isRecoverStabilizing) &&
            targetPos.y > transform.position.y + StunRootUpwardSyncStep)
        {
            targetPos.y = transform.position.y + StunRootUpwardSyncStep;
        }

        var delta = targetPos - transform.position;
        var upwardTarget = targetPos.y - transform.position.y;
        if (upwardTarget > 0.08f || delta.sqrMagnitude > 0.25f)
        {
            TraceStartupLaunchDiagnostics(
                "SyncRootToPhysicsBody",
                targetPos,
                force: true,
                note: $"originalTargetY={originalTargetPos.y:F2} upwardTarget={upwardTarget:F2} collapseAnchor={ShouldUseCollapseAnchor()}");
        }

        if (delta.sqrMagnitude < 0.001f)
            return;

        // 텔레포트 방지: 5m+ 거리면 즉시 스냅
        if (delta.sqrMagnitude > 25f)
        {
            transform.position = targetPos;
            return;
        }

        // 부드럽게 추적 — 카메라 앵커가 급격한 점프를 받지 않도록
        var dt = Runner != null ? Runner.DeltaTime : Time.fixedDeltaTime;
        transform.position = Vector3.Lerp(transform.position, targetPos, 8f * dt);
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
        _isGrounded = _groundProbe.IsGrounded(
            rigidbody3D.position,
            transform,
            config.groundProbeRadius,
            config.groundProbeDistance);

        // Coyote time: 착지 직후 떨어지면 짧은 유예 시간 동안 점프 허용
        if (_isGrounded)
        {
            _coyoteTimeRemaining = COYOTE_TIME;
            // 안정적으로 서 있을 때 안전 위치 기억 — recover 시 활용
            if (_isActiveRagdoll && !_isRecovering)
                RememberSafeTransform(transform.position, transform.rotation);
        }
        else
        {
            _coyoteTimeRemaining -= dt;
            // 하강 중(vy < 0)일 때 추가 중력 배율 적용 → 점프 어감 개선 (떠다니는 느낌 제거)
            var fallMult = rigidbody3D.velocity.y < 0f ? config.fallGravityMultiplier : 1f;
            rigidbody3D.AddForce(
                Vector3.down * config.extraGravity * fallMult * Mathf.Max(0.05f, gravityMultiplier),
                ForceMode.Acceleration);
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
        ApplyMovementForce(moveDirection, inputMagnitude, moveSpeedMultiplier, dt);
        ApplyJumpIfPossible(input.Jump, jumpMultiplier);
        _stateMachine.Tick(_isGrounded, inputMagnitude, dt, config);

        var planarVelocity = rigidbody3D.velocity;
        planarVelocity.y = 0f;
        // 0.4f 계수: PartyMonsterAnimationDriver의 PM_LocomotionThreshold(0.1f)와
        // 걷기 시작 속도(0.25 m/s) 사이의 보정값. 변경 시 임계값도 함께 조정 필요.
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

        if (!IsAnyHandHoldingObject() || !TryGetAverageHeldAnchorWorldPosition(out var grabAnchorWorld))
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

    private void ApplyMovementForce(Vector3 moveDirection, float inputMagnitude, float moveSpeedMultiplier, float dt)
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
        var phase = ResolveAuthorityPhysicalPhase(anyHolding, beingGrabbed, dragged);
        SetLocalPhysicalPhase(phase, _localInstability, dragged);
        UpdateLocalCarryMode();
        TickCarryReleaseSettle(dt);
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
        if (_isRecovering || _isRecoverStabilizing)
            return PhysicalPhase.Recovering;

        if (dragged)
            return PhysicalPhase.Dragged;

        var instabilityThreshold = _localPhysicalPhase == PhysicalPhase.Unstable
            ? UnstableExitThreshold
            : UnstableEnterThreshold;
        if (_localInstability >= instabilityThreshold)
            return PhysicalPhase.Unstable;

        if (beingGrabbed)
            return PhysicalPhase.BeingGrabbed;

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

        return PhysicalPhase.Stable;
    }

    private void SetLocalPhysicalPhase(PhysicalPhase phase, float instability, bool dragged)
    {
        var previousPhase = _localPhysicalPhase;
        if (debugGrabLog && phase != _localPhysicalPhase)
        {
            Debug.Log($"[Phase] {name}: {_localPhysicalPhase} → {phase} " +
                $"(anyHolding={IsAnyHandHoldingObject()}, beingGrabbed={_beingGrabbedRefCount > 0}, " +
                $"isAnyStunned={IsAnyHandHoldingStunnedPlayer}, isDual={IsDualGrabbingStunnedPlayer}, " +
                $"instability={instability:F2})", this);
        }

        _localPhysicalPhase = phase;
        _localInstability = Mathf.Clamp01(instability);
        _localIsDragged = dragged;

        if (previousPhase != phase)
        {
            TraceStartupLaunchDiagnostics(
                "SetLocalPhysicalPhase",
                force: true,
                note: $"phase={previousPhase}->{phase} instability={instability:F2} dragged={dragged}");
        }
    }

    private float ResolveStunStateMultiplier()
    {
        var multiplier = ResolveLowHealthStunMultiplier();

        if (_isRecovering)
            return multiplier * (CombatSettings.Instance != null ? CombatSettings.Instance.recoveringMultiplier : 2.0f);
        if (!_isGrounded)
            return multiplier * (CombatSettings.Instance != null ? CombatSettings.Instance.airborneMultiplier : 1.5f);

        return multiplier;
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

    private void TriggerStun(float duration, bool applyEntryDamping = true)
    {
        if (Runner != null && Object != null && Object.IsValid && !HasStateAuthority)
            return;

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
        ClearPunchHitDetectionWindow();
        _isLeftGrabActive = false;
        _isRightGrabActive = false;
        _isGrabActive = false;

        // 기절 시 장비 아이템 드롭
        _itemRuntimeHost?.NotifyStunned();
        SetStunTimeRemaining(duration);
        SetAccumulatedStun(0f);
        _stunCollapseTimer = _beingGrabbedRefCount > 0
            ? 0f
            : Mathf.Min(duration, StunCollapseDuration);
        ApplyStunCollapseSpringState(_stunCollapseTimer > 0f);
        CaptureCollapseAnchorPose(transform.position, transform.rotation);
        ArmStunForceDiagnostics("TriggerStun", $"duration={duration:F2}");
        TraceStunCollapsePose("TriggerStun-Entry", true);
        if (applyEntryDamping)
            DampenStunEntryVelocities();
        TraceStunCollapsePose("TriggerStun-Damped", true);
        SetLocalPhysicalPhase(ResolveCurrentStunnedPhase(_stunCollapseTimer > 0f), 1f, false);
        _bodyPartPhysicsManager?.SetStateImmediate(
            _beingGrabbedRefCount > 0
                ? SSAFYPlayTime.Character.BodyPartPhysicsProfile.CharacterPhysicsState.CarriedStunned
                : SSAFYPlayTime.Character.BodyPartPhysicsProfile.CharacterPhysicsState.StunnedCollapse);
        FlagPhysicsPresentationReset();
        RaiseAnimationEvent(AnimationEventType.StunFall, H_StunFall);
        SynchronizeStunPresentationPhase();

        // 기절 비주얼: 애니메이션 비주얼 숨기고 물리 타겟 스켈레톤(래그돌)을 보여줌
        SetStunVisualMode(true);

        // 호스트: 회복 래치가 켜져 있으면 해제하고 mappingWeight 복원 (래그돌 포즈가 보여야 하므로)
        DeactivateAuthorityAnimatorVisualLatch();

        // 로컬 플레이어 기절 시 슬로우모션 연출
        TriggerStunSlowMotion();

    }

    private void DampenStunEntryVelocities()
    {
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
                StunEntryRootPlanarVelocityScale,
                StunEntryRootPlanarSpeedCap);
            rigidbody3D.angularVelocity *= StunEntryRootAngularVelocityScale;
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
                StunEntryMusclePlanarVelocityScale,
                StunEntryMusclePlanarSpeedCap);
            rb.angularVelocity *= StunEntryMuscleAngularVelocityScale;

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

    private void ClampStunnedMotion(bool collapsePhase = false, bool beingCarried = false)
    {
        var earlyCollapsePhase = collapsePhase && IsEarlyCollapsePhaseActive();

        float rootPlanarSpeedCap, musclePlanarSpeedCap, rootAngularSpeedCap, muscleAngularSpeedCap;
        if (beingCarried)
        {
            // 운반 중인 기절 피해자: 손을 따라 끌려가야 하므로 캡을 크게 완화
            rootPlanarSpeedCap = CarriedStunnedRootPlanarSpeedCap;
            musclePlanarSpeedCap = CarriedStunnedMusclePlanarSpeedCap;
            rootAngularSpeedCap = CarriedStunnedRootAngularSpeedCap;
            muscleAngularSpeedCap = CarriedStunnedMuscleAngularSpeedCap;
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
            rigidbody3D.angularVelocity = Vector3.ClampMagnitude(
                rigidbody3D.angularVelocity,
                rootAngularSpeedCap);
        }

        var traceLabel = beingCarried ? "ClampStunnedMotion-BeingCarried"
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
            rb.angularVelocity = Vector3.ClampMagnitude(
                rb.angularVelocity,
                muscleAngularSpeedCap);

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
        if (rootToPelvisGap <= CarriedRootSnapVerticalGap)
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
        ClearPunchHitDetectionWindow();
        _isLeftGrabActive = false;
        _isRightGrabActive = false;
        _isGrabActive = false;

        // 2-phase: stabilization → recovering
        _isRecoverStabilizing = true;
        _recoverStabilizeTimer = RECOVER_STABILIZE_DURATION;
        _isRecovering = true;
        _recoveringTimer = RECOVERING_DURATION;
        _stunCollapseTimer = 0f;

        SetStunTimeRemaining(0f);
        SetAccumulatedStun(0f);

        // 회복 시 잡힌 상태가 유지 중이면 grab spring 적용
        if (IsGrabbedByOther)
            ApplyGrabbedJointState(true);

        SetLocalPhysicalPhase(PhysicalPhase.Recovering, Mathf.Max(_localInstability, 0.45f), false);
        ArmStunForceDiagnostics("ForceRecover");
        SynchronizeStunPresentationPhase();

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
    private const float PunchHitRadius = 0.38f;
    private const float PunchHitForwardOffset = 0.18f;
    private const float PunchActiveWindowSeconds = 0.10f;
    private const float PunchFallbackReach = 0.8f;
    private const int PunchHitBufferSize = 16;
    private int _punchCooldownUntilTick;
    private int _activePunchWindowEndTick = -1;
    private bool _activePunchIsLeft;
    private bool _activePunchHasPreviousSample;
    private float _activePunchHealthDamage;
    private float _activePunchStunDamage;
    private float _activePunchKnockbackForce;
    private float _activePunchAttackerSpeed;
    private Vector3 _activePunchPreviousSamplePosition;
    private readonly Collider[] _punchHitResults = new Collider[PunchHitBufferSize];

    // ─── 히트스탑 상태 ───
    private float _hitStopEndTime;
    private Vector3 _hitStopSavedVelocity;
    private Vector3 _hitStopSavedAngularVelocity;
    private const float HIT_STOP_DURATION = 0.05f;
    private const float HIT_STOP_VELOCITY_SCALE = 0.05f;

    // ─── 피격 후 짧은 불안정(hit recoil) ───
    private float _hitRecoilTimer;
    private const float HIT_RECOIL_DURATION = 0.15f;
    private const float HIT_RECOIL_INSTABILITY_FLOOR = 0.55f;
    private const float HIT_RECOIL_ACCEL_SCALE = 0.35f;
    private const float HIT_RECOIL_BRAKE_SCALE = 0.30f;
    private const float HIT_RECOIL_GROUND_STICK_SCALE = 0.20f;

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
            return Mathf.Max(stat.Value.CooldownSec, PunchActiveWindowSeconds);

        return 0.35f;
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
        _activePunchAttackerSpeed = rigidbody3D != null ? rigidbody3D.velocity.magnitude : 0f;
        _activePunchWindowEndTick = currentTick + Mathf.Max(1, Mathf.RoundToInt(PunchActiveWindowSeconds * tickRate));
        return true;
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

    private void ApplyMuscleImpulseOnHit(NetworkPlayer victim, Vector3 hitPoint, Vector3 knockbackDir, float force)
    {
        if (victim._puppetMaster == null || victim._puppetMaster.muscles == null)
            return;

        var muscles = victim._puppetMaster.muscles;
        var stunnedVictim = !victim._isActiveRagdoll;
        var collapseVictim = victim.GetPhysicalPhase() == PhysicalPhase.StunnedCollapse;
        var mitigateCollapseImpulse = stunnedVictim && collapseVictim;
        var impactBlend = NormalizePunchImpact(force);
        var localHitOffset = victim.ResolveImpactLocalOffset(hitPoint);
        var lateralRatio = Mathf.Clamp01(Mathf.Abs(localHitOffset.x) / 0.32f);
        var heightRatio = Mathf.Clamp01((localHitOffset.y + 0.05f) / 0.70f);
        var torqueBlend = Mathf.Clamp01(Mathf.Max(lateralRatio, heightRatio * 0.85f));
        var focusedImpulseScale = mitigateCollapseImpulse
            ? Mathf.Lerp(0.035f, 0.075f, impactBlend)
            : stunnedVictim
            ? Mathf.Lerp(collapseVictim ? 0.12f : 0.18f, collapseVictim ? 0.22f : 0.28f, impactBlend)
            : Mathf.Lerp(0.42f, 0.62f, impactBlend);
        var spreadImpulseScale = mitigateCollapseImpulse
            ? 0f
            : stunnedVictim
            ? Mathf.Lerp(collapseVictim ? 0.02f : 0.04f, collapseVictim ? 0.05f : 0.08f, impactBlend)
            : Mathf.Lerp(0.10f, 0.18f, impactBlend);
        var twistTorqueScale = mitigateCollapseImpulse
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
        if (mitigateCollapseImpulse && closestCoreIdx >= 0)
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
            mitigateCollapseImpulse
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
            var yawTorque = mitigateCollapseImpulse || collapseVictim
                ? Vector3.zero
                : Vector3.up * yawSign * force * Mathf.Lerp(0.015f, 0.075f, lateralRatio);
            var backwardLeanTorque = mitigateCollapseImpulse
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

        Debug.Log($"[StunSlowMo] 시작: timeScale={STUN_SLOWMO_SCALE}");
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
            Debug.Log("[StunSlowMo] 복원 완료");
            return;
        }

        // ramp 구간 — STUN_SLOWMO_SCALE → 1.0 으로 선형 보간
        float t = (now - _stunSlowMotionHoldEnd) / STUN_SLOWMO_RAMP_DURATION;
        float scale = Mathf.Lerp(STUN_SLOWMO_SCALE, 1f, t);
        Time.timeScale = scale;
        Time.fixedDeltaTime = 0.02f * scale;
    }
}
