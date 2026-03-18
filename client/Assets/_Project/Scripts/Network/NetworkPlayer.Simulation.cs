using RootMotion.Dynamics;
using UnityEngine;

public sealed partial class NetworkPlayer
{
    private float _localAccumulatedStun;
    private float _localStunTimeRemaining;

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
    private const float StunLaunchKnockbackScale = 0f;
    private const float StunEntryRootPlanarVelocityScale = 0.14f;
    private const float StunEntryRootPlanarSpeedCap = 0.95f;
    private const float StunEntryRootAngularVelocityScale = 0.08f;
    private const float StunEntryMusclePlanarVelocityScale = 0.18f;
    private const float StunEntryMusclePlanarSpeedCap = 0.75f;
    private const float StunEntryMuscleAngularVelocityScale = 0.12f;
    private const float StunnedRootPlanarSpeedCap = 0.85f;
    private const float StunnedMusclePlanarSpeedCap = 0.70f;
    private const float StunnedRootAngularSpeedCap = 2.4f;
    private const float StunnedMuscleAngularSpeedCap = 3.2f;
    private const float StunRootUpwardSyncStep = 0.08f;

    private void DoPhysicsStep(PlayerNetworkInput input, float dt)
    {
        if (config == null || rigidbody3D == null || mainJoint == null)
            return;

        _isLeftGrabActive = input.LeftGrabHold;
        _isRightGrabActive = input.RightGrabHold;
        _isGrabActive = _isLeftGrabActive || _isRightGrabActive;

        TickHitStopRecovery();
        UpdateStunDecay(dt);
        UpdateRecoveringWindow(dt);

        if (TryTickStunnedState(dt))
            return;

        SimulateLocomotion(input, dt);
        SynchronizeMotorPresentation();
        UpdateActiveRagdollJoints();
        ProcessInteractions(input);
        UpdatePhysicalPhaseState(dt);
        TickPunchHitDetectionWindow();
        SyncHeldItemNetworkState();
    }

    public void ApplyStunDamage(float stunDamage, float bodyPartMultiplier, float attackerVelocity, float impulseMagnitude)
    {
        if (!_isActiveRagdoll)
            return;
        if (Runner != null && Object != null && Object.IsValid && !HasStateAuthority)
            return;

        var buffApplier = ResolveItemBuffApplier();
        if (buffApplier != null && buffApplier.IsSuperArmorActive)
            return;

        var finalStunDamage = stunDamage * bodyPartMultiplier * ResolveStunStateMultiplier();
        var accumulated = AddStunDamage(finalStunDamage);

        RaiseAnimationEvent(AnimationEventType.GetHit, H_GetHit);

        var threshold = CombatSettings.Instance != null
            ? CombatSettings.Instance.knockoutThreshold
            : 30f;

        if (accumulated >= threshold)
            TriggerStun(CalculateStunDuration(attackerVelocity, impulseMagnitude));
    }

    public void OnPlayerBodyPartHit()
    {
        ApplyStunDamage(15f, 1.0f, 0f, 0f);
    }

    private void UpdateStunDecay(float dt)
    {
        const float decayRate = 5f;
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
    private Quaternion _recoverAnchorRotation = Quaternion.identity;

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
        var knockbackDir = BuildPunchKnockbackDirection(victimPlayer, forward);
        var speedBonus = 1f + Mathf.Clamp01(_activePunchAttackerSpeed / 8f) * 0.5f;
        var finalKnockback = _activePunchKnockbackForce * speedBonus;

        victimPlayer.ApplyStunDamage(_activePunchStunDamage, 1.0f, _activePunchAttackerSpeed, _activePunchKnockbackForce);
        var isStunnedByHit = !victimPlayer._isActiveRagdoll;
        var appliedKnockback = isStunnedByHit ? finalKnockback * StunLaunchKnockbackScale : finalKnockback;

        var victimRb = victimPlayer.rigidbody3D;
        var victimVelocityBeforeForce = victimRb != null && !victimRb.isKinematic
            ? victimRb.velocity
            : Vector3.zero;
        if (victimRb != null && !victimRb.isKinematic)
        {
            victimRb.AddForce(knockbackDir * appliedKnockback, ForceMode.Impulse);
            if (!isStunnedByHit)
            {
                victimRb.AddForceAtPosition(
                    knockbackDir * appliedKnockback * 0.28f,
                    hitPoint,
                    ForceMode.Impulse);
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
            $"isStunnedByHit={isStunnedByHit}");

        ApplyPunchFollowThrough(knockbackDir, finalKnockback);
        ApplyMuscleImpulseOnHit(victimPlayer, hitPoint, knockbackDir, appliedKnockback);
        if (isStunnedByHit)
            victimPlayer.DampenStunEntryVelocities();

        TriggerAttackCameraKick(forward, finalKnockback);
        victimPlayer.TriggerVictimCameraKick(knockbackDir, appliedKnockback);
        ApplyLocalHitStop(victimPlayer);
    }

    private Vector3 BuildPunchKnockbackDirection(NetworkPlayer victimPlayer, Vector3 forward)
    {
        var planarForward = Vector3.ProjectOnPlane(forward, Vector3.up);
        if (planarForward.sqrMagnitude < 0.0001f)
            planarForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (planarForward.sqrMagnitude < 0.0001f)
            planarForward = Vector3.forward;

        var dirToVictim = Vector3.ProjectOnPlane(victimPlayer.transform.position - transform.position, Vector3.up);
        if (dirToVictim.sqrMagnitude < 0.0001f)
            dirToVictim = planarForward;

        var blendedPlanar = (dirToVictim.normalized * 0.55f + planarForward.normalized * 0.45f).normalized;
        var upwardBias = victimPlayer._isGrounded ? 0.025f : 0.08f;
        var knockbackDir = (blendedPlanar + Vector3.up * upwardBias).normalized;
        knockbackDir.y = Mathf.Clamp(knockbackDir.y, -0.02f, 0.09f);
        return knockbackDir.normalized;
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
            _isRecoverStabilizing = false;

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

            SynchronizeStunPresentationPhase();
        }
    }

    private bool TryTickStunnedState(float dt)
    {
        if (_isActiveRagdoll)
            return false;

        var stunnedPhase = _beingGrabbedRefCount > 0
            ? PhysicalPhase.BeingCarriedStunned
            : PhysicalPhase.Stunned;
        SetLocalPhysicalPhase(stunnedPhase, 1f, false);

        var remaining = GetStunTimeRemaining() - dt;
        SetStunTimeRemaining(remaining);

        if (remaining <= 0f)
        {
            ForceRecover();
            if (_isActiveRagdoll)
                return true;
        }

        ClampStunnedMotion();
        TraceStunnedMotionSample("TryTickStunnedState");

        // 기절 중 물리 뼈(메인 리지드바디)가 잡기 조인트 등에 의해 끌려갈 수 있으므로
        // 루트 트랜스폼을 메인 리지드바디 위치에 맞춘다.
        // 이렇게 해야 NetworkTransform이 원격 클라이언트에 올바른 위치를 전달한다.
        SyncRootToPhysicsBody();

        return true;
    }

    /// <summary>
    /// 기절/레그돌 상태에서 실제 물리 뼈(pelvis) 위치를 루트 트랜스폼에 반영.
    /// 잡기 조인트로 끌려가는 건 PuppetMaster muscle 뼈이므로,
    /// rigidbody3D(루트)가 아닌 muscles[0](pelvis/hips)를 기준으로 해야 한다.
    ///
    /// 즉시 스냅 대신 Lerp를 사용하여 카메라 앵커에 급격한 점프가 전달되지 않도록 한다.
    /// 텔레포트 수준(5m+)이면 즉시 스냅.
    /// </summary>
    private void SyncRootToPhysicsBody()
    {
        Vector3 targetPos;

        if (_puppetMaster != null && _puppetMaster.muscles != null && _puppetMaster.muscles.Length > 0)
        {
            var pelvisMuscle = _puppetMaster.muscles[0];
            if (pelvisMuscle.joint != null)
                targetPos = pelvisMuscle.joint.transform.position;
            else
                return;
        }
        else if (rigidbody3D != null && !rigidbody3D.isKinematic)
        {
            targetPos = rigidbody3D.position;
        }
        else
        {
            return;
        }

        if (_hasRecoverAnchorPose && (_isRecovering || _isRecoverStabilizing))
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
            rigidbody3D.AddForce(
                Vector3.down * config.extraGravity * Mathf.Max(0.05f, gravityMultiplier),
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
        var normalizedMoveSpeed = planarVelocity.magnitude * 0.4f;
        var locomotionState = ResolveLocomotionState(normalizedMoveSpeed, input.Sprint);
        SetMotorPresentationState(normalizedMoveSpeed, (int)_stateMachine.CurrentState, locomotionState);

        // 원격 클라이언트 애니메이션용 스프린트 상태 동기화
        if (Runner != null && Object != null && Object.IsValid)
            NetworkedIsSprinting = input.Sprint;
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

        if (_isGrounded && inputMagnitude <= 0.001f)
        {
            ApplyPlanarBrake(planarVelocity, dt);
            return;
        }

        if (moveDirection == Vector3.zero)
            return;

        var targetSpeed = config.maxSpeed * Mathf.Max(0.05f, moveSpeedMultiplier) * inputMagnitude;
        var targetVelocity = moveDirection * targetSpeed;
        var acceleration = _isGrounded ? config.acceleration : config.airAcceleration;
        var maxVelocityChange = Mathf.Max(0f, acceleration) * dt;
        var velocityDelta = Vector3.ClampMagnitude(targetVelocity - planarVelocity, maxVelocityChange);

        if (dt > 0f)
        {
            rigidbody3D.AddForce(velocityDelta / dt, ForceMode.Acceleration);

            if (_isGrounded && config.groundStickForce > 0f)
            {
                rigidbody3D.AddForce(
                    Vector3.down * config.groundStickForce,
                    ForceMode.Acceleration);
            }
        }
    }

    private void ApplyPlanarBrake(Vector3 planarVelocity, float dt)
    {
        if (dt <= 0f)
            return;

        if (planarVelocity.sqrMagnitude <= config.stopSpeedEpsilon * config.stopSpeedEpsilon)
        {
            rigidbody3D.velocity = new Vector3(0f, rigidbody3D.velocity.y, 0f);
            return;
        }

        var brakeSpeed = Mathf.Max(0f, config.brakingAcceleration) * dt;
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
            SetLocalPhysicalPhase(PhysicalPhase.Stunned, 1f, false);
            return;
        }

        var anyHolding = IsAnyHandHoldingObject();
        var beingGrabbed = _beingGrabbedRefCount > 0;

        UpdateInstabilityScore(dt, anyHolding, beingGrabbed);

        var dragged = ResolveDraggedState(beingGrabbed);
        var phase = ResolveAuthorityPhysicalPhase(anyHolding, beingGrabbed, dragged);
        SetLocalPhysicalPhase(phase, _localInstability, dragged);
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
            if (IsDualGrabbingStunnedPlayer)
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
        _localPhysicalPhase = phase;
        _localInstability = Mathf.Clamp01(instability);
        _localIsDragged = dragged;
    }

    private float ResolveStunStateMultiplier()
    {
        if (_isRecovering)
            return 2.0f;
        if (!_isGrounded)
            return 1.5f;

        return 1.0f;
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

    private float CalculateStunDuration(float attackerVelocity, float impulseMagnitude)
    {
        const float baseMin = 1.5f;
        const float baseMax = 4.0f;
        const float velocityBonus = 0.15f;
        const float weightBonus = 0.02f;
        var stunMin = 1.5f;
        var stunMax = 8.0f;

        if (CombatSettings.Instance != null)
        {
            stunMin = 1.5f;
            stunMax = 8.0f;
        }

        var duration = Random.Range(baseMin, baseMax)
                       + attackerVelocity * velocityBonus
                       + impulseMagnitude * weightBonus;

        return Mathf.Clamp(duration, stunMin, stunMax);
    }

    private void TriggerStun(float duration)
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
        ArmStunForceDiagnostics("TriggerStun", $"duration={duration:F2}");
        DampenStunEntryVelocities();
        SetLocalPhysicalPhase(PhysicalPhase.Stunned, 1f, false);
        _bodyPartPhysicsManager?.SetStateImmediate(
            _beingGrabbedRefCount > 0
                ? SSAFYPlayTime.Character.BodyPartPhysicsProfile.CharacterPhysicsState.CarriedStunned
                : SSAFYPlayTime.Character.BodyPartPhysicsProfile.CharacterPhysicsState.Stunned);
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
        velocity.y = Mathf.Min(velocity.y, 0f);
        return velocity;
    }

    private void ClampStunnedMotion()
    {
        if (rigidbody3D != null && !rigidbody3D.isKinematic)
        {
            rigidbody3D.velocity = ClampStunnedVelocity(rigidbody3D.velocity, StunnedRootPlanarSpeedCap);
            rigidbody3D.angularVelocity = Vector3.ClampMagnitude(
                rigidbody3D.angularVelocity,
                StunnedRootAngularSpeedCap);
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

            rb.velocity = ClampStunnedVelocity(rb.velocity, StunnedMusclePlanarSpeedCap);
            rb.angularVelocity = Vector3.ClampMagnitude(
                rb.angularVelocity,
                StunnedMuscleAngularSpeedCap);
        }
    }

    private static Vector3 ClampStunnedVelocity(Vector3 velocity, float planarSpeedCap)
    {
        var planarVelocity = new Vector3(velocity.x, 0f, velocity.z);
        planarVelocity = Vector3.ClampMagnitude(planarVelocity, planarSpeedCap);

        velocity.x = planarVelocity.x;
        velocity.z = planarVelocity.z;
        velocity.y = Mathf.Min(velocity.y, 0f);
        return velocity;
    }

    private void ForceRecover()
    {
        if (Runner != null && Object != null && Object.IsValid && !HasStateAuthority)
            return;

        if (!TryResolveRecoveryTransform(out var recoveryPosition, out var recoveryRotation))
        {
            recoveryPosition = transform.position;
            recoveryRotation = transform.rotation;
        }

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
        AlignCharacterUpright(recoveryRotation);

        // ── 2.5) 안전 위치 텔레포트: 바닥 침투 방지 ──
        TeleportToSafeStandUpPosition(recoveryPosition, recoveryRotation);

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

        SetStunTimeRemaining(0f);
        SetAccumulatedStun(0f);
        SetLocalPhysicalPhase(PhysicalPhase.Recovering, Mathf.Max(_localInstability, 0.45f), false);
        ArmStunForceDiagnostics("ForceRecover");
        FlagPhysicsPresentationReset();
        RaiseAnimationEvent(AnimationEventType.StunRecover, H_StunRecover);
        SynchronizeStunPresentationPhase();

        // ── 4) 기립 보조: 약한 위쪽 충격량으로 일어나는 느낌 ──
        // 회복 비주얼: 물리 타겟 스켈레톤 숨기고 애니메이션 비주얼 복원
        SetStunVisualMode(false);

        // 호스트: PuppetMaster.Map()이 target skeleton을 덮어쓰지 않도록 즉시 래치 활성화.
        // LateUpdate의 SynchronizePhysicsPresentationState() 전환 감지를 기다리지 않고
        // ForceRecover 시점에 바로 mappingWeight=0을 적용한다.
        ActivateAuthorityAnimatorVisualLatch();

    }

    // ─── 회복 안정화 헬퍼 ───

    /// <summary>
    /// 모든 물리 뼈의 잔여 속도/각속도를 대폭 감쇠.
    /// 기절 중 축적된 충돌/관성을 제거해서 spring 복원 시 떨림을 방지.
    /// </summary>
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
        if ((!_isRecovering && !_isRecoverStabilizing) || !_hasRecoverAnchorPose)
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
        if ((!_isRecovering && !_isRecoverStabilizing) || !_hasRecoverAnchorPose)
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
    private float _activePunchStunDamage;
    private float _activePunchKnockbackForce;
    private float _activePunchAttackerSpeed;
    private Vector3 _activePunchPreviousSamplePosition;
    private readonly Collider[] _punchHitResults = new Collider[PunchHitBufferSize];

    // ─── 히트스탑 상태 ───
    private float _hitStopEndTime;
    private Vector3 _hitStopSavedVelocity;
    private Vector3 _hitStopSavedAngularVelocity;
    private const float HIT_STOP_DURATION = 0.075f;
    private const float HIT_STOP_VELOCITY_SCALE = 0.02f;

    // ─── 기절 슬로우모션 (로컬 전용) ───
    private bool _stunSlowMotionActive;
    private float _stunSlowMotionHoldEnd;    // unscaledTime 기준
    private float _stunSlowMotionRampEnd;    // unscaledTime 기준
    private const float STUN_SLOWMO_SCALE = 0.15f;        // 85% 감속
    private const float STUN_SLOWMO_HOLD_DURATION = 0.25f; // 최저 유지 시간 (realtime)
    private const float STUN_SLOWMO_RAMP_DURATION = 0.35f; // 복원 램프 시간 (realtime)

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
        _activePunchStunDamage = stat.HasValue ? stat.Value.StunDamage : FallbackPunchStunDamage;
        _activePunchKnockbackForce = stat.HasValue ? stat.Value.KnockbackForce : FallbackPunchKnockbackForce;
        _activePunchAttackerSpeed = rigidbody3D != null ? rigidbody3D.velocity.magnitude : 0f;
        _activePunchWindowEndTick = currentTick + Mathf.Max(1, Mathf.RoundToInt(PunchActiveWindowSeconds * tickRate));
        return true;
    }

    internal void ExecutePunchHitDetection(bool isLeft)
    {
        // 호스트에서만 판정
        if (Runner != null && Object != null && Object.IsValid && !HasStateAuthority)
            return;

        // 쿨다운 체크 (Fusion tick 기반 — 재시뮬레이션에서도 결정적)
        var cooldown = 0.35f;
        var stat = CombatSettings.Instance?.GetAttackStat(PunchCombatStatId);
        if (stat.HasValue) cooldown = stat.Value.CooldownSec;

        var currentTick = ResolveCurrentSimulationTick();
        var tickRate = Runner != null ? (int)Runner.TickRate : Mathf.Max(1, Mathf.RoundToInt(1f / Time.fixedDeltaTime));
        var cooldownTicks = Mathf.Max(1, Mathf.RoundToInt(cooldown * tickRate));
        if (currentTick < _punchCooldownUntilTick)
            return;
        _punchCooldownUntilTick = currentTick + cooldownTicks;
        _activePunchIsLeft = isLeft;
        _activePunchHasPreviousSample = false;
        _activePunchStunDamage = stat.HasValue ? stat.Value.StunDamage : FallbackPunchStunDamage;
        _activePunchKnockbackForce = stat.HasValue ? stat.Value.KnockbackForce : FallbackPunchKnockbackForce;
        _activePunchAttackerSpeed = rigidbody3D != null ? rigidbody3D.velocity.magnitude : 0f;
        _activePunchWindowEndTick = currentTick + Mathf.Max(1, Mathf.RoundToInt(PunchActiveWindowSeconds * tickRate));
        return;

        // CSV에서 수치 읽기
        var stunDamage = stat.HasValue ? stat.Value.StunDamage : FallbackPunchStunDamage;
        var knockbackForce = stat.HasValue ? stat.Value.KnockbackForce : FallbackPunchKnockbackForce;

        // 캐릭터 전방 OverlapSphere로 피격 대상 탐색
        var forward = _targetRoot != null ? _targetRoot.forward : transform.forward;
        var origin = transform.position + forward * PunchHitForwardOffset + Vector3.up * 0.3f;
        var hits = Physics.OverlapSphere(origin, PunchHitRadius);

        // 공격자 속도 — 달리면서 때리면 넉백/기절 시간이 더 길어짐
        var attackerSpeed = rigidbody3D != null ? rigidbody3D.velocity.magnitude : 0f;

        foreach (var hit in hits)
        {
            if (hit == null) continue;

            var victimPlayer = hit.transform.root.GetComponent<NetworkPlayer>();
            if (victimPlayer == null || victimPlayer == this) continue;
            if (!victimPlayer.IsActiveRagdoll) continue;

            // 피격 처리 — 공격자 속도를 반영
            victimPlayer.ApplyStunDamage(stunDamage, 1.0f, attackerSpeed, knockbackForce);
            var isStunnedByHit = !victimPlayer._isActiveRagdoll;

            // 넉백 방향: 공격자 forward 가중 + 위쪽 임펄스 강화
            var knockbackDir = BuildPunchKnockbackDirection(victimPlayer, forward);

            // 속도 보너스: 달리면서 때리면 최대 1.5배
            var speedBonus = 1f + Mathf.Clamp01(attackerSpeed / 8f) * 0.5f;
            var finalKnockback = knockbackForce * speedBonus;
            var appliedKnockback = isStunnedByHit ? finalKnockback * StunLaunchKnockbackScale : finalKnockback;

            var victimRb = victimPlayer.rigidbody3D;
            var victimVelocityBeforeForce = victimRb != null && !victimRb.isKinematic
                ? victimRb.velocity
                : Vector3.zero;
            if (victimRb != null && !victimRb.isKinematic)
                victimRb.AddForce(knockbackDir * appliedKnockback, ForceMode.Impulse);
            victimPlayer.TraceStunForceEvent(
                "PunchRootLegacy",
                victimRb,
                knockbackDir * appliedKnockback,
                ForceMode.Impulse,
                victimVelocityBeforeForce,
                victimRb != null && !victimRb.isKinematic ? victimRb.velocity : victimVelocityBeforeForce,
                appliedKnockback > 0.0001f,
                $"isStunnedByHit={isStunnedByHit}");

            // 피격 muscle 직접 임펄스 — 맞은 부위가 물리적으로 밀림
            ApplyMuscleImpulseOnHit(victimPlayer, hit.transform.position, knockbackDir, appliedKnockback);
            if (isStunnedByHit)
                victimPlayer.DampenStunEntryVelocities();

            // 히트스탑: 양쪽 rigidbody 일시 감속
            ApplyLocalHitStop(victimPlayer);

            break; // 1번의 펀치에 1명만 타격
        }
    }

    /// <summary>
    /// 피격 시 PuppetMaster muscle 뼈에 직접 임펄스를 가해서
    /// 맞은 부위가 물리적으로 밀리는 파티애니멀즈 스타일 효과.
    /// 가장 가까운 muscle에 집중 임펄스, 나머지에 분산 임펄스.
    /// </summary>
    private void ApplyMuscleImpulseOnHit(NetworkPlayer victim, Vector3 hitPoint, Vector3 knockbackDir, float force)
    {
        if (victim._puppetMaster == null || victim._puppetMaster.muscles == null)
            return;

        var muscles = victim._puppetMaster.muscles;
        var stunnedVictim = !victim._isActiveRagdoll;
        var impactBlend = NormalizePunchImpact(force);
        var focusedImpulseScale = stunnedVictim
            ? 0f
            : Mathf.Lerp(0.42f, 0.62f, impactBlend);
        var spreadImpulseScale = stunnedVictim
            ? 0f
            : Mathf.Lerp(0.10f, 0.18f, impactBlend);
        var twistTorqueScale = stunnedVictim ? 0f : 0.06f;
        float closestDist = float.MaxValue;
        int closestIdx = -1;

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
        }

        if (closestIdx < 0) return;

        var targetMuscleName = muscles[closestIdx].joint != null ? muscles[closestIdx].joint.name : "unknown";
        victim.TraceStunImpulseSummary(
            "ApplyMuscleImpulseOnHit",
            force,
            focusedImpulseScale,
            spreadImpulseScale,
            twistTorqueScale,
            targetMuscleName);

        var closestRb = muscles[closestIdx].joint.GetComponent<Rigidbody>();
        if (closestRb != null && !closestRb.isKinematic)
        {
            closestRb.AddForceAtPosition(
                knockbackDir * force * focusedImpulseScale,
                hitPoint,
                ForceMode.Impulse);
            closestRb.AddTorque(
                Vector3.Cross(Vector3.up, knockbackDir) * force * twistTorqueScale,
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
