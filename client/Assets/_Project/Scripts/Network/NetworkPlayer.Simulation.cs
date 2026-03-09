using UnityEngine;

public sealed partial class NetworkPlayer
{
    private float _localAccumulatedStun;
    private float _localStunTimeRemaining;

    private void DoPhysicsStep(PlayerNetworkInput input, float dt)
    {
        if (config == null || rigidbody3D == null || mainJoint == null)
            return;

        _isGrabActive = input.GrabHold;

        UpdateStunDecay(dt);
        UpdateRecoveringWindow(dt);

        if (TryTickStunnedState(dt))
            return;

        SimulateLocomotion(input, dt);
        SynchronizeMotorPresentation();
        UpdateActiveRagdollJoints();
        ProcessInteractions(input);
    }

    public void ApplyStunDamage(float stunDamage, float bodyPartMultiplier, float attackerVelocity, float impulseMagnitude)
    {
        if (!_isActiveRagdoll)
            return;
        if (Runner != null && Object != null && Object.IsValid && !HasStateAuthority)
            return;

        var finalStunDamage = stunDamage * bodyPartMultiplier * ResolveStunStateMultiplier();
        var accumulated = AddStunDamage(finalStunDamage);

        if (animator != null)
            animator.SetTrigger(H_GetHit);

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
            _isRecovering = false;
    }

    private bool TryTickStunnedState(float dt)
    {
        if (_isActiveRagdoll)
            return false;

        var remaining = GetStunTimeRemaining() - dt;
        SetStunTimeRemaining(remaining);

        if (remaining <= 0f)
            ForceRecover();

        return true;
    }

    private void SimulateLocomotion(PlayerNetworkInput input, float dt)
    {
        _isGrounded = _groundProbe.IsGrounded(
            rigidbody3D.position,
            transform,
            config.groundProbeRadius,
            config.groundProbeDistance);

        if (!_isGrounded)
            rigidbody3D.AddForce(Vector3.down * config.extraGravity, ForceMode.Acceleration);

        var inputMagnitude = input.Move.magnitude;
        var forwardVelocity = Vector3.Dot(transform.forward, rigidbody3D.velocity);

        RotateTowardInput(input.Move, inputMagnitude, dt);
        ApplyMovementForce(inputMagnitude, forwardVelocity);
        ApplyJumpIfPossible(input.Jump);
        _stateMachine.Tick(_isGrounded, inputMagnitude, dt, config);

        var normalizedMoveSpeed = Mathf.Abs(forwardVelocity) * 0.4f;
        SetMotorPresentationState(normalizedMoveSpeed, (int)_stateMachine.CurrentState);
    }

    private void RotateTowardInput(Vector2 moveInput, float inputMagnitude, float dt)
    {
        if (inputMagnitude <= 0.001f)
            return;

        var desired = Quaternion.LookRotation(new Vector3(moveInput.x, 0f, -moveInput.y), transform.up);
        mainJoint.targetRotation = Quaternion.RotateTowards(
            mainJoint.targetRotation,
            desired,
            dt * config.rotateSpeedDeg);
    }

    private void ApplyMovementForce(float inputMagnitude, float forwardVelocity)
    {
        if (inputMagnitude <= 0.001f)
            return;
        if (Mathf.Abs(forwardVelocity) >= config.maxSpeed)
            return;

        rigidbody3D.AddForce(transform.forward * inputMagnitude * config.acceleration, ForceMode.Acceleration);
    }

    private void ApplyJumpIfPossible(bool jumpPressed)
    {
        if (!_isGrounded || !jumpPressed)
            return;

        rigidbody3D.AddForce(Vector3.up * config.jumpImpulse, ForceMode.Impulse);
        _stateMachine.SetJump();
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
        if (!_isActiveRagdoll)
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

        if (mainJoint != null)
        {
            var jd = mainJoint.slerpDrive;
            jd.positionSpring = 0;
            mainJoint.slerpDrive = jd;
        }

        for (int i = 0; i < syncPhysicsObjects.Length; i++)
            syncPhysicsObjects[i].MakeRagdoll();

        _isActiveRagdoll = false;
        _isGrabActive = false;
        SetStunTimeRemaining(duration);
        SetAccumulatedStun(0f);

        if (animator != null)
            animator.SetTrigger(H_StunFall);

        Debug.Log($"[Combat] 기절! 시간: {duration:F1}초");
    }

    private void ForceRecover()
    {
        if (Runner != null && Object != null && Object.IsValid && !HasStateAuthority)
            return;

        if (mainJoint != null)
        {
            var jd = mainJoint.slerpDrive;
            jd.positionSpring = _startSlerpPositionSpring;
            mainJoint.slerpDrive = jd;
        }

        for (int i = 0; i < syncPhysicsObjects.Length; i++)
            syncPhysicsObjects[i].MakeActiveRagdoll();

        _isActiveRagdoll = true;
        _isGrabActive = false;
        _isRecovering = true;
        _recoveringTimer = RECOVERING_DURATION;
        SetStunTimeRemaining(0f);
        SetAccumulatedStun(0f);

        if (animator != null)
            animator.SetTrigger(H_StunRecover);

        Debug.Log("[Combat] 회복! (2초간 취약 상태)");
    }
}
