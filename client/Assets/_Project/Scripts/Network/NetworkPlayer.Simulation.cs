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

    private void DoPhysicsStep(PlayerNetworkInput input, float dt)
    {
        if (config == null || rigidbody3D == null || mainJoint == null)
            return;

        _isLeftGrabActive = input.LeftGrabHold;
        _isRightGrabActive = input.RightGrabHold;
        _isGrabActive = _isLeftGrabActive || _isRightGrabActive;

        UpdateStunDecay(dt);
        UpdateRecoveringWindow(dt);

        if (TryTickStunnedState(dt))
            return;

        SimulateLocomotion(input, dt);
        SynchronizeMotorPresentation();
        UpdateActiveRagdollJoints();
        ProcessInteractions(input);
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

    private void UpdateRecoveringWindow(float dt)
    {
        if (!_isRecovering)
            return;

        _recoveringTimer -= dt;
        if (_recoveringTimer <= 0f)
        {
            _isRecovering = false;
            _bodyPartPhysicsManager?.SetState(SSAFYPlayTime.Character.BodyPartPhysicsProfile.CharacterPhysicsState.Normal);
        }
    }

    private bool TryTickStunnedState(float dt)
    {
        if (_isActiveRagdoll)
            return false;

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
    /// </summary>
    private void SyncRootToPhysicsBody()
    {
        // PuppetMaster muscles[0] = pelvis/hips 뼈, 잡기로 끌려갈 때 실제로 이동하는 대상
        if (_puppetMaster != null && _puppetMaster.muscles != null && _puppetMaster.muscles.Length > 0)
        {
            var pelvisMuscle = _puppetMaster.muscles[0];
            if (pelvisMuscle.joint != null)
            {
                var pelvisPos = pelvisMuscle.joint.transform.position;
                if ((pelvisPos - transform.position).sqrMagnitude > 0.01f)
                    transform.position = pelvisPos;
                return;
            }
        }

        // PuppetMaster 없는 폴백: 루트 리지드바디 기준
        if (rigidbody3D != null && !rigidbody3D.isKinematic)
        {
            var physicsPos = rigidbody3D.position;
            if ((physicsPos - transform.position).sqrMagnitude > 0.01f)
                transform.position = physicsPos;
        }
    }

    private void SimulateLocomotion(PlayerNetworkInput input, float dt)
    {
        // PuppetMaster BehaviourPuppet가 Puppet 상태가 아니면(넘어짐/일어남 중) 이동 무시
        if (_behaviourPuppet != null && _behaviourPuppet.state != BehaviourPuppet.State.Puppet)
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
        ApplyMovementForce(moveDirection, inputMagnitude, alignedVelocity, moveSpeedMultiplier);
        ApplyJumpIfPossible(input.Jump, jumpMultiplier);
        _stateMachine.Tick(_isGrounded, inputMagnitude, dt, config);

        var normalizedMoveSpeed = Mathf.Abs(alignedVelocity) * 0.4f;
        SetMotorPresentationState(normalizedMoveSpeed, (int)_stateMachine.CurrentState);

        // 원격 클라이언트 애니메이션용 스프린트 상태 동기화
        if (Runner != null && Object != null && Object.IsValid)
            NetworkedIsSprinting = input.Sprint;
    }

    private void RotateTowardInput(Vector3 moveDirection, float inputMagnitude, float dt)
    {
        if (inputMagnitude <= 0.001f || moveDirection.sqrMagnitude <= 0.0001f)
            return;

        if (_targetRoot != null)
        {
            // PuppetMaster 모드: targetRoot(애니메이션 스켈레톤)를 직접 회전.
            // PuppetMaster가 이 타겟 포즈를 따라가므로 joint를 직접 건드리지 않는다.
            var desired = Quaternion.LookRotation(moveDirection.normalized, Vector3.up);
            _targetRoot.rotation = Quaternion.RotateTowards(
                _targetRoot.rotation,
                desired,
                dt * config.rotateSpeedDeg);
        }
        else
        {
            // PuppetMaster 없는 커스텀 래그돌: 기존 ConfigurableJoint 방식
            var visualDirection = new Vector3(-moveDirection.x, 0f, moveDirection.z);
            var desired = Quaternion.LookRotation(visualDirection.normalized, transform.up);
            mainJoint.targetRotation = Quaternion.RotateTowards(
                mainJoint.targetRotation,
                desired,
                dt * config.rotateSpeedDeg);
        }
    }

    private void ApplyMovementForce(Vector3 moveDirection, float inputMagnitude, float alignedVelocity, float moveSpeedMultiplier)
    {
        if (inputMagnitude <= 0.001f || moveDirection == Vector3.zero)
            return;
        if (Mathf.Abs(alignedVelocity) >= config.maxSpeed * Mathf.Max(0.05f, moveSpeedMultiplier))
            return;

        rigidbody3D.AddForce(
            moveDirection * inputMagnitude * config.acceleration * Mathf.Max(0.05f, moveSpeedMultiplier),
            ForceMode.Acceleration);
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
        }
    }

    private void SetMotorPresentationState(float moveSpeed, int motorState)
    {
        _localMoveSpeed = moveSpeed;
        _localMotorState = motorState;

        if (Runner != null && Object != null && Object.IsValid)
        {
            NetworkedMoveSpeed = moveSpeed;
            NetworkedMotorState = motorState;
        }
    }

    private void UpdateActiveRagdollJoints()
    {
        if (!_isActiveRagdoll || ShouldDisablePhysicsAnimationSync)
            return;

        for (var i = 0; i < syncPhysicsObjects.Length; i++)
            syncPhysicsObjects[i].UpdateJointFromAnimation();
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
        _isLeftGrabActive = false;
        _isRightGrabActive = false;
        _isGrabActive = false;
        SetStunTimeRemaining(duration);
        SetAccumulatedStun(0f);

        _bodyPartPhysicsManager?.SetState(SSAFYPlayTime.Character.BodyPartPhysicsProfile.CharacterPhysicsState.Stunned);
        RaiseAnimationEvent(AnimationEventType.StunFall, H_StunFall);

        Debug.Log($"[Combat] 기절! 시간: {duration:F1}초");
    }

    private void ForceRecover()
    {
        if (Runner != null && Object != null && Object.IsValid && !HasStateAuthority)
            return;

        if (!ShouldDisablePhysicsAnimationSync && mainJoint != null)
        {
            var jd = mainJoint.slerpDrive;
            jd.positionSpring = _startSlerpPositionSpring;
            mainJoint.slerpDrive = jd;
        }

        if (!ShouldDisablePhysicsAnimationSync)
        {
            for (int i = 0; i < syncPhysicsObjects.Length; i++)
                syncPhysicsObjects[i].MakeActiveRagdoll();
        }

        _isActiveRagdoll = true;
        _isLeftGrabActive = false;
        _isRightGrabActive = false;
        _isGrabActive = false;
        _isRecovering = true;
        _recoveringTimer = RECOVERING_DURATION;
        SetStunTimeRemaining(0f);
        SetAccumulatedStun(0f);

        _bodyPartPhysicsManager?.SetState(SSAFYPlayTime.Character.BodyPartPhysicsProfile.CharacterPhysicsState.Recovering);
        RaiseAnimationEvent(AnimationEventType.StunRecover, H_StunRecover);

        Debug.Log("[Combat] 회복! (2초간 취약 상태)");
    }

    // ─── 맨손 펀치 히트 판정 ───

    // CSV PUNCH 수치 폴백 (CombatSettings에서 로드 실패 시)
    private const string PunchCombatStatId = "PUNCH";
    private const float FallbackPunchStunDamage = 12f;
    private const float FallbackPunchKnockbackForce = 4f;
    private const float PunchHitRadius = 1.2f;
    private const float PunchHitForwardOffset = 0.5f;
    private int _punchCooldownUntilTick;

    internal void ExecutePunchHitDetection()
    {
        // 호스트에서만 판정
        if (Runner != null && Object != null && Object.IsValid && !HasStateAuthority)
            return;

        // 쿨다운 체크 (Fusion tick 기반 — 재시뮬레이션에서도 결정적)
        var cooldown = 0.4f;
        var stat = CombatSettings.Instance?.GetAttackStat(PunchCombatStatId);
        if (stat.HasValue) cooldown = stat.Value.CooldownSec;

        var currentTick = Runner != null ? Runner.Tick.Raw : (int)(Time.time / Time.fixedDeltaTime);
        var tickRate = Runner != null ? (int)Runner.TickRate : 60;
        var cooldownTicks = Mathf.Max(1, Mathf.RoundToInt(cooldown * tickRate));
        if (currentTick < _punchCooldownUntilTick)
            return;
        _punchCooldownUntilTick = currentTick + cooldownTicks;

        // CSV에서 수치 읽기
        var stunDamage = stat.HasValue ? stat.Value.StunDamage : FallbackPunchStunDamage;
        var knockbackForce = stat.HasValue ? stat.Value.KnockbackForce : FallbackPunchKnockbackForce;

        // 캐릭터 전방 OverlapSphere로 피격 대상 탐색
        var forward = _targetRoot != null ? _targetRoot.forward : transform.forward;
        var origin = transform.position + forward * PunchHitForwardOffset + Vector3.up * 0.3f;
        var hits = Physics.OverlapSphere(origin, PunchHitRadius);

        foreach (var hit in hits)
        {
            if (hit == null) continue;

            var victimPlayer = hit.transform.root.GetComponent<NetworkPlayer>();
            if (victimPlayer == null || victimPlayer == this) continue;
            if (!victimPlayer.IsActiveRagdoll) continue;

            // 피격 처리
            victimPlayer.ApplyStunDamage(stunDamage, 1.0f, 0f, 0f);

            // 넉백
            var knockbackDir = (victimPlayer.transform.position - transform.position).normalized + Vector3.up * 0.3f;
            var victimRb = victimPlayer.rigidbody3D;
            if (victimRb != null && !victimRb.isKinematic)
                victimRb.AddForce(knockbackDir.normalized * knockbackForce, ForceMode.Impulse);

            break; // 1번의 펀치에 1명만 타격
        }
    }
}
