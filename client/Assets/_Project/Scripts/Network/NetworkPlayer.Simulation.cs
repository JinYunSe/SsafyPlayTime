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

    private void UpdateRecoveringWindow(float dt)
    {
        if (_isRecoverStabilizing)
        {
            TickRecoverStabilization(dt);
            return;
        }

        if (!_isRecovering)
            return;

        _recoveringTimer -= dt;
        if (_recoveringTimer <= 0f)
        {
            _isRecovering = false;
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
        var appliedKnockback = isStunnedByHit ? finalKnockback * 0.4f : finalKnockback;

        var victimRb = victimPlayer.rigidbody3D;
        if (victimRb != null && !victimRb.isKinematic)
            victimRb.AddForce(knockbackDir * appliedKnockback, ForceMode.Impulse);

        ApplyMuscleImpulseOnHit(victimPlayer, hitPoint, knockbackDir, appliedKnockback);
        if (isStunnedByHit)
            victimPlayer.DampenStunEntryVelocities();

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

        var blendedPlanar = (dirToVictim.normalized * 0.7f + planarForward.normalized * 0.3f).normalized;
        var upwardBias = victimPlayer._isGrounded ? 0.05f : 0.1f;
        var knockbackDir = (blendedPlanar + Vector3.up * upwardBias).normalized;
        knockbackDir.y = Mathf.Clamp(knockbackDir.y, -0.02f, 0.12f);
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
        if (_puppetMaster != null && _puppetMaster.muscles != null)
        {
            foreach (var muscle in _puppetMaster.muscles)
            {
                if (muscle.joint == null) continue;
                var rb = muscle.joint.GetComponent<Rigidbody>();
                if (rb != null && !rb.isKinematic)
                    rb.angularVelocity *= 0.8f;
            }
        }

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

        SetLocalPhysicalPhase(PhysicalPhase.Stunned, 1f, false);

        var remaining = GetStunTimeRemaining() - dt;
        SetStunTimeRemaining(remaining);

        if (remaining <= 0f)
            ForceRecover();

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
            _coyoteTimeRemaining = COYOTE_TIME;
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
            var visualDirection = new Vector3(-moveDirection.x, 0f, moveDirection.z);
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
            return PhysicalPhase.Holding;

        if (_isGrabActive)
            return PhysicalPhase.GrabIntent;

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
        SetStunTimeRemaining(duration);
        SetAccumulatedStun(0f);
        DampenStunEntryVelocities();
        SetLocalPhysicalPhase(PhysicalPhase.Stunned, 1f, false);
        FlagPhysicsPresentationReset();
        RaiseAnimationEvent(AnimationEventType.StunFall, H_StunFall);
        SynchronizeStunPresentationPhase();

        // 기절 비주얼: 애니메이션 비주얼 숨기고 물리 타겟 스켈레톤(래그돌)을 보여줌
        SetStunVisualMode(true);

        Debug.Log($"[Combat] 기절! 시간: {duration:F1}초");
    }

    private void DampenStunEntryVelocities()
    {
        if (rigidbody3D != null && !rigidbody3D.isKinematic)
        {
            var velocity = rigidbody3D.velocity;
            velocity.x *= 0.4f;
            velocity.z *= 0.4f;
            velocity.y = Mathf.Min(velocity.y, 0.15f);
            rigidbody3D.velocity = velocity;
            rigidbody3D.angularVelocity *= 0.2f;
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

            var velocity = rb.velocity;
            velocity.x *= 0.45f;
            velocity.z *= 0.45f;
            velocity.y = Mathf.Min(velocity.y, 0.1f);
            rb.velocity = velocity;
            rb.angularVelocity *= 0.25f;
        }
    }

    private void ForceRecover()
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

        // ── 1) 물리 안정화: 잔여 속도 제거 ──
        DampenAllPhysicsBoneVelocities();

        // ── 2) 기립 정렬: 캐릭터를 월드 업 방향으로 세움 ──
        AlignCharacterUpright();

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
        FlagPhysicsPresentationReset();
        RaiseAnimationEvent(AnimationEventType.StunRecover, H_StunRecover);
        SynchronizeStunPresentationPhase();

        // ── 4) 기립 보조: 약한 위쪽 충격량으로 일어나는 느낌 ──
        if (rigidbody3D != null && !rigidbody3D.isKinematic && config != null)
            rigidbody3D.AddForce(Vector3.up * config.jumpImpulse * 0.25f, ForceMode.Impulse);

        // 회복 비주얼: 물리 타겟 스켈레톤 숨기고 애니메이션 비주얼 복원
        SetStunVisualMode(false);

        Debug.Log("[Combat] 회복! (안정화 0.4초 + 취약 2초)");
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
                    rb.velocity *= 0.1f;
                    rb.angularVelocity *= 0.1f;
                }
            }
        }
    }

    /// <summary>
    /// 캐릭터 루트(메인 rigidbody)를 월드 업 방향으로 정렬.
    /// 기절 중 옆으로 눕거나 뒤집힌 상태에서 회복 시 서있는 자세로 복원.
    /// yaw(수평 회전)는 유지하고 roll/pitch만 제거.
    /// </summary>
    private void AlignCharacterUpright()
    {
        if (rigidbody3D == null) return;

        var euler = rigidbody3D.rotation.eulerAngles;
        var uprightRotation = Quaternion.Euler(0f, euler.y, 0f);
        rigidbody3D.rotation = uprightRotation;
        rigidbody3D.angularVelocity = Vector3.zero;
        transform.rotation = uprightRotation;

        // PuppetMaster targetRoot도 정렬
        if (_targetRoot != null)
            _targetRoot.rotation = uprightRotation;

        SetPresentationVisualYaw(uprightRotation.eulerAngles.y);
    }

    // ─── 맨손 펀치 히트 판정 ───

    // CSV PUNCH 수치 폴백 (CombatSettings에서 로드 실패 시)
    private const string PunchCombatStatId = "PUNCH";
    private const float FallbackPunchStunDamage = 12f;
    private const float FallbackPunchKnockbackForce = 10f;
    private const float PunchHitRadius = 0.38f;
    private const float PunchHitForwardOffset = 0.18f;
    private const float PunchActiveWindowSeconds = 0.14f;
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
    private const float HIT_STOP_DURATION = 0.06f;
    private const float HIT_STOP_VELOCITY_SCALE = 0.05f;

    internal void ExecutePunchHitDetection(bool isLeft)
    {
        // 호스트에서만 판정
        if (Runner != null && Object != null && Object.IsValid && !HasStateAuthority)
            return;

        // 쿨다운 체크 (Fusion tick 기반 — 재시뮬레이션에서도 결정적)
        var cooldown = 0.4f;
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
            var appliedKnockback = isStunnedByHit ? finalKnockback * 0.4f : finalKnockback;

            var victimRb = victimPlayer.rigidbody3D;
            if (victimRb != null && !victimRb.isKinematic)
                victimRb.AddForce(knockbackDir * appliedKnockback, ForceMode.Impulse);

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
        var focusedImpulseScale = stunnedVictim ? 0.18f : 0.35f;
        var spreadImpulseScale = stunnedVictim ? 0.02f : 0.08f;
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

        // 가장 가까운 muscle에 집중 임펄스 (50%)
        var closestRb = muscles[closestIdx].joint.GetComponent<Rigidbody>();
        if (closestRb != null && !closestRb.isKinematic)
            closestRb.AddForce(knockbackDir * force * focusedImpulseScale, ForceMode.Impulse);

        // 나머지 muscle에 분산 임펄스 (15%)
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
}
